using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using BabyNamePicker.Data;
using BabyNamePicker.Services;
using BabyNamePicker.Services.Llm;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

LoadDotEnvFromAncestors();

if (args.Length > 0 && args[0].Equals("import-ssa", StringComparison.OrdinalIgnoreCase))
{
    await RunImportCommandAsync(args[1..]);
    return;
}

if (args.Length > 0 && args[0].Equals("enrich-names", StringComparison.OrdinalIgnoreCase))
{
    await RunEnrichCommandAsync(args[1..]);
    return;
}

if (args.Length > 0 && args[0].Equals("reclassify-gender", StringComparison.OrdinalIgnoreCase))
{
    await RunGenderRecalcCommandAsync(args[1..]);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SsaImportOptions>(builder.Configuration.GetSection("SsaImport"));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("ssa", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; BabyNamePicker/1.0)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/zip, application/octet-stream, */*");
});
builder.Services.AddSingleton<AdminLogHub>();
builder.Services.AddSingleton<AdminJobLock>();
builder.Services.AddSingleton<ILoggerProvider, AdminLogLoggerProvider>();
builder.Services.AddScoped<SsaImporter>();
builder.Services.AddScoped<NicknameSeeder>();
builder.Services.AddScoped<NameQueryService>();
builder.Services.AddScoped<NameEnrichmentService>();
builder.Services.AddScoped<GenderRecalculationService>();
builder.Services.AddScoped<AdminUserService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "BabyNamePicker.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("admin-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));
});

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/admin", () => Results.Redirect("/admin.html"));

var api = app.MapGroup("/api");

api.MapGet("/names", async (string? q, string? gender, string? rarity, NameQueryService service, CancellationToken ct) =>
    Results.Ok(await service.SearchAsync(q, gender, rarity, ct)));

api.MapGet("/names/random", async (string? q, string? gender, string? rarity, NameQueryService service, CancellationToken ct) =>
{
    var result = await service.GetRandomAsync(q, gender, rarity, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

api.MapGet("/names/{id:int}", async (int id, NameQueryService service, CancellationToken ct) =>
{
    var result = await service.GetByIdAsync(id, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

api.MapGet("/years", async (NameQueryService service, CancellationToken ct) =>
    Results.Ok(await service.GetYearsAsync(ct)));

api.MapGet("/popularity", async (int year, string? gender, int? limit, NameQueryService service, CancellationToken ct) =>
    Results.Ok(await service.GetPopularityAsync(year, gender, limit ?? 100, ct)));

var admin = api.MapGroup("/admin");

admin.MapGet("/status", (AdminUserService adminUsers, HttpContext http) =>
    Results.Ok(new
    {
        enabled = adminUsers.IsEnabled,
        authenticated = http.User.Identity?.IsAuthenticated == true
    }));

admin.MapPost("/login", async (LoginRequest? body, AdminUserService adminUsers, HttpContext http, CancellationToken ct) =>
{
    if (!adminUsers.IsEnabled)
    {
        return Results.Json(new { error = "Admin is not enabled." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var username = body?.Username?.Trim() ?? string.Empty;
    var password = body?.Password ?? string.Empty;
    var user = await adminUsers.AuthenticateAsync(username, password, ct);
    if (user is null)
    {
        return Results.Json(new { error = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Role, "Admin")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    return Results.Ok(new { username = user.Username });
}).RequireRateLimiting("admin-login");

admin.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

admin.MapGet("/pipeline-defaults", (IOptions<LlmOptions> llm) =>
{
    var provider = llm.Value.Provider;
    return Results.Ok(new
    {
        enrichNames = new
        {
            batchSize = 25,
            force = false,
            provider = (string?)null,
            suggestions = new
            {
                batchSize = new[] { 10, 25, 50, 100 },
                provider = new[] { "Ollama", "OpenAI" },
                configuredProvider = provider
            }
        },
        reclassifyGender = new
        {
            batchSize = 25,
            force = false,
            filter = "UnisexOnly",
            provider = (string?)null,
            suggestions = new
            {
                batchSize = new[] { 10, 25, 50 },
                filter = new[] { "All", "UnisexOnly", "Conflict" },
                provider = new[] { "Ollama", "OpenAI" },
                configuredProvider = provider
            }
        }
    });
}).RequireAuthorization();

admin.MapGet("/enrichment-status", async (NameQueryService service, CancellationToken ct) =>
    Results.Ok(await service.GetEnrichmentStatusAsync(ct))).RequireAuthorization();

admin.MapGet("/logs", (int? limit, long? afterId, AdminLogHub hub) =>
    Results.Ok(hub.Snapshot(limit ?? 200, afterId))).RequireAuthorization();

admin.MapDelete("/logs", (AdminLogHub hub) =>
{
    hub.Clear();
    return Results.Ok(new { cleared = true });
}).RequireAuthorization();

admin.MapGet("/logs/stream", async (HttpContext http, AdminLogHub hub, CancellationToken ct) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers.Connection = "keep-alive";

    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    async Task WriteEventAsync(AdminLogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, jsonOptions);
        await http.Response.WriteAsync($"data: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    foreach (var entry in hub.Snapshot())
    {
        await WriteEventAsync(entry);
    }

    await foreach (var entry in hub.Reader.ReadAllAsync(ct))
    {
        await WriteEventAsync(entry);
    }
}).RequireAuthorization();

admin.MapPost("/import-ssa", async (SsaImportOptions? options, SsaImporter importer, AdminLogHub hub, CancellationToken ct) =>
{
    hub.Info("Import", "SSA import started.");
    var result = await importer.ImportAsync(options ?? new SsaImportOptions(), ct);
    hub.Info("Import", $"Imported {result.YearsProcessed} years, {result.NamesUpserted} names, {result.StatsUpserted} stats from {result.Source}.");
    return Results.Ok(result);
}).RequireAuthorization();

admin.MapPost("/enrich-names", async (
    EnrichNamesOptions? options,
    NameEnrichmentService enricher,
    AdminJobLock jobLock,
    AdminLogHub hub,
    CancellationToken ct) =>
{
    if (!jobLock.TryAcquire("enrich-names", out var conflict))
    {
        return Results.Conflict(new { error = $"Job already running: {conflict}" });
    }

    try
    {
        var result = await enricher.EnrichBatchAsync(options ?? new EnrichNamesOptions(), ct);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        hub.Error("Enrich", ex.Message);
        throw;
    }
    finally
    {
        jobLock.Release();
    }
}).RequireAuthorization();

admin.MapPost("/reclassify-gender", async (
    GenderRecalcOptions? options,
    GenderRecalculationService service,
    AdminJobLock jobLock,
    AdminLogHub hub,
    CancellationToken ct) =>
{
    if (!jobLock.TryAcquire("reclassify-gender", out var conflict))
    {
        return Results.Conflict(new { error = $"Job already running: {conflict}" });
    }

    try
    {
        var result = await service.RecalculateBatchAsync(options ?? new GenderRecalcOptions(), ct);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        hub.Error("GenderLLM", ex.Message);
        throw;
    }
    finally
    {
        jobLock.Release();
    }
}).RequireAuthorization();

app.Run();

static async Task RunImportCommandAsync(string[] args)
{
    var options = ParseSsaOptions(args);

    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Services.Configure<SsaImportOptions>(builder.Configuration.GetSection("SsaImport"));
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient("ssa", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BabyNamePicker/1.0 (https://github.com/local/baby-name-picker)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/zip, application/octet-stream, */*");
    });
    builder.Services.AddScoped<SsaImporter>();
    builder.Services.AddScoped<NicknameSeeder>();

    var app = builder.Build();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var importer = scope.ServiceProvider.GetRequiredService<SsaImporter>();
    var result = await importer.ImportAsync(options);

    var seeder = scope.ServiceProvider.GetRequiredService<NicknameSeeder>();
    await seeder.SeedAsync();

    Console.WriteLine($"Imported {result.YearsProcessed} years, {result.NamesUpserted} names, {result.StatsUpserted} stats from {result.Source}.");
}

static async Task RunEnrichCommandAsync(string[] args)
{
    var options = ParseEnrichOptions(args);

    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<AdminLogHub>();
    builder.Services.AddScoped<NameEnrichmentService>();

    var app = builder.Build();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var enricher = scope.ServiceProvider.GetRequiredService<NameEnrichmentService>();
    var result = await enricher.EnrichBatchAsync(options);

    Console.WriteLine($"Enrichment complete: {result.Enriched}/{result.Processed} succeeded, {result.Failed} failed.");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  - {error}");
    }
}

static async Task RunGenderRecalcCommandAsync(string[] args)
{
    var options = ParseGenderRecalcOptions(args);

    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<AdminLogHub>();
    builder.Services.AddScoped<GenderRecalculationService>();

    var app = builder.Build();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var service = scope.ServiceProvider.GetRequiredService<GenderRecalculationService>();
    var result = await service.RecalculateBatchAsync(options);

    Console.WriteLine($"Gender reclassification complete: {result.Updated}/{result.Processed} updated, {result.Failed} failed.");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  - {error}");
    }
}

static SsaImportOptions ParseSsaOptions(string[] args)
{
    var options = new SsaImportOptions();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--from" when i + 1 < args.Length && int.TryParse(args[++i], out var fromYear):
                options.FromYear = fromYear;
                break;
            case "--to" when i + 1 < args.Length && int.TryParse(args[++i], out var toYear):
                options.ToYear = toYear;
                break;
            case "--top" when i + 1 < args.Length && int.TryParse(args[++i], out var top):
                options.TopPerSex = top;
                break;
            case "--zip" when i + 1 < args.Length:
                options.ZipPath = args[++i];
                break;
            case "--dir" when i + 1 < args.Length:
                options.DataDirectory = args[++i];
                break;
        }
    }

    return options;
}

static EnrichNamesOptions ParseEnrichOptions(string[] args)
{
    var options = new EnrichNamesOptions();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--batch" when i + 1 < args.Length && int.TryParse(args[++i], out var batch):
                options.BatchSize = batch;
                break;
            case "--provider" when i + 1 < args.Length:
                options.Provider = args[++i];
                break;
            case "--force":
                options.Force = true;
                break;
        }
    }

    return options;
}

static GenderRecalcOptions ParseGenderRecalcOptions(string[] args)
{
    var options = new GenderRecalcOptions();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--batch" when i + 1 < args.Length && int.TryParse(args[++i], out var batch):
                options.BatchSize = batch;
                break;
            case "--provider" when i + 1 < args.Length:
                options.Provider = args[++i];
                break;
            case "--force":
                options.Force = true;
                break;
            case "--filter" when i + 1 < args.Length:
                options.Filter = Enum.TryParse<GenderRecalcFilter>(args[++i], ignoreCase: true, out var filter)
                    ? filter
                    : GenderRecalcFilter.UnisexOnly;
                break;
        }
    }

    return options;
}

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (!await db.Names.AnyAsync())
    {
        var importer = scope.ServiceProvider.GetRequiredService<SsaImporter>();
        var ssaOptions = scope.ServiceProvider.GetRequiredService<IOptions<SsaImportOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogInformation("Database empty; seeding SSA data (from {FromYear}, top {TopPerSex})...",
            ssaOptions.FromYear, ssaOptions.TopPerSex);
        await importer.ImportAsync(ssaOptions);
    }

    var nicknameSeeder = scope.ServiceProvider.GetRequiredService<NicknameSeeder>();
    await nicknameSeeder.SeedAsync();

    var adminUsers = scope.ServiceProvider.GetRequiredService<AdminUserService>();
    await adminUsers.EnsureAdminUserAsync();
}

// Loads repo-root .env into the process environment so local runs pick up the same
// LLM_* settings as Docker Compose. Existing environment variables win.
// Friendly LLM_* / ADMIN_* keys are mapped to ASP.NET Core configuration names.
static void LoadDotEnvFromAncestors()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var path = Path.Combine(dir.FullName, ".env");
        if (File.Exists(path))
        {
            ApplyDotEnv(path);
            return;
        }

        dir = dir.Parent;
    }
}

static void ApplyDotEnv(string path)
{
    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var eq = line.IndexOf('=');
        if (eq <= 0)
        {
            continue;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        key = key switch
        {
            "LLM_PROVIDER" => "Llm__Provider",
            "LLM_BASE_URL" => "Llm__BaseUrl",
            "LLM_MODEL" => "Llm__Model",
            "LLM_API_KEY" => "Llm__ApiKey",
            "ADMIN_PASSWORD" => "Admin__Password",
            "ADMIN_USERNAME" => "Admin__Username",
            _ => key
        };

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

sealed record LoginRequest(string? Username, string? Password);

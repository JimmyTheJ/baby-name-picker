using BabyNamePicker.Data;
using BabyNamePicker.Services;
using BabyNamePicker.Services.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SsaImportOptions>(builder.Configuration.GetSection("SsaImport"));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("ssa", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; BabyNamePicker/1.0)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/zip, application/octet-stream, */*");
});
builder.Services.AddScoped<SsaImporter>();
builder.Services.AddScoped<NicknameSeeder>();
builder.Services.AddScoped<NameQueryService>();
builder.Services.AddScoped<NameEnrichmentService>();

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseDefaultFiles();
app.UseStaticFiles();

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

api.MapGet("/admin/enrichment-status", async (NameQueryService service, CancellationToken ct) =>
    Results.Ok(await service.GetEnrichmentStatusAsync(ct)));

api.MapPost("/admin/import-ssa", async (SsaImportOptions? options, SsaImporter importer, CancellationToken ct) =>
{
    var result = await importer.ImportAsync(options ?? new SsaImportOptions(), ct);
    return Results.Ok(result);
});

api.MapPost("/admin/enrich-names", async (EnrichNamesOptions? options, NameEnrichmentService enricher, CancellationToken ct) =>
{
    var result = await enricher.EnrichBatchAsync(options ?? new EnrichNamesOptions(), ct);
    return Results.Ok(result);
});

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
}

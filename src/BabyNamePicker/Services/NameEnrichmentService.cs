using System.Text.Json;
using BabyNamePicker.Data;
using BabyNamePicker.Models;
using BabyNamePicker.Services.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BabyNamePicker.Services;

public sealed class NameEnrichmentService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<LlmOptions> llmOptions,
    ILogger<NameEnrichmentService> logger)
{
    private const string SystemPrompt = """
        You are a baby name expert. Respond with valid JSON only — no markdown fences, no commentary.
        Use this exact schema:
        {
          "meaning": "brief meaning",
          "origins": ["language or culture"],
          "pronunciation": "simple phonetic guide",
          "description": "1-2 sentence parent-friendly description",
          "themes": ["tag1", "tag2"],
          "variants": ["spelling variant"],
          "nicknames": ["nickname1", "nickname2"]
        }
        Only include real, commonly used nicknames and variants. If unsure, use empty arrays.
        """;

    public async Task<EnrichNamesResult> EnrichBatchAsync(
        EnrichNamesOptions options,
        CancellationToken cancellationToken = default)
    {
        var llm = CreateClient(options.Provider);
        var names = await GetNamesToEnrichAsync(options, cancellationToken);

        var enriched = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await EnrichNameAsync(name, llm, cancellationToken);
                enriched++;
            }
            catch (Exception ex)
            {
                failed++;
                var message = $"{name.Name}: {ex.Message}";
                errors.Add(message);
                logger.LogWarning(ex, "Failed to enrich name {Name}", name.Name);
            }
        }

        return new EnrichNamesResult(names.Count, enriched, failed, errors);
    }

    private async Task<List<BabyName>> GetNamesToEnrichAsync(
        EnrichNamesOptions options,
        CancellationToken cancellationToken)
    {
        var query = db.Names
            .Include(n => n.Metadata)
            .Include(n => n.Nicknames)
            .AsQueryable();

        if (!options.Force)
        {
            query = query.Where(n => n.Metadata == null);
        }

        return await query
            .OrderBy(n => n.Name)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task EnrichNameAsync(BabyName name, ILlmClient llm, CancellationToken cancellationToken)
    {
        var genderHint = name.Gender switch
        {
            BabyGender.Male => "boy",
            BabyGender.Female => "girl",
            _ => "unisex"
        };

        var userPrompt = $"Enrich the baby name \"{name.Name}\" (typically a {genderHint} name).";
        var raw = await llm.CompleteAsync(SystemPrompt, userPrompt, cancellationToken);
        var parsed = ParseEnrichmentResponse(raw);

        var source = $"{llmOptions.Value.Provider}:{llmOptions.Value.Model}";
        if (name.Metadata is null)
        {
            name.Metadata = new NameMetadata { NameId = name.Id };
            db.NameMetadata.Add(name.Metadata);
        }

        name.Metadata.Meaning = parsed.Meaning;
        name.Metadata.Origins = JsonSerializer.Serialize(parsed.Origins);
        name.Metadata.Pronunciation = parsed.Pronunciation;
        name.Metadata.Description = parsed.Description;
        name.Metadata.Themes = JsonSerializer.Serialize(parsed.Themes);
        name.Metadata.Variants = JsonSerializer.Serialize(parsed.Variants);
        name.Metadata.EnrichmentSource = source;
        name.Metadata.EnrichedAt = DateTime.UtcNow;
        name.UpdatedAt = DateTime.UtcNow;

        await AddNicknamesAsync(name, parsed.Nicknames, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddNicknamesAsync(
        BabyName name,
        IReadOnlyList<string> nicknames,
        CancellationToken cancellationToken)
    {
        foreach (var value in nicknames.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            var trimmed = value.Trim();
            if (trimmed.Equals(name.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nickname = await db.Nicknames.FirstOrDefaultAsync(n => n.Value == trimmed, cancellationToken);
            if (nickname is null)
            {
                nickname = new Nickname { Value = trimmed };
                db.Nicknames.Add(nickname);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (name.Nicknames.All(n => n.Id != nickname.Id))
            {
                name.Nicknames.Add(nickname);
            }
        }
    }

    private static EnrichmentPayload ParseEnrichmentResponse(string raw)
    {
        var json = ExtractJson(raw);
        var parsed = JsonSerializer.Deserialize<EnrichmentPayload>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return parsed ?? new EnrichmentPayload();
    }

    private static string ExtractJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('\n') + 1;
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start)
            {
                trimmed = trimmed[start..end].Trim();
            }
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            return trimmed[objectStart..(objectEnd + 1)];
        }

        throw new InvalidOperationException("LLM response did not contain JSON.");
    }

    private ILlmClient CreateClient(string? providerOverride)
    {
        var options = llmOptions.Value;
        if (!string.IsNullOrWhiteSpace(providerOverride))
        {
            options = new LlmOptions
            {
                Provider = providerOverride,
                BaseUrl = options.BaseUrl,
                Model = options.Model,
                ApiKey = options.ApiKey
            };
        }

        return options.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiLlmClient(httpClientFactory, options),
            _ => new OllamaLlmClient(httpClientFactory, options)
        };
    }

    private sealed class EnrichmentPayload
    {
        public string? Meaning { get; set; }
        public List<string> Origins { get; set; } = [];
        public string? Pronunciation { get; set; }
        public string? Description { get; set; }
        public List<string> Themes { get; set; } = [];
        public List<string> Variants { get; set; } = [];
        public List<string> Nicknames { get; set; } = [];
    }
}

using System.Text.Json;
using BabyNamePicker.Data;
using BabyNamePicker.Models;
using BabyNamePicker.Services.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BabyNamePicker.Services;

public sealed class GenderRecalculationService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<LlmOptions> llmOptions,
    AdminLogHub logHub,
    ILogger<GenderRecalculationService> logger)
{
    private const string SystemPrompt = """
        You are a baby-name gender perception expert. Respond with a single valid JSON object only — no markdown fences, no commentary.
        Rules:
        - Use double quotes for all keys and string values.
        - Put commas between every property.
        - Do not put raw line breaks inside strings.
        Use this exact schema:
        {
          "gender": "Male" | "Female" | "Unisex",
          "maleShare": 0.0,
          "rationale": "brief cultural reason"
        }
        Classify by how the name is typically perceived culturally / linguistically for a newborn today,
        not by historical SSA birth counts alone. SSA stats are context only and may be culturally misleading.
        maleShare must be between 0 and 1 (1 = strongly boy-associated, 0 = strongly girl-associated, ~0.5 = balanced unisex).
        """;

    private const int MaxAttempts = 2;

    public async Task<GenderRecalcResult> RecalculateBatchAsync(
        GenderRecalcOptions options,
        CancellationToken cancellationToken = default)
    {
        var clientOptions = ResolveOptions(options.Provider);
        var llm = CreateClient(clientOptions);
        var names = await GetNamesAsync(options, cancellationToken);

        logHub.Info("GenderLLM", $"Starting gender reclassification: {names.Count} name(s), filter={options.Filter}, force={options.Force}");

        var updated = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RecalculateNameAsync(name, llm, clientOptions, cancellationToken);
                updated++;
                logHub.Info("GenderLLM", $"Updated {name.Name} → {name.Gender} (maleShare={name.MaleShare:F2})");
            }
            catch (Exception ex)
            {
                failed++;
                var message = $"{name.Name}: {ex.Message}";
                errors.Add(message);
                logger.LogWarning(ex, "Failed to reclassify gender for {Name}", name.Name);
                logHub.Warning("GenderLLM", message);
            }
        }

        logHub.Info("GenderLLM", $"Complete: {updated}/{names.Count} updated, {failed} failed.");
        return new GenderRecalcResult(names.Count, updated, failed, errors);
    }

    private async Task<List<BabyName>> GetNamesAsync(
        GenderRecalcOptions options,
        CancellationToken cancellationToken)
    {
        var query = db.Names.AsQueryable();

        if (!options.Force)
        {
            query = query.Where(n => n.GenderSource == null || !n.GenderSource.StartsWith("llm:"));
        }

        query = options.Filter switch
        {
            GenderRecalcFilter.UnisexOnly => query.Where(n => n.Gender == BabyGender.Unisex),
            GenderRecalcFilter.Conflict => query.Where(n =>
                n.MaleShare >= 0.10 && n.MaleShare <= 0.90 &&
                Math.Min(n.MaleShare, 1 - n.MaleShare) >= 0.10 &&
                Math.Min(n.MaleShare, 1 - n.MaleShare) <= 0.25),
            _ => query
        };

        return await query
            .OrderBy(n => n.Name)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task RecalculateNameAsync(
        BabyName name,
        ILlmClient llm,
        LlmOptions clientOptions,
        CancellationToken cancellationToken)
    {
        var recent = await db.NameYearStats
            .Where(s => s.NameId == name.Id)
            .GroupBy(s => s.Sex)
            .Select(g => new { Sex = g.Key, Total = g.Sum(x => x.Count) })
            .ToListAsync(cancellationToken);

        var maleCount = recent.FirstOrDefault(s => s.Sex == SsaSex.Male)?.Total ?? 0;
        var femaleCount = recent.FirstOrDefault(s => s.Sex == SsaSex.Female)?.Total ?? 0;

        var userPrompt = $"""
            Reclassify the baby name "{name.Name}".
            Current SSA-derived classification: gender={name.Gender}, maleShare={name.MaleShare:F3}.
            Lifetime SSA counts: male={maleCount}, female={femaleCount}.
            Return culturally perceived gender and maleShare.
            """;

        var parsed = await CompleteAndParseAsync(llm, name.Name, userPrompt, cancellationToken);
        var gender = ParseGender(parsed.Gender);
        var maleShare = Math.Clamp(parsed.MaleShare, 0, 1);

        name.Gender = gender;
        name.MaleShare = maleShare;
        name.GenderSource = $"llm:{clientOptions.Provider}:{clientOptions.Model}";
        name.GenderEnrichedAt = DateTime.UtcNow;
        name.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<GenderPayload> CompleteAndParseAsync(
        ILlmClient llm,
        string name,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        string? lastRaw = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = attempt == 1
                ? userPrompt
                : $"""
                  Your previous reply for "{name}" was not valid JSON.
                  Reply again with ONLY one JSON object matching the schema. No markdown, no commentary.
                  Previous reply was:
                  {Truncate(lastRaw, 1200)}
                  """;

            lastRaw = await llm.CompleteAsync(SystemPrompt, prompt, cancellationToken);

            try
            {
                var parsed = LlmJson.Deserialize<GenderPayload>(lastRaw);
                if (string.IsNullOrWhiteSpace(parsed.Gender))
                {
                    throw new InvalidOperationException("Missing gender field.");
                }

                ParseGender(parsed.Gender);
                return parsed;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                lastError = ex;
                logger.LogDebug(
                    ex,
                    "Gender JSON parse failed for {Name} (attempt {Attempt}/{Max})",
                    name,
                    attempt,
                    MaxAttempts);
            }
        }

        throw lastError ?? new InvalidOperationException("LLM response did not contain JSON.");
    }

    private static BabyGender ParseGender(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Gender is empty.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "male" or "boy" or "m" => BabyGender.Male,
            "female" or "girl" or "f" => BabyGender.Female,
            "unisex" or "neutral" or "both" => BabyGender.Unisex,
            _ => throw new InvalidOperationException($"Unknown gender value '{value}'.")
        };
    }

    private static string Truncate(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value ?? string.Empty;
        }

        return value[..maxChars] + "…";
    }

    private ILlmClient CreateClient(LlmOptions options) =>
        options.Provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiLlmClient(httpClientFactory, options),
            _ => new OllamaLlmClient(httpClientFactory, options)
        };

    private LlmOptions ResolveOptions(string? providerOverride)
    {
        var options = llmOptions.Value;
        if (string.IsNullOrWhiteSpace(providerOverride))
        {
            return options;
        }

        return new LlmOptions
        {
            Provider = providerOverride,
            BaseUrl = options.BaseUrl,
            Model = options.Model,
            ApiKey = options.ApiKey
        };
    }

    private sealed class GenderPayload
    {
        public string? Gender { get; set; }
        public double MaleShare { get; set; } = 0.5;
        public string? Rationale { get; set; }
    }
}

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BabyNamePicker.Services.Llm;

/// <summary>
/// Extracts and lightly repairs JSON from messy LLM responses.
/// </summary>
internal static partial class LlmJson
{
    public static T Deserialize<T>(string raw, JsonSerializerOptions? options = null)
    {
        options ??= new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var candidates = BuildCandidates(raw);
        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<T>(candidate, options);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (JsonException ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("LLM response did not contain JSON.");
    }

    private static IEnumerable<string> BuildCandidates(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var extracted = ExtractJsonObject(raw);
        if (extracted is null)
        {
            yield break;
        }

        yield return extracted;

        var repaired = Repair(extracted);
        if (!string.Equals(repaired, extracted, StringComparison.Ordinal))
        {
            yield return repaired;
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        var trimmed = raw.Trim();

        // Strip markdown fences (```json ... ``` or ``` ... ```).
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && endFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..endFence].Trim();
            }
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart < 0 || objectEnd <= objectStart)
        {
            return null;
        }

        return trimmed[objectStart..(objectEnd + 1)];
    }

    private static string Repair(string json)
    {
        var result = EscapeControlCharsInStrings(json);
        result = StripTrailingCommas().Replace(result, "$1");
        result = InsertMissingCommas(result);
        return result;
    }

    /// <summary>
    /// Escapes raw control characters inside JSON string values (common LLM mistake).
    /// </summary>
    private static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 32);
        var inString = false;
        var escaped = false;

        foreach (var ch in json)
        {
            if (!inString)
            {
                if (ch == '"')
                {
                    inString = true;
                }

                sb.Append(ch);
                continue;
            }

            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            switch (ch)
            {
                case '\\':
                    sb.Append(ch);
                    escaped = true;
                    break;
                case '"':
                    sb.Append(ch);
                    inString = false;
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append($"\\u{(int)ch:X4}");
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Inserts commas when the model omits them between properties/array elements.
    /// </summary>
    private static string InsertMissingCommas(string json)
    {
        var sb = new StringBuilder(json.Length + 16);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];

            if (inString)
            {
                sb.Append(ch);
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
            }

            if (IsValueStart(ch) && NeedsCommaBefore(sb))
            {
                sb.Append(',');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool NeedsCommaBefore(StringBuilder sb)
    {
        for (var i = sb.Length - 1; i >= 0; i--)
        {
            var prev = sb[i];
            if (char.IsWhiteSpace(prev))
            {
                continue;
            }

            // Strings/objects/arrays are the values in our enrichment schema.
            // Avoid treating digits/'e' as terminators — that breaks numbers like 1e10.
            return prev is '"' or '}' or ']';
        }

        return false;
    }

    private static bool IsValueStart(char ch) =>
        ch is '"' or '{' or '[' or 't' or 'f' or 'n' or '-' || char.IsDigit(ch);

    [GeneratedRegex(@",\s*([}\]])", RegexOptions.CultureInvariant)]
    private static partial Regex StripTrailingCommas();
}

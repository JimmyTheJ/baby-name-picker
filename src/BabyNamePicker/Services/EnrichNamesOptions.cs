namespace BabyNamePicker.Services;

public sealed class EnrichNamesOptions
{
    public int BatchSize { get; set; } = 25;
    public bool Force { get; set; }
    public string? Provider { get; set; }
}

public sealed record EnrichNamesResult(
    int Processed,
    int Enriched,
    int Failed,
    IReadOnlyList<string> Errors);

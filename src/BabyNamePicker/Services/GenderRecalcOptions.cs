namespace BabyNamePicker.Services;

public enum GenderRecalcFilter
{
    All = 0,
    UnisexOnly = 1,
    /// <summary>Names near the SSA unisex threshold (minority share 10–25%).</summary>
    Conflict = 2
}

public sealed class GenderRecalcOptions
{
    public int BatchSize { get; set; } = 25;
    public bool Force { get; set; }
    public string? Provider { get; set; }
    public GenderRecalcFilter Filter { get; set; } = GenderRecalcFilter.UnisexOnly;
}

public sealed record GenderRecalcResult(
    int Processed,
    int Updated,
    int Failed,
    IReadOnlyList<string> Errors);

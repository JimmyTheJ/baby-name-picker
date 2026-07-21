namespace BabyNamePicker.Services;

public sealed class SsaImportOptions
{
    public int FromYear { get; set; } = 1950;
    public int? ToYear { get; set; }
    public int TopPerSex { get; set; } = 100;
    public string? ZipPath { get; set; }
    public string? DataDirectory { get; set; }
    public bool DownloadIfMissing { get; set; } = true;
}

public sealed record SsaImportResult(
    int YearsProcessed,
    int NamesUpserted,
    int StatsUpserted,
    string Source);

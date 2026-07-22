using BabyNamePicker.Models;

namespace BabyNamePicker.Models.Dtos;

public record NameSummaryDto(
    int Id,
    string Name,
    string Gender,
    double MaleShare,
    int? PeakRank,
    IReadOnlyList<string> Nicknames);

public record NameDetailDto(
    int Id,
    string Name,
    string Gender,
    double MaleShare,
    int? PeakRank,
    IReadOnlyList<string> Nicknames,
    IReadOnlyList<YearStatDto> YearStats,
    NameMetadataDto? Metadata);

public record NameMetadataDto(
    string? Meaning,
    IReadOnlyList<string> Origins,
    string? Pronunciation,
    string? Description,
    IReadOnlyList<string> Themes,
    IReadOnlyList<string> Variants,
    string EnrichmentSource,
    DateTime EnrichedAt);

public record YearStatDto(int Year, string Sex, int Rank, int Count);

public record PopularityEntryDto(int Rank, string Name, string Gender, double MaleShare, int Count);

public record EnrichmentStatusDto(
    int TotalNames,
    int EnrichedNames,
    int PendingNames);

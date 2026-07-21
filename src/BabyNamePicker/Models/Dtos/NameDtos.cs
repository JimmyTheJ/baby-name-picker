using BabyNamePicker.Models;

namespace BabyNamePicker.Models.Dtos;

public record NameSummaryDto(
    int Id,
    string Name,
    string Gender,
    double MaleShare,
    IReadOnlyList<string> Nicknames);

public record NameDetailDto(
    int Id,
    string Name,
    string Gender,
    double MaleShare,
    IReadOnlyList<string> Nicknames,
    IReadOnlyList<YearStatDto> YearStats);

public record YearStatDto(int Year, string Sex, int Rank, int Count);

public record PopularityEntryDto(int Rank, string Name, string Gender, double MaleShare, int Count);

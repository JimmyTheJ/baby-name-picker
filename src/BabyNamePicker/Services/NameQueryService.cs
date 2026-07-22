using System.Text.Json;
using BabyNamePicker.Data;
using BabyNamePicker.Models;
using BabyNamePicker.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BabyNamePicker.Services;

public sealed class NameQueryService(AppDbContext db)
{
    public async Task<IReadOnlyList<NameSummaryDto>> SearchAsync(
        string? query,
        string? gender,
        string? rarity,
        CancellationToken cancellationToken = default)
    {
        var names = db.Names
            .AsNoTracking()
            .Include(n => n.Nicknames)
            .Include(n => n.YearStats)
            .AsQueryable();

        names = ApplyGenderFilter(names, gender);
        names = ApplyRarityFilter(names, rarity);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            names = names.Where(n =>
                EF.Functions.Like(n.Name, $"%{term}%") ||
                n.Nicknames.Any(nn => EF.Functions.Like(nn.Value, $"%{term}%")));
        }

        var results = await names
            .OrderBy(n => n.Name)
            .Take(100)
            .ToListAsync(cancellationToken);

        return results.Select(ToSummary).ToList();
    }

    public async Task<NameDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var name = await db.Names
            .AsNoTracking()
            .Include(n => n.Nicknames)
            .Include(n => n.YearStats)
            .Include(n => n.Metadata)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        return name is null ? null : ToDetail(name);
    }

    public async Task<NameDetailDto?> GetRandomAsync(
        string? query,
        string? gender,
        string? rarity,
        CancellationToken cancellationToken = default)
    {
        var names = db.Names
            .AsNoTracking()
            .Include(n => n.Nicknames)
            .Include(n => n.YearStats)
            .AsQueryable();
        names = ApplyGenderFilter(names, gender);
        names = ApplyRarityFilter(names, rarity);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            names = names.Where(n =>
                EF.Functions.Like(n.Name, $"%{term}%") ||
                n.Nicknames.Any(nn => EF.Functions.Like(nn.Value, $"%{term}%")));
        }

        var count = await names.CountAsync(cancellationToken);
        if (count == 0)
        {
            return null;
        }

        var skip = Random.Shared.Next(count);
        var id = await names.OrderBy(n => n.Id).Skip(skip).Select(n => n.Id).FirstAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken cancellationToken = default)
    {
        return await db.NameYearStats
            .AsNoTracking()
            .Select(s => s.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PopularityEntryDto>> GetPopularityAsync(
        int year,
        string? gender,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var cappedLimit = Math.Clamp(limit, 1, 1000);
        var stats = db.NameYearStats
            .AsNoTracking()
            .Include(s => s.Name)
            .Where(s => s.Year == year);

        stats = gender?.ToLowerInvariant() switch
        {
            "male" or "boy" => stats.Where(s => s.Sex == SsaSex.Male),
            "female" or "girl" => stats.Where(s => s.Sex == SsaSex.Female),
            _ => stats
        };

        var rows = await stats
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.Name.Name)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);

        return rows.Select(s => new PopularityEntryDto(
            s.Rank,
            s.Name.Name,
            s.Name.Gender.ToString(),
            s.Name.MaleShare,
            s.Count)).ToList();
    }

    public async Task<EnrichmentStatusDto> GetEnrichmentStatusAsync(CancellationToken cancellationToken = default)
    {
        var total = await db.Names.CountAsync(cancellationToken);
        var enriched = await db.NameMetadata.CountAsync(cancellationToken);
        return new EnrichmentStatusDto(total, enriched, total - enriched);
    }

    private static IQueryable<BabyName> ApplyGenderFilter(IQueryable<BabyName> query, string? gender)
    {
        return gender?.ToLowerInvariant() switch
        {
            "male" or "boy" => query.Where(n =>
                n.Gender == BabyGender.Male ||
                (n.Gender == BabyGender.Unisex && n.MaleShare >= 0.5)),
            "female" or "girl" => query.Where(n =>
                n.Gender == BabyGender.Female ||
                (n.Gender == BabyGender.Unisex && n.MaleShare < 0.5)),
            "unisex" => query.Where(n => n.Gender == BabyGender.Unisex),
            _ => query
        };
    }

    private static IQueryable<BabyName> ApplyRarityFilter(IQueryable<BabyName> query, string? rarity)
    {
        return rarity?.ToLowerInvariant() switch
        {
            "popular" => query.Where(n => n.YearStats.Any() && n.YearStats.Min(s => s.Rank) <= 100),
            "familiar" => query.Where(n => n.YearStats.Any() &&
                n.YearStats.Min(s => s.Rank) > 100 &&
                n.YearStats.Min(s => s.Rank) <= 500),
            "uncommon" => query.Where(n => n.YearStats.Any() && n.YearStats.Min(s => s.Rank) > 500),
            _ => query
        };
    }

    private static int? GetPeakRank(BabyName name) =>
        name.YearStats.Count == 0 ? null : name.YearStats.Min(s => s.Rank);

    private static NameSummaryDto ToSummary(BabyName name) =>
        new(name.Id, name.Name, name.Gender.ToString(), name.MaleShare, GetPeakRank(name),
            name.Nicknames.Select(n => n.Value).OrderBy(v => v).ToList());

    private static NameDetailDto ToDetail(BabyName name) =>
        new(name.Id, name.Name, name.Gender.ToString(), name.MaleShare, GetPeakRank(name),
            name.Nicknames.Select(n => n.Value).OrderBy(v => v).ToList(),
            name.YearStats
                .OrderByDescending(s => s.Year)
                .ThenBy(s => s.Sex)
                .Select(s => new YearStatDto(s.Year, s.Sex == SsaSex.Male ? "Male" : "Female", s.Rank, s.Count))
                .ToList(),
            name.Metadata is null ? null : ToMetadataDto(name.Metadata));

    private static NameMetadataDto ToMetadataDto(NameMetadata metadata) =>
        new(
            metadata.Meaning,
            DeserializeList(metadata.Origins),
            metadata.Pronunciation,
            metadata.Description,
            DeserializeList(metadata.Themes),
            DeserializeList(metadata.Variants),
            metadata.EnrichmentSource,
            metadata.EnrichedAt);

    private static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [json];
        }
    }
}

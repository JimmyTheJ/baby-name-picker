using System.IO.Compression;
using BabyNamePicker.Data;
using BabyNamePicker.Models;
using Microsoft.EntityFrameworkCore;

namespace BabyNamePicker.Services;

public sealed class SsaImporter(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IWebHostEnvironment env,
    ILogger<SsaImporter> logger)
{
    private const string SsaZipUrl = "https://www.ssa.gov/oact/babynames/names.zip";

    public async Task<SsaImportResult> ImportAsync(SsaImportOptions options, CancellationToken cancellationToken = default)
    {
        var source = await ResolveSourceAsync(options, cancellationToken);
        var nameIds = await db.Names.ToDictionaryAsync(
            n => n.Name,
            n => n.Id,
            StringComparer.OrdinalIgnoreCase,
            cancellationToken);

        var yearsProcessed = 0;
        var statsUpserted = 0;
        var namesTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (year, lines) in source.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yearsProcessed++;

            var maleRank = 0;
            var femaleRank = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 3)
                {
                    continue;
                }

                var rawName = parts[0].Trim();
                var sex = parts[1].Trim().ToUpperInvariant();
                if (!int.TryParse(parts[2].Trim(), out var count))
                {
                    continue;
                }

                var rank = sex == "M" ? ++maleRank : ++femaleRank;
                if (rank > options.TopPerSex)
                {
                    continue;
                }

                if (!nameIds.TryGetValue(rawName, out var nameId))
                {
                    var created = new BabyName
                    {
                        Name = rawName,
                        Gender = sex == "M" ? BabyGender.Male : BabyGender.Female,
                        MaleShare = sex == "M" ? 1 : 0
                    };
                    db.Names.Add(created);
                    await db.SaveChangesAsync(cancellationToken);
                    nameId = created.Id;
                    nameIds[rawName] = nameId;
                }

                namesTouched.Add(rawName);

                var ssaSex = sex == "M" ? SsaSex.Male : SsaSex.Female;
                var existing = await db.NameYearStats
                    .FirstOrDefaultAsync(s => s.NameId == nameId && s.Year == year && s.Sex == ssaSex, cancellationToken);

                if (existing is null)
                {
                    db.NameYearStats.Add(new NameYearStat
                    {
                        NameId = nameId,
                        Year = year,
                        Sex = ssaSex,
                        Rank = rank,
                        Count = count
                    });
                }
                else
                {
                    existing.Rank = rank;
                    existing.Count = count;
                }

                statsUpserted++;
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Imported SSA data for {Year}", year);
        }

        await RecomputeGenderAsync(namesTouched, cancellationToken);

        return new SsaImportResult(yearsProcessed, namesTouched.Count, statsUpserted, source.Label);
    }

    private async Task RecomputeGenderAsync(HashSet<string> namesTouched, CancellationToken cancellationToken)
    {
        foreach (var name in namesTouched)
        {
            var stats = await db.NameYearStats
                .Where(s => s.Name.Name == name)
                .GroupBy(s => s.Sex)
                .Select(g => new { Sex = g.Key, Total = g.Sum(x => x.Count) })
                .ToListAsync(cancellationToken);

            var maleCount = stats.FirstOrDefault(s => s.Sex == SsaSex.Male)?.Total ?? 0;
            var femaleCount = stats.FirstOrDefault(s => s.Sex == SsaSex.Female)?.Total ?? 0;
            var (gender, maleShare) = GenderClassifier.Classify(maleCount, femaleCount);

            var entity = await db.Names.FirstAsync(n => n.Name == name, cancellationToken);
            entity.Gender = gender;
            entity.MaleShare = maleShare;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SsaSource> ResolveSourceAsync(SsaImportOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.DataDirectory) && Directory.Exists(options.DataDirectory))
        {
            return LoadFromDirectory(options.DataDirectory, options);
        }

        var candidateZips = new[]
        {
            options.ZipPath,
            Path.Combine(env.ContentRootPath, "data", "names.zip"),
            Path.Combine(env.ContentRootPath, "..", "..", "data", "names.zip"),
            Path.Combine(Path.GetTempPath(), "baby-name-picker", "names.zip")
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct().ToList();

        foreach (var zipPath in candidateZips)
        {
            if (File.Exists(zipPath!))
            {
                return LoadFromZip(zipPath!, options);
            }
        }

        if (options.DownloadIfMissing)
        {
            var downloadPath = Path.Combine(Path.GetTempPath(), "baby-name-picker", "names.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);

            try
            {
                logger.LogInformation("Downloading SSA names.zip from {Url}", SsaZipUrl);
                var client = httpClientFactory.CreateClient("ssa");
                await using var stream = await client.GetStreamAsync(SsaZipUrl, cancellationToken);
                await using var file = File.Create(downloadPath);
                await stream.CopyToAsync(file, cancellationToken);
                return LoadFromZip(downloadPath, options);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to download SSA names.zip");
            }
        }

        var sampleDir = ResolveSampleDirectory();
        if (sampleDir is not null)
        {
            logger.LogWarning("Falling back to bundled SSA sample data at {Path}", sampleDir);
            return LoadFromDirectory(sampleDir, options);
        }

        throw new FileNotFoundException(
            "SSA data not found. Place names.zip in data/names.zip, pass --zip PATH, or pass --dir PATH to a folder of yobYYYY.txt files.");
    }

    private string? ResolveSampleDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "data", "ssa-sample"),
            Path.Combine(env.ContentRootPath, "..", "..", "data", "ssa-sample")
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static SsaSource LoadFromZip(string zipPath, SsaImportOptions options)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var files = archive.Entries
            .Where(e => e.Name.StartsWith("yob", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .Select(e => new
            {
                Entry = e,
                Year = int.Parse(e.Name[3..7])
            })
            .Where(x => x.Year >= options.FromYear && (!options.ToYear.HasValue || x.Year <= options.ToYear.Value))
            .OrderBy(x => x.Year)
            .Select(x =>
            {
                using var stream = x.Entry.Open();
                using var reader = new StreamReader(stream);
                return (x.Year, Lines: reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            })
            .ToList();

        return new SsaSource($"zip:{zipPath}", files);
    }

    private static SsaSource LoadFromDirectory(string directory, SsaImportOptions options)
    {
        var files = Directory.GetFiles(directory, "yob*.txt")
            .Select(path => new
            {
                Path = path,
                Year = int.Parse(Path.GetFileName(path)[3..7])
            })
            .Where(x => x.Year >= options.FromYear && (!options.ToYear.HasValue || x.Year <= options.ToYear.Value))
            .OrderBy(x => x.Year)
            .Select(x => (x.Year, Lines: File.ReadAllLines(x.Path)))
            .ToList();

        return new SsaSource($"dir:{directory}", files);
    }

    private sealed record SsaSource(string Label, List<(int Year, string[] Lines)> Files);
}

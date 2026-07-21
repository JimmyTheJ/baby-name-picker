using System.Text.Json;
using BabyNamePicker.Data;
using Microsoft.EntityFrameworkCore;

namespace BabyNamePicker.Services;

public sealed class NicknameSeeder(AppDbContext db, IWebHostEnvironment env, ILogger<NicknameSeeder> logger)
{
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(env.ContentRootPath, "data", "nicknames.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(env.ContentRootPath, "..", "..", "data", "nicknames.json");
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("Nickname seed file not found at {Path}", path);
            return 0;
        }

        await using var stream = File.OpenRead(path);
        var mappings = await JsonSerializer.DeserializeAsync<List<NicknameMapping>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? [];

        var linksCreated = 0;
        foreach (var mapping in mappings)
        {
            var name = await db.Names
                .Include(n => n.Nicknames)
                .FirstOrDefaultAsync(n => n.Name.ToLower() == mapping.Name.ToLower(), cancellationToken);
            if (name is null)
            {
                continue;
            }

            foreach (var nicknameValue in mapping.Nicknames)
            {
                var nickname = await db.Nicknames.FirstOrDefaultAsync(n => n.Value == nicknameValue, cancellationToken);
                if (nickname is null)
                {
                    nickname = new Models.Nickname { Value = nicknameValue };
                    db.Nicknames.Add(nickname);
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (!name.Nicknames.Any(n => n.Id == nickname.Id))
                {
                    name.Nicknames.Add(nickname);
                    linksCreated++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} nickname links", linksCreated);
        return linksCreated;
    }

    private sealed record NicknameMapping(string Name, List<string> Nicknames);
}

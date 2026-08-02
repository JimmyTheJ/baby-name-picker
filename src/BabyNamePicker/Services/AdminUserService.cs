using BabyNamePicker.Data;
using BabyNamePicker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BabyNamePicker.Services;

public sealed class AdminUserService(
    AppDbContext db,
    IOptions<AdminOptions> options,
    ILogger<AdminUserService> logger)
{
    // Precomputed hash used only to burn constant time when no user / admin disabled.
    private static readonly string DummyHash = PasswordHasher.HashPassword("dummy-password-for-timing");

    public bool IsEnabled => options.Value.IsEnabled;

    public async Task EnsureAdminUserAsync(CancellationToken cancellationToken = default)
    {
        var adminOptions = options.Value;
        if (!adminOptions.IsEnabled)
        {
            logger.LogInformation("Admin password not configured; admin account will not be created.");
            return;
        }

        var username = string.IsNullOrWhiteSpace(adminOptions.Username)
            ? "admin"
            : adminOptions.Username.Trim();

        var password = adminOptions.Password!.Trim();
        var hash = PasswordHasher.HashPassword(password);

        var existing = await db.AdminUsers
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (existing is null)
        {
            db.AdminUsers.Add(new AdminUser
            {
                Username = username,
                PasswordHash = hash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created admin user '{Username}'.", username);
            return;
        }

        if (!PasswordHasher.VerifyPassword(existing.PasswordHash, password))
        {
            existing.PasswordHash = hash;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Synced admin password for '{Username}' from configuration.", username);
        }
    }

    public async Task<AdminUser?> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            _ = PasswordHasher.VerifyPassword(DummyHash, password ?? string.Empty);
            return null;
        }

        var user = await db.AdminUsers
            .FirstOrDefaultAsync(u => u.Username == username.Trim(), cancellationToken);

        if (user is null)
        {
            _ = PasswordHasher.VerifyPassword(DummyHash, password);
            return null;
        }

        return PasswordHasher.VerifyPassword(user.PasswordHash, password) ? user : null;
    }
}

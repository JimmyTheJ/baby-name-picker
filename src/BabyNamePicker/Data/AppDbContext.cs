using BabyNamePicker.Models;
using Microsoft.EntityFrameworkCore;

namespace BabyNamePicker.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BabyName> Names => Set<BabyName>();
    public DbSet<Nickname> Nicknames => Set<Nickname>();
    public DbSet<NameYearStat> NameYearStats => Set<NameYearStat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BabyName>(entity =>
        {
            entity.HasIndex(n => n.Name).IsUnique();
            entity.Property(n => n.Name).HasMaxLength(64);
            entity.Property(n => n.MaleShare).HasPrecision(5, 4);
        });

        modelBuilder.Entity<Nickname>(entity =>
        {
            entity.HasIndex(n => n.Value).IsUnique();
            entity.Property(n => n.Value).HasMaxLength(64);
        });

        modelBuilder.Entity<NameYearStat>(entity =>
        {
            entity.HasIndex(s => new { s.NameId, s.Year, s.Sex }).IsUnique();
            entity.HasOne(s => s.Name)
                .WithMany(n => n.YearStats)
                .HasForeignKey(s => s.NameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BabyName>()
            .HasMany(n => n.Nicknames)
            .WithMany(n => n.Names);
    }
}

namespace BabyNamePicker.Models;

public class BabyName
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public BabyGender Gender { get; set; }
    public double MaleShare { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Nickname> Nicknames { get; set; } = [];
    public ICollection<NameYearStat> YearStats { get; set; } = [];
    public NameMetadata? Metadata { get; set; }
}

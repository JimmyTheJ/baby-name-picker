namespace BabyNamePicker.Models;

public class NameMetadata
{
    public int Id { get; set; }
    public int NameId { get; set; }
    public string? Meaning { get; set; }
    public string? Origins { get; set; }
    public string? Pronunciation { get; set; }
    public string? Description { get; set; }
    public string? Themes { get; set; }
    public string? Variants { get; set; }
    public string EnrichmentSource { get; set; } = string.Empty;
    public DateTime EnrichedAt { get; set; } = DateTime.UtcNow;

    public BabyName Name { get; set; } = null!;
}

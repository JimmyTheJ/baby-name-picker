namespace BabyNamePicker.Models;

public class NameYearStat
{
    public int Id { get; set; }
    public int NameId { get; set; }
    public BabyName Name { get; set; } = null!;
    public int Year { get; set; }
    public SsaSex Sex { get; set; }
    public int Rank { get; set; }
    public int Count { get; set; }
}

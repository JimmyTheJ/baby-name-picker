namespace BabyNamePicker.Models;

public class Nickname
{
    public int Id { get; set; }
    public required string Value { get; set; }

    public ICollection<BabyName> Names { get; set; } = [];
}

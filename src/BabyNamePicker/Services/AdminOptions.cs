namespace BabyNamePicker.Services;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "admin";
    public string? Password { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Password);
}

namespace BabyNamePicker.Services.Llm;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "Ollama";
    public string? BaseUrl { get; set; }
    public string Model { get; set; } = "llama3.2";
    public string? ApiKey { get; set; }
}

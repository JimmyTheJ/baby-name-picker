namespace BabyNamePicker.Services.Llm;

public interface ILlmClient
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

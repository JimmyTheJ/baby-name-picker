using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BabyNamePicker.Services.Llm;

public sealed class OllamaLlmClient(IHttpClientFactory httpClientFactory, LlmOptions options) : ILlmClient
{
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = (options.BaseUrl ?? "http://localhost:11434").TrimEnd('/');
        var client = httpClientFactory.CreateClient();
        var request = new OllamaChatRequest(
            options.Model,
            [
                new OllamaMessage("system", systemPrompt),
                new OllamaMessage("user", userPrompt)
            ],
            false);

        using var response = await client.PostAsJsonAsync($"{baseUrl}/api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
        return result?.Message?.Content?.Trim() ?? string.Empty;
    }

    private sealed record OllamaChatRequest(
        string Model,
        OllamaMessage[] Messages,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BabyNamePicker.Services.Llm;

public sealed class OpenAiLlmClient(IHttpClientFactory httpClientFactory, LlmOptions options) : ILlmClient
{
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured. Set Llm:ApiKey.");
        }

        var baseUrl = (options.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var request = new OpenAiChatRequest(
            options.Model,
            [
                new OpenAiMessage("system", systemPrompt),
                new OpenAiMessage("user", userPrompt)
            ]);

        using var response = await client.PostAsJsonAsync($"{baseUrl}/chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken);
        return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
    }

    private sealed record OpenAiChatRequest(string Model, OpenAiMessage[] Messages);

    private sealed record OpenAiMessage(string Role, string Content);

    private sealed class OpenAiChatResponse
    {
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }
}

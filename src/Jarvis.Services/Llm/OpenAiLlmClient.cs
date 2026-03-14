using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Llm;

/// <summary>
/// OpenAI-compatible LLM client using gpt-4o-mini (cheapest model).
/// Reads API key from the apiKey constructor parameter.
/// </summary>
public class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiLlmClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
    }

    public async Task<string> ChatAsync(string systemPrompt, List<ChatMessage> messages)
    {
        var requestMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var msg in messages)
        {
            requestMessages.Add(new { role = msg.Role, content = msg.Content });
        }

        var requestBody = new
        {
            model = _model,
            messages = requestMessages,
            temperature = 0.3,
            max_tokens = 500
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("OpenAI returned no choices.");

        return choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}

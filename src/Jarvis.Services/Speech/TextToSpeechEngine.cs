using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core.Interfaces;

namespace Jarvis.Services.Speech;

/// <summary>
/// Text-to-speech engine that sends text to the Sarvam Bulbul v3 API
/// and plays the returned audio through an <see cref="IAudioPlayer"/>.
/// All API communications use TLS (https).
/// </summary>
public class TextToSpeechEngine : ITextToSpeechEngine
{
    private const string SarvamTtsEndpoint = "https://api.sarvam.ai/text-to-speech";

    private readonly HttpClient _httpClient;
    private readonly IAudioPlayer _audioPlayer;
    private readonly string _apiKey;

    public TextToSpeechEngine(HttpClient httpClient, IAudioPlayer audioPlayer, string apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    }

    public async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var requestBody = new SarvamTtsRequest
        {
            Inputs = text,
            TargetLanguageCode = "en-IN",
            Speaker = "meera"
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, SarvamTtsEndpoint)
        {
            Content = content
        };
        request.Headers.Add("api-subscription-key", _apiKey);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var audioBytes = await response.Content.ReadAsByteArrayAsync();
        await _audioPlayer.PlayAsync(audioBytes);
    }

    internal sealed class SarvamTtsRequest
    {
        [JsonPropertyName("inputs")]
        public string Inputs { get; set; } = string.Empty;

        [JsonPropertyName("target_language_code")]
        public string TargetLanguageCode { get; set; } = "en-IN";

        [JsonPropertyName("speaker")]
        public string Speaker { get; set; } = "meera";
    }
}

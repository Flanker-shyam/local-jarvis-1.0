using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Speech;

/// <summary>
/// Speech-to-text engine that captures audio via <see cref="IAudioRecorder"/>
/// and transcribes it using the Sarvam Saaras v3 API.
/// All API communications use TLS (https).
/// </summary>
public class SpeechToTextEngine : ISpeechToTextEngine
{
    private const string SarvamSttEndpoint = "https://api.sarvam.ai/speech-to-text";
    private const float DefaultSilenceRmsThreshold = 0.02f;
    private static readonly TimeSpan DefaultSilenceDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SilenceCheckInterval = TimeSpan.FromMilliseconds(100);

    private readonly HttpClient _httpClient;
    private readonly IAudioRecorder _audioRecorder;
    private readonly string _apiKey;
    private readonly float _silenceRmsThreshold;
    private readonly TimeSpan _silenceDuration;

    private CancellationTokenSource? _silenceDetectionCts;
    private Task? _silenceDetectionTask;
    private bool _isListening;

    /// <summary>
    /// Creates a new SpeechToTextEngine.
    /// </summary>
    /// <param name="httpClient">HttpClient for Sarvam API calls (must use TLS).</param>
    /// <param name="audioRecorder">Platform-specific audio recorder abstraction.</param>
    /// <param name="apiKey">Sarvam API subscription key.</param>
    /// <param name="silenceRmsThreshold">RMS level below which audio is considered silence (default 0.02).</param>
    /// <param name="silenceDuration">Duration of continuous silence before auto-stop (default 2s).</param>
    public SpeechToTextEngine(
        HttpClient httpClient,
        IAudioRecorder audioRecorder,
        string apiKey,
        float silenceRmsThreshold = DefaultSilenceRmsThreshold,
        TimeSpan? silenceDuration = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _audioRecorder = audioRecorder ?? throw new ArgumentNullException(nameof(audioRecorder));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _silenceRmsThreshold = silenceRmsThreshold;
        _silenceDuration = silenceDuration ?? DefaultSilenceDuration;
    }

    /// <inheritdoc />
    public bool IsListening => _isListening;

    /// <inheritdoc />
    public async Task<TranscriptResult> TranscribeAsync(byte[] audioBuffer)
    {
        if (audioBuffer is null || audioBuffer.Length == 0)
            throw new ArgumentException("Audio buffer must be non-null and non-empty.", nameof(audioBuffer));

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(audioBuffer);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, SarvamSttEndpoint)
        {
            Content = content
        };
        request.Headers.Add("api-subscription-key", _apiKey);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var sttResponse = JsonSerializer.Deserialize<SarvamSttResponse>(json)
            ?? throw new InvalidOperationException("Failed to deserialize Sarvam STT response.");

        return new TranscriptResult
        {
            Text = sttResponse.Transcript ?? string.Empty,
            Confidence = Math.Clamp(sttResponse.Confidence, 0.0f, 1.0f)
        };
    }

    /// <inheritdoc />
    public async Task StartListeningAsync()
    {
        if (_isListening)
            return;

        _isListening = true;
        await _audioRecorder.StartAsync();

        // Start background silence detection
        _silenceDetectionCts = new CancellationTokenSource();
        _silenceDetectionTask = MonitorSilenceAsync(_silenceDetectionCts.Token);
    }

    /// <inheritdoc />
    public async Task StopListeningAsync()
    {
        if (!_isListening)
            return;

        _isListening = false;

        // Cancel silence detection
        if (_silenceDetectionCts is not null)
        {
            await _silenceDetectionCts.CancelAsync();
            if (_silenceDetectionTask is not null)
            {
                try { await _silenceDetectionTask; }
                catch (OperationCanceledException) { /* expected */ }
            }
            _silenceDetectionCts.Dispose();
            _silenceDetectionCts = null;
            _silenceDetectionTask = null;
        }

        if (_audioRecorder.IsRecording)
            await _audioRecorder.StopAsync();
    }

    /// <summary>
    /// Monitors audio RMS levels and auto-stops listening when silence
    /// (RMS below threshold) persists for the configured duration.
    /// </summary>
    private async Task MonitorSilenceAsync(CancellationToken cancellationToken)
    {
        var silentSince = (DateTimeOffset?)null;

        while (!cancellationToken.IsCancellationRequested && _isListening)
        {
            var rms = _audioRecorder.CurrentRmsLevel;

            if (rms < _silenceRmsThreshold)
            {
                silentSince ??= DateTimeOffset.UtcNow;

                if (DateTimeOffset.UtcNow - silentSince.Value >= _silenceDuration)
                {
                    // Silence exceeded threshold — auto-stop
                    await StopListeningAsync();
                    return;
                }
            }
            else
            {
                // Audio detected — reset silence timer
                silentSince = null;
            }

            try
            {
                await Task.Delay(SilenceCheckInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // ── Sarvam STT response DTO ───────────────────────────────────────

    internal sealed class SarvamSttResponse
    {
        [JsonPropertyName("transcript")]
        public string? Transcript { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }
}

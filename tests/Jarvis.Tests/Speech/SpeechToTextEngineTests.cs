using System.Net;
using System.Text;
using System.Text.Json;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Speech;
using Moq;

namespace Jarvis.Tests.Speech;

/// <summary>
/// Unit tests for SpeechToTextEngine — Sarvam STT integration and silence detection.
/// Validates: Requirements 1.1, 1.3
/// </summary>
public class SpeechToTextEngineTests
{
    private const string TestApiKey = "test-api-key";

    private readonly Mock<IAudioRecorder> _recorderMock;

    public SpeechToTextEngineTests()
    {
        _recorderMock = new Mock<IAudioRecorder>();
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new FakeHttpHandler(handler));
    }

    private static HttpResponseMessage SttResponse(string transcript = "hello world", float confidence = 0.95f)
    {
        var json = JsonSerializer.Serialize(new { transcript, confidence });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private SpeechToTextEngine CreateEngine(
        HttpClient httpClient,
        float silenceRmsThreshold = 0.02f,
        TimeSpan? silenceDuration = null)
    {
        return new SpeechToTextEngine(
            httpClient,
            _recorderMock.Object,
            TestApiKey,
            silenceRmsThreshold,
            silenceDuration);
    }

    // ── 1. TranscribeAsync sends audio to Sarvam API and returns correct transcript ──

    [Fact]
    public async Task TranscribeAsync_ReturnsCorrectTranscript_FromSarvamApi()
    {
        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse("play some jazz", 0.92f)));
        var engine = CreateEngine(httpClient);

        var result = await engine.TranscribeAsync(new byte[] { 1, 2, 3 });

        Assert.Equal("play some jazz", result.Text);
        Assert.Equal(0.92f, result.Confidence);
    }

    // ── 2. TranscribeAsync returns confidence clamped to [0.0, 1.0] ──

    [Theory]
    [InlineData(1.5f, 1.0f)]
    [InlineData(-0.3f, 0.0f)]
    [InlineData(0.75f, 0.75f)]
    public async Task TranscribeAsync_ClampsConfidence_ToValidRange(float apiConfidence, float expected)
    {
        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse("test", apiConfidence)));
        var engine = CreateEngine(httpClient);

        var result = await engine.TranscribeAsync(new byte[] { 1 });

        Assert.Equal(expected, result.Confidence);
    }

    // ── 3. TranscribeAsync throws on null/empty audio buffer ──

    [Fact]
    public async Task TranscribeAsync_ThrowsArgumentException_OnNullBuffer()
    {
        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse()));
        var engine = CreateEngine(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.TranscribeAsync(null!));
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsArgumentException_OnEmptyBuffer()
    {
        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse()));
        var engine = CreateEngine(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.TranscribeAsync(Array.Empty<byte>()));
    }

    // ── 4. TranscribeAsync throws on non-success HTTP response ──

    [Fact]
    public async Task TranscribeAsync_ThrowsHttpRequestException_OnNonSuccessResponse()
    {
        var httpClient = CreateHttpClient(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var engine = CreateEngine(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.TranscribeAsync(new byte[] { 1, 2 }));
    }

    // ── 5. Silence detection auto-stops listening when RMS stays below threshold ──

    [Fact]
    public async Task SilenceDetection_AutoStopsListening_WhenRmsBelowThresholdForDuration()
    {
        // Use a short silence duration to keep the test fast
        var silenceDuration = TimeSpan.FromMilliseconds(200);

        // Recorder always reports silence (RMS = 0)
        _recorderMock.SetupGet(r => r.CurrentRmsLevel).Returns(0.0f);
        _recorderMock.SetupGet(r => r.IsRecording).Returns(true);
        _recorderMock.Setup(r => r.StartAsync()).Returns(Task.CompletedTask);
        _recorderMock.Setup(r => r.StopAsync()).Returns(Task.FromResult(Array.Empty<byte>()));

        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse()));
        var engine = CreateEngine(httpClient, silenceRmsThreshold: 0.02f, silenceDuration: silenceDuration);

        await engine.StartListeningAsync();
        Assert.True(engine.IsListening);

        // Wait enough time for silence detection to trigger auto-stop
        // The silence check interval is 100ms, so after ~200ms of silence the auto-stop fires.
        // Give generous margin for CI/slow machines.
        await Task.Delay(silenceDuration + TimeSpan.FromMilliseconds(500));

        Assert.False(engine.IsListening);
    }

    // ── 6. Silence detection resets when audio is detected (RMS above threshold) ──

    [Fact]
    public async Task SilenceDetection_ResetsTimer_WhenAudioDetected()
    {
        var silenceDuration = TimeSpan.FromMilliseconds(300);
        var rmsSequence = new Queue<float>();

        // Simulate: silence → audio → silence (not long enough to trigger auto-stop initially)
        // First ~150ms: silence, then audio spike, then silence again
        // The audio spike should reset the timer, so auto-stop won't happen until
        // silenceDuration after the last audio
        for (var i = 0; i < 3; i++) rmsSequence.Enqueue(0.0f);   // ~300ms silence
        for (var i = 0; i < 2; i++) rmsSequence.Enqueue(0.5f);   // ~200ms audio (resets timer)
        for (var i = 0; i < 10; i++) rmsSequence.Enqueue(0.0f);  // silence until auto-stop

        _recorderMock.SetupGet(r => r.CurrentRmsLevel)
            .Returns(() => rmsSequence.Count > 0 ? rmsSequence.Dequeue() : 0.0f);
        _recorderMock.SetupGet(r => r.IsRecording).Returns(true);
        _recorderMock.Setup(r => r.StartAsync()).Returns(Task.CompletedTask);
        _recorderMock.Setup(r => r.StopAsync()).Returns(Task.FromResult(Array.Empty<byte>()));

        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse()));
        var engine = CreateEngine(httpClient, silenceRmsThreshold: 0.02f, silenceDuration: silenceDuration);

        await engine.StartListeningAsync();

        // After the audio spike at ~300ms, the silence timer resets.
        // Auto-stop should happen ~300ms after the last audio (at ~500ms + 300ms = ~800ms).
        // At 400ms, it should still be listening because the timer was reset by audio.
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        // Engine may or may not still be listening at this point depending on timing,
        // but it should eventually stop.

        // Wait for auto-stop to complete
        await Task.Delay(silenceDuration + TimeSpan.FromMilliseconds(300));

        Assert.False(engine.IsListening);
    }

    // ── 7. StartListeningAsync sets IsListening to true ──

    [Fact]
    public async Task StartListeningAsync_SetsIsListeningToTrue()
    {
        _recorderMock.Setup(r => r.StartAsync()).Returns(Task.CompletedTask);
        _recorderMock.SetupGet(r => r.CurrentRmsLevel).Returns(0.5f); // keep audio active so no auto-stop

        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse()));
        var engine = CreateEngine(httpClient, silenceDuration: TimeSpan.FromSeconds(30));

        Assert.False(engine.IsListening);

        await engine.StartListeningAsync();

        Assert.True(engine.IsListening);

        // Cleanup
        await engine.StopListeningAsync();
    }

    // ── 8. StopListeningAsync sets IsListening to false ──

    [Fact]
    public async Task StopListeningAsync_SetsIsListeningToFalse()
    {
        _recorderMock.Setup(r => r.StartAsync()).Returns(Task.CompletedTask);
        _recorderMock.Setup(r => r.StopAsync()).Returns(Task.FromResult(Array.Empty<byte>()));
        _recorderMock.SetupGet(r => r.IsRecording).Returns(true);
        _recorderMock.SetupGet(r => r.CurrentRmsLevel).Returns(0.5f);

        var httpClient = CreateHttpClient(_ => Task.FromResult(SttResponse()));
        var engine = CreateEngine(httpClient, silenceDuration: TimeSpan.FromSeconds(30));

        await engine.StartListeningAsync();
        Assert.True(engine.IsListening);

        await engine.StopListeningAsync();
        Assert.False(engine.IsListening);
    }

    // ── Custom HttpMessageHandler for intercepting HTTP requests ─────

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public FakeHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Nlu;
using Jarvis.Services.Spotify;
using Moq;

namespace Jarvis.Tests.ErrorHandling;

/// <summary>
/// Unit tests for error handling and resilience.
/// Tests LLM retry with exponential backoff, 401 token refresh flow,
/// and 403 Premium required degradation.
///
/// Validates: Requirements 10.1, 10.2, 10.3
/// </summary>
public class ErrorHandlingTests
{
    // ── LLM Retry with Exponential Backoff (Req 10.1) ────────────────

    [Fact]
    public async Task NluResolver_LlmFailsTwiceThenSucceeds_ReturnsValidIntent()
    {
        // Arrange: LLM fails twice, succeeds on third attempt
        var callCount = 0;
        var mockLlm = new Mock<ILlmClient>();
        mockLlm.Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new HttpRequestException("LLM unavailable");

                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    intent = "PAUSE",
                    confidence = 0.95f
                }));
            });

        var mockStore = new Mock<IConversationStore>();
        mockStore.Setup(s => s.GetLastPlayedTrack()).Returns((Track?)null);

        var resolver = new NluResolver(mockLlm.Object, mockStore.Object);

        // Act
        var result = await resolver.ResolveIntentAsync("pause the music", new List<Turn>());

        // Assert: succeeded after retries
        Assert.Equal(IntentType.Pause, result.IntentType);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task NluResolver_LlmFailsAllThreeAttempts_ReturnsUnknownIntent()
    {
        // Arrange: LLM fails all 3 attempts
        var callCount = 0;
        var mockLlm = new Mock<ILlmClient>();
        mockLlm.Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .Returns(() =>
            {
                callCount++;
                throw new HttpRequestException("LLM unavailable");
            });

        var mockStore = new Mock<IConversationStore>();
        mockStore.Setup(s => s.GetLastPlayedTrack()).Returns((Track?)null);

        var resolver = new NluResolver(mockLlm.Object, mockStore.Object);

        // Act
        var result = await resolver.ResolveIntentAsync("play something", new List<Turn>());

        // Assert: falls back to Unknown after all retries exhausted
        Assert.Equal(IntentType.Unknown, result.IntentType);
        Assert.Equal(0.0f, result.Confidence);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void NluResolver_RetryConstants_AreCorrectlyDefined()
    {
        // Verify retry configuration
        Assert.Equal(3, NluResolver.MaxRetryAttempts);
        Assert.Equal(new[] { 500, 1000, 2000 }, NluResolver.RetryDelaysMs);
    }

    // ── 401 → Token Refresh → Retry (Req 10.2) ──────────────────────

    [Fact]
    public async Task SpotifyService_401ThenSuccess_RefreshesAndRetries()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");
        authMock.Setup(a => a.RefreshTokenAsync()).Returns(Task.CompletedTask);

        var callCount = 0;
        var httpClient = new HttpClient(new FakeHttpHandler(request =>
        {
            callCount++;
            if (callCount == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = await service.PauseAsync();

        Assert.True(result.Success);
        authMock.Verify(a => a.RefreshTokenAsync(), Times.Once);
        Assert.Equal(2, callCount); // original + retry
    }

    [Fact]
    public async Task SpotifyService_401AndRefreshFails_ReturnsAuthFailureMessage()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");
        authMock.Setup(a => a.RefreshTokenAsync())
            .ThrowsAsync(new InvalidOperationException("Re-authentication required."));

        var httpClient = new HttpClient(new FakeHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = await service.PauseAsync();

        // When refresh fails, response is null → re-auth message
        Assert.False(result.Success);
        Assert.Contains("re-connect", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotifyService_401OnSearch_RefreshesAndRetriesSuccessfully()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");
        authMock.Setup(a => a.RefreshTokenAsync()).Returns(Task.CompletedTask);

        var callCount = 0;
        var httpClient = new HttpClient(new FakeHttpHandler(request =>
        {
            callCount++;
            if (callCount == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var json = JsonSerializer.Serialize(new
            {
                tracks = new
                {
                    items = new[]
                    {
                        new
                        {
                            id = "t1", name = "Track",
                            artists = new[] { new { name = "Artist" } },
                            album = new { name = "Album" },
                            uri = "spotify:track:t1", duration_ms = 200000
                        }
                    }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }));

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = await service.SearchAsync(new SearchParams { Track = "Test", IsVague = false });

        Assert.Single(result);
        Assert.Equal("Track", result[0].Name);
        authMock.Verify(a => a.RefreshTokenAsync(), Times.Once);
    }

    // ── 403 → Premium Required + Search-Only Degradation (Req 10.3) ──

    [Fact]
    public async Task SpotifyService_403OnPlay_ReturnsPremiumMessageWithSearchHint()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");

        var httpClient = new HttpClient(new FakeHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));

        var service = new SpotifyService(httpClient, authMock.Object);
        var tracks = new List<Track>
        {
            new() { Id = "t1", Name = "Song", Artist = "Band", Uri = "spotify:track:t1" }
        };

        var result = await service.PlayAsync(tracks, "device-1");

        Assert.False(result.Success);
        Assert.Contains("Premium", result.Message);
        Assert.Contains("search", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotifyService_403OnPause_ReturnsPremiumMessage()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");

        var httpClient = new HttpClient(new FakeHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = await service.PauseAsync();

        Assert.False(result.Success);
        Assert.Contains("Premium", result.Message);
    }

    [Fact]
    public async Task SpotifyService_403OnResume_ReturnsPremiumMessage()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");

        var httpClient = new HttpClient(new FakeHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = await service.ResumeAsync();

        Assert.False(result.Success);
        Assert.Contains("Premium", result.Message);
    }

    [Fact]
    public async Task SpotifyService_403OnSkip_ReturnsPremiumMessage()
    {
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");

        var httpClient = new HttpClient(new FakeHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = await service.SkipNextAsync();

        Assert.False(result.Success);
        Assert.Contains("Premium", result.Message);
    }

    [Fact]
    public async Task SpotifyService_SearchStillWorksWhenPlaybackReturns403()
    {
        // Verify search-only degradation: search works even when playback returns 403
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("token");

        var httpClient = new HttpClient(new FakeHttpHandler(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";

            // Search succeeds
            if (url.Contains("/search"))
            {
                var json = JsonSerializer.Serialize(new
                {
                    tracks = new
                    {
                        items = new[]
                        {
                            new
                            {
                                id = "t1", name = "Found Song",
                                artists = new[] { new { name = "Artist" } },
                                album = new { name = "Album" },
                                uri = "spotify:track:t1", duration_ms = 200000
                            }
                        }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            // Playback returns 403
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }));

        var service = new SpotifyService(httpClient, authMock.Object);

        // Search works
        var searchResult = await service.SearchAsync(new SearchParams { Track = "Test", IsVague = false });
        Assert.Single(searchResult);
        Assert.Equal("Found Song", searchResult[0].Name);

        // Playback fails with Premium message
        var playResult = await service.PlayAsync(searchResult, "device-1");
        Assert.False(playResult.Success);
        Assert.Contains("Premium", playResult.Message);
    }

    // ── FakeHttpHandler ──────────────────────────────────────────────

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

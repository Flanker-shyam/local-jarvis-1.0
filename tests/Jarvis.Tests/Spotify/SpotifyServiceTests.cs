using System.Net;
using System.Text;
using System.Text.Json;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Spotify;
using Moq;

namespace Jarvis.Tests.Spotify;

/// <summary>
/// Unit tests for SpotifyService — search, recommendations, playback, error handling.
/// Validates: Requirements 4.1, 4.2, 4.3, 4.6, 5.1, 5.4, 6.1, 6.2, 6.3, 10.2, 10.3, 10.4
/// </summary>
public class SpotifyServiceTests
{
    private readonly Mock<IAuthManager> _authMock;

    public SpotifyServiceTests()
    {
        _authMock = new Mock<IAuthManager>();
        _authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("test-token");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private SpotifyService CreateService(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var httpClient = new HttpClient(new FakeHttpHandler(handler));
        return new SpotifyService(httpClient, _authMock.Object);
    }

    private static string SearchResponse(params (string id, string name, string artist, string album, string uri)[] tracks)
    {
        var items = tracks.Select(t => new
        {
            id = t.id,
            name = t.name,
            artists = new[] { new { name = t.artist } },
            album = new { name = t.album },
            uri = t.uri,
            duration_ms = 240000
        }).ToArray();

        return JsonSerializer.Serialize(new { tracks = new { items } });
    }

    private static string EmptySearchResponse() =>
        JsonSerializer.Serialize(new { tracks = new { items = Array.Empty<object>() } });

    private static string RecommendationsResponse(params (string id, string name, string artist, string album, string uri)[] tracks)
    {
        var items = tracks.Select(t => new
        {
            id = t.id,
            name = t.name,
            artists = new[] { new { name = t.artist } },
            album = new { name = t.album },
            uri = t.uri,
            duration_ms = 200000
        }).ToArray();

        return JsonSerializer.Serialize(new { tracks = items });
    }

    private static string EmptyRecommendationsResponse() =>
        JsonSerializer.Serialize(new { tracks = Array.Empty<object>() });

    private static string DevicesResponse(bool hasActive = true)
    {
        if (!hasActive)
            return JsonSerializer.Serialize(new { devices = Array.Empty<object>() });

        return JsonSerializer.Serialize(new
        {
            devices = new[]
            {
                new { id = "device-1", is_active = true, name = "My Phone" }
            }
        });
    }

    // ── BuildDirectQuery tests (supplementing BuildDirectQueryTests) ─

    [Fact]
    public void BuildDirectQuery_ExplicitFields_UsesFieldFilterSyntax()
    {
        var sp = new SearchParams { Track = "Stairway to Heaven", Artist = "Led Zeppelin" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Contains("track:Stairway to Heaven", result);
        Assert.Contains("artist:Led Zeppelin", result);
    }

    [Fact]
    public void BuildDirectQuery_RawQuery_UsedWhenNoExplicitFields()
    {
        var sp = new SearchParams { Query = "classic rock hits" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("classic rock hits", result);
    }

    [Fact]
    public void BuildDirectQuery_InferredAttributes_ConcatenatesGenresMoodContext()
    {
        var sp = new SearchParams
        {
            Genres = new List<string> { "jazz", "blues" },
            Mood = "mellow",
            Context = "evening"
        };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("jazz blues mellow evening", result);
    }

    // ── MoodToValence tests ──────────────────────────────────────────

    [Theory]
    [InlineData("happy", 0.9f)]
    [InlineData("upbeat", 0.85f)]
    [InlineData("energetic", 0.8f)]
    [InlineData("party", 0.85f)]
    [InlineData("chill", 0.5f)]
    [InlineData("relaxed", 0.45f)]
    [InlineData("mellow", 0.4f)]
    [InlineData("melancholy", 0.2f)]
    [InlineData("sad", 0.15f)]
    [InlineData("angry", 0.3f)]
    [InlineData("dark", 0.2f)]
    [InlineData("romantic", 0.6f)]
    [InlineData("nostalgic", 0.45f)]
    [InlineData("focused", 0.4f)]
    [InlineData("epic", 0.75f)]
    public void MoodToValence_KnownMoods_ReturnsExpectedValence(string mood, float expected)
    {
        Assert.Equal(expected, SpotifyService.MoodToValence(mood));
    }

    [Fact]
    public void MoodToValence_Null_ReturnsNeutral()
    {
        Assert.Equal(0.5f, SpotifyService.MoodToValence(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonexistent")]
    [InlineData("XYZZY")]
    public void MoodToValence_UnknownOrEmpty_ReturnsNeutral(string mood)
    {
        Assert.Equal(0.5f, SpotifyService.MoodToValence(mood));
    }

    [Theory]
    [InlineData("HAPPY", 0.9f)]
    [InlineData("Happy", 0.9f)]
    [InlineData("SAD", 0.15f)]
    public void MoodToValence_CaseInsensitive(string mood, float expected)
    {
        Assert.Equal(expected, SpotifyService.MoodToValence(mood));
    }

    // ── Search fallback to recommendations ───────────────────────────

    [Fact]
    public async Task SearchAsync_EmptyDirectSearch_FallsBackToRecommendations()
    {
        bool recommendationsCalled = false;

        var service = CreateService(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("/search"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmptySearchResponse(), Encoding.UTF8, "application/json")
                });
            }

            if (url.Contains("/recommendations"))
            {
                recommendationsCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        RecommendationsResponse(("rec-1", "Rec Track", "Rec Artist", "Rec Album", "spotify:track:rec-1")),
                        Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var sp = new SearchParams { Track = "Nonexistent Song", IsVague = false };
        var result = await service.SearchAsync(sp);

        Assert.True(recommendationsCalled);
        Assert.Single(result);
        Assert.Equal("Rec Track", result[0].Name);
    }

    [Fact]
    public async Task SearchAsync_DirectSearchHasResults_DoesNotCallRecommendations()
    {
        bool recommendationsCalled = false;

        var service = CreateService(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("/search"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        SearchResponse(("t1", "Found Track", "Artist", "Album", "spotify:track:t1")),
                        Encoding.UTF8, "application/json")
                });
            }

            if (url.Contains("/recommendations"))
            {
                recommendationsCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmptyRecommendationsResponse(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var sp = new SearchParams { Track = "Found Track", IsVague = false };
        var result = await service.SearchAsync(sp);

        Assert.False(recommendationsCalled);
        Assert.Single(result);
    }

    [Fact]
    public async Task SearchAsync_VagueParams_UsesRecommendationsDirectly()
    {
        bool searchCalled = false;

        var service = CreateService(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("/search"))
            {
                searchCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmptySearchResponse(), Encoding.UTF8, "application/json")
                });
            }

            if (url.Contains("/recommendations"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        RecommendationsResponse(("r1", "Chill Track", "Artist", "Album", "spotify:track:r1")),
                        Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var sp = new SearchParams { Genres = new List<string> { "lo-fi" }, Mood = "chill", IsVague = true };
        var result = await service.SearchAsync(sp);

        Assert.False(searchCalled);
        Assert.Single(result);
    }

    // ── 401 handling triggers token refresh and retry ─────────────────

    [Fact]
    public async Task SearchAsync_401Response_RefreshesTokenAndRetries()
    {
        int callCount = 0;

        var service = CreateService(request =>
        {
            callCount++;
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("/search"))
            {
                if (callCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        SearchResponse(("t1", "Track", "Artist", "Album", "spotify:track:t1")),
                        Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var sp = new SearchParams { Track = "Test", IsVague = false };
        var result = await service.SearchAsync(sp);

        _authMock.Verify(a => a.RefreshTokenAsync(), Times.Once);
        Assert.Single(result);
        Assert.Equal("Track", result[0].Name);
    }

    [Fact]
    public async Task PlayAsync_401Response_RefreshesTokenAndRetries()
    {
        int callCount = 0;

        var service = CreateService(request =>
        {
            callCount++;
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("/me/player/play"))
            {
                if (callCount == 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var tracks = new List<Track>
        {
            new() { Id = "t1", Name = "Song", Artist = "Band", Uri = "spotify:track:t1" }
        };

        var result = await service.PlayAsync(tracks, "device-1");

        _authMock.Verify(a => a.RefreshTokenAsync(), Times.Once);
        Assert.True(result.Success);
    }

    // ── 403 handling returns Premium required message ─────────────────

    [Fact]
    public async Task PlayAsync_403Response_ReturnsPremiumRequiredMessage()
    {
        var service = CreateService(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });

        var tracks = new List<Track>
        {
            new() { Id = "t1", Name = "Song", Artist = "Band", Uri = "spotify:track:t1" }
        };

        var result = await service.PlayAsync(tracks, "device-1");

        Assert.False(result.Success);
        Assert.Contains("Premium", result.Message);
    }

    [Fact]
    public async Task PauseAsync_403Response_ReturnsPremiumRequiredMessage()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await service.PauseAsync();

        Assert.False(result.Success);
        Assert.Contains("Premium", result.Message);
    }

    // ── No active device returns appropriate failure ──────────────────

    [Fact]
    public async Task GetActiveDeviceIdAsync_NoDevices_ReturnsNull()
    {
        var service = CreateService(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DevicesResponse(hasActive: false), Encoding.UTF8, "application/json")
            });
        });

        var deviceId = await service.GetActiveDeviceIdAsync();
        Assert.Null(deviceId);
    }

    [Fact]
    public async Task GetActiveDeviceIdAsync_HasActiveDevice_ReturnsDeviceId()
    {
        var service = CreateService(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DevicesResponse(hasActive: true), Encoding.UTF8, "application/json")
            });
        });

        var deviceId = await service.GetActiveDeviceIdAsync();
        Assert.Equal("device-1", deviceId);
    }

    [Fact]
    public async Task GetActiveDeviceIdAsync_ApiFailure_ReturnsNull()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var deviceId = await service.GetActiveDeviceIdAsync();
        Assert.Null(deviceId);
    }

    // ── Playback control tests ───────────────────────────────────────

    [Fact]
    public async Task PlayAsync_Success_ReturnsNowPlayingTrack()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        var tracks = new List<Track>
        {
            new() { Id = "t1", Name = "Bohemian Rhapsody", Artist = "Queen", Uri = "spotify:track:t1" }
        };

        var result = await service.PlayAsync(tracks, "device-1");

        Assert.True(result.Success);
        Assert.Contains("Bohemian Rhapsody", result.Message);
        Assert.Contains("Queen", result.Message);
        Assert.NotNull(result.NowPlaying);
        Assert.Equal(1, result.QueueLength);
    }

    [Fact]
    public async Task PauseAsync_Success_ReturnsPausedMessage()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await service.PauseAsync();

        Assert.True(result.Success);
        Assert.Contains("Paused", result.Message);
    }

    [Fact]
    public async Task ResumeAsync_Success_ReturnsResumedMessage()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await service.ResumeAsync();

        Assert.True(result.Success);
        Assert.Contains("Resumed", result.Message);
    }

    [Fact]
    public async Task SkipNextAsync_Success_ReturnsSkipMessage()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await service.SkipNextAsync();

        Assert.True(result.Success);
        Assert.Contains("next", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SkipPreviousAsync_Success_ReturnsPreviousMessage()
    {
        var service = CreateService(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await service.SkipPreviousAsync();

        Assert.True(result.Success);
        Assert.Contains("previous", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Custom HttpMessageHandler ────────────────────────────────────

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

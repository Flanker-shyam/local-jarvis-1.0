using System.Net;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Spotify;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 13: Token refresh on expiry is transparent.
/// For any Spotify API call where the access token is expired, AuthManager refreshes
/// the token and retries so the caller never receives an auth error for expired tokens.
///
/// **Validates: Requirements 9.2, 10.2**
/// </summary>
public class Property13_TokenRefreshTests
{
    private enum SpotifyOp
    {
        Search, Pause, Resume, SkipNext, SkipPrevious, GetActiveDevice, Play, GetRecommendations
    }

    private static readonly Arbitrary<SpotifyOp> SpotifyOpArb =
        Gen.Elements(
            SpotifyOp.Search, SpotifyOp.Pause, SpotifyOp.Resume,
            SpotifyOp.SkipNext, SpotifyOp.SkipPrevious,
            SpotifyOp.GetActiveDevice, SpotifyOp.Play,
            SpotifyOp.GetRecommendations
        ).ToArbitrary();

    [Property(MaxTest = 50)]
    public Property TokenRefresh_IsTransparent_CallerNeverReceivesAuthError()
    {
        return Prop.ForAll(SpotifyOpArb, op =>
        {
            // Arrange: first call returns 401 (expired token), second call succeeds after refresh
            var authMock = new Mock<IAuthManager>();
            var callCount = 0;
            var refreshCalled = false;

            authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("test-token");
            authMock.Setup(a => a.RefreshTokenAsync())
                .Callback(() => refreshCalled = true)
                .Returns(Task.CompletedTask);

            var httpClient = new HttpClient(new FakeHttpHandler(request =>
            {
                callCount++;
                if (callCount == 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                var url = request.RequestUri?.ToString() ?? "";
                return Task.FromResult(BuildSuccessResponse(url));
            }));

            var service = new SpotifyService(httpClient, authMock.Object);

            // Act
            var receivedAuthError = false;
            try
            {
                ExecuteOperation(service, op).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (
                ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                receivedAuthError = true;
            }

            // Property: refresh was called AND caller never received an auth error
            return (refreshCalled && !receivedAuthError)
                .Label($"Op={op}: refreshCalled={refreshCalled}, receivedAuthError={receivedAuthError}");
        });
    }

    [Property(MaxTest = 50)]
    public Property TokenRefresh_OnExpiry_RefreshIsCalledExactlyOnce()
    {
        return Prop.ForAll(SpotifyOpArb, op =>
        {
            var authMock = new Mock<IAuthManager>();
            var refreshCallCount = 0;

            authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("test-token");
            authMock.Setup(a => a.RefreshTokenAsync())
                .Callback(() => refreshCallCount++)
                .Returns(Task.CompletedTask);

            var callCount = 0;
            var httpClient = new HttpClient(new FakeHttpHandler(request =>
            {
                callCount++;
                if (callCount == 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                var url = request.RequestUri?.ToString() ?? "";
                return Task.FromResult(BuildSuccessResponse(url));
            }));

            var service = new SpotifyService(httpClient, authMock.Object);

            ExecuteOperation(service, op).GetAwaiter().GetResult();

            return (refreshCallCount == 1)
                .Label($"Op={op}: refreshCallCount={refreshCallCount}, expected 1");
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static async Task ExecuteOperation(SpotifyService service, SpotifyOp op)
    {
        switch (op)
        {
            case SpotifyOp.Search:
                await service.SearchAsync(new SearchParams { Track = "Test", IsVague = false });
                break;
            case SpotifyOp.Pause:
                await service.PauseAsync();
                break;
            case SpotifyOp.Resume:
                await service.ResumeAsync();
                break;
            case SpotifyOp.SkipNext:
                await service.SkipNextAsync();
                break;
            case SpotifyOp.SkipPrevious:
                await service.SkipPreviousAsync();
                break;
            case SpotifyOp.GetActiveDevice:
                await service.GetActiveDeviceIdAsync();
                break;
            case SpotifyOp.Play:
                var tracks = new List<Track>
                {
                    new() { Id = "t1", Name = "Song", Artist = "Artist", Uri = "spotify:track:t1" }
                };
                await service.PlayAsync(tracks, "device-1");
                break;
            case SpotifyOp.GetRecommendations:
                await service.GetRecommendationsAsync(new SearchParams
                {
                    Genres = new List<string> { "pop" },
                    Mood = "happy",
                    IsVague = true
                });
                break;
        }
    }

    private static HttpResponseMessage BuildSuccessResponse(string url)
    {
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
                            id = "t1", name = "Track",
                            artists = new[] { new { name = "Artist" } },
                            album = new { name = "Album" },
                            uri = "spotify:track:t1", duration_ms = 200000
                        }
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        if (url.Contains("/recommendations"))
        {
            var json = JsonSerializer.Serialize(new
            {
                tracks = new[]
                {
                    new
                    {
                        id = "r1", name = "Rec",
                        artists = new[] { new { name = "Artist" } },
                        album = new { name = "Album" },
                        uri = "spotify:track:r1", duration_ms = 200000
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        if (url.Contains("/me/player/devices"))
        {
            var json = JsonSerializer.Serialize(new
            {
                devices = new[] { new { id = "device-1", is_active = true, name = "Phone" } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        // Playback control endpoints return 204 No Content
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

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

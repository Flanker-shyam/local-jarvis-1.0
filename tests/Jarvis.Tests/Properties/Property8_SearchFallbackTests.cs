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
/// Property 8: Empty search results trigger recommendation fallback.
/// For any SearchParams where direct search returns zero results,
/// the system falls back to Recommendations API before returning failure.
///
/// **Validates: Requirements 4.3**
/// </summary>
public class Property8_SearchFallbackTests
{
    private const string SpotifyApiBase = "https://api.spotify.com/v1";

    /// <summary>
    /// Wrapper for generated non-vague SearchParams with at least one explicit field.
    /// </summary>
    public class NonVagueSearchInput
    {
        public string? Track { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public List<string> Genres { get; set; } = new();
        public string? Mood { get; set; }

        public SearchParams ToSearchParams() => new()
        {
            Track = Track,
            Artist = Artist,
            Album = Album,
            Genres = Genres,
            Mood = Mood,
            IsVague = false
        };

        public override string ToString() =>
            $"Track={Track}, Artist={Artist}, Album={Album}, Genres=[{string.Join(",", Genres)}], Mood={Mood}";
    }

    public static Arbitrary<NonVagueSearchInput> ArbNonVagueSearchInput()
    {
        var genNonEmpty = Arb.Default.NonEmptyString().Generator.Select(s => s.Get);
        var genOptional = Gen.OneOf(
            Gen.Constant<string?>(null),
            genNonEmpty.Select<string, string?>(s => s));

        var genGenres = Gen.OneOf(
            Gen.Constant(new List<string>()),
            Gen.ListOf(genNonEmpty).Select(l => l.ToList()));

        var gen = from track in genOptional
                  from artist in genOptional
                  from album in genOptional
                  from genres in genGenres
                  from mood in genOptional
                  where !string.IsNullOrWhiteSpace(track)
                     || !string.IsNullOrWhiteSpace(artist)
                     || !string.IsNullOrWhiteSpace(album)
                  select new NonVagueSearchInput
                  {
                      Track = track,
                      Artist = artist,
                      Album = album,
                      Genres = genres,
                      Mood = mood
                  };

        return Arb.From(gen, Arb.Default.Derive<NonVagueSearchInput>().Shrinker);
    }

    private static string EmptySearchResponse() =>
        JsonSerializer.Serialize(new { tracks = new { items = Array.Empty<object>() } });

    private static string RecommendationsResponse() =>
        JsonSerializer.Serialize(new
        {
            tracks = new[]
            {
                new
                {
                    id = "rec-1",
                    name = "Recommended Track",
                    artists = new[] { new { name = "Rec Artist" } },
                    album = new { name = "Rec Album" },
                    uri = "spotify:track:rec-1",
                    duration_ms = 200000
                }
            }
        });

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(Property8_SearchFallbackTests) })]
    public bool EmptySearch_FallsBackToRecommendations(NonVagueSearchInput input)
    {
        var searchParams = input.ToSearchParams();
        var query = SpotifyService.BuildDirectQuery(searchParams);
        if (string.IsNullOrWhiteSpace(query))
            return true; // skip if no query can be built

        bool recommendationsEndpointCalled = false;

        var handler = new FakeHttpHandler(request =>
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
                recommendationsEndpointCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RecommendationsResponse(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var httpClient = new HttpClient(handler);
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("test-token");

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = service.SearchAsync(searchParams).GetAwaiter().GetResult();

        // Property: when direct search returns empty, recommendations endpoint must be called
        return recommendationsEndpointCalled;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(Property8_SearchFallbackTests) })]
    public bool EmptySearch_FallbackReturnsRecommendedTracks(NonVagueSearchInput input)
    {
        var searchParams = input.ToSearchParams();
        var query = SpotifyService.BuildDirectQuery(searchParams);
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var handler = new FakeHttpHandler(request =>
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
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RecommendationsResponse(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var httpClient = new HttpClient(handler);
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("test-token");

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = service.SearchAsync(searchParams).GetAwaiter().GetResult();

        // Property: fallback should return tracks from recommendations
        return result.Count > 0;
    }

    /// <summary>
    /// Custom HttpMessageHandler for intercepting HTTP requests.
    /// </summary>
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

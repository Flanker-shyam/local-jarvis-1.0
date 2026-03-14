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
/// Property 14: Failed searches preserve playback state.
/// For any search producing no results, the current Spotify playback state remains unchanged.
/// Verified by ensuring no PUT to /me/player/play is made when search returns no results.
///
/// **Validates: Requirements 10.4**
/// </summary>
public class Property14_PreservePlaybackTests
{
    /// <summary>
    /// Wrapper for generated SearchParams that will produce empty results.
    /// </summary>
    public class AnySearchInput
    {
        public string? Track { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Query { get; set; }
        public List<string> Genres { get; set; } = new();
        public string? Mood { get; set; }
        public bool IsVague { get; set; }

        public SearchParams ToSearchParams() => new()
        {
            Track = Track,
            Artist = Artist,
            Album = Album,
            Query = Query,
            Genres = Genres,
            Mood = Mood,
            IsVague = IsVague
        };

        public override string ToString() =>
            $"Track={Track}, Artist={Artist}, Album={Album}, Query={Query}, " +
            $"Genres=[{string.Join(",", Genres)}], Mood={Mood}, IsVague={IsVague}";
    }

    public static Arbitrary<AnySearchInput> ArbAnySearchInput()
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
                  from query in genOptional
                  from genres in genGenres
                  from mood in genOptional
                  from isVague in Arb.Default.Bool().Generator
                  where !string.IsNullOrWhiteSpace(track)
                     || !string.IsNullOrWhiteSpace(artist)
                     || !string.IsNullOrWhiteSpace(album)
                     || !string.IsNullOrWhiteSpace(query)
                     || genres.Count > 0
                     || !string.IsNullOrWhiteSpace(mood)
                  select new AnySearchInput
                  {
                      Track = track,
                      Artist = artist,
                      Album = album,
                      Query = query,
                      Genres = genres,
                      Mood = mood,
                      IsVague = isVague
                  };

        return Arb.From(gen, Arb.Default.Derive<AnySearchInput>().Shrinker);
    }

    private static string EmptySearchResponse() =>
        JsonSerializer.Serialize(new { tracks = new { items = Array.Empty<object>() } });

    private static string EmptyRecommendationsResponse() =>
        JsonSerializer.Serialize(new { tracks = Array.Empty<object>() });

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(Property14_PreservePlaybackTests) })]
    public bool FailedSearch_NeverCallsPlayEndpoint(AnySearchInput input)
    {
        var searchParams = input.ToSearchParams();
        bool playEndpointCalled = false;

        var handler = new FakeHttpHandler(request =>
        {
            var url = request.RequestUri?.ToString() ?? "";
            var method = request.Method;

            // Detect any PUT to /me/player/play (playback modification)
            if (method == HttpMethod.Put && url.Contains("/me/player/play"))
            {
                playEndpointCalled = true;
            }

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
                    Content = new StringContent(EmptyRecommendationsResponse(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var httpClient = new HttpClient(handler);
        var authMock = new Mock<IAuthManager>();
        authMock.Setup(a => a.GetValidTokenAsync()).ReturnsAsync("test-token");

        var service = new SpotifyService(httpClient, authMock.Object);
        var result = service.SearchAsync(searchParams).GetAwaiter().GetResult();

        // Property: when search returns no results, no playback API calls are made
        return !playEndpointCalled;
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

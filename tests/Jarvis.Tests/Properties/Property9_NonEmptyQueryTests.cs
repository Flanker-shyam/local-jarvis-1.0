using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Models;
using Jarvis.Services.Spotify;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 9: Query builder always produces non-empty strings.
/// For any SearchParams with at least one populated criterion,
/// BuildDirectQuery returns a non-empty string.
///
/// **Validates: Requirements 5.4**
/// </summary>
public class Property9_NonEmptyQueryTests
{
    /// <summary>
    /// Wrapper for generated SearchParams with at least one populated criterion.
    /// </summary>
    public class PopulatedQueryParams
    {
        public string? Query { get; set; }
        public string? Track { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public List<string> Genres { get; set; } = new();
        public string? Mood { get; set; }
        public string? Context { get; set; }
        public bool IsVague { get; set; }

        public SearchParams ToSearchParams() => new()
        {
            Query = Query,
            Track = Track,
            Artist = Artist,
            Album = Album,
            Genres = Genres,
            Mood = Mood,
            Context = Context,
            IsVague = IsVague
        };

        public override string ToString() =>
            $"Query={Query}, Track={Track}, Artist={Artist}, Album={Album}, " +
            $"Genres=[{string.Join(",", Genres)}], Mood={Mood}, Context={Context}, IsVague={IsVague}";
    }

    public static Arbitrary<PopulatedQueryParams> ArbPopulatedQueryParams()
    {
        var genNonEmpty = Arb.Default.NonEmptyString().Generator
            .Select(s => s.Get)
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var genOptional = Gen.OneOf(
            Gen.Constant<string?>(null),
            genNonEmpty.Select<string, string?>(s => s));
        var genGenres = Gen.OneOf(
            Gen.Constant(new List<string>()),
            Gen.Choose(1, 3).SelectMany(n =>
                Gen.ArrayOf(n, genNonEmpty).Select(a => a.ToList())));

        var gen = from query in genOptional
                  from track in genOptional
                  from artist in genOptional
                  from album in genOptional
                  from genres in genGenres
                  from mood in genOptional
                  from context in genOptional
                  from isVague in Arb.Default.Bool().Generator
                  where !string.IsNullOrWhiteSpace(query)
                     || !string.IsNullOrWhiteSpace(track)
                     || !string.IsNullOrWhiteSpace(artist)
                     || !string.IsNullOrWhiteSpace(album)
                     || genres.Count > 0
                     || !string.IsNullOrWhiteSpace(mood)
                     || !string.IsNullOrWhiteSpace(context)
                  select new PopulatedQueryParams
                  {
                      Query = query,
                      Track = track,
                      Artist = artist,
                      Album = album,
                      Genres = genres,
                      Mood = mood,
                      Context = context,
                      IsVague = isVague
                  };

        return Arb.From(gen, Arb.Default.Derive<PopulatedQueryParams>().Shrinker);
    }

    [Property(MaxTest = 300, Arbitrary = new[] { typeof(Property9_NonEmptyQueryTests) })]
    public bool QueryBuilder_WithPopulatedCriteria_ReturnsNonEmptyString(PopulatedQueryParams data)
    {
        var searchParams = data.ToSearchParams();
        var query = SpotifyService.BuildDirectQuery(searchParams);
        return !string.IsNullOrWhiteSpace(query);
    }
}

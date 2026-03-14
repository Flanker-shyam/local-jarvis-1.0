using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Models;
using Jarvis.Services.Spotify;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 7: Non-vague queries use field filter syntax in query construction.
/// For any SearchParams with IsVague=false and at least one explicit field (Track, Artist, or Album),
/// BuildDirectQuery produces a string containing "track:", "artist:", or "album:" syntax.
///
/// **Validates: Requirements 4.1, 5.1**
/// </summary>
public class Property7_FieldFilterSyntaxTests
{
    /// <summary>
    /// Wrapper for generated non-vague SearchParams with at least one explicit field.
    /// </summary>
    public class NonVagueExplicitParams
    {
        public string? Track { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }

        public SearchParams ToSearchParams() => new()
        {
            Track = Track,
            Artist = Artist,
            Album = Album,
            IsVague = false
        };

        public override string ToString() =>
            $"Track={Track}, Artist={Artist}, Album={Album}";
    }

    public static Arbitrary<NonVagueExplicitParams> ArbNonVagueExplicitParams()
    {
        var genNonEmpty = Arb.Default.NonEmptyString().Generator.Select(s => s.Get);
        var genOptional = Gen.OneOf(
            Gen.Constant<string?>(null),
            genNonEmpty.Select<string, string?>(s => s));

        var gen = from track in genOptional
                  from artist in genOptional
                  from album in genOptional
                  where !string.IsNullOrWhiteSpace(track)
                     || !string.IsNullOrWhiteSpace(artist)
                     || !string.IsNullOrWhiteSpace(album)
                  select new NonVagueExplicitParams
                  {
                      Track = track,
                      Artist = artist,
                      Album = album
                  };

        return Arb.From(gen, Arb.Default.Derive<NonVagueExplicitParams>().Shrinker);
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(Property7_FieldFilterSyntaxTests) })]
    public bool NonVagueQuery_WithExplicitFields_ContainsFieldFilterSyntax(NonVagueExplicitParams data)
    {
        var searchParams = data.ToSearchParams();
        var query = SpotifyService.BuildDirectQuery(searchParams);

        bool hasTrackFilter = !string.IsNullOrWhiteSpace(data.Track) && query.Contains("track:");
        bool hasArtistFilter = !string.IsNullOrWhiteSpace(data.Artist) && query.Contains("artist:");
        bool hasAlbumFilter = !string.IsNullOrWhiteSpace(data.Album) && query.Contains("album:");

        return hasTrackFilter || hasArtistFilter || hasAlbumFilter;
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(Property7_FieldFilterSyntaxTests) })]
    public bool NonVagueQuery_EachExplicitField_AppearsWithCorrectPrefix(NonVagueExplicitParams data)
    {
        var searchParams = data.ToSearchParams();
        var query = SpotifyService.BuildDirectQuery(searchParams);

        if (!string.IsNullOrWhiteSpace(data.Track) && !query.Contains($"track:{data.Track}"))
            return false;
        if (!string.IsNullOrWhiteSpace(data.Artist) && !query.Contains($"artist:{data.Artist}"))
            return false;
        if (!string.IsNullOrWhiteSpace(data.Album) && !query.Contains($"album:{data.Album}"))
            return false;

        return true;
    }
}

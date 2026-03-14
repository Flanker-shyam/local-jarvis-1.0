using Jarvis.Core.Models;
using Jarvis.Services.Spotify;

namespace Jarvis.Tests.Spotify;

public class BuildDirectQueryTests
{
    [Fact]
    public void ExplicitTrackAndArtist_UsesFieldFilterSyntax()
    {
        var sp = new SearchParams { Track = "Bohemian Rhapsody", Artist = "Queen" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("track:Bohemian Rhapsody artist:Queen", result);
    }

    [Fact]
    public void ExplicitAlbumOnly_UsesAlbumFilter()
    {
        var sp = new SearchParams { Album = "A Night at the Opera" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("album:A Night at the Opera", result);
    }

    [Fact]
    public void AllExplicitFields_CombinesAllFilters()
    {
        var sp = new SearchParams { Track = "Song", Artist = "Band", Album = "LP" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("track:Song artist:Band album:LP", result);
    }

    [Fact]
    public void NoExplicitFields_RawQuery_UsesQueryDirectly()
    {
        var sp = new SearchParams { Query = "90s rock ballads" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("90s rock ballads", result);
    }

    [Fact]
    public void NoExplicitFieldsNoQuery_InferredAttributes_ConcatenatesGenresMoodContext()
    {
        var sp = new SearchParams
        {
            Genres = new List<string> { "lo-fi", "ambient" },
            Mood = "chill",
            Context = "study"
        };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("lo-fi ambient chill study", result);
    }

    [Fact]
    public void GenresOnly_ReturnsGenresJoined()
    {
        var sp = new SearchParams { Genres = new List<string> { "rock", "indie" } };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("rock indie", result);
    }

    [Fact]
    public void MoodOnly_ReturnsMood()
    {
        var sp = new SearchParams { Mood = "happy" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("happy", result);
    }

    [Fact]
    public void ExplicitFieldsTakePriorityOverRawQuery()
    {
        var sp = new SearchParams { Track = "Hello", Query = "some raw query" };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("track:Hello", result);
    }

    [Fact]
    public void RawQueryTakesPriorityOverInferredAttributes()
    {
        var sp = new SearchParams
        {
            Query = "raw search",
            Genres = new List<string> { "pop" },
            Mood = "happy"
        };
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal("raw search", result);
    }

    [Fact]
    public void EmptySearchParams_ReturnsEmptyString()
    {
        var sp = new SearchParams();
        var result = SpotifyService.BuildDirectQuery(sp);
        Assert.Equal(string.Empty, result);
    }
}

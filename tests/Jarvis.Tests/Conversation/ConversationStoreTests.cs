using Jarvis.Core.Enums;
using Jarvis.Core.Models;
using Jarvis.Services.Conversation;

namespace Jarvis.Tests.Conversation;

/// <summary>
/// Unit tests for ConversationStore (Task 10.4).
/// Validates Requirements 3.1, 3.2.
/// </summary>
public class ConversationStoreTests
{
    private readonly ConversationStore _sut = new();

    [Fact]
    public void AddTurn_And_GetRecentTurns_ReturnsInOrder()
    {
        var turn1 = new Turn { Role = "user", Content = "Play something", Timestamp = DateTime.UtcNow };
        var turn2 = new Turn { Role = "assistant", Content = "Now playing...", Timestamp = DateTime.UtcNow };
        var turn3 = new Turn { Role = "user", Content = "Skip", Timestamp = DateTime.UtcNow };

        _sut.AddTurn(turn1);
        _sut.AddTurn(turn2);
        _sut.AddTurn(turn3);

        var recent = _sut.GetRecentTurns(10);

        Assert.Equal(3, recent.Count);
        Assert.Equal("Play something", recent[0].Content);
        Assert.Equal("Now playing...", recent[1].Content);
        Assert.Equal("Skip", recent[2].Content);
    }

    [Fact]
    public void GetRecentTurns_ReturnsOnlyLastN()
    {
        for (int i = 0; i < 5; i++)
        {
            _sut.AddTurn(new Turn { Role = "user", Content = $"Turn {i}", Timestamp = DateTime.UtcNow });
        }

        var recent = _sut.GetRecentTurns(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("Turn 3", recent[0].Content);
        Assert.Equal("Turn 4", recent[1].Content);
    }

    [Fact]
    public void GetRecentTurns_ZeroCount_ReturnsEmpty()
    {
        _sut.AddTurn(new Turn { Role = "user", Content = "Hello", Timestamp = DateTime.UtcNow });

        var recent = _sut.GetRecentTurns(0);

        Assert.Empty(recent);
    }

    [Fact]
    public void GetRecentTurns_NegativeCount_ReturnsEmpty()
    {
        _sut.AddTurn(new Turn { Role = "user", Content = "Hello", Timestamp = DateTime.UtcNow });

        var recent = _sut.GetRecentTurns(-1);

        Assert.Empty(recent);
    }

    [Fact]
    public void GetLastPlayedTrack_EmptyHistory_ReturnsNull()
    {
        var track = _sut.GetLastPlayedTrack();

        Assert.Null(track);
    }

    [Fact]
    public void GetLastPlayedTrack_NoPlayMusicTurns_ReturnsNull()
    {
        _sut.AddTurn(new Turn
        {
            Role = "user",
            Content = "Pause",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.Pause, 0.9f, null, "Pause")
        });

        var track = _sut.GetLastPlayedTrack();

        Assert.Null(track);
    }

    [Fact]
    public void GetLastPlayedTrack_WithPlayMusicTurn_ReturnsTrack()
    {
        var searchParams = new SearchParams
        {
            Track = "Bohemian Rhapsody",
            Artist = "Queen",
            SeedTrackId = "track-123"
        };

        _sut.AddTurn(new Turn
        {
            Role = "user",
            Content = "Play Bohemian Rhapsody by Queen",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.95f, searchParams, "Play Bohemian Rhapsody by Queen")
        });

        var track = _sut.GetLastPlayedTrack();

        Assert.NotNull(track);
        Assert.Equal("Bohemian Rhapsody", track.Name);
        Assert.Equal("Queen", track.Artist);
        Assert.Equal("track-123", track.Id);
    }

    [Fact]
    public void GetLastPlayedTrack_MultiplePlays_ReturnsMostRecent()
    {
        var params1 = new SearchParams { Track = "Song A", Artist = "Artist A", SeedTrackId = "id-a" };
        var params2 = new SearchParams { Track = "Song B", Artist = "Artist B", SeedTrackId = "id-b" };

        _sut.AddTurn(new Turn
        {
            Role = "user",
            Content = "Play Song A",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.9f, params1, "Play Song A")
        });

        _sut.AddTurn(new Turn
        {
            Role = "user",
            Content = "Play Song B",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.9f, params2, "Play Song B")
        });

        var track = _sut.GetLastPlayedTrack();

        Assert.NotNull(track);
        Assert.Equal("Song B", track.Name);
        Assert.Equal("Artist B", track.Artist);
    }

    [Fact]
    public void GetLastPlayedTrack_PlayMoreLikeThis_ReturnsTrack()
    {
        var searchParams = new SearchParams
        {
            Track = "Stairway to Heaven",
            Artist = "Led Zeppelin",
            SeedTrackId = "seed-456"
        };

        _sut.AddTurn(new Turn
        {
            Role = "user",
            Content = "Play more like that",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMoreLikeThis, 0.85f, searchParams, "Play more like that")
        });

        var track = _sut.GetLastPlayedTrack();

        Assert.NotNull(track);
        Assert.Equal("Stairway to Heaven", track.Name);
    }

    [Fact]
    public void AddTurn_NullTurn_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.AddTurn(null!));
    }
}

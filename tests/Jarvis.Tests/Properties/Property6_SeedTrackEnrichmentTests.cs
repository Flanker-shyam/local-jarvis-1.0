using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Models;
using Jarvis.Services.Conversation;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 6: PLAY_MORE_LIKE_THIS enriches with seed track.
/// For any PlayMoreLikeThis intent where history contains a previously played track,
/// SearchParams includes the seed track ID from the last played track.
/// **Validates: Requirements 3.2**
/// </summary>
public class Property6_SeedTrackEnrichmentTests
{
    [Property]
    public Property WhenHistoryContainsPlayedTrack_GetLastPlayedTrack_ReturnsSeedTrackId(
        NonEmptyString trackName, NonEmptyString artistName, NonEmptyString seedTrackId)
    {
        var store = new ConversationStore();

        // Add a PlayMusic turn with a played track
        var searchParams = new SearchParams
        {
            Track = trackName.Get,
            Artist = artistName.Get,
            SeedTrackId = seedTrackId.Get
        };

        store.AddTurn(new Turn
        {
            Role = "user",
            Content = $"Play {trackName.Get}",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.95f, searchParams, $"Play {trackName.Get}")
        });

        store.AddTurn(new Turn
        {
            Role = "assistant",
            Content = $"Now playing {trackName.Get}",
            Timestamp = DateTime.UtcNow,
            Intent = null
        });

        // Now simulate a PlayMoreLikeThis intent
        var lastPlayed = store.GetLastPlayedTrack();

        // The seed track should be available for enrichment
        return (lastPlayed != null && lastPlayed.Id == seedTrackId.Get).ToProperty();
    }

    [Property]
    public Property WhenHistoryHasNoPlayedTrack_GetLastPlayedTrack_ReturnsNull(
        NonEmptyString content)
    {
        var store = new ConversationStore();

        // Add only non-play turns
        store.AddTurn(new Turn
        {
            Role = "user",
            Content = content.Get,
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.Pause, 0.9f, null, content.Get)
        });

        var lastPlayed = store.GetLastPlayedTrack();

        return (lastPlayed == null).ToProperty();
    }

    [Property]
    public Property MultiplePlayedTracks_GetLastPlayedTrack_ReturnsMostRecent(
        NonEmptyString track1, NonEmptyString track2, NonEmptyString id1, NonEmptyString id2)
    {
        var store = new ConversationStore();

        store.AddTurn(new Turn
        {
            Role = "user",
            Content = $"Play {track1.Get}",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.9f,
                new SearchParams { Track = track1.Get, SeedTrackId = id1.Get }, $"Play {track1.Get}")
        });

        store.AddTurn(new Turn
        {
            Role = "user",
            Content = $"Play {track2.Get}",
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.9f,
                new SearchParams { Track = track2.Get, SeedTrackId = id2.Get }, $"Play {track2.Get}")
        });

        var lastPlayed = store.GetLastPlayedTrack();

        return (lastPlayed != null && lastPlayed.Id == id2.Get && lastPlayed.Name == track2.Get).ToProperty();
    }
}

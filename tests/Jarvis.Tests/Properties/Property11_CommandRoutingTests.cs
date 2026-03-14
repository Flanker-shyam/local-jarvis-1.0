using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Routing;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 11: Command routing returns unified CommandResult.
/// For any intent type routed through CommandRouter, the result is a valid
/// CommandResult with non-null Success and non-empty Message.
///
/// **Validates: Requirements 7.5**
/// </summary>
public class Property11_CommandRoutingTests
{
    private static CommandRouter CreateRouterWithMockedSpotify()
    {
        var spotifyMock = new Mock<ISpotifyService>();

        // Mock all ISpotifyService methods to return successful results
        spotifyMock.Setup(s => s.SearchAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(new List<Track>
            {
                new() { Id = "t1", Name = "Test Track", Artist = "Test Artist", Uri = "spotify:track:t1" }
            });

        spotifyMock.Setup(s => s.PlayAsync(It.IsAny<List<Track>>(), It.IsAny<string>()))
            .ReturnsAsync(new PlaybackResult
            {
                Success = true,
                Message = "Now playing Test Track by Test Artist",
                NowPlaying = new Track { Id = "t1", Name = "Test Track", Artist = "Test Artist" },
                QueueLength = 1
            });

        spotifyMock.Setup(s => s.GetActiveDeviceIdAsync())
            .ReturnsAsync("device-1");

        spotifyMock.Setup(s => s.PauseAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Paused." });

        spotifyMock.Setup(s => s.ResumeAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Resumed playback." });

        spotifyMock.Setup(s => s.SkipNextAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Skipped to next track." });

        spotifyMock.Setup(s => s.SkipPreviousAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Going back to previous track." });

        spotifyMock.Setup(s => s.GetRecommendationsAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(new List<Track>
            {
                new() { Id = "r1", Name = "Rec Track", Artist = "Rec Artist", Uri = "spotify:track:r1" }
            });

        return new CommandRouter(spotifyMock.Object);
    }

    private static IntentResult CreateIntentForType(IntentType intentType)
    {
        SearchParams? searchParams = null;

        if (intentType == IntentType.PlayMusic)
        {
            searchParams = new SearchParams { Track = "Test Track", Artist = "Test Artist", IsVague = false };
        }
        else if (intentType == IntentType.PlayMoreLikeThis)
        {
            searchParams = new SearchParams { SeedTrackId = "spotify:track:seed1", Genres = new List<string> { "rock" } };
        }

        return new IntentResult(intentType, 0.9f, searchParams, "test transcript");
    }

    [Property(MaxTest = 200)]
    public Property RouteAsync_ForAnyIntentType_ReturnsCommandResultWithNonEmptyMessage()
    {
        return Prop.ForAll(
            Gen.Elements(Enum.GetValues<IntentType>()).ToArbitrary(),
            intentType =>
            {
                var router = CreateRouterWithMockedSpotify();
                var intent = CreateIntentForType(intentType);

                var result = router.RouteAsync(intent).GetAwaiter().GetResult();

                return (result != null &&
                        !string.IsNullOrEmpty(result.Message))
                    .Label($"IntentType={intentType}: result was null or had empty message");
            });
    }
}

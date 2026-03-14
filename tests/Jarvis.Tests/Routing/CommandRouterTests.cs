using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Routing;
using Moq;

namespace Jarvis.Tests.Routing;

/// <summary>
/// Unit tests for CommandRouter.
/// Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5
/// </summary>
public class CommandRouterTests
{
    private readonly Mock<ISpotifyService> _spotifyMock;
    private readonly CommandRouter _router;

    public CommandRouterTests()
    {
        _spotifyMock = new Mock<ISpotifyService>();
        _router = new CommandRouter(_spotifyMock.Object);
    }

    // ── Dispatch tests: each intent type routes to correct handler ───

    [Fact]
    public async Task RouteAsync_PauseIntent_CallsPauseOnSpotify()
    {
        _spotifyMock.Setup(s => s.PauseAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Paused." });

        var intent = new IntentResult(IntentType.Pause, 0.95f, null, "pause");
        var result = await _router.RouteAsync(intent);

        _spotifyMock.Verify(s => s.PauseAsync(), Times.Once);
        Assert.True(result.Success);
        Assert.Equal("Paused.", result.Message);
    }

    [Fact]
    public async Task RouteAsync_ResumeIntent_CallsResumeOnSpotify()
    {
        _spotifyMock.Setup(s => s.ResumeAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Resumed playback." });

        var intent = new IntentResult(IntentType.Resume, 0.95f, null, "resume");
        var result = await _router.RouteAsync(intent);

        _spotifyMock.Verify(s => s.ResumeAsync(), Times.Once);
        Assert.True(result.Success);
        Assert.Contains("Resum", result.Message);
    }

    [Fact]
    public async Task RouteAsync_SkipNextIntent_CallsSkipNextOnSpotify()
    {
        _spotifyMock.Setup(s => s.SkipNextAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Skipped to next track." });

        var intent = new IntentResult(IntentType.SkipNext, 0.9f, null, "skip");
        var result = await _router.RouteAsync(intent);

        _spotifyMock.Verify(s => s.SkipNextAsync(), Times.Once);
        Assert.True(result.Success);
        Assert.Contains("next", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RouteAsync_SkipPreviousIntent_CallsSkipPreviousOnSpotify()
    {
        _spotifyMock.Setup(s => s.SkipPreviousAsync())
            .ReturnsAsync(new PlaybackResult { Success = true, Message = "Going back to previous track." });

        var intent = new IntentResult(IntentType.SkipPrevious, 0.9f, null, "go back");
        var result = await _router.RouteAsync(intent);

        _spotifyMock.Verify(s => s.SkipPreviousAsync(), Times.Once);
        Assert.True(result.Success);
        Assert.Contains("previous", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── PlayMusic handler: search-and-play flow ─────────────────────

    [Fact]
    public async Task RouteAsync_PlayMusicIntent_ExecutesSearchAndPlayFlow()
    {
        var tracks = new List<Track>
        {
            new() { Id = "t1", Name = "Bohemian Rhapsody", Artist = "Queen", Uri = "spotify:track:t1" }
        };

        _spotifyMock.Setup(s => s.SearchAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(tracks);
        _spotifyMock.Setup(s => s.GetActiveDeviceIdAsync())
            .ReturnsAsync("device-1");
        _spotifyMock.Setup(s => s.PlayAsync(tracks, "device-1"))
            .ReturnsAsync(new PlaybackResult
            {
                Success = true,
                Message = "Now playing Bohemian Rhapsody by Queen",
                NowPlaying = tracks[0],
                QueueLength = 1
            });

        var searchParams = new SearchParams { Track = "Bohemian Rhapsody", Artist = "Queen", IsVague = false };
        var intent = new IntentResult(IntentType.PlayMusic, 0.95f, searchParams, "play bohemian rhapsody by queen");
        var result = await _router.RouteAsync(intent);

        _spotifyMock.Verify(s => s.SearchAsync(It.IsAny<SearchParams>()), Times.Once);
        _spotifyMock.Verify(s => s.GetActiveDeviceIdAsync(), Times.Once);
        _spotifyMock.Verify(s => s.PlayAsync(tracks, "device-1"), Times.Once);
        Assert.True(result.Success);
        Assert.Contains("Bohemian Rhapsody", result.Message);
    }

    [Fact]
    public async Task RouteAsync_PlayMusicIntent_NoSearchParams_ReturnsFailure()
    {
        var intent = new IntentResult(IntentType.PlayMusic, 0.9f, null, "play music");
        var result = await _router.RouteAsync(intent);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task RouteAsync_PlayMusicIntent_NoTracksFound_ReturnsFailure()
    {
        _spotifyMock.Setup(s => s.SearchAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(new List<Track>());

        var searchParams = new SearchParams { Track = "Nonexistent", IsVague = false };
        var intent = new IntentResult(IntentType.PlayMusic, 0.9f, searchParams, "play nonexistent");
        var result = await _router.RouteAsync(intent);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task RouteAsync_PlayMusicIntent_NoActiveDevice_ReturnsFailure()
    {
        var tracks = new List<Track>
        {
            new() { Id = "t1", Name = "Song", Artist = "Artist", Uri = "spotify:track:t1" }
        };

        _spotifyMock.Setup(s => s.SearchAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(tracks);
        _spotifyMock.Setup(s => s.GetActiveDeviceIdAsync())
            .ReturnsAsync((string?)null);

        var searchParams = new SearchParams { Track = "Song", IsVague = false };
        var intent = new IntentResult(IntentType.PlayMusic, 0.9f, searchParams, "play song");
        var result = await _router.RouteAsync(intent);

        Assert.False(result.Success);
        Assert.Contains("device", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Unknown intent: clarification message ───────────────────────

    [Fact]
    public async Task RouteAsync_UnknownIntent_ReturnsClarificationMessage()
    {
        var intent = new IntentResult(IntentType.Unknown, 0.3f, null, "asdfghjkl");
        var result = await _router.RouteAsync(intent);

        Assert.False(result.Success);
        Assert.Contains("rephrase", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Unified CommandResult for all intent types ──────────────────

    [Theory]
    [InlineData(IntentType.GetNowPlaying)]
    [InlineData(IntentType.SetVolume)]
    public async Task RouteAsync_InformationalIntents_ReturnsValidCommandResult(IntentType intentType)
    {
        var intent = new IntentResult(intentType, 0.9f, null, "test");
        var result = await _router.RouteAsync(intent);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task RouteAsync_PlayMoreLikeThisIntent_WithSeedTrack_UsesRecommendations()
    {
        var recTracks = new List<Track>
        {
            new() { Id = "r1", Name = "Similar Song", Artist = "Similar Artist", Uri = "spotify:track:r1" }
        };

        _spotifyMock.Setup(s => s.GetRecommendationsAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(recTracks);
        _spotifyMock.Setup(s => s.GetActiveDeviceIdAsync())
            .ReturnsAsync("device-1");
        _spotifyMock.Setup(s => s.PlayAsync(recTracks, "device-1"))
            .ReturnsAsync(new PlaybackResult
            {
                Success = true,
                Message = "Playing similar tracks",
                NowPlaying = recTracks[0],
                QueueLength = 1
            });

        var searchParams = new SearchParams { SeedTrackId = "spotify:track:seed1", Genres = new List<string> { "rock" } };
        var intent = new IntentResult(IntentType.PlayMoreLikeThis, 0.9f, searchParams, "play more like that");
        var result = await _router.RouteAsync(intent);

        _spotifyMock.Verify(s => s.GetRecommendationsAsync(It.IsAny<SearchParams>()), Times.Once);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RouteAsync_PlayMoreLikeThisIntent_NoSeedTrack_ReturnsFailure()
    {
        var intent = new IntentResult(IntentType.PlayMoreLikeThis, 0.9f, null, "play more like that");
        var result = await _router.RouteAsync(intent);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }
}

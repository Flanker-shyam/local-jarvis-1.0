using Jarvis.Core.Models;
using Jarvis.Services.Response;

namespace Jarvis.Tests.Response;

/// <summary>
/// Unit tests for ResponseBuilder (Task 9.4).
/// Validates Requirements 8.1, 8.2.
/// </summary>
public class ResponseBuilderTests
{
    private readonly ResponseBuilder _sut = new();

    [Fact]
    public void BuildResponse_SuccessWithPlaybackResult_IncludesTrackNameAndArtist()
    {
        var result = new CommandResult
        {
            Success = true,
            Message = "Now playing",
            Data = new PlaybackResult
            {
                Success = true,
                Message = "Playing",
                NowPlaying = new Track
                {
                    Id = "1",
                    Name = "Bohemian Rhapsody",
                    Artist = "Queen",
                    Album = "A Night at the Opera",
                    Uri = "spotify:track:1"
                },
                QueueLength = 1
            }
        };

        var response = _sut.BuildResponse(result);

        Assert.Contains("Bohemian Rhapsody", response);
        Assert.Contains("Queen", response);
    }

    [Fact]
    public void BuildResponse_SuccessWithoutPlaybackResult_ReturnsMessage()
    {
        var result = new CommandResult
        {
            Success = true,
            Message = "Playback paused.",
            Data = null
        };

        var response = _sut.BuildResponse(result);

        Assert.Equal("Playback paused.", response);
    }

    [Fact]
    public void BuildResponse_SuccessWithEmptyMessage_ReturnsDone()
    {
        var result = new CommandResult
        {
            Success = true,
            Message = "",
            Data = null
        };

        var response = _sut.BuildResponse(result);

        Assert.NotEmpty(response);
        Assert.Equal("Done.", response);
    }

    [Fact]
    public void BuildResponse_Failure_IncludesRecoverySuggestion()
    {
        var result = new CommandResult
        {
            Success = false,
            Message = "No tracks found.",
            Data = null
        };

        var response = _sut.BuildResponse(result);

        Assert.Contains("No tracks found.", response);
        Assert.Contains("Try", response);
    }

    [Fact]
    public void BuildResponse_FailureWithEmptyMessage_ReturnsGenericRecovery()
    {
        var result = new CommandResult
        {
            Success = false,
            Message = "",
            Data = null
        };

        var response = _sut.BuildResponse(result);

        Assert.NotEmpty(response);
        Assert.Contains("Something went wrong", response);
    }

    [Fact]
    public void BuildResponse_NullResult_ReturnsNonEmptyString()
    {
        var response = _sut.BuildResponse(null!);

        Assert.NotEmpty(response);
    }

    [Fact]
    public void BuildResponse_SuccessWithPlaybackResultButNoNowPlaying_UsesMessage()
    {
        var result = new CommandResult
        {
            Success = true,
            Message = "Queued 5 tracks.",
            Data = new PlaybackResult
            {
                Success = true,
                Message = "Queued",
                NowPlaying = null,
                QueueLength = 5
            }
        };

        var response = _sut.BuildResponse(result);

        Assert.Equal("Queued 5 tracks.", response);
    }
}

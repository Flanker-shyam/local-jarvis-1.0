using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class PlayMoreLikeThisHandler : ICommandHandler
{
    private readonly ISpotifyService _spotify;

    public PlayMoreLikeThisHandler(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public async Task<CommandResult> HandleAsync(IntentResult intent)
    {
        var searchParams = intent.SearchParams;
        if (searchParams is null || string.IsNullOrWhiteSpace(searchParams.SeedTrackId))
        {
            return new CommandResult
            {
                Success = false,
                Message = "I don't have a reference track to base recommendations on. Play something first!",
                Data = null
            };
        }

        // Use seed track for recommendations
        var tracks = await _spotify.GetRecommendationsAsync(searchParams);

        if (tracks.Count == 0)
        {
            return new CommandResult
            {
                Success = false,
                Message = "I couldn't find similar tracks. Try playing something else first.",
                Data = null
            };
        }

        var deviceId = await _spotify.GetActiveDeviceIdAsync();
        if (deviceId is null)
        {
            return new CommandResult
            {
                Success = false,
                Message = "No active Spotify device found. Open Spotify on your phone first.",
                Data = null
            };
        }

        var playbackResult = await _spotify.PlayAsync(tracks, deviceId);

        return new CommandResult
        {
            Success = playbackResult.Success,
            Message = playbackResult.Message,
            Data = playbackResult
        };
    }
}

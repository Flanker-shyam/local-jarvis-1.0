using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class PlayMusicHandler : ICommandHandler
{
    private readonly ISpotifyService _spotify;

    public PlayMusicHandler(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public async Task<CommandResult> HandleAsync(IntentResult intent)
    {
        var searchParams = intent.SearchParams;
        if (searchParams is null)
        {
            return new CommandResult
            {
                Success = false,
                Message = "No search parameters provided. What would you like to listen to?",
                Data = null
            };
        }

        // Step 1: Search for tracks
        var tracks = await _spotify.SearchAsync(searchParams);

        // Step 2: If no tracks found, return failure
        if (tracks.Count == 0)
        {
            return new CommandResult
            {
                Success = false,
                Message = "I couldn't find anything matching that. Try being more specific?",
                Data = null
            };
        }

        // Step 3: Get active device
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

        // Step 4: Play tracks on active device
        var playbackResult = await _spotify.PlayAsync(tracks, deviceId);

        return new CommandResult
        {
            Success = playbackResult.Success,
            Message = playbackResult.Message,
            Data = playbackResult
        };
    }
}

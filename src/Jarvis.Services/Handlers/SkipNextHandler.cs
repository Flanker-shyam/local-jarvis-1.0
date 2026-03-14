using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class SkipNextHandler : ICommandHandler
{
    private readonly ISpotifyService _spotify;

    public SkipNextHandler(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public async Task<CommandResult> HandleAsync(IntentResult intent)
    {
        var result = await _spotify.SkipNextAsync();
        return new CommandResult
        {
            Success = result.Success,
            Message = result.Success ? "Skipping to next track." : result.Message,
            Data = result
        };
    }
}

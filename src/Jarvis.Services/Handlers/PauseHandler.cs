using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class PauseHandler : ICommandHandler
{
    private readonly ISpotifyService _spotify;

    public PauseHandler(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public async Task<CommandResult> HandleAsync(IntentResult intent)
    {
        var result = await _spotify.PauseAsync();
        return new CommandResult
        {
            Success = result.Success,
            Message = result.Success ? "Paused." : result.Message,
            Data = result
        };
    }
}

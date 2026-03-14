using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class SkipPreviousHandler : ICommandHandler
{
    private readonly ISpotifyService _spotify;

    public SkipPreviousHandler(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public async Task<CommandResult> HandleAsync(IntentResult intent)
    {
        var result = await _spotify.SkipPreviousAsync();
        return new CommandResult
        {
            Success = result.Success,
            Message = result.Success ? "Going back to previous track." : result.Message,
            Data = result
        };
    }
}

using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class GetNowPlayingHandler : ICommandHandler
{
    public Task<CommandResult> HandleAsync(IntentResult intent)
    {
        // GetNowPlaying is informational — the current SpotifyService interface
        // doesn't expose a "get current track" method, so we return a placeholder.
        // This can be expanded when the API method is added.
        return Task.FromResult(new CommandResult
        {
            Success = true,
            Message = "Now playing information is not yet available.",
            Data = null
        });
    }
}

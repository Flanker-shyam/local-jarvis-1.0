using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class SetVolumeHandler : ICommandHandler
{
    public Task<CommandResult> HandleAsync(IntentResult intent)
    {
        // SetVolume is not yet supported by the ISpotifyService interface.
        // Return a friendly message indicating the limitation.
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Message = "Volume control is not yet supported.",
            Data = null
        });
    }
}

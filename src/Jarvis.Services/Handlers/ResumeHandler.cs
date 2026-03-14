using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class ResumeHandler : ICommandHandler
{
    private readonly ISpotifyService _spotify;

    public ResumeHandler(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public async Task<CommandResult> HandleAsync(IntentResult intent)
    {
        var result = await _spotify.ResumeAsync();
        return new CommandResult
        {
            Success = result.Success,
            Message = result.Success ? "Resuming playback." : result.Message,
            Data = result
        };
    }
}

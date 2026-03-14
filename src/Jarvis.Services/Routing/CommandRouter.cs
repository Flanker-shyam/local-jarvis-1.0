using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Handlers;

namespace Jarvis.Services.Routing;

public class CommandRouter : ICommandRouter
{
    private readonly Dictionary<IntentType, ICommandHandler> _handlers = new();

    public CommandRouter(ISpotifyService spotifyService)
    {
        RegisterHandler(IntentType.PlayMusic, new PlayMusicHandler(spotifyService));
        RegisterHandler(IntentType.Pause, new PauseHandler(spotifyService));
        RegisterHandler(IntentType.Resume, new ResumeHandler(spotifyService));
        RegisterHandler(IntentType.SkipNext, new SkipNextHandler(spotifyService));
        RegisterHandler(IntentType.SkipPrevious, new SkipPreviousHandler(spotifyService));
        RegisterHandler(IntentType.PlayMoreLikeThis, new PlayMoreLikeThisHandler(spotifyService));
        RegisterHandler(IntentType.GetNowPlaying, new GetNowPlayingHandler());
        RegisterHandler(IntentType.SetVolume, new SetVolumeHandler());
        RegisterHandler(IntentType.Unknown, new UnknownHandler());
    }

    public void RegisterHandler(IntentType intentType, ICommandHandler handler)
    {
        _handlers[intentType] = handler;
    }

    public async Task<CommandResult> RouteAsync(IntentResult intent)
    {
        if (_handlers.TryGetValue(intent.IntentType, out var handler))
        {
            return await handler.HandleAsync(intent);
        }

        return new CommandResult
        {
            Success = false,
            Message = "I'm not sure what you mean. Could you rephrase?",
            Data = null
        };
    }
}

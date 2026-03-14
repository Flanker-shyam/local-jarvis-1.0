using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Handlers;

public class UnknownHandler : ICommandHandler
{
    public Task<CommandResult> HandleAsync(IntentResult intent)
    {
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Message = "I'm not sure what you mean. Could you rephrase?",
            Data = null
        });
    }
}

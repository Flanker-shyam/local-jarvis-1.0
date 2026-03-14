using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface ICommandHandler
{
    Task<CommandResult> HandleAsync(IntentResult intent);
}

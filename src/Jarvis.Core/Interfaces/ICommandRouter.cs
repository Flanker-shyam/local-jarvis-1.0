using Jarvis.Core.Enums;
using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface ICommandRouter
{
    Task<CommandResult> RouteAsync(IntentResult intent);
    void RegisterHandler(IntentType intentType, ICommandHandler handler);
}

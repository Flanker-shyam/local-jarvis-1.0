using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface IResponseBuilder
{
    string BuildResponse(CommandResult result);
}

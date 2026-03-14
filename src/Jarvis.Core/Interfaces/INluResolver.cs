using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface INluResolver
{
    Task<IntentResult> ResolveIntentAsync(string transcript, List<Turn> conversationHistory);
}

using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface ILlmClient
{
    Task<string> ChatAsync(string systemPrompt, List<ChatMessage> messages);
}

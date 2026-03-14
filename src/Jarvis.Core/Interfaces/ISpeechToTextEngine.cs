using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface ISpeechToTextEngine
{
    Task<TranscriptResult> TranscribeAsync(byte[] audioBuffer);
    Task StartListeningAsync();
    Task StopListeningAsync();
    bool IsListening { get; }
}

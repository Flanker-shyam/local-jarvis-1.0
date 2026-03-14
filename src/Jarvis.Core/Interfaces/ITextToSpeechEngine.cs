namespace Jarvis.Core.Interfaces;

public interface ITextToSpeechEngine
{
    Task SpeakAsync(string text);
}

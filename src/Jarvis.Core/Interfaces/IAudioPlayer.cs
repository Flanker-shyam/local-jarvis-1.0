namespace Jarvis.Core.Interfaces;

/// <summary>
/// Abstraction for playing audio data through the device speaker.
/// MAUI implementations use platform APIs; tests can mock this interface.
/// </summary>
public interface IAudioPlayer
{
    /// <summary>Plays the given WAV audio bytes through the device speaker.</summary>
    Task PlayAsync(byte[] audioData);
}

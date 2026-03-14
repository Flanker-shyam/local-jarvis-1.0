namespace Jarvis.Core.Interfaces;

/// <summary>
/// Abstraction for platform-specific microphone audio capture.
/// MAUI implementations use platform APIs; tests can mock this interface.
/// </summary>
public interface IAudioRecorder
{
    /// <summary>Starts capturing audio from the device microphone.</summary>
    Task StartAsync();

    /// <summary>Stops capturing and returns the recorded audio as a WAV byte buffer.</summary>
    Task<byte[]> StopAsync();

    /// <summary>Whether the recorder is currently capturing audio.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Returns the RMS (root mean square) level of the most recent audio frame.
    /// Value is in the range [0.0, 1.0] where 0.0 is silence.
    /// </summary>
    float CurrentRmsLevel { get; }
}

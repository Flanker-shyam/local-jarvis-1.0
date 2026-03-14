namespace Jarvis.Core.Models;

public class PlaybackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Track? NowPlaying { get; set; }
    public int QueueLength { get; set; }
}

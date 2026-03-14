namespace Jarvis.Core.Models;

public class Track
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public int DurationMs { get; set; }
}

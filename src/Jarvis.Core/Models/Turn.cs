namespace Jarvis.Core.Models;

public class Turn
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public IntentResult? Intent { get; set; }
}

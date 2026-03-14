namespace Jarvis.Core.Models;

public class TranscriptResult
{
    public string Text { get; set; } = string.Empty;

    private float _confidence;
    public float Confidence
    {
        get => _confidence;
        set
        {
            if (value < 0.0f || value > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(Confidence), value, "Confidence must be between 0.0 and 1.0.");
            _confidence = value;
        }
    }
}

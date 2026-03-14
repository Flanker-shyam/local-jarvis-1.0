using Jarvis.Core.Enums;

namespace Jarvis.Core.Models;

public class IntentResult
{
    public IntentType IntentType { get; }

    private readonly float _confidence;
    public float Confidence => _confidence;

    public SearchParams? SearchParams { get; }
    public string RawTranscript { get; }

    public IntentResult(IntentType intentType, float confidence, SearchParams? searchParams, string rawTranscript)
    {
        if (confidence < 0.0f || confidence > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0.0 and 1.0.");

        IntentType = intentType;
        _confidence = confidence;
        SearchParams = searchParams;
        RawTranscript = rawTranscript ?? throw new ArgumentNullException(nameof(rawTranscript));
    }
}

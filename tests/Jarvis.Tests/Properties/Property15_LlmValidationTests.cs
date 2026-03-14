using System.Text.Json;
using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Xunit;
using Jarvis.Services.Nlu;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 15: LLM response validation rejects malformed input.
/// For any LLM response string not conforming to expected JSON schema,
/// the validator rejects it and prevents it from being parsed into an IntentResult.
///
/// **Validates: Requirements 11.3**
/// </summary>
public class Property15_LlmValidationTests
{
    private static readonly string[] ValidIntents =
    {
        "PLAY_MUSIC", "PAUSE", "RESUME", "SKIP_NEXT", "SKIP_PREVIOUS",
        "SET_VOLUME", "GET_NOW_PLAYING", "PLAY_MORE_LIKE_THIS", "UNKNOWN"
    };

    // ---------------------------------------------------------------
    // Scenario 1: Arbitrary strings that are NOT valid JSON → false
    // ---------------------------------------------------------------
    [Property(MaxTest = 100)]
    public Property ArbitraryNonJsonStrings_AreRejected(NonEmptyString input)
    {
        var raw = input.Get;

        Func<bool> property = () =>
        {
            // Skip strings that happen to be valid JSON
            try
            {
                JsonNode.Parse(raw);
                return true; // vacuously true – this is valid JSON, not our target
            }
            catch (JsonException)
            {
                // Good – it's not valid JSON, so validator must reject
            }

            var result = NluResolver.ValidateLlmResponse(raw, out var node);
            return result == false && node == null;
        };

        return property.ToProperty();
    }

    // ---------------------------------------------------------------
    // Scenario 2: Valid JSON but missing "intent" field → false
    // ---------------------------------------------------------------
    [Property(MaxTest = 100)]
    public Property ValidJson_MissingIntent_IsRejected()
    {
        var gen = from confidence in Gen.Choose(0, 100).Select(i => i / 100.0)
                  select confidence;

        return Prop.ForAll(gen.ToArbitrary(), confidence =>
        {
            var json = JsonSerializer.Serialize(new { confidence });
            var result = NluResolver.ValidateLlmResponse(json, out _);
            return (!result).ToProperty();
        });
    }

    // ---------------------------------------------------------------
    // Scenario 3: Valid JSON but missing "confidence" field → false
    // ---------------------------------------------------------------
    [Property(MaxTest = 100)]
    public Property ValidJson_MissingConfidence_IsRejected()
    {
        var gen = Gen.Elements(ValidIntents);

        return Prop.ForAll(gen.ToArbitrary(), intent =>
        {
            var json = JsonSerializer.Serialize(new { intent });
            var result = NluResolver.ValidateLlmResponse(json, out _);
            return (!result).ToProperty();
        });
    }

    // ---------------------------------------------------------------
    // Scenario 4: Valid JSON with "intent" not in the valid set → false
    // ---------------------------------------------------------------
    [Property(MaxTest = 100)]
    public Property ValidJson_InvalidIntent_IsRejected(NonEmptyString randomIntent)
    {
        var intentStr = randomIntent.Get;

        Func<bool> property = () =>
        {
            // Skip if the random string happens to be a valid intent (case-insensitive)
            if (ValidIntents.Any(v => string.Equals(v, intentStr, StringComparison.OrdinalIgnoreCase)))
                return true; // vacuously true

            var json = JsonSerializer.Serialize(new { intent = intentStr, confidence = 0.5 });
            var result = NluResolver.ValidateLlmResponse(json, out _);
            return result == false;
        };

        return property.ToProperty();
    }

    // ---------------------------------------------------------------
    // Scenario 5: Valid JSON with "confidence" outside [0.0, 1.0] → false
    // ---------------------------------------------------------------
    [Property(MaxTest = 100)]
    public Property ValidJson_ConfidenceOutOfRange_IsRejected()
    {
        // Generate confidence values that are strictly outside [0.0, 1.0]
        var gen = Gen.OneOf(
            Gen.Choose(-1000, -1).Select(i => i / 100.0),   // negative values
            Gen.Choose(101, 1000).Select(i => i / 100.0)     // values > 1.0
        );

        return Prop.ForAll(gen.ToArbitrary(), confidence =>
        {
            var intent = ValidIntents[0]; // use a known valid intent
            var json = JsonSerializer.Serialize(new { intent, confidence });
            var result = NluResolver.ValidateLlmResponse(json, out _);
            return (!result).ToProperty();
        });
    }

    // ---------------------------------------------------------------
    // Scenario 6: Valid JSON with correct schema → true (positive control)
    // ---------------------------------------------------------------
    [Property(MaxTest = 100)]
    public Property ValidJson_CorrectSchema_IsAccepted()
    {
        var gen = from intent in Gen.Elements(ValidIntents)
                  from confidenceInt in Gen.Choose(0, 100)
                  let confidence = confidenceInt / 100.0
                  select new { intent, confidence };

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var json = JsonSerializer.Serialize(new { intent = input.intent, confidence = input.confidence });
            var result = NluResolver.ValidateLlmResponse(json, out var node);
            return (result && node != null).ToProperty();
        });
    }
}

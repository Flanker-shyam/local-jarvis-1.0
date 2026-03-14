using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Nlu;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 2: Intent resolution returns valid intent type and bounded confidence.
/// For any non-empty transcript string, ResolveIntentAsync returns an IntentResult
/// with IntentType from the enum and confidence in [0.0, 1.0].
///
/// **Validates: Requirements 2.1**
/// </summary>
public class Property2_IntentResolutionTests
{
    private static readonly string[] ValidIntentStrings =
    {
        "PLAY_MUSIC", "PAUSE", "RESUME", "SKIP_NEXT", "SKIP_PREVIOUS",
        "SET_VOLUME", "GET_NOW_PLAYING", "PLAY_MORE_LIKE_THIS", "UNKNOWN"
    };

    private static readonly IntentType[] AllIntentTypes = Enum.GetValues<IntentType>();

    /// <summary>
    /// Custom Arbitrary that generates non-empty, non-whitespace transcript strings.
    /// </summary>
    public static Arbitrary<NonEmptyString> Arb_NonEmptyString()
    {
        return Arb.Default.NonEmptyString();
    }

    [Property(MaxTest = 100)]
    public Property IntentResolution_ReturnsValidIntentType_AndBoundedConfidence(NonEmptyString nonEmptyTranscript)
    {
        var transcript = nonEmptyTranscript.Get;

        // Filter out whitespace-only strings
        Func<bool> property = () =>
        {
            if (string.IsNullOrWhiteSpace(transcript))
                return true; // vacuously true for whitespace; FsCheck may generate these

            // Pick a random valid intent and confidence for the mock LLM response
            var rng = new System.Random(transcript.GetHashCode());
            var intentString = ValidIntentStrings[rng.Next(ValidIntentStrings.Length)];
            var confidence = (float)Math.Round(rng.NextDouble(), 2);

            var llmJson = BuildLlmResponse(intentString, confidence);

            var mockLlmClient = new Mock<ILlmClient>();
            mockLlmClient
                .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
                .ReturnsAsync(llmJson);

            var mockConversationStore = new Mock<IConversationStore>();
            mockConversationStore
                .Setup(s => s.GetRecentTurns(It.IsAny<int>()))
                .Returns(new List<Turn>());
            mockConversationStore
                .Setup(s => s.GetLastPlayedTrack())
                .Returns((Track?)null);

            var resolver = new NluResolver(mockLlmClient.Object, mockConversationStore.Object);

            var result = resolver.ResolveIntentAsync(transcript, new List<Turn>()).GetAwaiter().GetResult();

            // Assert: IntentType is a valid enum value
            var isValidIntentType = Enum.IsDefined(typeof(IntentType), result.IntentType);

            // Assert: Confidence is in [0.0, 1.0]
            var isConfidenceBounded = result.Confidence >= 0.0f && result.Confidence <= 1.0f;

            return isValidIntentType && isConfidenceBounded;
        };

        return property.When(!string.IsNullOrWhiteSpace(transcript));
    }

    private static string BuildLlmResponse(string intentString, float confidence)
    {
        var responseObj = new Dictionary<string, object>
        {
            ["intent"] = intentString,
            ["confidence"] = confidence
        };

        // Add searchParams for PLAY_MUSIC and PLAY_MORE_LIKE_THIS to satisfy downstream logic
        if (intentString is "PLAY_MUSIC" or "PLAY_MORE_LIKE_THIS")
        {
            responseObj["searchParams"] = new Dictionary<string, object>
            {
                ["query"] = "test query",
                ["isVague"] = false
            };
        }

        return JsonSerializer.Serialize(responseObj);
    }
}

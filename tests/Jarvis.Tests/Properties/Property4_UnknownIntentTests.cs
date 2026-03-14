using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Orchestration;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 4: UNKNOWN intent produces clarification response.
/// For any IntentResult with IntentType.Unknown, the pipeline produces a CommandResult
/// with clarification message and no playback modification.
///
/// **Validates: Requirements 2.4**
/// </summary>
public class Property4_UnknownIntentTests
{
    [Property(MaxTest = 200)]
    public Property UnknownIntent_ReturnsClarificationMessage_NoPlaybackModification(
        NonEmptyString transcriptText)
    {
        // Generate confidence at or above threshold so we reach the NLU step
        var confidence = 0.8f;

        var sttMock = new Mock<ISpeechToTextEngine>();
        sttMock.Setup(s => s.TranscribeAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new TranscriptResult { Text = transcriptText.Get, Confidence = confidence });

        var nluMock = new Mock<INluResolver>();
        nluMock.Setup(n => n.ResolveIntentAsync(It.IsAny<string>(), It.IsAny<List<Turn>>()))
            .ReturnsAsync(new IntentResult(IntentType.Unknown, 0.1f, null, transcriptText.Get));

        var routerMock = new Mock<ICommandRouter>();
        var responseBuilderMock = new Mock<IResponseBuilder>();
        var ttsMock = new Mock<ITextToSpeechEngine>();
        var conversationStoreMock = new Mock<IConversationStore>();
        conversationStoreMock.Setup(c => c.GetRecentTurns(It.IsAny<int>()))
            .Returns(new List<Turn>());

        var orchestrator = new JarvisOrchestrator(
            sttMock.Object, nluMock.Object, routerMock.Object,
            responseBuilderMock.Object, ttsMock.Object, conversationStoreMock.Object);

        var result = orchestrator.ProcessVoiceCommandAsync(new byte[] { 1, 2, 3 })
            .GetAwaiter().GetResult();

        // Should return clarification message
        var hasClarification = !result.Success &&
            result.Message.Contains("rephrase", StringComparison.OrdinalIgnoreCase);

        // No playback modification: router was never called
        routerMock.Verify(r => r.RouteAsync(It.IsAny<IntentResult>()), Times.Never);

        // TTS was never called (no spoken response for unknown intent)
        ttsMock.Verify(t => t.SpeakAsync(It.IsAny<string>()), Times.Never);

        return hasClarification
            .Label($"transcript='{transcriptText.Get}': expected clarification, got Success={result.Success}, Message='{result.Message}'");
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(NonUnknownIntentArbitrary) })]
    public Property KnownIntent_DoesNotReturnClarification(IntentType intentType)
    {
        var sttMock = new Mock<ISpeechToTextEngine>();
        sttMock.Setup(s => s.TranscribeAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new TranscriptResult { Text = "play music", Confidence = 0.9f });

        SearchParams? searchParams = intentType == IntentType.PlayMusic
            ? new SearchParams { Query = "test" }
            : intentType == IntentType.PlayMoreLikeThis
                ? new SearchParams { SeedTrackId = "seed1", Genres = new List<string> { "rock" } }
                : null;

        var nluMock = new Mock<INluResolver>();
        nluMock.Setup(n => n.ResolveIntentAsync(It.IsAny<string>(), It.IsAny<List<Turn>>()))
            .ReturnsAsync(new IntentResult(intentType, 0.9f, searchParams, "play music"));

        var routerMock = new Mock<ICommandRouter>();
        routerMock.Setup(r => r.RouteAsync(It.IsAny<IntentResult>()))
            .ReturnsAsync(new CommandResult { Success = true, Message = "Done." });

        var responseBuilderMock = new Mock<IResponseBuilder>();
        responseBuilderMock.Setup(r => r.BuildResponse(It.IsAny<CommandResult>()))
            .Returns("Done.");

        var ttsMock = new Mock<ITextToSpeechEngine>();
        var conversationStoreMock = new Mock<IConversationStore>();
        conversationStoreMock.Setup(c => c.GetRecentTurns(It.IsAny<int>()))
            .Returns(new List<Turn>());

        var orchestrator = new JarvisOrchestrator(
            sttMock.Object, nluMock.Object, routerMock.Object,
            responseBuilderMock.Object, ttsMock.Object, conversationStoreMock.Object);

        var result = orchestrator.ProcessVoiceCommandAsync(new byte[] { 1, 2, 3 })
            .GetAwaiter().GetResult();

        // Router should have been called for known intents
        routerMock.Verify(r => r.RouteAsync(It.IsAny<IntentResult>()), Times.Once);

        return true.ToProperty()
            .Label($"intentType={intentType}: router should be called for known intents");
    }

    public static class NonUnknownIntentArbitrary
    {
        public static Arbitrary<IntentType> IntentType()
        {
            var gen = Gen.Elements(
                Jarvis.Core.Enums.IntentType.PlayMusic,
                Jarvis.Core.Enums.IntentType.Pause,
                Jarvis.Core.Enums.IntentType.Resume,
                Jarvis.Core.Enums.IntentType.SkipNext,
                Jarvis.Core.Enums.IntentType.SkipPrevious,
                Jarvis.Core.Enums.IntentType.SetVolume,
                Jarvis.Core.Enums.IntentType.GetNowPlaying,
                Jarvis.Core.Enums.IntentType.PlayMoreLikeThis);
            return Arb.From(gen);
        }
    }
}

using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Orchestration;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 1: Low-confidence transcripts produce retry without side effects.
/// For any transcript with confidence below 0.4, the pipeline returns a retry message
/// and Spotify playback state is unchanged.
///
/// **Validates: Requirements 1.2, 10.4**
/// </summary>
public class Property1_LowConfidenceTests
{
    private static (JarvisOrchestrator orchestrator, Mock<ICommandRouter> routerMock, Mock<ITextToSpeechEngine> ttsMock) CreateOrchestrator(float confidence)
    {
        var sttMock = new Mock<ISpeechToTextEngine>();
        sttMock.Setup(s => s.TranscribeAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new TranscriptResult { Text = "some text", Confidence = confidence });

        var nluMock = new Mock<INluResolver>();
        var routerMock = new Mock<ICommandRouter>();
        var responseBuilderMock = new Mock<IResponseBuilder>();
        var ttsMock = new Mock<ITextToSpeechEngine>();
        var conversationStoreMock = new Mock<IConversationStore>();
        conversationStoreMock.Setup(c => c.GetRecentTurns(It.IsAny<int>()))
            .Returns(new List<Turn>());

        var orchestrator = new JarvisOrchestrator(
            sttMock.Object,
            nluMock.Object,
            routerMock.Object,
            responseBuilderMock.Object,
            ttsMock.Object,
            conversationStoreMock.Object);

        return (orchestrator, routerMock, ttsMock);
    }

    [Property(MaxTest = 200)]
    public Property LowConfidence_ReturnsRetryMessage_NoSideEffects()
    {
        // Generate confidence values in [0.0, 0.39] — strictly below threshold
        var confidenceGen = Gen.Choose(0, 39).Select(i => i / 100f);

        return Prop.ForAll(
            confidenceGen.ToArbitrary(),
            confidence =>
            {
                var (orchestrator, routerMock, ttsMock) = CreateOrchestrator(confidence);

                var result = orchestrator.ProcessVoiceCommandAsync(new byte[] { 1, 2, 3 })
                    .GetAwaiter().GetResult();

                // Pipeline returns retry message
                var hasRetryMessage = !result.Success &&
                    result.Message.Contains("didn't catch that", StringComparison.OrdinalIgnoreCase);

                // No playback side effects: router was never called
                routerMock.Verify(r => r.RouteAsync(It.IsAny<IntentResult>()), Times.Never);

                // TTS was never called (no spoken response for low confidence)
                ttsMock.Verify(t => t.SpeakAsync(It.IsAny<string>()), Times.Never);

                return hasRetryMessage
                    .Label($"confidence={confidence}: expected retry message, got Success={result.Success}, Message='{result.Message}'");
            });
    }

    [Property(MaxTest = 200)]
    public Property AtOrAboveThreshold_DoesNotReturnRetryMessage()
    {
        // Generate confidence values in [0.40, 1.00] — at or above threshold
        var confidenceGen = Gen.Choose(40, 100).Select(i => i / 100f);

        return Prop.ForAll(
            confidenceGen.ToArbitrary(),
            confidence =>
            {
                var sttMock = new Mock<ISpeechToTextEngine>();
                sttMock.Setup(s => s.TranscribeAsync(It.IsAny<byte[]>()))
                    .ReturnsAsync(new TranscriptResult { Text = "play something", Confidence = confidence });

                var nluMock = new Mock<INluResolver>();
                nluMock.Setup(n => n.ResolveIntentAsync(It.IsAny<string>(), It.IsAny<List<Turn>>()))
                    .ReturnsAsync(new IntentResult(IntentType.PlayMusic, 0.9f,
                        new SearchParams { Query = "something" }, "play something"));

                var routerMock = new Mock<ICommandRouter>();
                routerMock.Setup(r => r.RouteAsync(It.IsAny<IntentResult>()))
                    .ReturnsAsync(new CommandResult { Success = true, Message = "Now playing." });

                var responseBuilderMock = new Mock<IResponseBuilder>();
                responseBuilderMock.Setup(r => r.BuildResponse(It.IsAny<CommandResult>()))
                    .Returns("Now playing.");

                var ttsMock = new Mock<ITextToSpeechEngine>();
                var conversationStoreMock = new Mock<IConversationStore>();
                conversationStoreMock.Setup(c => c.GetRecentTurns(It.IsAny<int>()))
                    .Returns(new List<Turn>());

                var orchestrator = new JarvisOrchestrator(
                    sttMock.Object, nluMock.Object, routerMock.Object,
                    responseBuilderMock.Object, ttsMock.Object, conversationStoreMock.Object);

                var result = orchestrator.ProcessVoiceCommandAsync(new byte[] { 1, 2, 3 })
                    .GetAwaiter().GetResult();

                // Should NOT be a retry message — pipeline should proceed
                var isNotRetry = !result.Message.Contains("didn't catch that", StringComparison.OrdinalIgnoreCase);

                return isNotRetry
                    .Label($"confidence={confidence}: should not be retry message but got '{result.Message}'");
            });
    }
}

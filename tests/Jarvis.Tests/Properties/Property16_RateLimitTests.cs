using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Orchestration;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 16: Rate limiting enforces command cap.
/// For any sequence of voice commands exceeding 30 within one minute,
/// commands beyond the limit are rejected.
///
/// **Validates: Requirements 11.4**
/// </summary>
public class Property16_RateLimitTests
{
    private static JarvisOrchestrator CreateOrchestrator()
    {
        var sttMock = new Mock<ISpeechToTextEngine>();
        sttMock.Setup(s => s.TranscribeAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new TranscriptResult { Text = "play music", Confidence = 0.9f });

        var nluMock = new Mock<INluResolver>();
        nluMock.Setup(n => n.ResolveIntentAsync(It.IsAny<string>(), It.IsAny<List<Turn>>()))
            .ReturnsAsync(new IntentResult(IntentType.Pause, 0.9f, null, "pause"));

        var routerMock = new Mock<ICommandRouter>();
        routerMock.Setup(r => r.RouteAsync(It.IsAny<IntentResult>()))
            .ReturnsAsync(new CommandResult { Success = true, Message = "Paused." });

        var responseBuilderMock = new Mock<IResponseBuilder>();
        responseBuilderMock.Setup(r => r.BuildResponse(It.IsAny<CommandResult>()))
            .Returns("Paused.");

        var ttsMock = new Mock<ITextToSpeechEngine>();
        var conversationStoreMock = new Mock<IConversationStore>();
        conversationStoreMock.Setup(c => c.GetRecentTurns(It.IsAny<int>()))
            .Returns(new List<Turn>());

        return new JarvisOrchestrator(
            sttMock.Object, nluMock.Object, routerMock.Object,
            responseBuilderMock.Object, ttsMock.Object, conversationStoreMock.Object);
    }

    [Property(MaxTest = 50)]
    public Property CommandsBeyondLimit_AreRejected(PositiveInt extraCommands)
    {
        var orchestrator = CreateOrchestrator();
        var audio = new byte[] { 1, 2, 3 };
        var extra = Math.Min(extraCommands.Get, 20); // cap for test speed

        // Send exactly 30 commands (the limit)
        for (int i = 0; i < 30; i++)
        {
            var r = orchestrator.ProcessVoiceCommandAsync(audio).GetAwaiter().GetResult();
            // First 30 should succeed (not be rate-limited)
            if (r.Message.Contains("Too many commands", StringComparison.OrdinalIgnoreCase))
                return false.Label($"Command {i + 1} was rate-limited but should not have been");
        }

        // Commands beyond 30 should be rejected
        var allRejected = true;
        for (int i = 0; i < extra; i++)
        {
            var result = orchestrator.ProcessVoiceCommandAsync(audio).GetAwaiter().GetResult();
            if (!result.Message.Contains("Too many commands", StringComparison.OrdinalIgnoreCase))
            {
                allRejected = false;
                break;
            }
        }

        return allRejected.Label($"Expected commands 31-{30 + extra} to be rejected");
    }

    [Fact]
    public void TryAcquireRateLimit_AllowsExactly30Commands()
    {
        var orchestrator = CreateOrchestrator();

        for (int i = 0; i < 30; i++)
        {
            Assert.True(orchestrator.TryAcquireRateLimit(), $"Command {i + 1} should be allowed");
        }

        Assert.False(orchestrator.TryAcquireRateLimit(), "Command 31 should be rejected");
    }

    [Fact]
    public void TryAcquireRateLimit_RejectsCommand31()
    {
        var orchestrator = CreateOrchestrator();

        // Fill up the rate limit
        for (int i = 0; i < 30; i++)
        {
            orchestrator.TryAcquireRateLimit();
        }

        // 31st should be rejected
        var result = orchestrator.ProcessVoiceCommandAsync(new byte[] { 1 }).GetAwaiter().GetResult();
        Assert.False(result.Success);
        Assert.Contains("Too many commands", result.Message);
    }
}

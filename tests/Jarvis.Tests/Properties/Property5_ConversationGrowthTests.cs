using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Models;
using Jarvis.Services.Conversation;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 5: Conversation history grows by two per command.
/// For any successfully processed voice command, conversation history length increases by exactly 2.
/// **Validates: Requirements 3.1**
/// </summary>
public class Property5_ConversationGrowthTests
{
    [Property]
    public Property AddingUserAndAssistantTurn_IncreasesHistoryByTwo(NonEmptyString userContent, NonEmptyString assistantContent)
    {
        var store = new ConversationStore();

        var beforeCount = store.GetRecentTurns(int.MaxValue).Count;

        // Simulate a processed voice command: one user turn + one assistant turn
        store.AddTurn(new Turn
        {
            Role = "user",
            Content = userContent.Get,
            Timestamp = DateTime.UtcNow,
            Intent = new IntentResult(IntentType.PlayMusic, 0.9f,
                new SearchParams { Query = "test" }, userContent.Get)
        });

        store.AddTurn(new Turn
        {
            Role = "assistant",
            Content = assistantContent.Get,
            Timestamp = DateTime.UtcNow,
            Intent = null
        });

        var afterCount = store.GetRecentTurns(int.MaxValue).Count;

        return (afterCount - beforeCount == 2).ToProperty();
    }

    [Property(Arbitrary = new[] { typeof(IntentTypeArbitrary) })]
    public Property MultipleCommands_EachGrowsByTwo(IntentType intentType, PositiveInt commandCount)
    {
        var store = new ConversationStore();
        var count = Math.Min(commandCount.Get, 20); // cap to keep test fast

        for (int i = 0; i < count; i++)
        {
            store.AddTurn(new Turn
            {
                Role = "user",
                Content = $"Command {i}",
                Timestamp = DateTime.UtcNow,
                Intent = new IntentResult(intentType, 0.8f, null, $"Command {i}")
            });

            store.AddTurn(new Turn
            {
                Role = "assistant",
                Content = $"Response {i}",
                Timestamp = DateTime.UtcNow,
                Intent = null
            });
        }

        var totalTurns = store.GetRecentTurns(int.MaxValue).Count;
        return (totalTurns == count * 2).ToProperty();
    }

    public static class IntentTypeArbitrary
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
                Jarvis.Core.Enums.IntentType.PlayMoreLikeThis,
                Jarvis.Core.Enums.IntentType.Unknown);
            return Arb.From(gen);
        }
    }
}

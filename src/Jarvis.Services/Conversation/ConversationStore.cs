using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Conversation;

/// <summary>
/// In-memory conversation store that tracks user and assistant turns.
/// Supports retrieving recent turns for LLM context and finding the last played track.
/// </summary>
public class ConversationStore : IConversationStore
{
    private readonly List<Turn> _turns = new();
    private readonly object _lock = new();

    public void AddTurn(Turn turn)
    {
        if (turn is null)
            throw new ArgumentNullException(nameof(turn));

        lock (_lock)
        {
            _turns.Add(turn);
        }
    }

    public List<Turn> GetRecentTurns(int count)
    {
        if (count <= 0)
            return new List<Turn>();

        lock (_lock)
        {
            var skip = Math.Max(0, _turns.Count - count);
            return _turns.Skip(skip).Take(count).ToList();
        }
    }

    public Track? GetLastPlayedTrack()
    {
        lock (_lock)
        {
            // Scan from most recent to oldest for a turn with a PlayMusic or PlayMoreLikeThis intent
            // that has a played track in its data
            for (int i = _turns.Count - 1; i >= 0; i--)
            {
                var turn = _turns[i];
                if (turn.Intent is null)
                    continue;

                if (turn.Intent.IntentType is IntentType.PlayMusic or IntentType.PlayMoreLikeThis)
                {
                    // The SearchParams might contain a SeedTrackId, but we need the actual played track.
                    // The played track info is typically stored in the assistant turn's content or
                    // we look for a turn that has intent with SearchParams containing track info.
                    if (turn.Intent.SearchParams?.Track is not null)
                    {
                        return new Track
                        {
                            Id = turn.Intent.SearchParams.SeedTrackId ?? string.Empty,
                            Name = turn.Intent.SearchParams.Track,
                            Artist = turn.Intent.SearchParams.Artist ?? string.Empty
                        };
                    }
                }
            }

            return null;
        }
    }
}

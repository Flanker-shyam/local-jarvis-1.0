using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface IConversationStore
{
    List<Turn> GetRecentTurns(int count);
    void AddTurn(Turn turn);
    Track? GetLastPlayedTrack();
}

using Jarvis.Core.Models;

namespace Jarvis.Core.Interfaces;

public interface ISpotifyService
{
    Task<List<Track>> SearchAsync(SearchParams searchParams);
    Task<PlaybackResult> PlayAsync(List<Track> tracks, string deviceId);
    Task<PlaybackResult> PauseAsync();
    Task<PlaybackResult> ResumeAsync();
    Task<PlaybackResult> SkipNextAsync();
    Task<PlaybackResult> SkipPreviousAsync();
    Task<List<Track>> GetRecommendationsAsync(SearchParams searchParams);
    Task<string?> GetActiveDeviceIdAsync();
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Spotify;

public class SpotifyService : ISpotifyService
{
    private const string SpotifyApiBase = "https://api.spotify.com/v1";

    private readonly HttpClient _httpClient;
    private readonly IAuthManager _authManager;

    public SpotifyService(HttpClient httpClient, IAuthManager authManager)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
    }

    // ── Search & Recommendations (Task 6.6) ──────────────────────────

    public async Task<List<Track>> SearchAsync(SearchParams searchParams)
    {
        if (searchParams.IsVague)
        {
            return await GetRecommendationsAsync(searchParams);
        }

        // Non-vague: direct search
        var query = BuildDirectQuery(searchParams);
        if (string.IsNullOrWhiteSpace(query))
            return new List<Track>();

        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{SpotifyApiBase}/search?q={encodedQuery}&type=track&limit=10";

        var response = await SendWithAuthAsync(HttpMethod.Get, url);
        if (response == null || !response.IsSuccessStatusCode)
            return new List<Track>();

        var tracks = await ParseSearchResponseAsync(response);

        // Fallback to recommendations if direct search returns no results
        if (tracks.Count == 0)
        {
            return await GetRecommendationsAsync(searchParams);
        }

        return tracks;
    }

    public async Task<List<Track>> GetRecommendationsAsync(SearchParams searchParams)
    {
        var genres = searchParams.Genres is { Count: > 0 }
            ? string.Join(",", searchParams.Genres)
            : "pop";

        var energy = searchParams.Energy ?? 0.5f;
        var valence = MoodToValence(searchParams.Mood);

        var url = $"{SpotifyApiBase}/recommendations" +
            $"?seed_genres={Uri.EscapeDataString(genres)}" +
            $"&target_energy={energy:F2}" +
            $"&target_valence={valence:F2}" +
            $"&limit=20";

        var response = await SendWithAuthAsync(HttpMethod.Get, url);
        if (response == null || !response.IsSuccessStatusCode)
            return new List<Track>();

        return await ParseRecommendationsResponseAsync(response);
    }

    // ── Playback Control (Task 6.7) ──────────────────────────────────

    public async Task<PlaybackResult> PlayAsync(List<Track> tracks, string deviceId)
    {
        var uris = tracks.Select(t => t.Uri).ToList();
        var body = JsonSerializer.Serialize(new { uris });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var url = $"{SpotifyApiBase}/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";
        var response = await SendWithAuthAsync(HttpMethod.Put, url, content);

        if (response == null)
            return new PlaybackResult { Success = false, Message = "Authentication failed. Please re-connect your Spotify account." };

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new PlaybackResult { Success = false, Message = "Playback control requires Spotify Premium. I can still search for songs though." };

        if (!response.IsSuccessStatusCode)
            return new PlaybackResult { Success = false, Message = $"Playback failed with status {response.StatusCode}." };

        return new PlaybackResult
        {
            Success = true,
            Message = tracks.Count > 0
                ? $"Now playing {tracks[0].Name} by {tracks[0].Artist}"
                : "Playback started.",
            NowPlaying = tracks.FirstOrDefault(),
            QueueLength = tracks.Count
        };
    }

    public async Task<PlaybackResult> PauseAsync()
    {
        var url = $"{SpotifyApiBase}/me/player/pause";
        var response = await SendWithAuthAsync(HttpMethod.Put, url);
        return BuildPlaybackResult(response, "Paused.", "Failed to pause playback.");
    }

    public async Task<PlaybackResult> ResumeAsync()
    {
        var url = $"{SpotifyApiBase}/me/player/play";
        var response = await SendWithAuthAsync(HttpMethod.Put, url);
        return BuildPlaybackResult(response, "Resumed playback.", "Failed to resume playback.");
    }

    public async Task<PlaybackResult> SkipNextAsync()
    {
        var url = $"{SpotifyApiBase}/me/player/next";
        var response = await SendWithAuthAsync(HttpMethod.Post, url);
        return BuildPlaybackResult(response, "Skipping to next track.", "Failed to skip to next track.");
    }

    public async Task<PlaybackResult> SkipPreviousAsync()
    {
        var url = $"{SpotifyApiBase}/me/player/previous";
        var response = await SendWithAuthAsync(HttpMethod.Post, url);
        return BuildPlaybackResult(response, "Skipping to previous track.", "Failed to skip to previous track.");
    }

    public async Task<string?> GetActiveDeviceIdAsync()
    {
        var url = $"{SpotifyApiBase}/me/player/devices";
        var response = await SendWithAuthAsync(HttpMethod.Get, url);

        if (response == null || !response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("devices", out var devices))
            return null;

        foreach (var device in devices.EnumerateArray())
        {
            if (device.TryGetProperty("is_active", out var isActive) && isActive.GetBoolean())
            {
                if (device.TryGetProperty("id", out var id))
                    return id.GetString();
            }
        }

        return null;
    }

    // ── Static helpers (existing from tasks 6.1, 6.4) ────────────────

    /// <summary>
    /// Constructs a Spotify search query string from the given SearchParams.
    /// Uses field filter syntax for explicit fields, falls back to raw query,
    /// then to inferred attributes (genres, mood, context).
    /// </summary>
    public static string BuildDirectQuery(SearchParams searchParams)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(searchParams.Track))
            parts.Add($"track:{searchParams.Track}");

        if (!string.IsNullOrWhiteSpace(searchParams.Artist))
            parts.Add($"artist:{searchParams.Artist}");

        if (!string.IsNullOrWhiteSpace(searchParams.Album))
            parts.Add($"album:{searchParams.Album}");

        if (parts.Count > 0)
            return string.Join(" ", parts);

        // Fallback to raw query
        if (!string.IsNullOrWhiteSpace(searchParams.Query))
            return searchParams.Query;

        // Build from inferred attributes
        var queryParts = new List<string>();

        if (searchParams.Genres is { Count: > 0 })
            queryParts.Add(string.Join(" ", searchParams.Genres));

        if (!string.IsNullOrWhiteSpace(searchParams.Mood))
            queryParts.Add(searchParams.Mood);

        if (!string.IsNullOrWhiteSpace(searchParams.Context))
            queryParts.Add(searchParams.Context);

        return string.Join(" ", queryParts);
    }

    private static readonly Dictionary<string, float> MoodValenceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["happy"] = 0.9f,
        ["upbeat"] = 0.85f,
        ["energetic"] = 0.8f,
        ["party"] = 0.85f,
        ["chill"] = 0.5f,
        ["relaxed"] = 0.45f,
        ["mellow"] = 0.4f,
        ["melancholy"] = 0.2f,
        ["sad"] = 0.15f,
        ["angry"] = 0.3f,
        ["dark"] = 0.2f,
        ["romantic"] = 0.6f,
        ["nostalgic"] = 0.45f,
        ["focused"] = 0.4f,
        ["epic"] = 0.75f
    };

    /// <summary>
    /// Maps a mood string to a Spotify valence float in [0.0, 1.0].
    /// Returns 0.5 for null or unrecognized moods.
    /// </summary>
    public static float MoodToValence(string? mood)
    {
        if (string.IsNullOrWhiteSpace(mood))
            return 0.5f;

        return MoodValenceMap.TryGetValue(mood.Trim(), out var valence) ? valence : 0.5f;
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Sends an authenticated HTTP request. Handles 401 by refreshing the token and retrying once.
    /// If token refresh fails, returns null to signal re-authentication is needed.
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithAuthAsync(HttpMethod method, string url, HttpContent? content = null)
    {
        var token = await _authManager.GetValidTokenAsync();
        var request = BuildRequest(method, url, token, content);

        var response = await _httpClient.SendAsync(request);

        // Handle 401: refresh token and retry once
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            try
            {
                await _authManager.RefreshTokenAsync();
                token = await _authManager.GetValidTokenAsync();
                request = BuildRequest(method, url, token, content);
                response = await _httpClient.SendAsync(request);
            }
            catch (InvalidOperationException)
            {
                // Refresh token is expired/revoked — re-authentication required
                return null;
            }
        }

        return response;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string token, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content != null)
            request.Content = content;
        return request;
    }

    private static PlaybackResult BuildPlaybackResult(HttpResponseMessage? response, string successMessage, string failureMessage)
    {
        if (response == null)
            return new PlaybackResult { Success = false, Message = "Authentication failed. Please re-connect your Spotify account." };

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new PlaybackResult { Success = false, Message = "Playback control requires Spotify Premium. I can still search for songs though." };

        if (!response.IsSuccessStatusCode)
            return new PlaybackResult { Success = false, Message = failureMessage };

        return new PlaybackResult { Success = true, Message = successMessage };
    }

    private static async Task<List<Track>> ParseSearchResponseAsync(HttpResponseMessage response)
    {
        var tracks = new List<Track>();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tracks", out var tracksObj))
            return tracks;

        if (!tracksObj.TryGetProperty("items", out var items))
            return tracks;

        foreach (var item in items.EnumerateArray())
        {
            tracks.Add(ParseTrackFromJson(item));
        }

        return tracks;
    }

    private static async Task<List<Track>> ParseRecommendationsResponseAsync(HttpResponseMessage response)
    {
        var tracks = new List<Track>();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tracks", out var items))
            return tracks;

        foreach (var item in items.EnumerateArray())
        {
            tracks.Add(ParseTrackFromJson(item));
        }

        return tracks;
    }

    private static Track ParseTrackFromJson(JsonElement item)
    {
        var artistName = "";
        if (item.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
        {
            var firstArtist = artists[0];
            if (firstArtist.TryGetProperty("name", out var name))
                artistName = name.GetString() ?? "";
        }

        var albumName = "";
        if (item.TryGetProperty("album", out var album) && album.TryGetProperty("name", out var albumNameProp))
            albumName = albumNameProp.GetString() ?? "";

        return new Track
        {
            Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Name = item.TryGetProperty("name", out var trackName) ? trackName.GetString() ?? "" : "",
            Artist = artistName,
            Album = albumName,
            Uri = item.TryGetProperty("uri", out var uri) ? uri.GetString() ?? "" : "",
            DurationMs = item.TryGetProperty("duration_ms", out var duration) ? duration.GetInt32() : 0
        };
    }
}

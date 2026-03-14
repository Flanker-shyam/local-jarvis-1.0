using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Nlu;

public class NluResolver : INluResolver
{
    private readonly ILlmClient _llmClient;
    private readonly IConversationStore _conversationStore;

    private static readonly HashSet<string> ValidIntentStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        "PLAY_MUSIC", "PAUSE", "RESUME", "SKIP_NEXT", "SKIP_PREVIOUS",
        "SET_VOLUME", "GET_NOW_PLAYING", "PLAY_MORE_LIKE_THIS", "UNKNOWN"
    };

    public NluResolver(ILlmClient llmClient, IConversationStore conversationStore)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
    }

    internal const int MaxRetryAttempts = 3;
    internal static readonly int[] RetryDelaysMs = { 500, 1000, 2000 };

    public async Task<IntentResult> ResolveIntentAsync(string transcript, List<Turn> conversationHistory)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return new IntentResult(IntentType.Unknown, 0.0f, null, transcript ?? string.Empty);

        var systemPrompt = BuildSystemPrompt();
        var messages = FormatConversationHistory(conversationHistory);

        var userContent = $"User said: {transcript}\nExtract the intent and search parameters as JSON.";
        messages.Add(new ChatMessage("user", userContent));

        string llmResponse;
        try
        {
            llmResponse = await CallLlmWithRetryAsync(systemPrompt, messages);
        }
        catch
        {
            return new IntentResult(IntentType.Unknown, 0.0f, null, transcript);
        }

        if (!ValidateLlmResponse(llmResponse, out var jsonNode))
            return new IntentResult(IntentType.Unknown, 0.0f, null, transcript);

        var intentType = MapToIntentType(jsonNode!["intent"]!.GetValue<string>());
        var confidence = GetConfidence(jsonNode!);
        var searchParams = BuildSearchParams(jsonNode!, intentType);

        if (intentType == IntentType.PlayMoreLikeThis)
        {
            var lastPlayed = _conversationStore.GetLastPlayedTrack();
            if (lastPlayed != null)
            {
                searchParams ??= new SearchParams();
                searchParams.SeedTrackId = lastPlayed.Id;
            }
        }

        return new IntentResult(intentType, confidence, searchParams, transcript);
    }

    internal async Task<string> CallLlmWithRetryAsync(string systemPrompt, List<ChatMessage> messages)
    {
        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            try
            {
                return await _llmClient.ChatAsync(systemPrompt, messages);
            }
            catch
            {
                if (attempt == MaxRetryAttempts - 1)
                    throw; // Re-throw on final attempt

                var delayMs = RetryDelaysMs[attempt];
                await Task.Delay(delayMs);
            }
        }

        // Should not reach here, but just in case
        throw new InvalidOperationException("LLM API call failed after all retry attempts.");
    }

    internal static string BuildSystemPrompt()
    {
        return "You are Jarvis, a music assistant. "
            + "Given a user's voice command, extract:\n"
            + "1. intent: one of [PLAY_MUSIC, PAUSE, RESUME, SKIP_NEXT, SKIP_PREVIOUS, "
            + "SET_VOLUME, GET_NOW_PLAYING, PLAY_MORE_LIKE_THIS, UNKNOWN]\n"
            + "2. confidence: float 0.0-1.0\n"
            + "3. searchParams (if PLAY_MUSIC):\n"
            + "   - query: direct search string\n"
            + "   - artist, track, album: if explicitly named\n"
            + "   - genres: inferred genre list\n"
            + "   - mood: inferred mood (chill, energetic, melancholy, upbeat, etc.)\n"
            + "   - era: decade if mentioned or implied\n"
            + "   - context: situational context (workout, study, party, driving, etc.)\n"
            + "   - energy: 0.0-1.0 mapping to energy level\n"
            + "   - isVague: true if no explicit artist/track/album\n\n"
            + "For vague requests like 'play something chill' or 'play beach vibes', "
            + "infer genres, mood, and energy. Be creative but accurate.\n"
            + "Respond ONLY with valid JSON.";
    }

    internal static List<ChatMessage> FormatConversationHistory(List<Turn> conversationHistory)
    {
        var messages = new List<ChatMessage>();
        if (conversationHistory == null)
            return messages;

        foreach (var turn in conversationHistory)
        {
            messages.Add(new ChatMessage(turn.Role, turn.Content));
        }

        return messages;
    }

    internal static bool ValidateLlmResponse(string response, out JsonNode? jsonNode)
    {
        jsonNode = null;

        if (string.IsNullOrWhiteSpace(response))
            return false;

        try
        {
            jsonNode = JsonNode.Parse(response);
        }
        catch (JsonException)
        {
            return false;
        }

        if (jsonNode is not JsonObject jsonObj)
            return false;

        // Must have "intent" as a string
        var intentNode = jsonObj["intent"];
        if (intentNode == null)
            return false;

        string intentStr;
        try
        {
            intentStr = intentNode.GetValue<string>();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(intentStr))
            return false;

        if (!ValidIntentStrings.Contains(intentStr))
            return false;

        // Must have "confidence" as a number
        var confidenceNode = jsonObj["confidence"];
        if (confidenceNode == null)
            return false;

        float confidence;
        try
        {
            confidence = confidenceNode.GetValue<float>();
        }
        catch
        {
            return false;
        }

        if (confidence < 0.0f || confidence > 1.0f)
            return false;

        return true;
    }

    internal static IntentType MapToIntentType(string intent)
    {
        return intent.ToUpperInvariant() switch
        {
            "PLAY_MUSIC" => IntentType.PlayMusic,
            "PAUSE" => IntentType.Pause,
            "RESUME" => IntentType.Resume,
            "SKIP_NEXT" => IntentType.SkipNext,
            "SKIP_PREVIOUS" => IntentType.SkipPrevious,
            "SET_VOLUME" => IntentType.SetVolume,
            "GET_NOW_PLAYING" => IntentType.GetNowPlaying,
            "PLAY_MORE_LIKE_THIS" => IntentType.PlayMoreLikeThis,
            _ => IntentType.Unknown
        };
    }

    private static float GetConfidence(JsonNode jsonNode)
    {
        try
        {
            var val = jsonNode["confidence"]!.GetValue<float>();
            return Math.Clamp(val, 0.0f, 1.0f);
        }
        catch
        {
            return 0.0f;
        }
    }

    internal static SearchParams? BuildSearchParams(JsonNode jsonNode, IntentType intentType)
    {
        if (intentType != IntentType.PlayMusic && intentType != IntentType.PlayMoreLikeThis)
            return null;

        var spNode = jsonNode["searchParams"];
        if (spNode is not JsonObject spObj)
            return null;

        var searchParams = new SearchParams
        {
            Query = GetStringOrNull(spObj, "query"),
            Artist = GetStringOrNull(spObj, "artist"),
            Track = GetStringOrNull(spObj, "track"),
            Album = GetStringOrNull(spObj, "album"),
            Mood = GetStringOrNull(spObj, "mood"),
            Era = GetStringOrNull(spObj, "era"),
            Context = GetStringOrNull(spObj, "context"),
            SeedTrackId = GetStringOrNull(spObj, "seedTrackId")
        };

        // Parse genres
        var genresNode = spObj["genres"];
        if (genresNode is JsonArray genresArray)
        {
            foreach (var g in genresArray)
            {
                var genre = g?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(genre))
                    searchParams.Genres.Add(genre);
            }
        }

        // Parse energy
        var energyNode = spObj["energy"];
        if (energyNode != null)
        {
            try
            {
                var energy = energyNode.GetValue<float>();
                searchParams.Energy = Math.Clamp(energy, 0.0f, 1.0f);
            }
            catch
            {
                // Ignore invalid energy values
            }
        }

        // Parse isVague
        var isVagueNode = spObj["isVague"];
        if (isVagueNode != null)
        {
            try
            {
                searchParams.IsVague = isVagueNode.GetValue<bool>();
            }
            catch
            {
                // Default: infer from explicit fields
                searchParams.IsVague = string.IsNullOrWhiteSpace(searchParams.Artist)
                    && string.IsNullOrWhiteSpace(searchParams.Track)
                    && string.IsNullOrWhiteSpace(searchParams.Album);
            }
        }
        else
        {
            searchParams.IsVague = string.IsNullOrWhiteSpace(searchParams.Artist)
                && string.IsNullOrWhiteSpace(searchParams.Track)
                && string.IsNullOrWhiteSpace(searchParams.Album);
        }

        return searchParams;
    }

    private static string? GetStringOrNull(JsonObject obj, string key)
    {
        var node = obj[key];
        if (node == null)
            return null;

        try
        {
            var val = node.GetValue<string>();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }
        catch
        {
            return null;
        }
    }
}

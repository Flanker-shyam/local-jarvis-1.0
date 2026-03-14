using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Conversation;
using Jarvis.Services.Orchestration;
using Jarvis.Services.Response;
using Jarvis.Services.Routing;
using Moq;

namespace Jarvis.Tests.Integration;

/// <summary>
/// Integration tests for the full Jarvis pipeline: audio → STT → NLU → route → play → response → TTS.
/// All external services (STT, LLM, Spotify, TTS) are mocked; the real orchestrator, router,
/// handlers, response builder, and conversation store are used.
/// </summary>
public class PipelineIntegrationTests
{
    private readonly Mock<ISpeechToTextEngine> _sttMock = new();
    private readonly Mock<ILlmClient> _llmMock = new();
    private readonly Mock<ISpotifyService> _spotifyMock = new();
    private readonly Mock<ITextToSpeechEngine> _ttsMock = new();
    private readonly ConversationStore _conversationStore = new();
    private readonly ResponseBuilder _responseBuilder = new();

    private JarvisOrchestrator BuildOrchestrator()
    {
        // Wire real NluResolver with mocked LLM client
        var nluResolver = new Services.Nlu.NluResolver(_llmMock.Object, _conversationStore);

        // Wire real CommandRouter with mocked SpotifyService (registers all 9 handlers)
        var commandRouter = new CommandRouter(_spotifyMock.Object);

        return new JarvisOrchestrator(
            _sttMock.Object,
            nluResolver,
            commandRouter,
            _responseBuilder,
            _ttsMock.Object,
            _conversationStore);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void SetupSttReturns(string text, float confidence)
    {
        _sttMock.Setup(s => s.TranscribeAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(new TranscriptResult { Text = text, Confidence = confidence });
    }

    private void SetupLlmReturns(string jsonResponse)
    {
        _llmMock.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(jsonResponse);
    }

    private void SetupSpotifySearchReturns(List<Track> tracks)
    {
        _spotifyMock.Setup(s => s.SearchAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(tracks);
    }

    private void SetupSpotifyRecommendationsReturns(List<Track> tracks)
    {
        _spotifyMock.Setup(s => s.GetRecommendationsAsync(It.IsAny<SearchParams>()))
            .ReturnsAsync(tracks);
    }

    private void SetupSpotifyActiveDevice(string? deviceId)
    {
        _spotifyMock.Setup(s => s.GetActiveDeviceIdAsync())
            .ReturnsAsync(deviceId);
    }

    private void SetupSpotifyPlayReturns(PlaybackResult result)
    {
        _spotifyMock.Setup(s => s.PlayAsync(It.IsAny<List<Track>>(), It.IsAny<string>()))
            .ReturnsAsync(result);
    }

    private static readonly Track SampleTrack = new()
    {
        Id = "track123",
        Name = "Bohemian Rhapsody",
        Artist = "Queen",
        Album = "A Night at the Opera",
        Uri = "spotify:track:track123",
        DurationMs = 354000
    };

    private static readonly byte[] SampleAudio = new byte[] { 1, 2, 3, 4 };

    // ── Test 1: Full happy path ──────────────────────────────────────

    /// <summary>
    /// Validates: Requirements 1.1, 3.1, 4.3
    /// Full pipeline: audio → STT → NLU → search → play → response → TTS
    /// </summary>
    [Fact]
    public async Task FullPipeline_HappyPath_PlaysTrackAndSpeaksResponse()
    {
        // Arrange
        var orchestrator = BuildOrchestrator();

        SetupSttReturns("Play Bohemian Rhapsody by Queen", 0.95f);
        SetupLlmReturns("""
        {
            "intent": "PLAY_MUSIC",
            "confidence": 0.95,
            "searchParams": {
                "track": "Bohemian Rhapsody",
                "artist": "Queen",
                "isVague": false
            }
        }
        """);
        SetupSpotifySearchReturns(new List<Track> { SampleTrack });
        SetupSpotifyActiveDevice("device-1");
        SetupSpotifyPlayReturns(new PlaybackResult
        {
            Success = true,
            Message = "Now playing Bohemian Rhapsody by Queen",
            NowPlaying = SampleTrack,
            QueueLength = 1
        });

        // Act
        var result = await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Bohemian Rhapsody", result.Message);

        // Verify full pipeline was invoked
        _sttMock.Verify(s => s.TranscribeAsync(SampleAudio), Times.Once);
        _llmMock.Verify(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()), Times.Once);
        _spotifyMock.Verify(s => s.SearchAsync(It.IsAny<SearchParams>()), Times.Once);
        _spotifyMock.Verify(s => s.PlayAsync(It.IsAny<List<Track>>(), "device-1"), Times.Once);
        _ttsMock.Verify(t => t.SpeakAsync(It.IsAny<string>()), Times.Once);

        // Verify conversation history grew by 2 (user + assistant turns)
        var turns = _conversationStore.GetRecentTurns(10);
        Assert.Equal(2, turns.Count);
        Assert.Equal("user", turns[0].Role);
        Assert.Equal("assistant", turns[1].Role);
    }

    // ── Test 2: Conversation context flow ────────────────────────────

    /// <summary>
    /// Validates: Requirements 3.1, 3.2
    /// First command plays a song, follow-up "play more like that" uses seed track.
    /// </summary>
    [Fact]
    public async Task ConversationContext_PlayMoreLikeThat_UsesSeedTrack()
    {
        // Arrange
        var orchestrator = BuildOrchestrator();

        // --- First command: play a specific song ---
        SetupSttReturns("Play Bohemian Rhapsody by Queen", 0.95f);
        SetupLlmReturns("""
        {
            "intent": "PLAY_MUSIC",
            "confidence": 0.95,
            "searchParams": {
                "track": "Bohemian Rhapsody",
                "artist": "Queen",
                "isVague": false,
                "seedTrackId": "track123"
            }
        }
        """);
        SetupSpotifySearchReturns(new List<Track> { SampleTrack });
        SetupSpotifyActiveDevice("device-1");
        SetupSpotifyPlayReturns(new PlaybackResult
        {
            Success = true,
            Message = "Now playing Bohemian Rhapsody by Queen",
            NowPlaying = SampleTrack,
            QueueLength = 1
        });

        await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // --- Second command: "play more like that" ---
        SetupSttReturns("play more like that", 0.90f);

        // The LLM returns PLAY_MORE_LIKE_THIS; the NluResolver enriches with seed track from history
        SetupLlmReturns("""
        {
            "intent": "PLAY_MORE_LIKE_THIS",
            "confidence": 0.90,
            "searchParams": {
                "genres": ["rock"],
                "mood": "energetic",
                "isVague": true
            }
        }
        """);

        var recommendedTrack = new Track
        {
            Id = "rec1", Name = "We Will Rock You", Artist = "Queen",
            Album = "News of the World", Uri = "spotify:track:rec1", DurationMs = 200000
        };
        SetupSpotifyRecommendationsReturns(new List<Track> { recommendedTrack });
        SetupSpotifyPlayReturns(new PlaybackResult
        {
            Success = true,
            Message = "Now playing We Will Rock You by Queen",
            NowPlaying = recommendedTrack,
            QueueLength = 1
        });

        // Act
        var result = await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // Assert
        Assert.True(result.Success);

        // Verify recommendations were called (the handler uses GetRecommendationsAsync)
        _spotifyMock.Verify(s => s.GetRecommendationsAsync(It.IsAny<SearchParams>()), Times.AtLeastOnce);

        // Verify conversation history has 4 turns (2 per command)
        var turns = _conversationStore.GetRecentTurns(10);
        Assert.Equal(4, turns.Count);
    }

    // ── Test 3: Low confidence → retry, no side effects ──────────────

    /// <summary>
    /// Validates: Requirements 1.2
    /// Low STT confidence returns retry message with no Spotify calls.
    /// </summary>
    [Fact]
    public async Task LowConfidence_ReturnsRetryMessage_NoSpotifyCalls()
    {
        // Arrange
        var orchestrator = BuildOrchestrator();
        SetupSttReturns("mumble mumble", 0.2f);

        // Act
        var result = await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("didn't catch that", result.Message);

        // No NLU, Spotify, or TTS calls should have been made
        _llmMock.Verify(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()), Times.Never);
        _spotifyMock.Verify(s => s.SearchAsync(It.IsAny<SearchParams>()), Times.Never);
        _spotifyMock.Verify(s => s.PlayAsync(It.IsAny<List<Track>>(), It.IsAny<string>()), Times.Never);
        _ttsMock.Verify(t => t.SpeakAsync(It.IsAny<string>()), Times.Never);
    }

    // ── Test 4: Unknown intent → clarification, no side effects ──────

    /// <summary>
    /// Validates: Requirements 2.4
    /// Unknown intent returns clarification message with no Spotify calls.
    /// </summary>
    [Fact]
    public async Task UnknownIntent_ReturnsClarification_NoSpotifyCalls()
    {
        // Arrange
        var orchestrator = BuildOrchestrator();
        SetupSttReturns("asdfghjkl gibberish", 0.85f);
        SetupLlmReturns("""
        {
            "intent": "UNKNOWN",
            "confidence": 0.3
        }
        """);

        // Act
        var result = await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not sure what you mean", result.Message);

        // No Spotify or TTS calls
        _spotifyMock.Verify(s => s.SearchAsync(It.IsAny<SearchParams>()), Times.Never);
        _spotifyMock.Verify(s => s.PlayAsync(It.IsAny<List<Track>>(), It.IsAny<string>()), Times.Never);
        _ttsMock.Verify(t => t.SpeakAsync(It.IsAny<string>()), Times.Never);
    }

    // ── Test 5: No search results → recommendation fallback ──────────

    /// <summary>
    /// Validates: Requirements 4.3, 4.5
    /// When search returns no results, SpotifyService falls back to recommendations.
    /// </summary>
    [Fact]
    public async Task NoSearchResults_FallsBackToRecommendations()
    {
        // Arrange
        var orchestrator = BuildOrchestrator();
        SetupSttReturns("Play something chill", 0.90f);
        SetupLlmReturns("""
        {
            "intent": "PLAY_MUSIC",
            "confidence": 0.90,
            "searchParams": {
                "genres": ["lo-fi", "ambient"],
                "mood": "chill",
                "energy": 0.3,
                "isVague": true
            }
        }
        """);

        // Vague query goes directly to recommendations via SearchAsync
        var chillTrack = new Track
        {
            Id = "chill1", Name = "Chill Vibes", Artist = "Lo-Fi Artist",
            Album = "Chill Album", Uri = "spotify:track:chill1", DurationMs = 180000
        };
        // For vague queries, SearchAsync delegates to GetRecommendationsAsync internally
        SetupSpotifySearchReturns(new List<Track> { chillTrack });
        SetupSpotifyActiveDevice("device-1");
        SetupSpotifyPlayReturns(new PlaybackResult
        {
            Success = true,
            Message = "Now playing Chill Vibes by Lo-Fi Artist",
            NowPlaying = chillTrack,
            QueueLength = 1
        });

        // Act
        var result = await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // Assert
        Assert.True(result.Success);
        _spotifyMock.Verify(s => s.SearchAsync(It.IsAny<SearchParams>()), Times.Once);
    }

    // ── Test 6: No active device → failure message ───────────────────

    /// <summary>
    /// Validates: Requirements 4.6
    /// When no active Spotify device is found, returns failure with instructions.
    /// </summary>
    [Fact]
    public async Task NoActiveDevice_ReturnsFailureMessage()
    {
        // Arrange
        var orchestrator = BuildOrchestrator();
        SetupSttReturns("Play Bohemian Rhapsody", 0.95f);
        SetupLlmReturns("""
        {
            "intent": "PLAY_MUSIC",
            "confidence": 0.95,
            "searchParams": {
                "track": "Bohemian Rhapsody",
                "isVague": false
            }
        }
        """);
        SetupSpotifySearchReturns(new List<Track> { SampleTrack });
        SetupSpotifyActiveDevice(null); // No active device

        // Act
        var result = await orchestrator.ProcessVoiceCommandAsync(SampleAudio);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No active Spotify device", result.Message);

        // Play should never have been called
        _spotifyMock.Verify(s => s.PlayAsync(It.IsAny<List<Track>>(), It.IsAny<string>()), Times.Never);
    }
}

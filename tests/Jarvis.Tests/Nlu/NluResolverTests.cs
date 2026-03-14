using System.Text.Json;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Nlu;
using Moq;

namespace Jarvis.Tests.Nlu;

/// <summary>
/// Unit tests for NluResolver.
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 3.3
/// </summary>
public class NluResolverTests
{
    private readonly Mock<ILlmClient> _mockLlmClient;
    private readonly Mock<IConversationStore> _mockConversationStore;
    private readonly NluResolver _resolver;

    public NluResolverTests()
    {
        _mockLlmClient = new Mock<ILlmClient>();
        _mockConversationStore = new Mock<IConversationStore>();
        _mockConversationStore
            .Setup(s => s.GetLastPlayedTrack())
            .Returns((Track?)null);

        _resolver = new NluResolver(_mockLlmClient.Object, _mockConversationStore.Object);
    }

    #region Scenario 1: Explicit request extraction

    [Fact]
    public async Task ResolveIntentAsync_ExplicitRequest_ExtractsArtistAndTrack()
    {
        // Arrange — LLM returns PLAY_MUSIC with explicit artist/track, isVague=false
        var llmResponse = JsonSerializer.Serialize(new
        {
            intent = "PLAY_MUSIC",
            confidence = 0.95f,
            searchParams = new
            {
                artist = "Queen",
                track = "Bohemian Rhapsody",
                isVague = false
            }
        });

        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "Play Bohemian Rhapsody by Queen", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.PlayMusic, result.IntentType);
        Assert.Equal(0.95f, result.Confidence, precision: 2);
        Assert.NotNull(result.SearchParams);
        Assert.Equal("Queen", result.SearchParams!.Artist);
        Assert.Equal("Bohemian Rhapsody", result.SearchParams.Track);
        Assert.False(result.SearchParams.IsVague);
    }

    #endregion

    #region Scenario 2: Vague request inference

    [Fact]
    public async Task ResolveIntentAsync_VagueRequest_InfersGenresMoodAndContext()
    {
        // Arrange — LLM returns PLAY_MUSIC with inferred genres/mood/context, isVague=true
        var llmResponse = JsonSerializer.Serialize(new
        {
            intent = "PLAY_MUSIC",
            confidence = 0.88f,
            searchParams = new
            {
                genres = new[] { "lo-fi", "ambient" },
                mood = "chill",
                context = "study",
                energy = 0.3f,
                isVague = true
            }
        });

        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "play something chill for studying", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.PlayMusic, result.IntentType);
        Assert.NotNull(result.SearchParams);
        Assert.True(result.SearchParams!.IsVague);
        Assert.Contains("lo-fi", result.SearchParams.Genres);
        Assert.Contains("ambient", result.SearchParams.Genres);
        Assert.Equal("chill", result.SearchParams.Mood);
        Assert.Equal("study", result.SearchParams.Context);
        Assert.NotNull(result.SearchParams.Energy);
        Assert.Equal(0.3f, result.SearchParams.Energy!.Value, precision: 2);
    }

    #endregion

    #region Scenario 3: Unknown intent

    [Fact]
    public async Task ResolveIntentAsync_UnknownIntent_ReturnsIntentTypeUnknown()
    {
        // Arrange — LLM returns UNKNOWN intent
        var llmResponse = JsonSerializer.Serialize(new
        {
            intent = "UNKNOWN",
            confidence = 0.2f
        });

        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "asdf jkl qwerty", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.Unknown, result.IntentType);
        Assert.Null(result.SearchParams);
    }

    #endregion

    #region Scenario 4: Gibberish / malformed LLM response

    [Fact]
    public async Task ResolveIntentAsync_MalformedLlmResponse_FallsBackToUnknown()
    {
        // Arrange — LLM returns non-JSON garbage
        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync("this is not json at all!!!");

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "some gibberish input", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.Unknown, result.IntentType);
        Assert.Equal(0.0f, result.Confidence);
        Assert.Null(result.SearchParams);
    }

    [Fact]
    public async Task ResolveIntentAsync_LlmThrowsException_FallsBackToUnknown()
    {
        // Arrange — LLM client throws
        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ThrowsAsync(new HttpRequestException("LLM unavailable"));

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "play something", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.Unknown, result.IntentType);
        Assert.Equal(0.0f, result.Confidence);
    }

    #endregion

    #region Scenario 5: Conversation history passed to LLM

    [Fact]
    public async Task ResolveIntentAsync_ConversationHistory_IsPassedToLlmAsMessages()
    {
        // Arrange — provide conversation history turns
        var history = new List<Turn>
        {
            new Turn { Role = "user", Content = "Play Bohemian Rhapsody" },
            new Turn { Role = "assistant", Content = "Now playing Bohemian Rhapsody by Queen" }
        };

        var llmResponse = JsonSerializer.Serialize(new
        {
            intent = "PLAY_MORE_LIKE_THIS",
            confidence = 0.9f,
            searchParams = new
            {
                query = "similar to Bohemian Rhapsody",
                isVague = true
            }
        });

        List<ChatMessage>? capturedMessages = null;
        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .Callback<string, List<ChatMessage>>((_, msgs) => capturedMessages = msgs)
            .ReturnsAsync(llmResponse);

        // Act
        await _resolver.ResolveIntentAsync("play more like that", history);

        // Assert — conversation history turns should appear as messages before the user message
        Assert.NotNull(capturedMessages);
        // History turns (2) + the new user message (1) = 3 messages total
        Assert.Equal(3, capturedMessages!.Count);
        Assert.Equal("user", capturedMessages[0].Role);
        Assert.Equal("Play Bohemian Rhapsody", capturedMessages[0].Content);
        Assert.Equal("assistant", capturedMessages[1].Role);
        Assert.Equal("Now playing Bohemian Rhapsody by Queen", capturedMessages[1].Content);
        Assert.Equal("user", capturedMessages[2].Role);
    }

    #endregion

    #region Scenario 6: PlayMoreLikeThis enriches SeedTrackId

    [Fact]
    public async Task ResolveIntentAsync_PlayMoreLikeThis_EnrichesSeedTrackIdFromLastPlayed()
    {
        // Arrange — conversation store has a last played track
        var lastTrack = new Track
        {
            Id = "spotify:track:abc123",
            Name = "Bohemian Rhapsody",
            Artist = "Queen"
        };

        _mockConversationStore
            .Setup(s => s.GetLastPlayedTrack())
            .Returns(lastTrack);

        var llmResponse = JsonSerializer.Serialize(new
        {
            intent = "PLAY_MORE_LIKE_THIS",
            confidence = 0.92f,
            searchParams = new
            {
                query = "similar to Queen",
                isVague = true
            }
        });

        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "play more like that", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.PlayMoreLikeThis, result.IntentType);
        Assert.NotNull(result.SearchParams);
        Assert.Equal("spotify:track:abc123", result.SearchParams!.SeedTrackId);
    }

    [Fact]
    public async Task ResolveIntentAsync_PlayMoreLikeThis_NoLastPlayed_SeedTrackIdIsNull()
    {
        // Arrange — no last played track
        _mockConversationStore
            .Setup(s => s.GetLastPlayedTrack())
            .Returns((Track?)null);

        var llmResponse = JsonSerializer.Serialize(new
        {
            intent = "PLAY_MORE_LIKE_THIS",
            confidence = 0.85f,
            searchParams = new
            {
                query = "something similar",
                isVague = true
            }
        });

        _mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(llmResponse);

        // Act
        var result = await _resolver.ResolveIntentAsync(
            "play more like that", new List<Turn>());

        // Assert
        Assert.Equal(IntentType.PlayMoreLikeThis, result.IntentType);
        Assert.NotNull(result.SearchParams);
        Assert.Null(result.SearchParams!.SeedTrackId);
    }

    #endregion
}

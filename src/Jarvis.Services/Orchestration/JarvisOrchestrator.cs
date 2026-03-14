using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Orchestration;

/// <summary>
/// Main pipeline orchestrator for the Jarvis voice assistant.
/// Processes voice commands through: STT → confidence check → NLU → route → conversation store → response → TTS.
/// Enforces rate limiting and does not persist raw audio beyond the current pipeline execution.
/// </summary>
public class JarvisOrchestrator
{
    private const float ConfidenceThreshold = 0.4f;
    private const int MaxCommandsPerMinute = 30;
    private const int MaxContextTurns = 10;

    private readonly ISpeechToTextEngine _sttEngine;
    private readonly INluResolver _nluResolver;
    private readonly ICommandRouter _commandRouter;
    private readonly IResponseBuilder _responseBuilder;
    private readonly ITextToSpeechEngine _ttsEngine;
    private readonly IConversationStore _conversationStore;

    private readonly Queue<DateTime> _commandTimestamps = new();
    private readonly object _rateLimitLock = new();

    public JarvisOrchestrator(
        ISpeechToTextEngine sttEngine,
        INluResolver nluResolver,
        ICommandRouter commandRouter,
        IResponseBuilder responseBuilder,
        ITextToSpeechEngine ttsEngine,
        IConversationStore conversationStore)
    {
        _sttEngine = sttEngine ?? throw new ArgumentNullException(nameof(sttEngine));
        _nluResolver = nluResolver ?? throw new ArgumentNullException(nameof(nluResolver));
        _commandRouter = commandRouter ?? throw new ArgumentNullException(nameof(commandRouter));
        _responseBuilder = responseBuilder ?? throw new ArgumentNullException(nameof(responseBuilder));
        _ttsEngine = ttsEngine ?? throw new ArgumentNullException(nameof(ttsEngine));
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
    }

    public async Task<CommandResult> ProcessVoiceCommandAsync(byte[] audioInput)
    {
        if (audioInput is null || audioInput.Length == 0)
            return new CommandResult { Success = false, Message = "No audio input received." };

        // Rate limiting check
        if (!TryAcquireRateLimit())
        {
            return new CommandResult
            {
                Success = false,
                Message = "Too many commands. Please wait a moment before trying again."
            };
        }

        // Step 1: Transcribe audio to text (raw audio is not persisted beyond this scope)
        var transcript = await _sttEngine.TranscribeAsync(audioInput);

        // Step 2: Confidence check — low confidence returns retry with no side effects
        if (transcript.Confidence < ConfidenceThreshold)
        {
            return new CommandResult
            {
                Success = false,
                Message = "I didn't catch that. Could you say it again?"
            };
        }

        // Step 3: Resolve intent via NLU with conversation history
        var conversationHistory = _conversationStore.GetRecentTurns(MaxContextTurns);
        var intentResult = await _nluResolver.ResolveIntentAsync(transcript.Text, conversationHistory);

        // Step 4: Unknown intent returns clarification with no side effects
        if (intentResult.IntentType == IntentType.Unknown)
        {
            return new CommandResult
            {
                Success = false,
                Message = "I'm not sure what you mean. Could you rephrase?"
            };
        }

        // Step 5: Route to appropriate handler
        var commandResult = await _commandRouter.RouteAsync(intentResult);

        // Step 6: Store user turn and assistant turn in conversation history
        _conversationStore.AddTurn(new Turn
        {
            Role = "user",
            Content = transcript.Text,
            Timestamp = DateTime.UtcNow,
            Intent = intentResult
        });

        _conversationStore.AddTurn(new Turn
        {
            Role = "assistant",
            Content = commandResult.Message,
            Timestamp = DateTime.UtcNow,
            Intent = null
        });

        // Step 7: Build response text and speak via TTS
        var responseText = _responseBuilder.BuildResponse(commandResult);
        await _ttsEngine.SpeakAsync(responseText);

        return commandResult;
    }

    /// <summary>
    /// Attempts to acquire a rate limit slot. Returns false if the command cap is exceeded.
    /// Enforces a maximum of 30 commands per rolling one-minute window.
    /// </summary>
    internal bool TryAcquireRateLimit()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddMinutes(-1);

            // Remove timestamps outside the one-minute window
            while (_commandTimestamps.Count > 0 && _commandTimestamps.Peek() <= windowStart)
            {
                _commandTimestamps.Dequeue();
            }

            if (_commandTimestamps.Count >= MaxCommandsPerMinute)
            {
                return false;
            }

            _commandTimestamps.Enqueue(now);
            return true;
        }
    }
}

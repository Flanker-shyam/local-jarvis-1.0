# Implementation Plan: Jarvis Voice Assistant (Spotify MVP)

## Overview

Implement a voice-controlled Spotify assistant in C# that captures voice commands, transcribes them via STT, resolves intent via an LLM-powered NLU layer, executes Spotify actions (search, playback, recommendations), and provides spoken feedback via TTS. The architecture follows a pipeline pattern with discrete, testable components.

## Tasks

- [x] 1. Set up project structure, data models, and core interfaces
  - [x] 1.1 Create solution and project structure
    - Create a C# solution with projects: `Jarvis.Core` (interfaces, models, enums), `Jarvis.Services` (implementations), `Jarvis.App` (.NET MAUI mobile app — entry point/orchestrator with mic capture and audio playback), `Jarvis.Tests` (test project)
    - Add NuGet references: `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`, `Microsoft.Extensions.Http`, `Microsoft.Maui.Essentials` (for secure storage, audio, permissions)
    - _Requirements: All_

  - [x] 1.2 Define enums and data model classes
    - Create `IntentType` enum: `PlayMusic`, `Pause`, `Resume`, `SkipNext`, `SkipPrevious`, `SetVolume`, `GetNowPlaying`, `PlayMoreLikeThis`, `Unknown`
    - Create `SearchParams` class with fields: `Query`, `Artist`, `Track`, `Album`, `Genres` (List\<string\>), `Mood`, `Era`, `Context`, `Energy` (float?), `IsVague` (bool), `SeedTrackId`
    - Create `IntentResult` class with fields: `IntentType`, `Confidence` (float), `SearchParams`, `RawTranscript`
    - Create `Track` class with fields: `Id`, `Name`, `Artist`, `Album`, `Uri`, `DurationMs`
    - Create `PlaybackResult` class with fields: `Success`, `Message`, `NowPlaying` (Track?), `QueueLength`
    - Create `CommandResult` class with fields: `Success`, `Message`, `Data` (object?)
    - Create `Turn` class with fields: `Role`, `Content`, `Timestamp`, `Intent` (IntentResult?)
    - Create `TranscriptResult` class with fields: `Text`, `Confidence` (float)
    - Add validation: `IntentResult.Confidence` must be in [0.0, 1.0]; `SearchParams.Energy` must be in [0.0, 1.0] if set; `SearchParams` must have at least one populated criterion when `IntentType` is `PlayMusic`
    - _Requirements: 2.1, 2.2, 2.3, 2.5, 3.1_

  - [x] 1.3 Define core interfaces
    - Create `ISpeechToTextEngine` with methods: `Task<TranscriptResult> TranscribeAsync(byte[] audioBuffer)`, `Task StartListeningAsync()`, `Task StopListeningAsync()`, `bool IsListening`
    - Create `INluResolver` with method: `Task<IntentResult> ResolveIntentAsync(string transcript, List<Turn> conversationHistory)`
    - Create `ICommandRouter` with methods: `Task<CommandResult> RouteAsync(IntentResult intent)`, `void RegisterHandler(IntentType intentType, ICommandHandler handler)`
    - Create `ICommandHandler` with method: `Task<CommandResult> HandleAsync(IntentResult intent)`
    - Create `ISpotifyService` with methods: `Task<List<Track>> SearchAsync(SearchParams searchParams)`, `Task<PlaybackResult> PlayAsync(List<Track> tracks, string deviceId)`, `Task<PlaybackResult> PauseAsync()`, `Task<PlaybackResult> ResumeAsync()`, `Task<PlaybackResult> SkipNextAsync()`, `Task<PlaybackResult> SkipPreviousAsync()`, `Task<List<Track>> GetRecommendationsAsync(SearchParams searchParams)`, `Task<string?> GetActiveDeviceIdAsync()`
    - Create `IAuthManager` with methods: `Task<string> GetValidTokenAsync()`, `Task AuthenticateAsync()`, `Task RefreshTokenAsync()`, `bool IsSessionValid`
    - Create `IResponseBuilder` with method: `string BuildResponse(CommandResult result)`
    - Create `ITextToSpeechEngine` with method: `Task SpeakAsync(string text)`
    - Create `IConversationStore` with methods: `List<Turn> GetRecentTurns(int count)`, `void AddTurn(Turn turn)`, `Track? GetLastPlayedTrack()`
    - _Requirements: 1.1, 2.1, 3.1, 4.1, 7.5, 8.1, 9.1_

  - [x] 1.4 Write unit tests for data model validation
    - Test `IntentResult` confidence bounds validation
    - Test `SearchParams` energy bounds validation
    - Test `SearchParams` requires at least one criterion for `PlayMusic` intent
    - _Requirements: 2.1, 2.5_

- [x] 2. Implement Spotify authentication (AuthManager)
  - [x] 2.1 Implement `AuthManager` class
    - Implement OAuth 2.0 Authorization Code with PKCE flow against Spotify's `/authorize` and `/api/token` endpoints
    - Store tokens using platform-secure storage (use MAUI `SecureStorage` backed by iOS Keychain / Android Keystore)
    - Implement `GetValidTokenAsync()`: check token expiry, auto-refresh if expired, prompt re-auth if refresh token is also expired
    - Implement `RefreshTokenAsync()`: exchange refresh token for new access token
    - Implement `AuthenticateAsync()`: launch browser for Spotify login, handle redirect callback, exchange code for tokens
    - Ensure no client secret is stored on device (PKCE only)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 11.2_

  - [x] 2.2 Write unit tests for AuthManager
    - Test token refresh when access token is expired
    - Test re-authentication prompt when refresh token is expired
    - Test PKCE code verifier/challenge generation
    - Mock `ISecureStorage` to verify tokens are stored/retrieved correctly
    - _Requirements: 9.1, 9.2, 9.3, 9.5_

- [x] 3. Implement Speech-to-Text engine
  - [x] 3.1 Implement `SpeechToTextEngine` class
    - Implement `TranscribeAsync()` to send audio buffer to Sarvam Saaras v3 API and return `TranscriptResult` with text and confidence score
    - Implement `StartListeningAsync()` / `StopListeningAsync()` for MAUI microphone capture with silence detection that auto-stops listening
    - Use TLS for all STT API communications
    - _Requirements: 1.1, 1.3, 11.2_

  - [x] 3.2 Write unit tests for SpeechToTextEngine
    - Test that silence detection triggers auto-stop
    - Test that `TranscriptResult` returns valid confidence score
    - Mock STT API responses
    - _Requirements: 1.1, 1.3_

- [x] 4. Implement NLU Resolver (LLM-powered intent extraction)
  - [x] 4.1 Implement `NluResolver` class
    - Build LLM system prompt per design (intent types, search param extraction, JSON-only output)
    - Implement `ResolveIntentAsync()`: format conversation history, call LLM API with system prompt + context + user message, parse JSON response into `IntentResult`
    - For explicit requests: extract artist, track, album into `SearchParams`, set `IsVague = false`
    - For vague/mood requests: infer genres, mood, energy, era, context into `SearchParams`, set `IsVague = true`
    - Handle `PlayMoreLikeThis` intent: enrich `SearchParams` with `SeedTrackId` from conversation history's last played track
    - Return `IntentType.Unknown` when intent cannot be determined
    - Validate LLM JSON response against expected schema before parsing; reject malformed responses to guard against prompt injection
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.2, 3.3, 11.3_

  - [x] 4.2 Write property test: Intent resolution returns valid intent type and bounded confidence (Property 2)
    - **Property 2: Intent resolution returns valid intent type and bounded confidence**
    - For any non-empty transcript string, `ResolveIntentAsync` returns an `IntentResult` with `IntentType` from the enum and confidence in [0.0, 1.0]
    - **Validates: Requirements 2.1**

  - [x] 4.3 Write property test: PLAY_MUSIC intent always has populated search params (Property 3)
    - **Property 3: PLAY_MUSIC intent always has populated search params**
    - For any `IntentResult` with `IntentType.PlayMusic`, `SearchParams` is non-null with at least one populated criterion
    - **Validates: Requirements 2.5**

  - [x] 4.4 Write property test: LLM response validation rejects malformed input (Property 15)
    - **Property 15: LLM response validation rejects malformed input**
    - For any LLM response string not conforming to expected JSON schema, the validator rejects it
    - **Validates: Requirements 11.3**

  - [x] 4.5 Write unit tests for NluResolver
    - Test explicit request extraction ("Play Bohemian Rhapsody by Queen")
    - Test vague request inference ("play something chill for studying")
    - Test `Unknown` intent for gibberish input
    - Test conversation history is passed to LLM for context
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.3_

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement Spotify Service (search, recommendations, playback)
  - [x] 6.1 Implement query construction (`BuildDirectQuery` method)
    - When `SearchParams` has explicit track/artist/album fields, construct Spotify field filter syntax (e.g., `"track:X artist:Y"`)
    - When `SearchParams` has no explicit fields but has a raw query, use the raw query directly
    - When `SearchParams` has only inferred attributes (genres, mood, context), concatenate into space-separated query string
    - Ensure non-empty string is always returned for any `SearchParams` with at least one populated criterion
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 6.2 Write property test: Non-vague queries use field filter syntax (Property 7)
    - **Property 7: Non-vague queries use field filter syntax in query construction**
    - For any `SearchParams` with `IsVague = false` and at least one explicit field, `BuildDirectQuery` produces a string containing `"track:"`, `"artist:"`, or `"album:"` syntax
    - **Validates: Requirements 4.1, 5.1**

  - [x] 6.3 Write property test: Query builder always produces non-empty strings (Property 9)
    - **Property 9: Query builder always produces non-empty strings**
    - For any `SearchParams` with at least one populated criterion, `BuildDirectQuery` returns a non-empty string
    - **Validates: Requirements 5.4**

  - [x] 6.4 Implement mood-to-valence mapping (`MoodToValence` method)
    - Implement the mood-to-valence lookup table per design (happy→0.9, sad→0.15, chill→0.5, etc.)
    - Return 0.5 for null or unrecognized mood strings
    - Normalize mood to lowercase before lookup
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 6.5 Write property test: Mood-to-valence mapping is bounded (Property 10)
    - **Property 10: Mood-to-valence mapping is bounded**
    - For any mood string (including null and unrecognized), `MoodToValence` returns a float in [0.0, 1.0], with null/unknown returning 0.5
    - **Validates: Requirements 6.1, 6.2**

  - [x] 6.6 Implement `SpotifyService` search and recommendations
    - Implement `SearchAsync()`: for non-vague params, call Spotify Search API with `BuildDirectQuery` result; for vague params, call Recommendations API with seed genres, target energy, and target valence from `MoodToValence`
    - Implement `GetRecommendationsAsync()`: call Spotify `/recommendations` endpoint with seed genres, target energy, target valence
    - When direct search returns no results for non-vague query, fall back to Recommendations API
    - Return empty list (not throw) when no results found
    - Use `IAuthManager.GetValidTokenAsync()` for all API calls (transparent token refresh)
    - _Requirements: 4.1, 4.2, 4.3, 4.5, 10.2_

  - [x] 6.7 Implement `SpotifyService` playback control
    - Implement `PlayAsync()`: start playback on active device with track URIs
    - Implement `PauseAsync()`, `ResumeAsync()`, `SkipNextAsync()`, `SkipPreviousAsync()`
    - Implement `GetActiveDeviceIdAsync()`: query `/me/player/devices` and return active device ID or null
    - Handle 401 responses: trigger token refresh via `IAuthManager` and retry
    - Handle 403 responses: detect Spotify free-tier limitation, return failure with Premium required message
    - When no active device found, return failure with device activation instructions
    - _Requirements: 4.4, 4.6, 7.1, 7.2, 7.3, 7.4, 10.2, 10.3_

  - [x] 6.8 Write property test: Empty search results trigger recommendation fallback (Property 8)
    - **Property 8: Empty search results trigger recommendation fallback**
    - For any `SearchParams` where direct search returns zero results, the system falls back to Recommendations API before returning failure
    - **Validates: Requirements 4.3**

  - [x] 6.9 Write property test: Failed searches preserve playback state (Property 14)
    - **Property 14: Failed searches preserve playback state**
    - For any search producing no results, the current Spotify playback state remains unchanged
    - **Validates: Requirements 10.4**

  - [x] 6.10 Write unit tests for SpotifyService
    - Test `BuildDirectQuery` with explicit fields, raw query, and inferred attributes
    - Test `MoodToValence` for all known moods, null, and unknown strings
    - Test search fallback to recommendations when direct search returns empty
    - Test 401 handling triggers token refresh and retry
    - Test 403 handling returns Premium required message
    - Test no active device returns appropriate failure
    - _Requirements: 4.1, 4.2, 4.3, 4.6, 5.1, 5.4, 6.1, 6.2, 6.3, 10.2, 10.3_

- [x] 7. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement Command Router
  - [x] 8.1 Implement `CommandRouter` class
    - Implement `RouteAsync()`: dispatch `IntentResult` to the registered `ICommandHandler` based on `IntentType`
    - Register handlers for: `PlayMusic` (search + play), `Pause`, `Resume`, `SkipNext`, `SkipPrevious`, `PlayMoreLikeThis`, `GetNowPlaying`, `SetVolume`, `Unknown` (clarification)
    - Implement individual command handlers that call `ISpotifyService` methods and return `CommandResult`
    - The `PlayMusic` handler should call `searchAndPlay` logic: search → fallback to recommendations → play on active device
    - The `PlayMoreLikeThis` handler should use seed track from `SearchParams.SeedTrackId` for recommendations
    - Always return a unified `CommandResult` regardless of handler
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

  - [x] 8.2 Write property test: Command routing returns unified CommandResult (Property 11)
    - **Property 11: Command routing returns unified CommandResult**
    - For any intent type routed through `CommandRouter`, the result is a valid `CommandResult` with non-null `Success` and non-empty `Message`
    - **Validates: Requirements 7.5**

  - [x] 8.3 Write unit tests for CommandRouter
    - Test each intent type dispatches to correct handler
    - Test `PlayMusic` handler executes search-and-play flow
    - Test `Unknown` intent returns clarification message
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

- [x] 9. Implement Response Builder and TTS
  - [x] 9.1 Implement `ResponseBuilder` class
    - For successful `CommandResult`: generate conversational confirmation (e.g., "Now playing Bohemian Rhapsody by Queen")
    - For failed `CommandResult`: generate helpful error message with suggested recovery action
    - Always return a non-empty string
    - _Requirements: 8.1, 8.2_

  - [x] 9.2 Write property test: Response builder produces non-empty output for all results (Property 12)
    - **Property 12: Response builder produces non-empty output for all results**
    - For any `CommandResult` (success or failure), `BuildResponse` returns a non-empty string
    - **Validates: Requirements 8.1, 8.2**

  - [x] 9.3 Implement `TextToSpeechEngine` class
    - Implement `SpeakAsync()` to convert text to audio and play through device speaker
    - Use TLS for all TTS API communications
    - _Requirements: 8.3, 11.2_

  - [x] 9.4 Write unit tests for ResponseBuilder
    - Test success response includes track name and artist
    - Test failure response includes recovery suggestion
    - Test empty/null edge cases
    - _Requirements: 8.1, 8.2_

- [x] 10. Implement Conversation Store
  - [x] 10.1 Implement `ConversationStore` class
    - Implement `AddTurn()`: store user and assistant turns with timestamp and optional `IntentResult`
    - Implement `GetRecentTurns(int count)`: return last N turns for LLM context
    - Implement `GetLastPlayedTrack()`: scan history for most recent `PlayMusic` or `PlayMoreLikeThis` result containing a played track
    - _Requirements: 3.1, 3.2_

  - [x] 10.2 Write property test: Conversation history grows by two per command (Property 5)
    - **Property 5: Conversation history grows by two per command**
    - For any successfully processed voice command, conversation history length increases by exactly 2
    - **Validates: Requirements 3.1**

  - [x] 10.3 Write property test: PLAY_MORE_LIKE_THIS enriches with seed track (Property 6)
    - **Property 6: PLAY_MORE_LIKE_THIS enriches with seed track**
    - For any `PlayMoreLikeThis` intent where history contains a previously played track, `SearchParams` includes the seed track ID
    - **Validates: Requirements 3.2**

  - [x] 10.4 Write unit tests for ConversationStore
    - Test turns are stored and retrieved in order
    - Test `GetLastPlayedTrack` returns correct track from history
    - Test empty history returns null for last played track
    - _Requirements: 3.1, 3.2_

- [x] 11. Implement main pipeline orchestrator (JarvisApp)
  - [x] 11.1 Implement `JarvisApp` orchestrator class
    - Implement `ProcessVoiceCommandAsync(byte[] audioInput)` following the main pipeline: STT → confidence check → NLU → route → conversation store → response → TTS
    - If STT confidence < 0.4 (`ConfidenceThreshold`), return retry message with no playback side effects
    - If intent is `Unknown`, return clarification message with no playback side effects
    - Store both user turn and assistant turn in conversation history after each command
    - Implement rate limiting: reject commands exceeding 30 per minute
    - Do not log or persist raw audio recordings beyond the current pipeline execution
    - _Requirements: 1.1, 1.2, 2.4, 3.1, 11.1, 11.4_

  - [x] 11.2 Write property test: Low-confidence transcripts produce retry without side effects (Property 1)
    - **Property 1: Low-confidence transcripts produce retry without side effects**
    - For any transcript with confidence below 0.4, the pipeline returns a retry message and Spotify playback state is unchanged
    - **Validates: Requirements 1.2, 10.4**

  - [x] 11.3 Write property test: UNKNOWN intent produces clarification response (Property 4)
    - **Property 4: UNKNOWN intent produces clarification response**
    - For any `IntentResult` with `IntentType.Unknown`, the pipeline produces a `CommandResult` with clarification message and no playback modification
    - **Validates: Requirements 2.4**

  - [x] 11.4 Write property test: Rate limiting enforces command cap (Property 16)
    - **Property 16: Rate limiting enforces command cap**
    - For any sequence of voice commands exceeding 30 within one minute, commands beyond the limit are rejected
    - **Validates: Requirements 11.4**

- [x] 12. Implement error handling and resilience
  - [x] 12.1 Implement retry with exponential backoff for LLM API calls
    - Wrap LLM API calls with retry logic: up to 3 attempts with exponential backoff
    - Return user-friendly error message if all attempts fail
    - _Requirements: 10.1_

  - [x] 12.2 Implement transparent token refresh on 401 responses
    - In `SpotifyService`, intercept 401 responses, call `IAuthManager.RefreshTokenAsync()`, and retry the original request
    - If refresh also fails, prompt re-authentication
    - _Requirements: 10.2, 9.2, 9.3_

  - [x] 12.3 Implement Spotify free-tier graceful degradation
    - Detect 403 from playback control endpoints
    - Inform user that playback control requires Spotify Premium
    - Degrade to search-only functionality
    - _Requirements: 10.3_

  - [x] 12.4 Write property test: Token refresh on expiry is transparent (Property 13)
    - **Property 13: Token refresh on expiry is transparent**
    - For any Spotify API call where the access token is expired, `AuthManager` refreshes the token and retries so the caller never receives an auth error for expired tokens
    - **Validates: Requirements 9.2, 10.2**

  - [x] 12.5 Write unit tests for error handling
    - Test LLM retry with exponential backoff (mock 3 failures then success, and 3 failures then error message)
    - Test 401 → token refresh → retry flow
    - Test 403 → Premium required message and search-only degradation
    - _Requirements: 10.1, 10.2, 10.3_

- [x] 13. Wire up dependency injection and integration
  - [x] 13.1 Configure DI container and wire all components
    - Register all interfaces and implementations in `IServiceCollection`
    - Configure `HttpClient` instances for Spotify API, LLM API, Sarvam Saaras v3 STT API, Sarvam Bulbul v3 TTS API with TLS
    - Set up configuration for API keys, Spotify client ID, confidence threshold, rate limit
    - Wire the `JarvisApp` orchestrator as the main entry point within the MAUI app lifecycle
    - _Requirements: 11.2_

  - [x] 13.2 Write integration tests for the full pipeline
    - Test full pipeline with mocked external services: audio → transcript → intent → search → play → response → TTS
    - Test conversation context flow: first command plays a song, follow-up "play more like that" uses seed track
    - Test error scenarios: low confidence, unknown intent, no results, no device
    - _Requirements: 1.1, 1.2, 2.4, 3.1, 3.2, 4.3, 4.5, 4.6_

- [x] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- All external APIs (Spotify, LLM, STT, TTS) should be mocked in tests
- Implementation language: C# with .NET

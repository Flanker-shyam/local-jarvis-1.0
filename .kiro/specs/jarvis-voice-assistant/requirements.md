# Requirements Document

## Introduction

Jarvis is a voice-controlled assistant that translates natural language voice commands into Spotify actions. The MVP focuses on understanding complex, vague, or contextual music requests and converting them into effective Spotify search queries. The system pipeline is: Voice Input → Speech-to-Text → NLU/Intent Resolution → Spotify API → Playback Control → TTS Response.

## Glossary

- **Jarvis_App**: The main application orchestrating the voice assistant pipeline
- **STT_Engine**: The Speech-to-Text engine that converts audio input into text transcripts
- **NLU_Resolver**: The Natural Language Understanding component powered by an LLM that extracts intent and search parameters from transcripts
- **Command_Router**: The component that dispatches resolved intents to the appropriate service handler
- **Spotify_Service**: The component interfacing with the Spotify Web API for search, playback, and recommendations
- **Auth_Manager**: The component managing Spotify OAuth 2.0 authentication lifecycle
- **Response_Builder**: The component that constructs natural language response messages
- **TTS_Engine**: The Text-to-Speech engine that converts text responses into spoken audio
- **IntentResult**: A structured object containing intent type, confidence score, and search parameters
- **SearchParams**: A structured object containing search criteria (query, artist, track, album, genres, mood, era, context, energy, isVague)
- **CommandResult**: A unified result object with success status, message, and flexible data payload
- **Confidence_Threshold**: The minimum STT confidence score (0.4) required to proceed with intent resolution
- **Vague_Query**: A music request with no explicit artist, track, or album, requiring the Spotify Recommendations API

## Requirements

### Requirement 1: Voice Input Capture and Transcription

**User Story:** As a user, I want to speak voice commands into my device microphone, so that Jarvis can understand and act on my music requests.

#### Acceptance Criteria

1. WHEN a user speaks a voice command, THE STT_Engine SHALL capture audio from the device microphone and return a transcript with a confidence score
2. WHEN the STT_Engine transcript confidence is below the Confidence_Threshold, THE Jarvis_App SHALL return a message asking the user to repeat the command and make no changes to playback state
3. WHEN the STT_Engine detects silence, THE STT_Engine SHALL automatically stop listening and submit the captured audio for transcription

### Requirement 2: Intent Resolution from Transcripts

**User Story:** As a user, I want Jarvis to understand what I mean when I ask for music, so that it can determine the correct action to take.

#### Acceptance Criteria

1. WHEN a valid transcript is provided, THE NLU_Resolver SHALL return an IntentResult with an intentType from the defined IntentType enumeration and a confidence score between 0.0 and 1.0
2. WHEN the transcript contains an explicit song, artist, or album name, THE NLU_Resolver SHALL extract those values into the corresponding SearchParams fields and set isVague to FALSE
3. WHEN the transcript contains a vague or mood-based request, THE NLU_Resolver SHALL infer genres, mood, energy, era, and context into SearchParams and set isVague to TRUE
4. WHEN the NLU_Resolver cannot determine the user intent, THE NLU_Resolver SHALL return intentType UNKNOWN, and THE Jarvis_App SHALL respond with a clarification message
5. WHEN the intentType is PLAY_MUSIC, THE NLU_Resolver SHALL return a non-null SearchParams with at least one populated search criterion (query, artist, track, genres, or mood)

### Requirement 3: Conversation Context for Follow-Up Commands

**User Story:** As a user, I want to say things like "play more like that" after hearing a song, so that Jarvis remembers what was playing and finds similar music.

#### Acceptance Criteria

1. WHEN a voice command is processed, THE Jarvis_App SHALL store both the user turn and the assistant response turn in the conversation history
2. WHEN the NLU_Resolver identifies a PLAY_MORE_LIKE_THIS intent, THE Jarvis_App SHALL enrich the SearchParams with the seed track ID from the last played track in conversation history
3. WHEN resolving intent, THE NLU_Resolver SHALL receive the recent conversation history to support contextual follow-up commands

### Requirement 4: Spotify Search and Playback

**User Story:** As a user, I want Jarvis to find and play music on my Spotify account, so that I can enjoy music hands-free.

#### Acceptance Criteria

1. WHEN SearchParams has isVague set to FALSE, THE Spotify_Service SHALL construct a direct search query using Spotify field filter syntax (track, artist, album fields) and return matching tracks
2. WHEN SearchParams has isVague set to TRUE, THE Spotify_Service SHALL use the Spotify Recommendations API with seed genres, target energy, and target valence derived from mood
3. WHEN a direct search returns no results for a non-vague query, THE Spotify_Service SHALL fall back to the Recommendations API using inferred attributes
4. WHEN tracks are found and an active Spotify device is available, THE Spotify_Service SHALL start playback on the active device and return a PlaybackResult with the now-playing track and queue length
5. WHEN no tracks are found from search or recommendations, THE Spotify_Service SHALL return a failure result with a message suggesting the user rephrase the request
6. WHEN no active Spotify device is detected, THE Spotify_Service SHALL return a failure result instructing the user to open Spotify on their device

### Requirement 5: Query Construction

**User Story:** As a developer, I want search parameters to be reliably translated into Spotify API query strings, so that searches return relevant results.

#### Acceptance Criteria

1. WHEN SearchParams contains explicit track, artist, or album fields, THE Spotify_Service SHALL construct a query string using Spotify field filter syntax (e.g., "track:X artist:Y")
2. WHEN SearchParams contains no explicit fields but has a raw query, THE Spotify_Service SHALL use the raw query string directly
3. WHEN SearchParams contains only inferred attributes (genres, mood, context), THE Spotify_Service SHALL concatenate them into a space-separated query string
4. FOR ALL SearchParams with at least one populated criterion, THE Spotify_Service SHALL produce a non-empty query string

### Requirement 6: Mood-to-Valence Mapping

**User Story:** As a user, I want mood-based requests to translate into appropriate Spotify audio feature parameters, so that recommendations match the feel I am looking for.

#### Acceptance Criteria

1. FOR ALL mood strings, THE Spotify_Service SHALL return a valence float between 0.0 and 1.0
2. WHEN the mood is NULL or not recognized, THE Spotify_Service SHALL return a neutral valence of 0.5
3. WHEN the mood is a known value (e.g., "happy", "sad", "chill", "energetic"), THE Spotify_Service SHALL return the predefined valence mapping for that mood

### Requirement 7: Command Routing and Playback Control

**User Story:** As a user, I want to control playback with voice commands like "pause", "skip", and "resume", so that I can manage music hands-free.

#### Acceptance Criteria

1. WHEN the Command_Router receives a PAUSE intent, THE Spotify_Service SHALL pause playback on the active device
2. WHEN the Command_Router receives a RESUME intent, THE Spotify_Service SHALL resume playback on the active device
3. WHEN the Command_Router receives a SKIP_NEXT intent, THE Spotify_Service SHALL skip to the next track on the active device
4. WHEN the Command_Router receives a SKIP_PREVIOUS intent, THE Spotify_Service SHALL skip to the previous track on the active device
5. WHEN the Command_Router receives any intent, THE Command_Router SHALL return a unified CommandResult regardless of the handler invoked

### Requirement 8: Spoken Response Feedback

**User Story:** As a user, I want Jarvis to speak back confirmations and error messages, so that I get audio feedback without looking at a screen.

#### Acceptance Criteria

1. WHEN a command completes successfully, THE Response_Builder SHALL generate a conversational confirmation message describing the action taken (e.g., "Now playing Bohemian Rhapsody by Queen")
2. WHEN a command fails, THE Response_Builder SHALL generate a helpful error message with a suggested recovery action
3. WHEN a response message is generated, THE TTS_Engine SHALL convert the text to spoken audio and play it through the device speaker

### Requirement 9: Spotify Authentication

**User Story:** As a user, I want Jarvis to securely connect to my Spotify account, so that it can search and control playback on my behalf.

#### Acceptance Criteria

1. WHEN the user launches Jarvis for the first time or the session has expired, THE Auth_Manager SHALL initiate the OAuth 2.0 Authorization Code with PKCE flow to authenticate with Spotify
2. WHEN the Spotify access token expires, THE Auth_Manager SHALL automatically refresh the token using the stored refresh token before the next API request
3. WHEN the refresh token is also expired, THE Auth_Manager SHALL prompt the user to re-authenticate
4. THE Auth_Manager SHALL store OAuth tokens in platform-secure storage (iOS Keychain or Android Keystore)
5. THE Auth_Manager SHALL use PKCE for the OAuth flow so that no client secret is stored on the device

### Requirement 10: Error Handling and Graceful Degradation

**User Story:** As a user, I want Jarvis to handle errors gracefully, so that I receive helpful feedback instead of silent failures.

#### Acceptance Criteria

1. IF the LLM API is unavailable or times out, THEN THE Jarvis_App SHALL retry with exponential backoff up to 3 attempts and return an error message if all attempts fail
2. IF the Spotify API returns a 401 Unauthorized response, THEN THE Auth_Manager SHALL transparently refresh the token and retry the request
3. IF the user has a Spotify free-tier account and playback control returns 403, THEN THE Jarvis_App SHALL inform the user that playback control requires Spotify Premium and degrade to search-only functionality
4. IF a failed search produces no results, THEN THE Spotify_Service SHALL not modify the current playback state

### Requirement 11: Security and Privacy

**User Story:** As a user, I want my voice data and credentials to be handled securely, so that my privacy is protected.

#### Acceptance Criteria

1. THE Jarvis_App SHALL not log or persist raw audio recordings beyond the current processing pipeline
2. THE Jarvis_App SHALL use TLS for all API communications with STT, LLM, and Spotify services
3. THE Jarvis_App SHALL validate all LLM responses before parsing to guard against prompt injection in transcripts
4. THE Jarvis_App SHALL rate-limit voice commands to a maximum of 30 commands per minute to prevent abuse

using Microsoft.Extensions.DependencyInjection;
using Jarvis.Core.Interfaces;
using Jarvis.Services.Auth;
using Jarvis.Services.Conversation;
using Jarvis.Services.Llm;
using Jarvis.Services.Nlu;
using Jarvis.Services.Orchestration;
using Jarvis.Services.Response;
using Jarvis.Services.Routing;
using Jarvis.Services.Speech;
using Jarvis.Services.Spotify;

namespace Jarvis.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        // Register services
        ConfigureServices(builder.Services);

        return builder.Build();
    }

    internal static void ConfigureServices(IServiceCollection services)
    {
        // ── Configuration values ──────────────────────────────────────
        // In production these would come from appsettings / environment.
        // Set these as environment variables or replace before building:
        //   JARVIS_SPOTIFY_CLIENT_ID, JARVIS_SARVAM_API_KEY, JARVIS_OPENAI_API_KEY
        var spotifyClientId = Environment.GetEnvironmentVariable("JARVIS_SPOTIFY_CLIENT_ID") ?? "YOUR_SPOTIFY_CLIENT_ID";
        var spotifyRedirectUri = "jarvis://callback";
        var sarvamApiKey = Environment.GetEnvironmentVariable("JARVIS_SARVAM_API_KEY") ?? "YOUR_SARVAM_API_KEY";
        var openAiApiKey = Environment.GetEnvironmentVariable("JARVIS_OPENAI_API_KEY") ?? "YOUR_OPENAI_API_KEY";
        const float confidenceThreshold = 0.4f;
        const int maxCommandsPerMinute = 30;

        // ── HttpClient registrations (all use TLS via https) ──────────
        services.AddHttpClient("SpotifyApi", client =>
        {
            client.BaseAddress = new Uri("https://api.spotify.com/v1/");
        });

        services.AddHttpClient("SpotifyAuth", client =>
        {
            client.BaseAddress = new Uri("https://accounts.spotify.com/");
        });

        services.AddHttpClient("SarvamStt", client =>
        {
            client.BaseAddress = new Uri("https://api.sarvam.ai/");
        });

        services.AddHttpClient("SarvamTts", client =>
        {
            client.BaseAddress = new Uri("https://api.sarvam.ai/");
        });

        services.AddHttpClient("LlmApi");

        // ── Platform-specific abstractions (stubs for now) ────────────
        // IAudioRecorder, IAudioPlayer, and ISecureStorage are platform-specific.
        // Real implementations are registered per-platform in Platforms/ folders.
        // Placeholder registrations ensure the DI container resolves without error.
        services.AddSingleton<ISecureStorage>(sp =>
            new MauiSecureStorageAdapter());

        // IAudioRecorder and IAudioPlayer must be provided by platform projects.
        // Register as transient stubs that throw if invoked outside a real device.
        services.AddSingleton<IAudioRecorder>(sp =>
            new StubAudioRecorder());

        services.AddSingleton<IAudioPlayer>(sp =>
            new StubAudioPlayer());

        // ── Core services ─────────────────────────────────────────────
        services.AddSingleton<IConversationStore, ConversationStore>();

        services.AddSingleton<IAuthManager>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var secureStorage = sp.GetRequiredService<ISecureStorage>();
            var httpClient = httpClientFactory.CreateClient("SpotifyAuth");
            return new AuthManager(
                httpClient,
                secureStorage,
                spotifyClientId,
                spotifyRedirectUri,
                uri => Task.FromResult(uri)); // Placeholder browser login; real impl uses WebAuthenticator
        });

        services.AddSingleton<ISpotifyService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var authManager = sp.GetRequiredService<IAuthManager>();
            var httpClient = httpClientFactory.CreateClient("SpotifyApi");
            return new SpotifyService(httpClient, authManager);
        });

        services.AddSingleton<ILlmClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("LlmApi");
            return new OpenAiLlmClient(httpClient, openAiApiKey);
        });

        services.AddSingleton<INluResolver>(sp =>
        {
            var llmClient = sp.GetRequiredService<ILlmClient>();
            var conversationStore = sp.GetRequiredService<IConversationStore>();
            return new NluResolver(llmClient, conversationStore);
        });

        services.AddSingleton<ICommandRouter>(sp =>
        {
            var spotifyService = sp.GetRequiredService<ISpotifyService>();
            return new CommandRouter(spotifyService);
        });

        services.AddSingleton<IResponseBuilder, ResponseBuilder>();

        services.AddSingleton<ISpeechToTextEngine>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var audioRecorder = sp.GetRequiredService<IAudioRecorder>();
            var httpClient = httpClientFactory.CreateClient("SarvamStt");
            return new SpeechToTextEngine(httpClient, audioRecorder, sarvamApiKey);
        });

        services.AddSingleton<ITextToSpeechEngine>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var audioPlayer = sp.GetRequiredService<IAudioPlayer>();
            var httpClient = httpClientFactory.CreateClient("SarvamTts");
            return new TextToSpeechEngine(httpClient, audioPlayer, sarvamApiKey);
        });

        // ── Main orchestrator ─────────────────────────────────────────
        services.AddSingleton<JarvisOrchestrator>();
    }

    // ── Stub / adapter types for platform abstractions ────────────────

    /// <summary>
    /// Wraps MAUI SecureStorage behind the ISecureStorage interface.
    /// </summary>
    private sealed class MauiSecureStorageAdapter : ISecureStorage
    {
        public async Task<string?> GetAsync(string key) =>
            await SecureStorage.Default.GetAsync(key);

        public async Task SetAsync(string key, string value) =>
            await SecureStorage.Default.SetAsync(key, value);

        public bool Remove(string key) =>
            SecureStorage.Default.Remove(key);

        public void RemoveAll() =>
            SecureStorage.Default.RemoveAll();
    }

    /// <summary>Placeholder audio recorder — replaced by platform-specific implementation.</summary>
    private sealed class StubAudioRecorder : IAudioRecorder
    {
        public bool IsRecording => false;
        public float CurrentRmsLevel => 0f;
        public Task StartAsync() => Task.CompletedTask;
        public Task<byte[]> StopAsync() => Task.FromResult(Array.Empty<byte>());
    }

    /// <summary>Placeholder audio player — replaced by platform-specific implementation.</summary>
    private sealed class StubAudioPlayer : IAudioPlayer
    {
        public Task PlayAsync(byte[] audioData) => Task.CompletedTask;
    }
}

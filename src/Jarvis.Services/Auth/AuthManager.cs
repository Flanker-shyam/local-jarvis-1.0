using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Core.Interfaces;

namespace Jarvis.Services.Auth;

/// <summary>
/// Manages Spotify OAuth 2.0 Authorization Code with PKCE flow.
/// No client secret is stored on device — PKCE only.
/// </summary>
public class AuthManager : IAuthManager
{
    private const string AccessTokenKey = "spotify_access_token";
    private const string RefreshTokenKey = "spotify_refresh_token";
    private const string TokenExpiryKey = "spotify_token_expiry";

    private const string SpotifyAuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string SpotifyTokenUrl = "https://accounts.spotify.com/api/token";

    private readonly HttpClient _httpClient;
    private readonly ISecureStorage _secureStorage;
    private readonly string _clientId;
    private readonly string _redirectUri;
    private readonly Func<Uri, Task<Uri>> _browserLoginFunc;

    private string? _cachedAccessToken;
    private DateTimeOffset _cachedExpiry = DateTimeOffset.MinValue;

    /// <summary>
    /// Creates a new AuthManager.
    /// </summary>
    /// <param name="httpClient">HttpClient for token exchange requests.</param>
    /// <param name="secureStorage">Platform-secure storage for persisting tokens.</param>
    /// <param name="clientId">Spotify application client ID.</param>
    /// <param name="redirectUri">OAuth redirect URI registered with Spotify.</param>
    /// <param name="browserLoginFunc">
    /// Function that opens the authorization URI in a browser and returns the
    /// redirect URI containing the authorization code. In MAUI this wraps
    /// WebAuthenticator.AuthenticateAsync; in tests it can be stubbed.
    /// </param>
    public AuthManager(
        HttpClient httpClient,
        ISecureStorage secureStorage,
        string clientId,
        string redirectUri,
        Func<Uri, Task<Uri>> browserLoginFunc)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _redirectUri = redirectUri ?? throw new ArgumentNullException(nameof(redirectUri));
        _browserLoginFunc = browserLoginFunc ?? throw new ArgumentNullException(nameof(browserLoginFunc));
    }

    /// <inheritdoc />
    public bool IsSessionValid =>
        _cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedExpiry;

    /// <inheritdoc />
    public async Task<string> GetValidTokenAsync()
    {
        // 1. Return cached token if still valid
        if (IsSessionValid)
            return _cachedAccessToken!;

        // 2. Try to load from secure storage
        var storedToken = await _secureStorage.GetAsync(AccessTokenKey);
        var storedExpiry = await _secureStorage.GetAsync(TokenExpiryKey);

        if (storedToken is not null && storedExpiry is not null
            && DateTimeOffset.TryParse(storedExpiry, out var expiry)
            && DateTimeOffset.UtcNow < expiry)
        {
            _cachedAccessToken = storedToken;
            _cachedExpiry = expiry;
            return storedToken;
        }

        // 3. Token expired — try refresh
        var refreshToken = await _secureStorage.GetAsync(RefreshTokenKey);
        if (refreshToken is not null)
        {
            try
            {
                await RefreshTokenAsync();
                return _cachedAccessToken!;
            }
            catch
            {
                // Refresh failed — fall through to re-auth
            }
        }

        // 4. No valid tokens — full re-authentication
        await AuthenticateAsync();
        return _cachedAccessToken!;
    }

    /// <inheritdoc />
    public async Task RefreshTokenAsync()
    {
        var refreshToken = await _secureStorage.GetAsync(RefreshTokenKey)
            ?? throw new InvalidOperationException("No refresh token available. Re-authentication required.");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId
        });

        var response = await _httpClient.PostAsync(SpotifyTokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            // Refresh token is likely expired/revoked — clear stored tokens
            _secureStorage.Remove(AccessTokenKey);
            _secureStorage.Remove(RefreshTokenKey);
            _secureStorage.Remove(TokenExpiryKey);
            _cachedAccessToken = null;
            _cachedExpiry = DateTimeOffset.MinValue;
            throw new InvalidOperationException(
                $"Token refresh failed with status {response.StatusCode}. Re-authentication required.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize token response.");

        await StoreTokensAsync(tokenResponse);
    }

    /// <inheritdoc />
    public async Task AuthenticateAsync()
    {
        // Generate PKCE code verifier and challenge (no client secret needed)
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing";

        var authUrl = $"{SpotifyAuthorizeUrl}" +
            $"?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&code_challenge_method=S256" +
            $"&code_challenge={codeChallenge}";

        // Open browser for user login; receive redirect with auth code
        var callbackUri = await _browserLoginFunc(new Uri(authUrl));

        var queryParams = System.Web.HttpUtility.ParseQueryString(callbackUri.Query);
        var code = queryParams["code"]
            ?? throw new InvalidOperationException("Authorization code not found in callback URI.");

        // Exchange authorization code for tokens (PKCE — no client secret)
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUri,
            ["client_id"] = _clientId,
            ["code_verifier"] = codeVerifier
        });

        var response = await _httpClient.PostAsync(SpotifyTokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Token exchange failed with status {response.StatusCode}: {body}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize token response.");

        await StoreTokensAsync(tokenResponse);
    }

    // ── PKCE helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random code verifier (43-128 chars, unreserved characters).
    /// Visible for testing via <c>internal</c> + InternalsVisibleTo.
    /// </summary>
    internal static string GenerateCodeVerifier(int length = 64)
    {
        const string unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = unreserved[bytes[i] % unreserved.Length];
        return new string(chars);
    }

    /// <summary>
    /// Derives the S256 code challenge from a code verifier per RFC 7636.
    /// </summary>
    internal static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // ── Private helpers ───────────────────────────────────────────────

    private async Task StoreTokensAsync(SpotifyTokenResponse tokenResponse)
    {
        _cachedAccessToken = tokenResponse.AccessToken;
        _cachedExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        await _secureStorage.SetAsync(AccessTokenKey, tokenResponse.AccessToken);
        await _secureStorage.SetAsync(TokenExpiryKey, _cachedExpiry.ToString("O"));

        if (tokenResponse.RefreshToken is not null)
            await _secureStorage.SetAsync(RefreshTokenKey, tokenResponse.RefreshToken);
    }

    // ── Token response DTO ────────────────────────────────────────────

    internal sealed class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}

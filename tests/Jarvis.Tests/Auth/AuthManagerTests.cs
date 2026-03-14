using System.Net;
using System.Text;
using System.Text.Json;
using Jarvis.Core.Interfaces;
using Jarvis.Services.Auth;
using Moq;

namespace Jarvis.Tests.Auth;

/// <summary>
/// Unit tests for AuthManager — Spotify OAuth 2.0 PKCE flow.
/// Validates: Requirements 9.1, 9.2, 9.3, 9.5
/// </summary>
public class AuthManagerTests
{
    private const string ClientId = "test-client-id";
    private const string RedirectUri = "jarvis://callback";

    private readonly Mock<ISecureStorage> _storageMock;
    private readonly Func<Uri, Task<Uri>> _fakeBrowserLogin;

    public AuthManagerTests()
    {
        _storageMock = new Mock<ISecureStorage>();

        // Default browser login stub: returns a callback URI with an auth code
        _fakeBrowserLogin = _ => Task.FromResult(
            new Uri($"{RedirectUri}?code=test-auth-code"));
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new FakeHttpHandler(handler));
    }

    private static HttpResponseMessage TokenResponse(
        string accessToken = "new-access-token",
        int expiresIn = 3600,
        string? refreshToken = "new-refresh-token")
    {
        var json = JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = expiresIn,
            refresh_token = refreshToken,
            scope = "user-read-playback-state"
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private AuthManager CreateManager(HttpClient httpClient) =>
        new(httpClient, _storageMock.Object, ClientId, RedirectUri, _fakeBrowserLogin);

    private AuthManager CreateManager(HttpClient httpClient, Func<Uri, Task<Uri>> browserLogin) =>
        new(httpClient, _storageMock.Object, ClientId, RedirectUri, browserLogin);

    // ── 1. GetValidTokenAsync returns cached token when not expired ──

    [Fact]
    public async Task GetValidTokenAsync_ReturnsCachedToken_WhenNotExpired()
    {
        // Arrange: first call authenticates, second should return cached
        var httpClient = CreateHttpClient(_ => Task.FromResult(TokenResponse()));
        var manager = CreateManager(httpClient);

        var firstToken = await manager.GetValidTokenAsync();
        var secondToken = await manager.GetValidTokenAsync();

        Assert.Equal(firstToken, secondToken);
        Assert.Equal("new-access-token", secondToken);
    }

    // ── 2. GetValidTokenAsync refreshes when access token expired but refresh token available ──

    [Fact]
    public async Task GetValidTokenAsync_RefreshesToken_WhenAccessTokenExpiredButRefreshAvailable()
    {
        var callCount = 0;
        var httpClient = CreateHttpClient(_ =>
        {
            callCount++;
            return Task.FromResult(TokenResponse(
                accessToken: $"token-{callCount}",
                refreshToken: "refresh-token"));
        });

        // Storage returns expired access token but valid refresh token
        _storageMock.Setup(s => s.GetAsync("spotify_access_token"))
            .ReturnsAsync("expired-token");
        _storageMock.Setup(s => s.GetAsync("spotify_token_expiry"))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")); // expired
        _storageMock.Setup(s => s.GetAsync("spotify_refresh_token"))
            .ReturnsAsync("stored-refresh-token");

        var manager = CreateManager(httpClient);
        var token = await manager.GetValidTokenAsync();

        // Should have refreshed and returned a new token
        Assert.Equal("token-1", token);
    }

    // ── 3. GetValidTokenAsync triggers full re-auth when refresh token also fails ──

    [Fact]
    public async Task GetValidTokenAsync_TriggersFullReAuth_WhenRefreshFails()
    {
        var requestIndex = 0;
        var httpClient = CreateHttpClient(_ =>
        {
            requestIndex++;
            if (requestIndex == 1)
            {
                // First call: refresh fails
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }
            // Second call: full auth succeeds
            return Task.FromResult(TokenResponse(accessToken: "reauth-token"));
        });

        // Storage: expired access token, refresh token present
        _storageMock.Setup(s => s.GetAsync("spotify_access_token"))
            .ReturnsAsync("expired-token");
        _storageMock.Setup(s => s.GetAsync("spotify_token_expiry"))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
        _storageMock.Setup(s => s.GetAsync("spotify_refresh_token"))
            .ReturnsAsync("expired-refresh-token");

        var manager = CreateManager(httpClient);
        var token = await manager.GetValidTokenAsync();

        Assert.Equal("reauth-token", token);
    }

    // ── 4. RefreshTokenAsync exchanges refresh token and stores new tokens ──

    [Fact]
    public async Task RefreshTokenAsync_ExchangesRefreshToken_AndStoresNewTokens()
    {
        var httpClient = CreateHttpClient(_ =>
            Task.FromResult(TokenResponse(
                accessToken: "refreshed-access",
                refreshToken: "refreshed-refresh")));

        _storageMock.Setup(s => s.GetAsync("spotify_refresh_token"))
            .ReturnsAsync("old-refresh-token");

        var manager = CreateManager(httpClient);
        await manager.RefreshTokenAsync();

        _storageMock.Verify(s => s.SetAsync("spotify_access_token", "refreshed-access"), Times.Once);
        _storageMock.Verify(s => s.SetAsync("spotify_refresh_token", "refreshed-refresh"), Times.Once);
        _storageMock.Verify(s => s.SetAsync("spotify_token_expiry", It.IsAny<string>()), Times.Once);
    }

    // ── 5. RefreshTokenAsync clears storage and throws when refresh fails ──

    [Fact]
    public async Task RefreshTokenAsync_ClearsStorageAndThrows_WhenRefreshFails()
    {
        var httpClient = CreateHttpClient(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        _storageMock.Setup(s => s.GetAsync("spotify_refresh_token"))
            .ReturnsAsync("bad-refresh-token");

        var manager = CreateManager(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.RefreshTokenAsync());

        Assert.Contains("Re-authentication required", ex.Message);
        _storageMock.Verify(s => s.Remove("spotify_access_token"), Times.Once);
        _storageMock.Verify(s => s.Remove("spotify_refresh_token"), Times.Once);
        _storageMock.Verify(s => s.Remove("spotify_token_expiry"), Times.Once);
    }

    // ── 6. AuthenticateAsync generates valid PKCE verifier (43-128 chars, unreserved chars only) ──

    [Fact]
    public void GenerateCodeVerifier_ProducesValidPkceVerifier()
    {
        const string unreservedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

        var verifier = AuthManager.GenerateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.All(verifier, c => Assert.Contains(c, unreservedChars));
    }

    // ── 7. AuthenticateAsync generates valid S256 code challenge ──

    [Fact]
    public void GenerateCodeChallenge_ProducesValidS256Challenge()
    {
        var verifier = AuthManager.GenerateCodeVerifier();
        var challenge = AuthManager.GenerateCodeChallenge(verifier);

        Assert.NotEmpty(challenge);
        // S256 challenge is base64url-encoded SHA256 — verify by recomputing
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        var expected = Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expected, challenge);
    }

    // ── 8. PKCE code verifier length is correct ──

    [Theory]
    [InlineData(43)]
    [InlineData(64)]
    [InlineData(128)]
    public void GenerateCodeVerifier_RespectsRequestedLength(int length)
    {
        var verifier = AuthManager.GenerateCodeVerifier(length);
        Assert.Equal(length, verifier.Length);
    }

    // ── 9. PKCE code challenge is base64url encoded (no +, /, or = chars) ──

    [Fact]
    public void GenerateCodeChallenge_IsBase64UrlEncoded()
    {
        // Run multiple times to increase confidence (PKCE is random)
        for (var i = 0; i < 20; i++)
        {
            var verifier = AuthManager.GenerateCodeVerifier();
            var challenge = AuthManager.GenerateCodeChallenge(verifier);

            Assert.DoesNotContain("+", challenge);
            Assert.DoesNotContain("/", challenge);
            Assert.DoesNotContain("=", challenge);
        }
    }

    // ── Custom HttpMessageHandler for intercepting HTTP requests ─────

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public FakeHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}

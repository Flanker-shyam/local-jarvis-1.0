namespace Jarvis.Core.Interfaces;

/// <summary>
/// Abstraction over platform-secure storage (iOS Keychain / Android Keystore).
/// Allows mocking in tests without depending on MAUI SecureStorage directly.
/// </summary>
public interface ISecureStorage
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    bool Remove(string key);
    void RemoveAll();
}

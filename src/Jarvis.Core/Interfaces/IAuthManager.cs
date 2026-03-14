namespace Jarvis.Core.Interfaces;

public interface IAuthManager
{
    Task<string> GetValidTokenAsync();
    Task AuthenticateAsync();
    Task RefreshTokenAsync();
    bool IsSessionValid { get; }
}

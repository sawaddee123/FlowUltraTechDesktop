using System;
using System.Threading.Tasks;

namespace TiktokStudioSandbox.Core.Interfaces;

public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? Username { get; set; }
    public string? Role { get; set; }
    public int MaxTiktokAccounts { get; set; } = 1;
    public string? Plan { get; set; }
    public DateTime? ExpiredAt { get; set; }
}

public interface IAuthClientService
{
    Task<LoginResult> LoginAsync(string username, string password, string hwid, string? endpoint = null);
    Task<LoginResult> GetProfileAsync(string accessToken, string? endpoint = null);
}

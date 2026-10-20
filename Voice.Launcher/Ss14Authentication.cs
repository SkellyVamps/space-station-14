using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Skelly.VoiceLauncher;

/// <summary>Official SS14 auth protocol. The launcher never persists credentials or sessions.</summary>
public sealed class Ss14Authentication(HttpClient http)
{
    public const string AuthServer = "https://auth.spacestation14.com/";
    public sealed record Session(string Token, string Username, Guid UserId, DateTimeOffset ExpireTime);
    public sealed class LoginDenied(string code) : Exception(code) { public string Code { get; } = code; }

    public async Task<Session> Login(string username, string password, string? tfa, CancellationToken cancel)
    {
        using var response = await http.PostAsJsonAsync(AuthServer + "api/auth/authenticate",
            new { Username = username, UserId = (Guid?)null, Password = password, TfaCode = tfa }, cancel);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            using var denied = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            throw new LoginDenied(denied.RootElement.GetProperty("code").ToString());
        }
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<Session>(cancellationToken: cancel)
            ?? throw new InvalidDataException("Empty authentication response");
        if (session.UserId == Guid.Empty || string.IsNullOrWhiteSpace(session.Token) ||
            string.IsNullOrWhiteSpace(session.Username) || session.ExpireTime <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Invalid authentication response");
        return session;
    }

    public async Task<Session> Refresh(Session session, CancellationToken cancel)
    {
        using var response = await http.PostAsJsonAsync(AuthServer + "api/auth/refresh", new { session.Token }, cancel);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var token = body.RootElement.GetProperty("newToken").GetString();
        var expiry = body.RootElement.GetProperty("expireTime").GetDateTimeOffset();
        if (string.IsNullOrWhiteSpace(token) || expiry <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Invalid renewal response");
        return session with { Token = token, ExpireTime = expiry };
    }

    public async Task Logout(Session session, CancellationToken cancel)
    {
        using var response = await http.PostAsJsonAsync(AuthServer + "api/auth/logout", new { session.Token }, cancel);
        response.EnsureSuccessStatusCode();
    }
}

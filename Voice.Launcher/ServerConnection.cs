using System.Diagnostics;
using System.Text.Json;

namespace Skelly.VoiceLauncher;

public static class ServerConnection
{
    public static Uri ParseAddress(string address)
    {
        if (!address.Contains("://")) address = "ss14://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("ss14" or "ss14s") ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("Enter an ss14:// or ss14s:// server address.");
        return uri;
    }

    public sealed record Target(string PublicKey, Uri Endpoint);
    public static async Task<Target> GetTarget(HttpClient http, Uri address, CancellationToken cancel)
    {
        var endpoint = new UriBuilder(address)
        { Scheme = address.Scheme == "ss14s" ? "https" : "http", Port = address.Port == -1 ? (address.Scheme == "ss14s" ? 443 : 1212) : address.Port, Path = "info" }.Uri;
        using var response = await http.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Server information too large");
        using var input = await response.Content.ReadAsStreamAsync(cancel);
        var bytes = new byte[65537];
        var used = 0;
        while (used < bytes.Length)
        {
            var read = await input.ReadAsync(bytes.AsMemory(used), cancel);
            if (read == 0) break;
            used += read;
        }
        if (used == bytes.Length) throw new InvalidDataException("Server information too large");
        using var info = JsonDocument.Parse(bytes.AsMemory(0, used));
        var auth = info.RootElement.GetProperty("auth");
        if (auth.GetProperty("mode").ToString() is not ("Required" or "Optional" or "1" or "0"))
            throw new InvalidDataException("An authenticated server is required");
        var key = auth.GetProperty("public_key").GetString();
        if (string.IsNullOrWhiteSpace(key) || Convert.FromBase64String(key).Length is < 32 or > 4096)
            throw new InvalidDataException("Invalid server authentication key");
        var connect = new UriBuilder("udp", address.Host, endpoint.Port).Uri;
        if (info.RootElement.TryGetProperty("connect_address", out var advertised) && advertised.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(advertised.GetString()))
        {
            if (!Uri.TryCreate(advertised.GetString(), UriKind.Absolute, out var candidate) || candidate.Scheme != "udp" ||
                string.IsNullOrEmpty(candidate.Host) || candidate.Port is < 1 or > 65535 || !string.IsNullOrEmpty(candidate.UserInfo) ||
                !string.IsNullOrEmpty(candidate.Query) || !string.IsNullOrEmpty(candidate.Fragment) || candidate.AbsolutePath != "/")
                throw new InvalidDataException("Invalid advertised game address");
            connect = candidate;
        }
        return new Target(key, connect);
    }

    public static ProcessStartInfo CreateLaunch(Target target, Ss14Authentication.Session session)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Client", OperatingSystem.IsWindows() ? "SkellySS14.exe" : "SkellySS14");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var arg in new[] { "--username", session.Username, "--connect", "--connect-address", target.Endpoint.AbsoluteUri })
            start.ArgumentList.Add(arg);
        start.Environment["ROBUST_AUTH_TOKEN"] = session.Token;
        start.Environment["ROBUST_AUTH_USERID"] = session.UserId.ToString();
        start.Environment["ROBUST_AUTH_PUBKEY"] = target.PublicKey;
        start.Environment["ROBUST_AUTH_SERVER"] = Ss14Authentication.AuthServer;
        return start;
    }
}

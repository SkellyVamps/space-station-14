namespace EchoesVoice;

/// <summary>
/// Where to connect and as whom: a game server's web address and the player's link code, from whatever the player
/// pasted or clicked.
/// </summary>
public sealed record VoiceLink(Uri Server, string Code)
{
    public const string Scheme = "echoesvoice";

    /// <summary>
    /// Accepts the link the game copies (<c>https://server/voice#CODE</c>), the app link the voice page opens
    /// (<c>echoesvoice://connect?server=https://server&amp;code=CODE</c>), or a server address and code separated by a
    /// space.
    /// </summary>
    public static bool TryParse(string? text, out VoiceLink link)
    {
        link = default!;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var app))
                return false;

            string? server = null, code = null;
            foreach (var part in app.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2)
                    continue;

                var value = Uri.UnescapeDataString(pair[1]);
                if (pair[0] == "server")
                    server = value;
                else if (pair[0] == "code")
                    code = value;
            }

            return TryMake(server, code, out link);
        }

        var space = text.IndexOf(' ');
        if (space > 0)
            return TryMake(text[..space], text[(space + 1)..], out link);

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Fragment.Length < 2)
            return false;

        return TryMake(uri.GetLeftPart(UriPartial.Authority), uri.Fragment[1..], out link);
    }

    private static bool TryMake(string? server, string? code, out VoiceLink link)
    {
        link = default!;
        code = Uri.UnescapeDataString(code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(server) || code.Length < 8 || code.Length > 64 || !code.All(char.IsLetterOrDigit))
            return false;

        if (!server.Contains("://"))
            server = "https://" + server;

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;

        link = new VoiceLink(new Uri(uri.GetLeftPart(UriPartial.Authority)), code);
        return true;
    }

    /// <summary>
    /// The WebSocket address on that server.
    /// </summary>
    public Uri SocketUri => new((Server.Scheme == "https" ? "wss" : "ws") + "://" + Server.Authority + "/voice/ws");

    public override string ToString() => Server.Authority;
}

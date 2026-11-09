using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Content.Shared.Voice.Codecs;
using Robust.Server.ServerStatus;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;

namespace Content.Server.Voice.External;

/// <summary>
/// Serves the voice webpage (<c>/voice</c>) and the live connection (<c>/voice/ws</c>) that the webpage and the
/// standalone voice app send microphone audio over, on the server's existing web (status) port. Also serves the
/// standalone app's downloads.
/// </summary>
/// <remarks>
/// <para>Runs on the web server's threads. Everything that touches the game goes through <see cref="TryDequeue"/>,
/// which <see cref="VoiceExternalMicSystem"/> drains on the main thread.</para>
/// <para>A page or app proves who it is with a one-time code the game client asked for over its own (authenticated)
/// connection, so nobody can speak as someone else. Codes last until the player leaves the server.</para>
/// </remarks>
public sealed partial class VoiceWebManager
{
    [Dependency] private IStatusHost _status = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private ILogManager _logManager = default!;

    public const string BasePath = "/voice";

    /// <summary>
    /// First byte of an audio message: what's in it.
    /// </summary>
    public const byte AudioAdpcm = 1;

    /// <summary>
    /// Audio message: kind, sequence number (uint16 LE), then one <see cref="ImaAdpcm"/> frame.
    /// </summary>
    public const int AudioMessageBytes = 3 + ImaAdpcm.FrameBytes;

    public const WebSocketCloseStatus CloseBadCode = (WebSocketCloseStatus) 4001;
    public const WebSocketCloseStatus CloseReplaced = (WebSocketCloseStatus) 4002;
    public const WebSocketCloseStatus CloseLeftGame = (WebSocketCloseStatus) 4003;
    public const WebSocketCloseStatus CloseShutdown = (WebSocketCloseStatus) 4004;
    public const WebSocketCloseStatus CloseDisabled = (WebSocketCloseStatus) 4005;

    private const int MaxConnections = 512;
    private const int MaxQueuedFrames = 4096;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    // Audio rate limit per connection: a little over 50 frames a second, with some slack for network bursts.
    private const double FramesPerSecond = 60;
    private const double FrameBurst = 30;

    private static readonly string[] DownloadExtensions = { ".zip", ".exe", ".msi", ".dmg", ".appimage", ".tar.gz", ".deb" };

    private ISawmill _log = default!;
    private readonly ConcurrentDictionary<string, NetUserId> _codes = new();
    private readonly ConcurrentDictionary<NetUserId, string> _userCodes = new();
    private readonly ConcurrentDictionary<NetUserId, Connection> _connections = new();
    private readonly ConcurrentQueue<VoiceWebEvent> _events = new();
    private readonly Dictionary<string, byte[]> _assets = new();
    private readonly CancellationTokenSource _shutdown = new();

    private int _connectionCount;
    private int _queuedFrames;
    private volatile bool _enabled;
    private volatile string _appDownloadUrl = string.Empty;

    public void Initialize()
    {
        _log = _logManager.GetSawmill("voice.web");

        _cfg.OnValueChanged(CCVars.VoiceExternalEnabled, OnEnabledChanged, true);
        _cfg.OnValueChanged(CCVars.VoiceExternalAppDownloadUrl, v => _appDownloadUrl = v.Trim(), true);

        LoadAssets();
        _status.AddHandler(HandleRequest);
    }

    public void Shutdown()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Close(CloseShutdown, "The server is shutting down.");
        }

        _shutdown.CancelAfter(TimeSpan.FromSeconds(2));
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
            return;

        foreach (var connection in _connections.Values)
        {
            connection.Close(CloseDisabled, "This server turned off the voice webpage.");
        }
    }

    #region Codes and connections (any thread)

    /// <summary>
    /// The code that links a webpage or app to <paramref name="user"/>, made the first time it's asked for.
    /// </summary>
    public string GetOrCreateCode(NetUserId user)
    {
        return _userCodes.GetOrAdd(user, u =>
        {
            // 120 bits: not guessable. URL safe, and short enough to type into the app if needed.
            var code = Base32(RandomNumberGenerator.GetBytes(15));
            _codes[code] = u;
            return code;
        });
    }

    /// <summary>
    /// <paramref name="user"/> left the game: their code stops working and their page or app is disconnected.
    /// </summary>
    public void Forget(NetUserId user)
    {
        if (_userCodes.TryRemove(user, out var code))
            _codes.TryRemove(code, out _);

        Disconnect(user, CloseLeftGame, "You left the game server.");
    }

    public void Disconnect(NetUserId user, WebSocketCloseStatus status, string reason)
    {
        if (_connections.TryGetValue(user, out var connection))
            connection.Close(status, reason);
    }

    public bool IsConnected(NetUserId user) => _connections.ContainsKey(user);

    /// <summary>
    /// Sends a status message to <paramref name="user"/>'s page or app.
    /// </summary>
    public void Send(NetUserId user, JsonObject message)
    {
        if (_connections.TryGetValue(user, out var connection))
            connection.Send(message.ToJsonString());
    }

    public bool TryDequeue(out VoiceWebEvent ev)
    {
        if (!_events.TryDequeue(out ev!))
            return false;

        if (ev is VoiceWebFrame)
            Interlocked.Decrement(ref _queuedFrames);

        return true;
    }

    /// <summary>
    /// For tests: pretends a page connected and sent this audio.
    /// </summary>
    public void InjectForTest(VoiceWebEvent ev) => _events.Enqueue(ev);

    #endregion

    #region HTTP

    private async Task<bool> HandleRequest(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != BasePath && !path.StartsWith(BasePath + "/", StringComparison.Ordinal))
            return false;

        if (!_enabled)
        {
            await context.RespondAsync("The voice webpage is turned off on this server.", HttpStatusCode.NotFound);
            return true;
        }

        var rest = path.Length > BasePath.Length ? path[(BasePath.Length + 1)..] : string.Empty;

        if (rest == "ws")
        {
            await AcceptWebSocket(context);
            return true;
        }

        if (!context.IsGetLike)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        if (rest == "downloads")
        {
            await RespondDownloadList(context);
            return true;
        }

        if (rest.StartsWith("download/", StringComparison.Ordinal))
        {
            await RespondDownload(context, rest["download/".Length..]);
            return true;
        }

        if (rest == string.Empty)
            rest = "index.html";

        if (!_assets.TryGetValue(rest, out var data))
        {
            await context.RespondErrorAsync(HttpStatusCode.NotFound);
            return true;
        }

        context.ResponseHeaders["Cache-Control"] = "no-cache";
        context.ResponseHeaders["X-Content-Type-Options"] = "nosniff";
        context.ResponseHeaders["Referrer-Policy"] = "no-referrer";
        // The page is allowed the microphone; nothing may frame it.
        context.ResponseHeaders["Permissions-Policy"] = "microphone=(self)";
        context.ResponseHeaders["Content-Security-Policy"] =
            "default-src 'self'; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'none'";
        await context.RespondAsync(data, HttpStatusCode.OK, ContentType(rest));
        return true;
    }

    private void LoadAssets()
    {
        var assembly = typeof(VoiceWebManager).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            const string prefix = "VoiceWeb.";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _assets[name[prefix.Length..]] = memory.ToArray();
        }
    }

    private static string ContentType(string file)
    {
        return Path.GetExtension(file) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
    }

    private string? DownloadDirectory()
    {
        return _res.UserData.RootDir is { } root ? Path.Combine(root, "voice-app") : null;
    }

    private IEnumerable<FileInfo> DownloadFiles()
    {
        if (DownloadDirectory() is not { } dir || !Directory.Exists(dir))
            return Enumerable.Empty<FileInfo>();

        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => DownloadExtensions.Any(e => f.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
    }

    private async Task RespondDownloadList(IStatusHandlerContext context)
    {
        var files = new JsonArray();
        foreach (var file in DownloadFiles())
        {
            files.Add(new JsonObject
            {
                ["name"] = file.Name,
                ["size"] = file.Length,
                ["url"] = $"{BasePath}/download/{Uri.EscapeDataString(file.Name)}",
            });
        }

        var result = new JsonObject
        {
            ["url"] = string.IsNullOrEmpty(_appDownloadUrl) ? null : _appDownloadUrl,
            ["files"] = files,
        };

        context.ResponseHeaders["Cache-Control"] = "no-cache";
        await context.RespondAsync(result.ToJsonString(), HttpStatusCode.OK, "application/json");
    }

    private async Task RespondDownload(IStatusHandlerContext context, string escapedName)
    {
        var name = Uri.UnescapeDataString(escapedName);

        // Only exactly the files listed: never a path into anywhere else.
        var file = DownloadFiles().FirstOrDefault(f => f.Name == name);
        if (file == null)
        {
            await context.RespondErrorAsync(HttpStatusCode.NotFound);
            return;
        }

        context.ResponseHeaders["Content-Disposition"] = $"attachment; filename=\"{file.Name}\"";
        context.ResponseHeaders["Content-Type"] = "application/octet-stream";
        context.ResponseHeaders["Content-Length"] = file.Length.ToString();
        await using var output = await context.RespondStreamAsync();
        await using var input = file.OpenRead();
        await input.CopyToAsync(output);
    }

    #endregion

    #region WebSocket

    private async Task AcceptWebSocket(IStatusHandlerContext context)
    {
        if (!context.IsWebSocketRequest)
        {
            await context.RespondAsync("This address is for the voice webpage and app.", HttpStatusCode.BadRequest);
            return;
        }

        if (Interlocked.Increment(ref _connectionCount) > MaxConnections)
        {
            Interlocked.Decrement(ref _connectionCount);
            await context.RespondErrorAsync(HttpStatusCode.ServiceUnavailable);
            return;
        }

        WebSocket socket;
        try
        {
            socket = await context.AcceptWebSocketAsync();
        }
        catch (Exception e)
        {
            Interlocked.Decrement(ref _connectionCount);
            _log.Debug($"WebSocket upgrade failed: {e.Message}");
            return;
        }

        // Keep the connection on its own task: the web server's request slots are few and must not be held.
        _ = Task.Run(async () =>
        {
            try
            {
                await RunConnection(socket);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
            {
                // Network gone.
            }
            catch (Exception e)
            {
                _log.Error($"Voice web connection error: {e}");
            }
            finally
            {
                Interlocked.Decrement(ref _connectionCount);
                socket.Dispose();
            }
        });
    }

    private async Task RunConnection(WebSocket socket)
    {
        var buffer = new byte[4096];
        var cancel = _shutdown.Token;

        // Hello: {"type":"hello","code":"...","kind":"web"|"app"}
        using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        helloTimeout.CancelAfter(HelloTimeout);
        var (helloType, helloLength) = await ReceiveMessage(socket, buffer, helloTimeout.Token);
        if (helloType != WebSocketMessageType.Text || helloLength <= 0)
            return;

        string? code = null;
        var kind = "web";
        try
        {
            var hello = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, helloLength));
            code = hello?["code"]?.GetValue<string>()?.Trim().ToUpperInvariant();
            kind = hello?["kind"]?.GetValue<string>() == "app" ? "app" : "web";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
        }

        if (code == null || !_codes.TryGetValue(code, out var user))
        {
            await CloseQuietly(socket, CloseBadCode,
                "This link has expired. Open it again from the game (Options > Voice).");
            return;
        }

        var connection = new Connection(socket, user, kind);
        if (_connections.TryGetValue(user, out var previous))
            previous.Close(CloseReplaced, "Your voice is now connected somewhere else.");

        _connections[user] = connection;
        _events.Enqueue(new VoiceWebConnectionChanged(user, kind, true));

        try
        {
            connection.Send(new JsonObject { ["type"] = "welcome" }.ToJsonString());
            await ReceiveLoop(connection, buffer, cancel);
        }
        finally
        {
            // Only if it wasn't already replaced by a newer connection.
            if (_connections.TryRemove(new KeyValuePair<NetUserId, Connection>(user, connection)))
                _events.Enqueue(new VoiceWebConnectionChanged(user, kind, false));

            await connection.Closed();
        }
    }

    private async Task ReceiveLoop(Connection connection, byte[] buffer, CancellationToken cancel)
    {
        var socket = connection.Socket;
        var tokens = FrameBurst;
        var last = DateTime.UtcNow;

        while (socket.State == WebSocketState.Open && !connection.Closing)
        {
            var (type, length) = await ReceiveMessage(socket, buffer, cancel);
            if (type == WebSocketMessageType.Close || length < 0)
                return;

            if (type == WebSocketMessageType.Text)
            {
                // Keepalive; anything else is ignored.
                if (Encoding.UTF8.GetString(buffer, 0, length).Contains("\"ping\"", StringComparison.Ordinal))
                    connection.Send("{\"type\":\"pong\"}");

                continue;
            }

            if (length != AudioMessageBytes || buffer[0] != AudioAdpcm || !ImaAdpcm.IsValid(buffer.AsSpan(3, length - 3)))
            {
                connection.Close(WebSocketCloseStatus.InvalidPayloadData, "Unexpected data.");
                return;
            }

            // Rate limit: drop what's over, rather than letting a broken client flood the game.
            var now = DateTime.UtcNow;
            tokens = Math.Min(FrameBurst, tokens + (now - last).TotalSeconds * FramesPerSecond);
            last = now;
            if (tokens < 1)
                continue;

            tokens -= 1;

            if (Interlocked.Increment(ref _queuedFrames) > MaxQueuedFrames)
            {
                Interlocked.Decrement(ref _queuedFrames);
                continue;
            }

            var sequence = (ushort) (buffer[1] | buffer[2] << 8);
            _events.Enqueue(new VoiceWebFrame(connection.User, sequence, buffer.AsSpan(3, ImaAdpcm.FrameBytes).ToArray()));
        }
    }

    /// <returns>The message type and length, or -1 if it was too big or the socket closed.</returns>
    private static async Task<(WebSocketMessageType Type, int Length)> ReceiveMessage(WebSocket socket, byte[] buffer,
        CancellationToken cancel)
    {
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), cancel);
            if (result.MessageType == WebSocketMessageType.Close)
                return (WebSocketMessageType.Close, -1);

            length += result.Count;
            if (result.EndOfMessage)
                return (result.MessageType, length);

            if (length == buffer.Length)
            {
                await CloseQuietly(socket, WebSocketCloseStatus.MessageTooBig, "Message too big.");
                return (WebSocketMessageType.Close, -1);
            }
        }
    }

    private static async Task CloseQuietly(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await socket.CloseOutputAsync(status, reason, timeout.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
    }

    private static string Base32(byte[] data)
    {
        // Crockford's alphabet: no I, L, O or U, so a code read out loud or typed can't be misread.
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var output = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = buffer << 8 | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            output.Append(alphabet[(buffer << (5 - bits)) & 31]);

        return output.ToString();
    }

    private sealed class Connection
    {
        public readonly WebSocket Socket;
        public readonly NetUserId User;
        public readonly string Kind;
        public volatile bool Closing;

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private Task _sending = Task.CompletedTask;

        public Connection(WebSocket socket, NetUserId user, string kind)
        {
            Socket = socket;
            User = user;
            Kind = kind;
        }

        public void Send(string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            _ = SendAsync(data);
        }

        private async Task SendAsync(byte[] data)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State == WebSocketState.Open && !Closing)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Socket.SendAsync(data, WebSocketMessageType.Text, true, timeout.Token);
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException)
            {
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Close(WebSocketCloseStatus status, string reason)
        {
            if (Closing)
                return;

            Closing = true;
            _sending = CloseAsync(status, reason);
        }

        private async Task CloseAsync(WebSocketCloseStatus status, string reason)
        {
            await _sendLock.WaitAsync();
            try
            {
                await CloseQuietly(Socket, status, reason);
                // Stops the receive loop if the other end never answers the close.
                Socket.Abort();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public Task Closed() => _sending;
    }

    #endregion
}

public abstract record VoiceWebEvent(NetUserId User);

/// <summary>
/// A page or app connected (or went away) as <see cref="VoiceWebEvent.User"/>'s microphone.
/// </summary>
public sealed record VoiceWebConnectionChanged(NetUserId User, string Kind, bool Connected) : VoiceWebEvent(User);

/// <summary>
/// 20 ms of microphone audio from <see cref="VoiceWebEvent.User"/>'s page or app.
/// </summary>
public sealed record VoiceWebFrame(NetUserId User, ushort Sequence, byte[] Frame) : VoiceWebEvent(User);

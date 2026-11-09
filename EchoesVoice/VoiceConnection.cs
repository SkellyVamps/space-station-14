using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EchoesVoice;

/// <summary>
/// The live connection to a game server's voice endpoint: says who we are (the link code), then streams microphone
/// frames. Reconnects by itself if the network drops; stops if the server says the link is no good.
/// </summary>
public sealed class VoiceConnection : IAsyncDisposable
{
    private const byte AudioAdpcm = 1;

    private readonly VoiceLink _link;
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private Task? _task;
    private ushort _sequence;

    public VoiceConnection(VoiceLink link)
    {
        _link = link;
    }

    /// <summary>
    /// Connection state for the window: (connected, message). Raised on a background thread.
    /// </summary>
    public event Action<bool, string>? StatusChanged;

    /// <summary>
    /// The game is playing our voice right now (or stopped). Raised on a background thread.
    /// </summary>
    public event Action<bool, bool>? TalkingChanged;

    /// <summary>
    /// The server ended it for good (bad or expired link, the player left...): (reason). Raised on a background thread.
    /// </summary>
    public event Action<string>? Ended;

    public bool Connected { get; private set; }

    public void Start()
    {
        _task = Task.Run(RunAsync);
    }

    /// <summary>
    /// Queues one 20 ms frame. Frames that can't be sent in time are dropped, never delayed.
    /// </summary>
    public void SendFrame(byte[] frame)
    {
        if (!Connected)
            return;

        var message = new byte[3 + frame.Length];
        message[0] = AudioAdpcm;
        message[1] = (byte) _sequence;
        message[2] = (byte) (_sequence >> 8);
        frame.CopyTo(message, 3);
        _sequence++;
        _outgoing.Writer.TryWrite(message);
    }

    private async Task RunAsync()
    {
        var delay = TimeSpan.FromSeconds(1);
        var cancel = _stop.Token;

        while (!cancel.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.SetRequestHeader("User-Agent", "EchoesVoice/1.0");

            try
            {
                StatusChanged?.Invoke(false, $"Connecting to {_link}...");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await socket.ConnectAsync(_link.SocketUri, timeout.Token);
                }

                var hello = JsonSerializer.Serialize(new { type = "hello", code = _link.Code, kind = "app" });
                await socket.SendAsync(Encoding.UTF8.GetBytes(hello), WebSocketMessageType.Text, true, cancel);

                var finalReason = await RunConnectedAsync(socket, cancel);
                if (finalReason != null)
                {
                    Ended?.Invoke(finalReason);
                    return;
                }

                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException
                                          or System.Net.Http.HttpRequestException)
            {
                // Network trouble: try again shortly.
            }
            finally
            {
                SetConnected(false);
            }

            StatusChanged?.Invoke(false, $"Can't reach {_link}, retrying...");
            try
            {
                await Task.Delay(delay, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromSeconds(Math.Min(15, delay.TotalSeconds * 2));
        }
    }

    /// <returns>A reason if the server ended it for good, null if the connection just dropped.</returns>
    private async Task<string?> RunConnectedAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var sender = SendLoopAsync(socket, linked.Token);

        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    var code = (int?) socket.CloseStatus ?? 0;
                    // 4001-4005: the server said why, and retrying won't help.
                    return code is >= 4001 and <= 4005
                        ? socket.CloseStatusDescription ?? "The game server closed the connection."
                        : null;
                }

                if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage)
                    continue;

                HandleMessage(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }

            return null;
        }
        finally
        {
            linked.Cancel();
            try
            {
                await sender;
            }
            catch (Exception)
            {
                // Already going away.
            }
        }
    }

    private void HandleMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "welcome":
                    SetConnected(true);
                    StatusChanged?.Invoke(true, $"Connected to {_link}");
                    break;
                case "state":
                    var talking = root.TryGetProperty("talking", out var t) && t.GetBoolean();
                    var muted = root.TryGetProperty("adminMuted", out var m) && m.GetBoolean();
                    TalkingChanged?.Invoke(talking, muted);
                    if (root.TryGetProperty("name", out var name) && name.GetString() is { } n)
                        StatusChanged?.Invoke(true, $"Connected to {_link} as {n}");
                    break;
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Not for us.
        }
    }

    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        // Drop anything queued before this connection: it's stale now.
        while (_outgoing.Reader.TryRead(out _))
        {
        }

        while (!cancel.IsCancellationRequested)
        {
            var message = await _outgoing.Reader.ReadAsync(cancel);
            await socket.SendAsync(message, WebSocketMessageType.Binary, true, cancel);
        }
    }

    private void SetConnected(bool connected)
    {
        Connected = connected;
        if (!connected)
            TalkingChanged?.Invoke(false, false);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_task != null)
        {
            try
            {
                await _task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                // Shutting down anyway.
            }
        }
    }
}

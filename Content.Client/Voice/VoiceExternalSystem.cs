using Content.Shared.CCVar;
using Content.Shared.Voice;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// Using the voice webpage or the standalone voice app as the microphone. Gets the link that ties it to us from the
/// server and opens or copies it, and tells the server what our talk keys are doing, since the page only records.
/// </summary>
public sealed partial class VoiceExternalSystem : EntitySystem
{
    [Dependency] private IUriOpener _uri = default!;
    [Dependency] private IClipboardManager _clipboard = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// Key state is sent again this often even if unchanged, in case the server restarted its side.
    /// </summary>
    private static readonly TimeSpan KeyRefresh = TimeSpan.FromSeconds(5);

    private VoiceExternalKeyEvent? _lastKeys;
    private TimeSpan _lastKeysSent;
    private LinkAction _pending;

    /// <summary>
    /// A voice webpage or app is connected as our microphone.
    /// </summary>
    public bool Connected { get; private set; }

    /// <summary>
    /// "web" or "app".
    /// </summary>
    public string? Kind { get; private set; }

    /// <summary>
    /// The server is playing our voice from it right now.
    /// </summary>
    public bool Talking { get; private set; }

    /// <summary>
    /// How loud, 0-1, while talking.
    /// </summary>
    public float Level { get; private set; }

    /// <summary>
    /// The last link the server gave us.
    /// </summary>
    public string? LastLink { get; private set; }

    public bool Available => _cfg.GetCVar(CCVars.VoiceExternalEnabled) && _net.IsConnected;

    private enum LinkAction : byte
    {
        None,
        Open,
        Copy,
        Print,
    }

    /// <summary>
    /// Raised with the link when <see cref="RequestLink"/> was asked to print it.
    /// </summary>
    public event Action<string>? LinkReceived;

    /// <summary>
    /// Gets the link without opening or copying it; it arrives through <see cref="LinkReceived"/>.
    /// </summary>
    public void RequestLink()
    {
        _pending = LinkAction.Print;
        RaiseNetworkEvent(new VoiceExternalLinkRequestEvent());
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VoiceExternalLinkEvent>(OnLink);
        SubscribeNetworkEvent<VoiceExternalStatusEvent>(OnStatus);
        _net.Disconnect += OnDisconnect;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _net.Disconnect -= OnDisconnect;
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        Connected = false;
        Talking = false;
        Kind = null;
        LastLink = null;
        _lastKeys = null;
    }

    /// <summary>
    /// Opens the voice webpage in the browser, linked to us.
    /// </summary>
    public void OpenWebpage()
    {
        _pending = LinkAction.Open;
        RaiseNetworkEvent(new VoiceExternalLinkRequestEvent());
    }

    /// <summary>
    /// Copies the link (for the standalone app, or another device) to the clipboard.
    /// </summary>
    public void CopyLink()
    {
        _pending = LinkAction.Copy;
        RaiseNetworkEvent(new VoiceExternalLinkRequestEvent());
    }

    /// <summary>
    /// Goes back to the game's own microphone.
    /// </summary>
    public void Disconnect()
    {
        RaiseNetworkEvent(new VoiceExternalDisconnectEvent());
    }

    private void OnLink(VoiceExternalLinkEvent ev)
    {
        var baseUrl = ev.BaseUrl ?? LocalBaseUrl();
        if (baseUrl == null)
            return;

        LastLink = baseUrl.TrimEnd('/') + "/voice#" + Uri.EscapeDataString(ev.Code);

        var action = _pending;
        _pending = LinkAction.None;
        try
        {
            switch (action)
            {
                case LinkAction.Open:
                    _uri.OpenUri(LastLink);
                    break;
                case LinkAction.Copy:
                    _clipboard.SetText(LastLink);
                    break;
                case LinkAction.Print:
                    LinkReceived?.Invoke(LastLink);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't open the voice webpage: {e.Message}");
        }
    }

    /// <summary>
    /// The server's web port at the address we connected to, when it has no public address set: fine for playing on
    /// the same computer or network.
    /// </summary>
    private string? LocalBaseUrl()
    {
        if (_net.ServerChannel?.RemoteEndPoint is not { } endpoint)
            return null;

        var host = endpoint.Address.ToString();
        if (host.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            host = host[7..];
        else if (host.Contains(':'))
            host = "[" + host + "]";

        return "http://" + host + ":" + endpoint.Port;
    }

    private void OnStatus(VoiceExternalStatusEvent ev)
    {
        if (ev.Connected != Connected)
            _lastKeys = null; // tell the new page's server side where things stand straight away

        Connected = ev.Connected;
        Kind = ev.Kind;
        Talking = ev.Connected && ev.Talking;
        Level = ev.Level / 255f;
    }

    /// <summary>
    /// Tells the server what our talk keys are doing, when it changes.
    /// </summary>
    public void SendKeys(VoiceExternalKeyEvent keys)
    {
        var now = _timing.RealTime;
        if (keys.SameAs(_lastKeys) && now - _lastKeysSent < KeyRefresh)
            return;

        _lastKeys = keys;
        _lastKeysSent = now;
        RaiseNetworkEvent(keys);
    }
}

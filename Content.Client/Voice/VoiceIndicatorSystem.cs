using System.Globalization;
using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Input;
using Content.Shared.Radio;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Enums;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// Speaking indicators: the overhead ring overlay and the movable radio panel, according to the player's settings.
/// Also tells the server which radio channels the player doesn't want to hear.
/// </summary>
public sealed partial class VoiceIndicatorSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private VoicePlaybackSystem _playback = default!;
    [Dependency] private VoiceTransmitSystem _transmit = default!;

    private VoiceIndicatorOverlay? _overlay;
    private VoiceRadioPanel? _panel;

    private readonly List<(string Channel, string Name)> _radioSpeakers = new();
    private readonly List<ProtoId<RadioChannelPrototype>> _noChannels = new();
    private readonly HashSet<string> _muted = new();

    private bool _showPanel;
    private bool _clientEnabled;
    private bool _serverEnabled;
    private bool _radioEnabled;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.VoiceToggleRadioPanel, InputCmdHandler.FromDelegate(_ => ToggleRadioPanel()))
            .Register<VoiceIndicatorSystem>();

        Subs.CVar(_cfg, CCVars.VoiceShowIndicators, SetOverlayEnabled, true);
        Subs.CVar(_cfg, CCVars.VoiceShowRadioPanel, v => _showPanel = v, true);
        Subs.CVar(_cfg, CCVars.VoiceClientEnabled, v => _clientEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceEnabled, v => _serverEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioEnabled, v => _radioEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioMutedChannels, OnMutedChannelsChanged, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioPanelPosition, _ => PlacePanel());
        Subs.CVar(_cfg, CCVars.VoiceRadioPanelExpanded, v => _panel?.SetExpanded(v));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<VoiceIndicatorSystem>();
        SetOverlayEnabled(false);

        if (_panel != null)
        {
            _ui.WindowRoot.OnResized -= PlacePanel;
            _panel.Orphan();
            _panel = null;
        }
    }

    private void SetOverlayEnabled(bool enabled)
    {
        if (enabled)
        {
            _overlay ??= new VoiceIndicatorOverlay(EntityManager, _player, _timing, _eye);
            if (!_overlays.HasOverlay<VoiceIndicatorOverlay>())
                _overlays.AddOverlay(_overlay);
        }
        else if (_overlay != null)
        {
            _overlays.RemoveOverlay(_overlay);
        }
    }

    #region Radio listening

    private void OnMutedChannelsChanged(string value)
    {
        _muted.Clear();
        foreach (var channel in VoicePlaybackSystem.ParseChannelList(value))
        {
            _muted.Add(channel);
        }

        SendMutedChannels();
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        // The server forgets on disconnect, so tell it again once we're in game.
        SendMutedChannels();
    }

    private void SendMutedChannels()
    {
        if (!_net.IsConnected || _player.LocalSession?.Status != SessionStatus.InGame)
            return;

        RaiseNetworkEvent(new VoiceSetRadioMutedChannelsEvent(new List<string>(_muted)));
    }

    /// <summary>
    /// Whether the player chose not to hear voice on <paramref name="channel"/>.
    /// </summary>
    public bool IsListening(string channel) => !_muted.Contains(channel);

    public void SetListening(string channel, bool listening)
    {
        var changed = listening ? _muted.Remove(channel) : _muted.Add(channel);
        if (!changed)
            return;

        var list = new List<string>(_muted);
        list.Sort(string.CompareOrdinal);
        _cfg.SetCVar(CCVars.VoiceRadioMutedChannels, string.Join(',', list));
        _cfg.SaveToFile();
    }

    #endregion

    #region Radio panel

    /// <summary>
    /// Shows or hides the radio panel (its keybind, F4 by default). Remembered between sessions.
    /// </summary>
    public void ToggleRadioPanel()
    {
        _cfg.SetCVar(CCVars.VoiceShowRadioPanel, !_cfg.GetCVar(CCVars.VoiceShowRadioPanel));
        _cfg.SaveToFile();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _radioSpeakers.Clear();

        // Always available in game, whether or not you have a radio right now; the player hides it with its key.
        var show = _showPanel && _clientEnabled && _serverEnabled && _radioEnabled
                   && _player.LocalEntity != null;

        if (!show)
        {
            if (_panel is { IsOpen: true })
                _panel.Orphan();

            return;
        }

        if (_panel == null)
            CreatePanel();

        if (!_panel!.IsOpen)
        {
            _ui.WindowRoot.AddChild(_panel);
            PlacePanel();
        }

        TryComp(_player.LocalEntity, out VoiceSpeakerComponent? speaker);
        _playback.GetRadioSpeakers(_radioSpeakers);

        _panel.UpdateSpeakers(_radioSpeakers);
        _panel.UpdateChannels(speaker?.ListenChannels ?? _noChannels, speaker?.RadioChannels ?? _noChannels,
            speaker?.DefaultRadioChannel, _transmit.SelectedRadioChannel, c => !IsListening(c));
    }

    private void CreatePanel()
    {
        _panel = new VoiceRadioPanel(_proto);
        _panel.SetExpanded(_cfg.GetCVar(CCVars.VoiceRadioPanelExpanded));

        _panel.Moved += position =>
        {
            var value = position.X.ToString("0", CultureInfo.InvariantCulture) + "," +
                        position.Y.ToString("0", CultureInfo.InvariantCulture);
            if (value == _cfg.GetCVar(CCVars.VoiceRadioPanelPosition))
                return;

            _cfg.SetCVar(CCVars.VoiceRadioPanelPosition, value);
            _cfg.SaveToFile();
        };

        _panel.ExpandedChanged += expanded =>
        {
            _cfg.SetCVar(CCVars.VoiceRadioPanelExpanded, expanded);
            _cfg.SaveToFile();
        };

        _panel.ListenToggled += SetListening;
        _ui.WindowRoot.OnResized += PlacePanel;
        _panel.TalkChannelSelected += channel => _transmit.SelectedRadioChannel = channel;
    }

    /// <summary>
    /// Puts the panel where the player last left it, or the default spot, kept on screen.
    /// </summary>
    private void PlacePanel()
    {
        if (_panel is not { IsOpen: true } panel || panel.Parent is not { } root)
            return;

        panel.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var size = panel.DesiredSize;

        if (!TryParsePosition(_cfg.GetCVar(CCVars.VoiceRadioPanelPosition), out var position))
            position = new Vector2(root.Size.X * 0.15f, root.Size.Y * 0.2f);

        position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, root.Size - size));
        LayoutContainer.SetPosition(panel, position);
    }

    public static bool TryParsePosition(string value, out Vector2 position)
    {
        position = default;
        var parts = value.Split(',');
        if (parts.Length != 2
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.IsFinite(x) || !float.IsFinite(y))
        {
            return false;
        }

        position = new Vector2(x, y);
        return true;
    }

    #endregion
}

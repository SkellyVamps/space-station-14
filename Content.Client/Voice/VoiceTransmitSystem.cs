using Content.Shared.CCVar;
using Content.Shared.Input;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

public enum VoiceInputMode : byte
{
    PushToTalk = 0,
    VoiceActivation = 1,
}

/// <summary>
/// The talking side of voice chat on the client: push to talk keys, radio keys, intercom buttons, whisper / talk /
/// shout and muting.
/// </summary>
/// <remarks>
/// The microphone itself is the voice webpage or the Echoes Voice app (<see cref="VoiceExternalSystem"/>); the game
/// never records audio. The server only lets that audio through while this says a talk key is held (or voice
/// activation is on), so the game still decides when someone is heard.
/// </remarks>
public sealed partial class VoiceTransmitSystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private VoiceExternalSystem _external = default!;

    private bool _transmitting;
    private bool _transmittingRadio;
    private string? _radioChannelSent;
    private NetEntity? _deviceSent;

    // Settings.
    private bool _serverEnabled;
    private bool _clientEnabled;
    private bool _radioEnabled;
    private VoiceInputMode _mode;
    private float _activationThreshold;
    private int _activationRelease;

    private bool _pttLocal;

    /// <summary>
    /// Radio push-to-talk key held: 0 none, 1 the main radio key, 2-4 the extra radio keys.
    /// </summary>
    private int _pttRadio;

    /// <summary>
    /// Device whose transmit button is held (an intercom), or null.
    /// </summary>
    private NetEntity? _deviceTransmit;

    /// <summary>
    /// Channel set for each extra radio key (index 2-4).
    /// </summary>
    private readonly string[] _radioKeyChannels = new string[5];

    // Hearing yourself (Options -> Voice, the character editor).
    private bool _testing;
    private VoiceProfile? _testProfile;
    private List<ProtoId<VoiceFilterPrototype>>? _testFilters;

    /// <summary>
    /// Radio channel picked by the player for radio push to talk. Null uses the default channel.
    /// </summary>
    public ProtoId<RadioChannelPrototype>? SelectedRadioChannel { get; set; }

    /// <summary>
    /// Whether we are currently sending voice.
    /// </summary>
    public bool Transmitting => _transmitting;

    public bool TransmittingRadio => _transmitting && (_transmittingRadio || _deviceSent != null);

    /// <summary>
    /// Talking into a device (an intercom) right now.
    /// </summary>
    public bool TransmittingDevice => _transmitting && _deviceSent != null;

    /// <summary>
    /// The radio channel we're talking on right now (radio key or intercom), or null when just talking.
    /// </summary>
    public string? TransmitChannel
    {
        get
        {
            if (!_transmitting)
                return null;

            if (_deviceSent is { } device)
            {
                return TryGetEntity(device, out var uid) && TryComp(uid, out IntercomComponent? intercom)
                    ? intercom.CurrentChannel?.Id
                    : null;
            }

            if (!_transmittingRadio)
                return null;

            return _radioChannelSent
                   ?? (TryComp(_player.LocalEntity, out VoiceSpeakerComponent? speaker)
                       ? speaker.DefaultRadioChannel?.Id
                       : null);
        }
    }

    /// <summary>
    /// A talk key (or an intercom's button) is held but we can't be heard: admin muted, gagged, unable to speak...
    /// </summary>
    public bool KeyBlocked => (_pttLocal || _pttRadio != 0 || _deviceTransmit != null) && !CanTransmit(out _);

    /// <summary>
    /// A talk key is held but there's no microphone: the voice webpage or app isn't connected.
    /// </summary>
    public bool KeyWithoutMicrophone => (_pttLocal || _pttRadio != 0 || _deviceTransmit != null)
                                        && !_external.Connected && CanTransmit(out _);

    /// <summary>
    /// How loud we sound right now while transmitting (after our voice effects), smoothed.
    /// </summary>
    public VoiceLevels SelfLevels { get; private set; }

    private VoiceLevels _selfTarget;

    /// <summary>
    /// Whispering, talking or shouting: how far our voice carries.
    /// </summary>
    public VoiceRangeMode Range { get; private set; } = VoiceRangeMode.Talk;

    /// <summary>
    /// When <see cref="Range"/> last changed (real time), for showing the range circle for a moment.
    /// </summary>
    public TimeSpan RangeChangedAt { get; private set; }

    /// <summary>
    /// We muted our own microphone: nothing is sent, whatever keys are held.
    /// </summary>
    public bool SelfMuted { get; private set; }

    /// <summary>
    /// Set when an admin has voice muted us.
    /// </summary>
    public bool AdminMuted { get; private set; }

    public TimeSpan? AdminMuteExpires { get; private set; }

    /// <summary>
    /// Hearing ourselves back through our voice.
    /// </summary>
    public bool Testing => _testing;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VoiceAdminMuteStateEvent>(OnAdminMute);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(_ => SendRange());

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.VoicePushToTalk,
                InputCmdHandler.FromDelegate(_ => SetPushToTalk(true, 0), _ => SetPushToTalk(false, 0), handle: false))
            .Bind(ContentKeyFunctions.VoiceRadioPushToTalk,
                InputCmdHandler.FromDelegate(_ => SetPushToTalk(true, 1), _ => SetPushToTalk(false, 1), handle: false))
            .Bind(ContentKeyFunctions.VoiceRadioPushToTalk2,
                InputCmdHandler.FromDelegate(_ => SetPushToTalk(true, 2), _ => SetPushToTalk(false, 2), handle: false))
            .Bind(ContentKeyFunctions.VoiceRadioPushToTalk3,
                InputCmdHandler.FromDelegate(_ => SetPushToTalk(true, 3), _ => SetPushToTalk(false, 3), handle: false))
            .Bind(ContentKeyFunctions.VoiceRadioPushToTalk4,
                InputCmdHandler.FromDelegate(_ => SetPushToTalk(true, 4), _ => SetPushToTalk(false, 4), handle: false))
            .Bind(ContentKeyFunctions.VoiceCycleRange, InputCmdHandler.FromDelegate(_ => CycleRange()))
            .Bind(ContentKeyFunctions.VoiceToggleMute, InputCmdHandler.FromDelegate(_ => SetSelfMuted(!SelfMuted)))
            .Register<VoiceTransmitSystem>();

        Subs.CVar(_cfg, CCVars.VoiceEnabled, v => _serverEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceClientEnabled, v => _clientEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioEnabled, v => _radioEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceInputMode, v => _mode = (VoiceInputMode) Math.Clamp(v, 0, 1), true);
        Subs.CVar(_cfg, CCVars.VoiceRadioKey2Channel, v => _radioKeyChannels[2] = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioKey3Channel, v => _radioKeyChannels[3] = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioKey4Channel, v => _radioKeyChannels[4] = v, true);
        Subs.CVar(_cfg, CCVars.VoiceActivationThreshold, v => _activationThreshold = v, true);
        Subs.CVar(_cfg, CCVars.VoiceActivationRelease, v => _activationRelease = v, true);

        _net.Disconnect += OnDisconnect;
        _clyde.OnWindowFocused += OnWindowFocused;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<VoiceTransmitSystem>();
        _net.Disconnect -= OnDisconnect;
        _clyde.OnWindowFocused -= OnWindowFocused;
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        _pttLocal = false;
        _pttRadio = 0;
        _deviceTransmit = null;
        _transmitting = false;
        _testing = false;
        AdminMuted = false;
    }

    private void OnWindowFocused(WindowFocusedEventArgs args)
    {
        // The key-up may never arrive if the window loses focus while push to talk is held: never stay keyed.
        if (!args.Focused)
        {
            _pttLocal = false;
            _pttRadio = 0;
            _deviceTransmit = null;
        }
    }

    private void OnAdminMute(VoiceAdminMuteStateEvent ev)
    {
        AdminMuted = ev.Muted;
        AdminMuteExpires = ev.Remaining is { } remaining ? _timing.RealTime + remaining : null;
    }

    #region Input

    /// <summary>
    /// Whisper -> talk -> shout -> whisper.
    /// </summary>
    public void CycleRange()
    {
        SetRange(Range switch
        {
            VoiceRangeMode.Whisper => VoiceRangeMode.Talk,
            VoiceRangeMode.Talk => VoiceRangeMode.Shout,
            _ => VoiceRangeMode.Whisper,
        });
    }

    public void SetRange(VoiceRangeMode mode)
    {
        if (Range == mode)
            return;

        Range = mode;
        RangeChangedAt = _timing.RealTime;
        SendRange();
    }

    private void SendRange()
    {
        // The server forgets on disconnect; LocalPlayerAttachedEvent sends it again.
        if (_net.IsConnected)
            RaiseNetworkEvent(new VoiceSetRangeEvent(Range));
    }

    public void SetSelfMuted(bool muted)
    {
        SelfMuted = muted;
    }

    /// <param name="radioKey">0 for the local key, 1 for the main radio key, 2-4 for the extra radio keys.</param>
    private void SetPushToTalk(bool down, int radioKey)
    {
        // Typing in chat or any text box must never key the microphone.
        if (down && _ui.KeyboardFocused != null)
            return;

        if (radioKey == 0)
            _pttLocal = down;
        else if (down)
            _pttRadio = radioKey; // The last radio key pressed wins.
        else if (_pttRadio == radioKey)
            _pttRadio = 0;
    }

    /// <summary>
    /// Talks into <paramref name="device"/> (an intercom) while its transmit button is held; null when released.
    /// Works like push to talk on its own, no key needed.
    /// </summary>
    public void SetDeviceTransmit(NetEntity? device)
    {
        _deviceTransmit = device;
    }

    /// <summary>
    /// Releases the transmit button of <paramref name="device"/> if it is the one held.
    /// </summary>
    public void ReleaseDeviceTransmit(NetEntity device)
    {
        if (_deviceTransmit == device)
            _deviceTransmit = null;
    }

    #endregion

    #region Hearing yourself

    /// <summary>
    /// Plays our microphone (the voice webpage or app) back to us through our voice, without sending it to anyone.
    /// Needs the webpage or app connected.
    /// </summary>
    /// <param name="profile">Voice to hear instead of the current character's (character editor).</param>
    /// <param name="filters">Filters to hear instead of the current character's.</param>
    public void StartTest(VoiceProfile? profile = null, List<ProtoId<VoiceFilterPrototype>>? filters = null)
    {
        _testing = true;
        _testProfile = profile;
        _testFilters = filters;
        SendTest();
    }

    /// <summary>
    /// Updates the voice being heard while testing, e.g. as character editor sliders move.
    /// </summary>
    public void SetTestProfile(VoiceProfile? profile, List<ProtoId<VoiceFilterPrototype>>? filters = null)
    {
        _testProfile = profile;
        _testFilters = filters;
        if (_testing)
            SendTest();
    }

    public void StopTest()
    {
        if (!_testing)
            return;

        _testing = false;
        _testProfile = null;
        _testFilters = null;
        SendTest();
    }

    private void SendTest()
    {
        if (_net.IsConnected)
            RaiseNetworkEvent(new VoiceExternalTestEvent(_testing, _testProfile, _testFilters));
    }

    #endregion

    /// <summary>
    /// Whether the local player is currently able to send voice at all.
    /// </summary>
    public bool CanTransmit(out VoiceSpeakerComponent? speaker)
    {
        speaker = null;

        if (!_serverEnabled || !_clientEnabled || AdminMuted || SelfMuted || _net.ServerChannel == null)
            return false;

        if (_player.LocalEntity is not { } local || !TryComp(local, out speaker))
            return false;

        return speaker.CanSpeak;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_external.Connected)
        {
            _transmitting = false;
            _selfTarget = default;
        }
        else
        {
            UpdateKeys();
        }

        SelfLevels = SelfLevels.SmoothTowards(_selfTarget, frameTime);
    }

    /// <summary>
    /// Tells the server what our keys are doing, and takes its word for whether we're being heard.
    /// </summary>
    private void UpdateKeys()
    {
        var canTransmit = CanTransmit(out var speaker);
        var device = _radioEnabled ? _deviceTransmit : null;
        ProtoId<RadioChannelPrototype>? radioChannel = null;
        var radio = device == null && _pttRadio != 0 && _radioEnabled && TryGetRadioKeyChannel(speaker, out radioChannel);
        var keyed = canTransmit && (_pttLocal || _pttRadio != 0 || _deviceTransmit != null);

        _external.SendKeys(new VoiceExternalKeyEvent
        {
            Keyed = keyed,
            VoiceActivation = canTransmit && _mode == VoiceInputMode.VoiceActivation,
            ActivationThreshold = _activationThreshold,
            ActivationReleaseMs = _activationRelease,
            Radio = keyed && radio,
            RadioChannel = keyed && radio ? radioChannel?.Id : null,
            Device = keyed ? device : null,
        });

        // What the server says it's sending, for our own indicators.
        _transmitting = _external.Talking && !_testing;
        _transmittingRadio = radio;
        _radioChannelSent = radio ? radioChannel?.Id : null;
        _deviceSent = device;

        var level = _external.Level;
        _selfTarget = _transmitting ? new VoiceLevels(level, level * 0.8f, level * 0.6f, level) : default;
    }

    /// <summary>
    /// The channel the held radio key transmits on. False if it can't transmit (no radio, or an extra key set to a
    /// channel this character doesn't have); then the key just talks locally.
    /// </summary>
    /// <param name="channel">The channel, or null for the radio's default channel.</param>
    private bool TryGetRadioKeyChannel(VoiceSpeakerComponent? speaker, out ProtoId<RadioChannelPrototype>? channel)
    {
        channel = null;
        if (speaker is not { RadioChannels.Count: > 0 })
            return false;

        if (_pttRadio == 1)
        {
            if (SelectedRadioChannel is { } selected && speaker.RadioChannels.Contains(selected))
                channel = selected;

            return true;
        }

        var configured = _radioKeyChannels[_pttRadio];
        if (string.IsNullOrEmpty(configured))
            return false;

        var id = new ProtoId<RadioChannelPrototype>(configured);
        if (!speaker.RadioChannels.Contains(id))
            return false;

        channel = id;
        return true;
    }
}

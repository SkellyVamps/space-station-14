using Content.Shared.CCVar;
using Content.Shared.Input;
using Content.Shared.Radio;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Content.Shared.Voice.Dsp;
using Robust.Client.Audio.Realtime;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Codecs;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

public enum VoiceInputMode : byte
{
    PushToTalk = 0,
    VoiceActivation = 1,
}

/// <summary>
/// Sending side of voice chat on the client: microphone frames go through preprocessing, the gameplay effect chain
/// the server dictates for our character, the Opus encoder, and out to the server.
/// </summary>
/// <remarks>
/// <para>PROCESSING: input gain, rumble filter, noise gate, then <see cref="VoiceSpeakerComponent"/>'s profile and
/// filters (character, species, mask, helmet, disguise...), then a safety limiter.</para>
/// <para>ENCODING: Opus, mono 48 kHz, frame size and bitrate set by the server.</para>
/// <para>TRANSPORT: <see cref="MsgVoiceTransmit"/>. The speaker is never named in the packet.</para>
/// <para>Also provides a local loopback (microphone test / character voice preview) that runs the same chain and
/// codec but plays the result locally instead of sending it.</para>
/// </remarks>
public sealed partial class VoiceTransmitSystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IRealtimeAudioManager _realtime = default!;
    [Dependency] private VoiceCaptureSystem _capture = default!;

    private const int MaxPacketBytes = 1275;

    // Samples read from the microphone but not yet a whole frame.
    private readonly short[] _accum = new short[VoiceConstants.SampleRate];
    private int _accumCount;
    private readonly short[] _readBuffer = new short[4096];
    private readonly byte[] _packet = new byte[MaxPacketBytes];
    private float[] _frame = Array.Empty<float>();
    private short[] _frameShort = Array.Empty<short>();

    private OpusVoiceEncoder? _encoder;
    private ProcessorChain? _chain;
    private int _chainHash;
    private Biquad? _rumble;
    private NoiseGateProcessor? _gate;
    private float _gateDb = float.NaN;
    private CompressorProcessor? _limiter;

    private ushort _sequence;
    private bool _transmitting;
    private bool _transmittingRadio;
    private string? _radioChannelSent;
    private NetEntity? _deviceSent;
    private TimeSpan _vadHoldUntil;

    // Settings.
    private bool _serverEnabled;
    private bool _clientEnabled;
    private bool _radioEnabled;
    private int _frameMs = 20;
    private int _bitrate;
    private int _expectedLoss;
    private float _inputGain;
    private VoiceInputMode _mode;
    private float _activationThreshold;
    private int _activationRelease;
    private float _noiseGate;

    // Loopback test.
    private bool _testing;
    private VoiceProfile? _testProfile;
    private List<ProtoId<VoiceFilterPrototype>>? _testFilters;
    private OpusVoiceDecoder? _testDecoder;
    private IStreamingAudioSource? _testSource;
    private short[] _testDecoded = Array.Empty<short>();

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

    private float _noiseSuppression;
    private NoiseSuppressor? _suppressor;

    /// <summary>
    /// Radio channel picked by the player for radio push to talk. Null uses the default channel.
    /// </summary>
    public ProtoId<RadioChannelPrototype>? SelectedRadioChannel { get; set; }

    /// <summary>
    /// Most recent microphone level in dBFS, after input gain. For the level meter.
    /// </summary>
    public float InputLevelDb { get; private set; } = -120f;

    /// <summary>
    /// Whether we are currently sending voice.
    /// </summary>
    public bool Transmitting => _transmitting;

    public bool TransmittingRadio => _transmitting && (_transmittingRadio || _deviceSent != null);

    /// <summary>
    /// Set when an admin has voice muted us.
    /// </summary>
    public bool AdminMuted { get; private set; }

    public TimeSpan? AdminMuteExpires { get; private set; }

    public bool Testing => _testing;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VoiceAdminMuteStateEvent>(OnAdminMute);

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
            .Register<VoiceTransmitSystem>();

        Subs.CVar(_cfg, CCVars.VoiceEnabled, v => _serverEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceClientEnabled, v => _clientEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioEnabled, v => _radioEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceFrameMs, v => SetFrameMs(v), true);
        Subs.CVar(_cfg, CCVars.VoiceBitrate, v => { _bitrate = v; ConfigureEncoder(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceExpectedLoss, v => { _expectedLoss = v; ConfigureEncoder(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceInputGain, v => _inputGain = Math.Clamp(v, 0f, 8f), true);
        Subs.CVar(_cfg, CCVars.VoiceInputMode, v => _mode = (VoiceInputMode) Math.Clamp(v, 0, 1), true);
        Subs.CVar(_cfg, CCVars.VoiceNoiseSuppression, v => _noiseSuppression = Math.Clamp(v, 0f, 1f), true);
        Subs.CVar(_cfg, CCVars.VoiceRadioKey2Channel, v => _radioKeyChannels[2] = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioKey3Channel, v => _radioKeyChannels[3] = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioKey4Channel, v => _radioKeyChannels[4] = v, true);
        Subs.CVar(_cfg, CCVars.VoiceActivationThreshold, v => _activationThreshold = v, true);
        Subs.CVar(_cfg, CCVars.VoiceActivationRelease, v => _activationRelease = v, true);
        Subs.CVar(_cfg, CCVars.VoiceNoiseGate, v => _noiseGate = v, true);

        _net.Disconnect += OnDisconnect;
        _clyde.OnWindowFocused += OnWindowFocused;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<VoiceTransmitSystem>();
        _net.Disconnect -= OnDisconnect;
        _clyde.OnWindowFocused -= OnWindowFocused;
        StopTest();
        _capture.Release(this);
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        _pttLocal = false;
        _pttRadio = 0;
        _deviceTransmit = null;
        _transmitting = false;
        AdminMuted = false;
        _capture.Release(this);
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
        AdminMuteExpires = ev.Expires;
    }

    #region Input

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

        // Drop audio buffered before the key went down.
        if (down && !_transmitting)
            _capture.Flush();
    }

    /// <summary>
    /// Talks into <paramref name="device"/> (an intercom) while its transmit button is held; null when released.
    /// Works like push to talk on its own, no key needed.
    /// </summary>
    public void SetDeviceTransmit(NetEntity? device)
    {
        if (device != null && _deviceTransmit == null && !_transmitting)
            _capture.Flush();

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

    #region Settings

    private void SetFrameMs(int ms)
    {
        _frameMs = VoiceConstants.IsValidFrameMs(ms) ? ms : 20;
        var samples = VoiceConstants.SamplesForMs(_frameMs);
        _frame = new float[samples];
        _frameShort = new short[samples];
        _testDecoded = new short[VoiceConstants.SamplesForMs(60)];
        _accumCount = 0;
    }

    private void ConfigureEncoder()
    {
        try
        {
            _encoder ??= new OpusVoiceEncoder(VoiceConstants.SampleRate);
            _encoder.Bitrate = _bitrate;
            _encoder.Complexity = 6;
            _encoder.UseInbandFec = true;
            _encoder.ExpectedPacketLossPercent = _expectedLoss;
            _encoder.UseDtx = false;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create voice encoder: {e}");
            _encoder = null;
        }
    }

    #endregion

    #region Mic test

    /// <summary>
    /// Starts a local loopback: you hear yourself through the full voice chain without anything being sent.
    /// </summary>
    /// <param name="profile">Voice profile to preview instead of the current character's (character editor).</param>
    /// <param name="filters">Filters to preview instead of the current character's.</param>
    public void StartTest(VoiceProfile? profile = null, List<ProtoId<VoiceFilterPrototype>>? filters = null)
    {
        _testing = true;
        _testProfile = profile;
        _testFilters = filters;
        _chainHash = 0; // force rebuild
        _testDecoder ??= new OpusVoiceDecoder(VoiceConstants.SampleRate);
        _capture.Flush();
    }

    /// <summary>
    /// Updates the preview profile while testing, e.g. as character editor sliders move.
    /// </summary>
    public void SetTestProfile(VoiceProfile? profile, List<ProtoId<VoiceFilterPrototype>>? filters = null)
    {
        _testProfile = profile;
        _testFilters = filters;
    }

    public void StopTest()
    {
        _testing = false;
        _testProfile = null;
        _testFilters = null;
        _testSource?.Dispose();
        _testSource = null;
        _chainHash = 0;
    }

    private void PlayTest(ReadOnlySpan<byte> packet)
    {
        if (_testDecoder == null)
            return;

        if (_testSource == null)
        {
            _testSource = _realtime.CreateStreamingSource(VoiceConstants.SampleRate, 24);
            if (_testSource == null)
                return;

            _testSource.Global = true;
            _testSource.Position = System.Numerics.Vector2.Zero;
            _testSource.Gain = _cfg.GetCVar(CCVars.VoiceOutputVolume);
        }

        var count = _testDecoder.Decode(packet, _testDecoded);
        if (count > 0)
            _testSource.QueueSamples(_testDecoded.AsSpan(0, count));
    }

    #endregion

    #region Frame processing

    /// <summary>
    /// Whether the local player is currently able to send voice at all.
    /// </summary>
    public bool CanTransmit(out VoiceSpeakerComponent? speaker)
    {
        speaker = null;

        if (!_serverEnabled || !_clientEnabled || AdminMuted || _net.ServerChannel == null)
            return false;

        if (_player.LocalEntity is not { } local || !TryComp(local, out speaker))
            return false;

        return speaker.CanSpeak;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        try
        {
            Process();
        }
        catch (Exception e)
        {
            // Voice must never take the game down.
            Log.Error($"Voice transmit error: {e}");
            EndTransmission();
            _chain = null;
            _chainHash = 0;
        }
    }

    private void Process()
    {
        var canTransmit = CanTransmit(out var speaker);
        var wantsAudio = _testing
                         || canTransmit && (_mode == VoiceInputMode.VoiceActivation || _pttLocal || _pttRadio != 0
                                            || _deviceTransmit != null);

        if (!wantsAudio)
        {
            _capture.Release(this);
            EndTransmission();
            InputLevelDb = -120f;
            return;
        }

        _capture.Acquire(this);

        if (_encoder == null)
            ConfigureEncoder();

        var frameSamples = _frame.Length;
        var now = _timing.RealTime;

        while (true)
        {
            var read = _capture.Read(_readBuffer.AsSpan(0, Math.Min(_readBuffer.Length, _accum.Length - _accumCount)));
            if (read <= 0)
                break;

            _readBuffer.AsSpan(0, read).CopyTo(_accum.AsSpan(_accumCount));
            _accumCount += read;

            if (_accumCount >= _accum.Length)
                break;
        }

        var offset = 0;
        while (_accumCount - offset >= frameSamples)
        {
            ProcessFrame(_accum.AsSpan(offset, frameSamples), speaker, canTransmit, now);
            offset += frameSamples;
        }

        if (offset > 0)
        {
            _accum.AsSpan(offset, _accumCount - offset).CopyTo(_accum);
            _accumCount -= offset;
        }
    }

    private void ProcessFrame(ReadOnlySpan<short> input, VoiceSpeakerComponent? speaker, bool canTransmit, TimeSpan now)
    {
        var frame = _frame.AsSpan();
        VoiceDsp.ShortToFloat(input, frame);

        // Preprocessing: input gain and removal of desk thumps / rumble.
        if (MathF.Abs(_inputGain - 1f) > 0.001f)
        {
            for (var i = 0; i < frame.Length; i++)
            {
                frame[i] *= _inputGain;
            }
        }

        _rumble ??= new Biquad(BiquadType.HighPass, VoiceConstants.SampleRate, 80f);
        _rumble.Process(frame);

        // Background noise removal. It keeps learning the noise floor while you're quiet, so it runs on every frame.
        if (_noiseSuppression > 0.001f)
        {
            _suppressor ??= new NoiseSuppressor(_noiseSuppression);
            _suppressor.Strength = _noiseSuppression;
            _suppressor.Process(frame);
        }
        else
        {
            _suppressor = null;
        }

        InputLevelDb = VoiceDsp.RmsDb(frame);

        // Decide whether this frame is sent.
        // A held device button wins over the radio keys: you're talking into it.
        var device = _radioEnabled ? _deviceTransmit : null;
        ProtoId<RadioChannelPrototype>? radioChannel = null;
        var radio = device == null && _pttRadio != 0 && _radioEnabled
                    && TryGetRadioKeyChannel(speaker, out radioChannel);
        bool send;
        if (!canTransmit)
        {
            send = false;
        }
        else if (_pttLocal || _pttRadio != 0 || _deviceTransmit != null)
        {
            send = true;
        }
        else if (_mode == VoiceInputMode.VoiceActivation)
        {
            if (InputLevelDb >= _activationThreshold)
                _vadHoldUntil = now + TimeSpan.FromMilliseconds(_activationRelease);

            send = now < _vadHoldUntil;
        }
        else
        {
            send = false;
        }

        if (!send && !_testing)
        {
            EndTransmission();
            return;
        }

        // Noise gate.
        if (!_gateDb.Equals(_noiseGate))
        {
            _gateDb = _noiseGate;
            _gate = _noiseGate > -90f ? new NoiseGateProcessor(VoiceConstants.SampleRate, _noiseGate) : null;
        }

        _gate?.Process(frame);

        // Gameplay voice: character, species, equipment, status.
        GetChain(speaker).Process(frame);

        _limiter ??= new CompressorProcessor(VoiceConstants.SampleRate, -3f, 20f, 1f, 50f, 0f, 2f);
        _limiter.Process(frame);

        VoiceDsp.FloatToShort(frame, _frameShort);

        if (_encoder == null)
            return;

        var length = _encoder.Encode(_frameShort, _packet);
        if (length <= 0)
            return;

        if (_testing)
            PlayTest(_packet.AsSpan(0, length));

        if (!send)
            return;

        var channelId = radio ? radioChannel?.Id : null;

        // Switched between talking, radio and a device mid-sentence: finish the old one so its listeners hear it end.
        if (_transmitting && (radio != _transmittingRadio || channelId != _radioChannelSent || device != _deviceSent))
            EndTransmission();

        _transmitting = true;
        _transmittingRadio = radio;
        _radioChannelSent = channelId;
        _deviceSent = device;

        var msg = new MsgVoiceTransmit
        {
            Flags = radio ? VoiceTransmitFlags.Radio
                : device != null ? VoiceTransmitFlags.Device
                : VoiceTransmitFlags.None,
            Sequence = _sequence++,
            RadioChannel = _radioChannelSent,
            Device = device ?? NetEntity.Invalid,
            Payload = _packet.AsSpan(0, length).ToArray(),
        };

        _net.ClientSendMessage(msg);
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

    private ProcessorChain GetChain(VoiceSpeakerComponent? speaker)
    {
        var profile = _testing && _testProfile != null ? _testProfile.Value : speaker?.Profile ?? VoiceProfile.Neutral;
        var filters = _testing && _testFilters != null
            ? _testFilters
            : speaker?.Filters ?? new List<ProtoId<VoiceFilterPrototype>>();
        var seed = speaker?.Seed ?? 0;

        var hash = HashCode.Combine(profile, seed);
        foreach (var filter in filters)
        {
            hash = HashCode.Combine(hash, filter.Id);
        }

        if (_chain == null || hash != _chainHash)
        {
            _chain = VoiceChainBuilder.Build(_proto, profile, filters, seed);
            _chainHash = hash;
        }

        return _chain;
    }

    private void EndTransmission()
    {
        if (!_transmitting)
            return;

        _transmitting = false;

        if (_net.ServerChannel != null)
        {
            _net.ClientSendMessage(new MsgVoiceTransmit
            {
                Flags = VoiceTransmitFlags.EndOfSpurt
                        | (_transmittingRadio ? VoiceTransmitFlags.Radio : 0)
                        | (_deviceSent != null ? VoiceTransmitFlags.Device : 0),
                Sequence = _sequence++,
                RadioChannel = _transmittingRadio ? _radioChannelSent : null,
                Device = _deviceSent ?? NetEntity.Invalid,
            });
        }

        // Effect tails (echo etc.) shouldn't bleed into the next transmission.
        _chain?.Reset();
        _gate?.Reset();
    }

    #endregion
}

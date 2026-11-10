using System.Numerics;
using System.Text.Json.Nodes;
using Content.Shared.CCVar;
using Content.Shared.Voice;
using Content.Shared.Voice.Codecs;
using Content.Shared.Voice.Components;
using Content.Shared.Voice.Dsp;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Voice.External;

/// <summary>
/// Players whose microphone is the voice webpage or the standalone voice app: their audio arrives through
/// <see cref="VoiceWebManager"/>, and this turns it into normal game voice.
/// </summary>
/// <remarks>
/// <para>The page only records. When the player is heard is still decided by the game: their client reports what
/// its talk keys are doing (<see cref="VoiceExternalKeyEvent"/>), and audio outside of that is dropped here.</para>
/// <para>Each 20 ms frame is decoded (16 kHz ADPCM), brought up to the game's 48 kHz, given the speaker's voice
/// (character, species, mask, status... the same chain their client would use), encoded and
/// handed to <see cref="VoiceSystem.HandleTransmit"/> as 16 kHz ADPCM, so routing, radio, moderation, voice-to-text and everything
/// else work exactly as for the game's own microphone.</para>
/// </remarks>
public sealed partial class VoiceExternalMicSystem : EntitySystem
{
    [Dependency] private VoiceWebManager _web = default!;
    [Dependency] private VoiceSystem _voice = default!;
    [Dependency] private VoiceModerationSystem _moderation = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// A page that stops sending mid-sentence (closed, connection lost) is treated as having let go after this long.
    /// </summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How often the speaking level is sent to the player's client while talking, for their own indicator.
    /// </summary>
    private static readonly TimeSpan LevelInterval = TimeSpan.FromMilliseconds(100);

    private const int FrameSamples48 = ImaAdpcm.FrameSamples * 3;

    private readonly Dictionary<NetUserId, Mic> _mics = new();
    private readonly short[] _decoded = new short[ImaAdpcm.FrameSamples];
    private readonly float[] _frame = new float[FrameSamples48];
    private readonly short[] _frameShort = new short[ImaAdpcm.FrameSamples];

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VoiceExternalLinkRequestEvent>(OnLinkRequest);
        SubscribeNetworkEvent<VoiceExternalDisconnectEvent>(OnDisconnectRequest);
        SubscribeNetworkEvent<VoiceExternalKeyEvent>(OnKeys);
        SubscribeNetworkEvent<VoiceExternalTestEvent>(OnTest);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;

        Subs.CVar(_cfg, CCVars.VoiceExternalEnabled, v => _enabled = v, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    #region Linking

    private void OnLinkRequest(VoiceExternalLinkRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_enabled)
            return;

        var code = _web.GetOrCreateCode(args.SenderSession.UserId);
        RaiseNetworkEvent(new VoiceExternalLinkEvent(GetPublicUrl(), code), args.SenderSession);
    }

    private void OnDisconnectRequest(VoiceExternalDisconnectEvent ev, EntitySessionEventArgs args)
    {
        _web.Disconnect(args.SenderSession.UserId, VoiceWebManager.CloseReplaced,
            "You switched back to the game's microphone.");
    }

    /// <summary>
    /// Where players reach this server's web port from outside, or null to let the client use the address it
    /// connected to.
    /// </summary>
    public string? GetPublicUrl()
    {
        var url = _cfg.GetCVar(CCVars.VoiceExternalPublicUrl).Trim();
        if (url.Length > 0)
            return url.TrimEnd('/');

        // The address the server advertises on the hub: ss14s:// is https, ss14:// is plain http.
        var hub = _cfg.GetCVar(Robust.Shared.CVars.HubServerUrl).Trim();
        if (hub.StartsWith("ss14s://", StringComparison.OrdinalIgnoreCase))
            return "https://" + hub["ss14s://".Length..].TrimEnd('/');

        if (hub.StartsWith("ss14://", StringComparison.OrdinalIgnoreCase))
            return "http://" + hub["ss14://".Length..].TrimEnd('/');

        return null;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        _web.Forget(e.Session.UserId);
        _mics.Remove(e.Session.UserId);
    }

    #endregion

    private void OnKeys(VoiceExternalKeyEvent ev, EntitySessionEventArgs args)
    {
        SetKeys(args.SenderSession.UserId, ev);
    }

    /// <summary>
    /// What <paramref name="user"/>'s talk keys are doing, as their client reported.
    /// </summary>
    public void SetKeys(NetUserId user, VoiceExternalKeyEvent keys)
    {
        if (keys.RadioChannel is { Length: > VoiceConstants.MaxWireStringLength })
            return;

        GetMic(user).Keys = keys;
    }

    private const int MaxTestFilters = 16;

    private void OnTest(VoiceExternalTestEvent ev, EntitySessionEventArgs args)
    {
        var mic = GetMic(args.SenderSession.UserId);
        mic.Test = ev.Enabled;

        // Only ever heard by the player themselves, but keep it to real, reasonable voices all the same.
        mic.TestProfile = ev.Profile?.Clamped(_cfg.GetCVar(CCVars.VoiceProfileMaxPitch), _cfg.GetCVar(CCVars.VoiceProfileMaxFormant));
        mic.TestFilters = null;
        if (ev.Filters is { Count: <= MaxTestFilters } filters)
        {
            mic.TestFilters = new List<ProtoId<VoiceFilterPrototype>>();
            foreach (var filter in filters)
            {
                if (_proto.HasIndex(filter))
                    mic.TestFilters.Add(filter);
            }
        }

        mic.TestChain = null;
        if (ev.Enabled)
            EndTransmission(args.SenderSession, mic);
    }

    /// <summary>
    /// Hearing yourself: the page's audio through your voice (or the one being previewed), back to you alone.
    /// </summary>
    private void ProcessTestFrame(ICommonSession session, Mic mic)
    {
        if (mic.TestChain == null)
        {
            if (mic.TestProfile is { } profile)
            {
                mic.TestChain = VoiceChainBuilder.Build(_proto, profile,
                    mic.TestFilters ?? new List<ProtoId<VoiceFilterPrototype>>(), 0);
            }
            else if (session.AttachedEntity is { } entity && TryComp(entity, out VoiceSpeakerComponent? speaker))
            {
                mic.TestChain = VoiceChainBuilder.Build(_proto, speaker.Profile, speaker.Filters, speaker.Seed);
            }
            else
            {
                mic.TestChain = VoiceChainBuilder.Build(_proto, VoiceProfile.Neutral,
                    new List<ProtoId<VoiceFilterPrototype>>(), 0);
            }
        }

        mic.TestChain.Process(_frame);
        mic.Limiter.Process(_frame);
        _voice.SendLoopback(session, Encode(mic), mic.TestSequence++, false);
    }

    private Mic GetMic(NetUserId user)
    {
        if (!_mics.TryGetValue(user, out var mic))
        {
            mic = new Mic();
            _mics[user] = mic;
        }

        return mic;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        while (_web.TryDequeue(out var ev))
        {
            if (!_players.TryGetSessionById(ev.User, out var session))
                continue;

            var mic = GetMic(ev.User);
            switch (ev)
            {
                case VoiceWebConnectionChanged changed:
                    mic.Connected = changed.Connected;
                    mic.Kind = changed.Kind;
                    if (!changed.Connected)
                        EndTransmission(session, mic);

                    SendStatus(session, mic, true);
                    break;
                case VoiceWebFrame frame:
                    mic.LastFrame = now;
                    ProcessFrame(session, mic, frame, now);
                    break;
            }
        }

        foreach (var (user, mic) in _mics)
        {
            // The page went quiet mid-sentence.
            if (mic.Transmitting && now - mic.LastFrame > FrameTimeout && _players.TryGetSessionById(user, out var session))
                EndTransmission(session, mic);
        }
    }

    private void ProcessFrame(ICommonSession session, Mic mic, VoiceWebFrame frame, TimeSpan now)
    {
        if (!_enabled || !ImaAdpcm.Decode(frame.Frame, _decoded))
            return;

        // 16 kHz -> 48 kHz: linear interpolation, then a low-pass to smooth the steps out.
        var previous = mic.LastSample;
        for (var i = 0; i < ImaAdpcm.FrameSamples; i++)
        {
            var current = _decoded[i] / 32768f;
            for (var j = 0; j < 3; j++)
            {
                var t = (j + 1) / 3f;
                var sample = previous + (current - previous) * t;
                _frame[i * 3 + j] = mic.Smooth2.ProcessSample(mic.Smooth1.ProcessSample(sample));
            }

            previous = current;
        }

        mic.LastSample = previous;

        var level = VoiceDsp.RmsDb(_frame);

        // Testing: only the player hears it, whatever the keys say.
        if (mic.Test)
        {
            ProcessTestFrame(session, mic);
            return;
        }

        var keys = mic.Keys;

        // Same rules as the game's own microphone: a talk key held, or voice activation and loud enough.
        bool send;
        if (keys == null)
        {
            send = false;
        }
        else if (keys.Keyed)
        {
            send = true;
        }
        else if (keys.VoiceActivation)
        {
            if (level >= keys.ActivationThreshold)
                mic.HoldUntil = now + TimeSpan.FromMilliseconds(Math.Clamp(keys.ActivationReleaseMs, 0, 5000));

            send = now < mic.HoldUntil;
        }
        else
        {
            send = false;
        }

        if (!send || session.AttachedEntity is not { } speaker || !TryComp(speaker, out VoiceSpeakerComponent? speakerComp))
        {
            EndTransmission(session, mic);
            return;
        }

        var radio = keys!.Radio && keys.Device == null;
        var channel = radio ? keys.RadioChannel : null;
        var device = keys.Device;

        // Switched between talking, the radio and an intercom: finish the old one so its listeners hear it end.
        if (mic.Transmitting && (radio != mic.SentRadio || channel != mic.SentChannel || device != mic.SentDevice))
            EndTransmission(session, mic);

        // Their voice: character, species, gear, status, exactly as their client would.
        GetChain(mic, speakerComp).Process(_frame);
        mic.Limiter.Process(_frame);
        var payload = Encode(mic);

        mic.Transmitting = true;
        mic.SentRadio = radio;
        mic.SentChannel = channel;
        mic.SentDevice = device;
        mic.Level = (byte) Math.Clamp((level + 52f) / 40f * 255f, 0f, 255f);

        _voice.HandleTransmit(session, new MsgVoiceTransmit
        {
            Flags = radio ? VoiceTransmitFlags.Radio : device != null ? VoiceTransmitFlags.Device : VoiceTransmitFlags.None,
            Sequence = mic.Sequence++,
            RadioChannel = channel,
            Device = device ?? NetEntity.Invalid,
            Payload = payload,
        }, fromServer: true);

        SendStatus(session, mic, false);
    }

    /// <summary>
    /// The processed 48 kHz frame, back down to 16 kHz ADPCM for the wire.
    /// </summary>
    private byte[] Encode(Mic mic)
    {
        // Low-pass, then every third sample.
        for (var i = 0; i < ImaAdpcm.FrameSamples; i++)
        {
            var sample = 0f;
            for (var j = 0; j < 3; j++)
            {
                sample = mic.Down2.ProcessSample(mic.Down1.ProcessSample(_frame[i * 3 + j]));
            }

            _frameShort[i] = (short) (Math.Clamp(sample, -1f, 1f) * 32767f);
        }

        var payload = new byte[ImaAdpcm.FrameBytes];
        ImaAdpcm.Encode(_frameShort, ref mic.Adpcm, payload);
        return payload;
    }

    private void EndTransmission(ICommonSession session, Mic mic)
    {
        if (!mic.Transmitting)
            return;

        mic.Transmitting = false;
        mic.Level = 0;

        _voice.HandleTransmit(session, new MsgVoiceTransmit
        {
            Flags = VoiceTransmitFlags.EndOfSpurt
                    | (mic.SentRadio ? VoiceTransmitFlags.Radio : 0)
                    | (mic.SentDevice != null ? VoiceTransmitFlags.Device : 0),
            Sequence = mic.Sequence++,
            RadioChannel = mic.SentRadio ? mic.SentChannel : null,
            Device = mic.SentDevice ?? NetEntity.Invalid,
        }, fromServer: true);

        // Effect tails (echo...) shouldn't bleed into the next sentence.
        mic.Chain?.Reset();
        SendStatus(session, mic, true);
    }

    private ProcessorChain GetChain(Mic mic, VoiceSpeakerComponent speaker)
    {
        var hash = HashCode.Combine(speaker.Profile, speaker.Seed);
        foreach (var filter in speaker.Filters)
        {
            hash = HashCode.Combine(hash, filter.Id);
        }

        if (mic.Chain == null || hash != mic.ChainHash)
        {
            mic.Chain = VoiceChainBuilder.Build(_proto, speaker.Profile, speaker.Filters, speaker.Seed);
            mic.ChainHash = hash;
        }

        return mic.Chain;
    }

    /// <summary>
    /// Tells the player's client (and their page) whether their page is connected and whether it's sending.
    /// </summary>
    private void SendStatus(ICommonSession session, Mic mic, bool changed)
    {
        var now = _timing.RealTime;
        var talkingChanged = mic.Transmitting != mic.ReportedTalking;
        if (!changed && !talkingChanged && now - mic.LastReport < LevelInterval)
            return;

        mic.LastReport = now;
        RaiseNetworkEvent(new VoiceExternalStatusEvent(mic.Connected, mic.Kind, mic.Transmitting, mic.Level), session);

        if (!changed && !talkingChanged)
            return;

        mic.ReportedTalking = mic.Transmitting;
        _web.Send(session.UserId, new JsonObject
        {
            ["type"] = "state",
            ["talking"] = mic.Transmitting,
            ["name"] = session.Name,
            ["adminMuted"] = _moderation.IsAdminMuted(session.UserId),
        });
    }

    /// <summary>
    /// Whether <paramref name="user"/>'s microphone is a voice webpage or app right now.
    /// </summary>
    public bool IsConnected(NetUserId user) => _mics.TryGetValue(user, out var mic) && mic.Connected;

    private sealed class Mic
    {
        public bool Connected;
        public string Kind = "web";
        public VoiceExternalKeyEvent? Keys;

        public readonly Biquad Smooth1 = new(BiquadType.LowPass, VoiceConstants.SampleRate, 7000f);
        public readonly Biquad Smooth2 = new(BiquadType.LowPass, VoiceConstants.SampleRate, 7000f);
        public readonly CompressorProcessor Limiter = new(VoiceConstants.SampleRate, -3f, 20f, 1f, 50f, 0f, 2f);
        public float LastSample;

        public readonly Biquad Down1 = new(BiquadType.LowPass, VoiceConstants.SampleRate, 7000f);
        public readonly Biquad Down2 = new(BiquadType.LowPass, VoiceConstants.SampleRate, 7000f);
        public ImaAdpcm.State Adpcm;

        public bool Test;
        public VoiceProfile? TestProfile;
        public List<ProtoId<VoiceFilterPrototype>>? TestFilters;
        public ProcessorChain? TestChain;
        public ushort TestSequence;
        public ProcessorChain? Chain;
        public int ChainHash;

        public ushort Sequence = 1;
        public bool Transmitting;
        public bool SentRadio;
        public string? SentChannel;
        public NetEntity? SentDevice;
        public TimeSpan LastFrame;
        public TimeSpan HoldUntil;

        public byte Level;
        public bool ReportedTalking;
        public TimeSpan LastReport;
    }
}

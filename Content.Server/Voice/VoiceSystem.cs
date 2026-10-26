using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Ghost.Components;
using Content.Shared.Interaction;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Server.Player;
using Robust.Shared.Audio.Codecs;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>
/// TRANSPORT and ROUTING layers of voice chat on the server.
/// </summary>
/// <remarks>
/// <para>
/// Receives encoded voice frames from clients, validates them, works out who may hear them and forwards the untouched
/// Opus payload with per-recipient gain and muffling. The server never decodes or re-encodes audio.
/// </para>
/// <para>
/// The speaker is always the entity attached to the sending session; clients cannot claim to be someone else.
/// Clients only ever receive voice they are allowed to hear, so a modified client cannot eavesdrop.
/// </para>
/// <para>
/// Recipient lists are recomputed a few times per second per active speaker (see
/// <see cref="CCVars.VoiceAudibilityInterval"/>) using <see cref="VoiceAudibilitySystem"/>; individual packets only
/// walk the cached lists.
/// </para>
/// </remarks>
public sealed partial class VoiceSystem : EntitySystem
{
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private VoiceNetManager _voiceNet = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private VoiceAudibilitySystem _audibility = default!;
    [Dependency] private VoiceRadioSystem _radio = default!;
    [Dependency] private VoiceModerationSystem _moderation = default!;
    [Dependency] private VoiceCreatureSystem _creature = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    /// <summary>
    /// How long after its last packet a speaker keeps getting its recipients refreshed.
    /// </summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How often radio recipients re-receive the speaker's display name.
    /// </summary>
    private static readonly TimeSpan NameInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A radio microphone route nobody has spoken into for this long is dropped.
    /// </summary>
    private static readonly TimeSpan MicrophoneRouteTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Largest microphone listen range considered when looking for microphones around a speaker.
    /// </summary>
    private const float MaxMicrophoneSearchRange = 8f;

    private EntityQuery<VoiceSpeakerComponent> _speakerQuery;
    private EntityQuery<GhostComponent> _ghostQuery;
    private EntityQuery<GhostHearingComponent> _ghostHearingQuery;

    private readonly Dictionary<ICommonSession, SpeakerState> _speakers = new();

    /// <summary>
    /// Radio channels each player chose not to hear voice on. Kept per account so it survives body changes.
    /// </summary>
    private readonly Dictionary<NetUserId, HashSet<string>> _radioMuted = new();

    /// <summary>
    /// More than this many muted channels from one client is ignored.
    /// </summary>
    private const int MaxRadioMutedChannels = 64;
    private readonly HashSet<ushort> _usedStreamIds = new();

    // Scratch collections reused between updates.
    private readonly Dictionary<EntityUid, AcousticPosition> _positionCache = new();
    private readonly Dictionary<EntityUid, VoiceHearingEvent> _hearingCache = new();
    private readonly Dictionary<ICommonSession, List<(SpeakerState Speaker, int Index)>> _listenerLoad = new();
    private readonly MsgVoiceReceive _outgoing = new();
    private readonly List<EntityUid> _loudspeakerScratch = new();
    private readonly List<EntityUid> _expiredDevices = new();
    private readonly List<Entity<RadioMicrophoneComponent>> _microphoneScratch = new();
    private readonly List<(Entity<RadioMicrophoneComponent> Mic, float Gain)> _microphoneCandidates = new();
    private readonly HashSet<ProtoId<RadioChannelPrototype>> _microphoneChannels = new();
    private readonly List<EntityUid> _expiredMicrophones = new();

    private bool _enabled;
    private bool _radioEnabled;
    private int _maxPacketBytes;
    private float _maxPacketsPerSecond;
    private int _maxBurstMs;
    private int _abuseThreshold;
    private int _maxStreams;
    private float _minGain;
    private float _radioLocalGain;
    private float _loudspeakerGain;
    private bool _radioMicrophones;
    private bool _ghostsHearLiving;
    private bool _ghostVoice;
    private bool _livingHearGhosts;
    private TimeSpan _interval;
    private TimeSpan _nextUpdate;

    /// <summary>
    /// Counters for the voicestats command.
    /// </summary>
    public VoiceStats Stats;

    public override void Initialize()
    {
        base.Initialize();

        _speakerQuery = GetEntityQuery<VoiceSpeakerComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();
        _ghostHearingQuery = GetEntityQuery<GhostHearingComponent>();

        _voiceNet.TransmitReceived += OnTransmit;
        SubscribeNetworkEvent<VoiceSetRadioMutedChannelsEvent>(OnSetRadioMuted);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;

        Subs.CVar(_cfg, CCVars.VoiceEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioEnabled, v => _radioEnabled = v, true);
        Subs.CVar(_cfg, CCVars.VoiceMaxPacketBytes, v => _maxPacketBytes = Math.Clamp(v, 16, VoiceConstants.MaxWirePayloadBytes), true);
        Subs.CVar(_cfg, CCVars.VoiceMaxPacketsPerSecond, v => _maxPacketsPerSecond = v, true);
        Subs.CVar(_cfg, CCVars.VoiceMaxBurstMs, v => _maxBurstMs = v, true);
        Subs.CVar(_cfg, CCVars.VoiceAbuseAlertThreshold, v => _abuseThreshold = v, true);
        Subs.CVar(_cfg, CCVars.VoiceMaxStreamsPerListener, v => _maxStreams = v, true);
        Subs.CVar(_cfg, CCVars.VoiceMinAudibleGain, v => _minGain = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioLocalGain, v => _radioLocalGain = v, true);
        Subs.CVar(_cfg, CCVars.VoiceLoudspeakerGain, v => _loudspeakerGain = Math.Max(0f, v), true);
        Subs.CVar(_cfg, CCVars.VoiceRadioMicrophones, v => _radioMicrophones = v, true);
        Subs.CVar(_cfg, CCVars.VoiceGhostsHearLiving, v => _ghostsHearLiving = v, true);
        Subs.CVar(_cfg, CCVars.VoiceGhostVoice, v => _ghostVoice = v, true);
        Subs.CVar(_cfg, CCVars.VoiceLivingHearGhosts, v => _livingHearGhosts = v, true);
        Subs.CVar(_cfg, CCVars.VoiceAudibilityInterval, v => _interval = TimeSpan.FromSeconds(Math.Clamp(v, 0.05f, 2f)), true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _voiceNet.TransmitReceived -= OnTransmit;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        if (_speakers.Remove(e.Session, out var state))
            ReleaseState(state);

        _radioMuted.Remove(e.Session.UserId);
    }

    private void OnSetRadioMuted(VoiceSetRadioMutedChannelsEvent ev, EntitySessionEventArgs args)
    {
        SetRadioMutedChannels(args.SenderSession.UserId, ev.Channels);
    }

    /// <summary>
    /// Sets the radio channels <paramref name="user"/> doesn't want to hear voice on.
    /// </summary>
    public void SetRadioMutedChannels(NetUserId user, IReadOnlyCollection<string>? channels)
    {
        if (channels == null || channels.Count == 0 || channels.Count > MaxRadioMutedChannels)
        {
            _radioMuted.Remove(user);
            return;
        }

        var set = new HashSet<string>();
        foreach (var channel in channels)
        {
            if (!string.IsNullOrEmpty(channel) && channel.Length <= VoiceConstants.MaxWireStringLength)
                set.Add(channel);
        }

        if (set.Count == 0)
            _radioMuted.Remove(user);
        else
            _radioMuted[user] = set;
    }

    public bool IsRadioChannelMuted(NetUserId user, string channel)
    {
        return _radioMuted.TryGetValue(user, out var muted) && muted.Contains(channel);
    }

    #region Receiving

    private void OnTransmit(MsgVoiceTransmit msg)
    {
        if (_players.TryGetSessionByChannel(msg.MsgChannel, out var session))
            HandleTransmit(session, msg);
    }

    /// <summary>
    /// Processes a voice frame sent by <paramref name="session"/>. The speaker is always the session's attached
    /// entity.
    /// </summary>
    public void HandleTransmit(ICommonSession session, MsgVoiceTransmit msg)
    {
        Stats.PacketsReceived++;

        if (!_enabled
            || session.Status != SessionStatus.InGame
            || session.AttachedEntity is not { } speaker)
        {
            return;
        }

        var state = GetState(session);
        var now = _timing.RealTime;

        if (!Validate(msg, state, now, out var reason))
        {
            Stats.PacketsRejected++;
            NoteAbuse(session, state, now, reason);
            return;
        }

        if (_moderation.IsAdminMuted(session.UserId))
        {
            Stats.PacketsMuted++;
            return;
        }

        if (!_speakerQuery.TryComp(speaker, out var speakerComp) || !speakerComp.CanSpeak)
        {
            Stats.PacketsRejected++;
            return;
        }

        // Hamsters, dogs, zombies...: nobody understands them, so nobody gets their audio, just their usual noise.
        if (!speakerComp.Ghost && _creature.IsUnintelligible(speaker))
        {
            Stats.PacketsReplaced++;
            _creature.OnSpeech(speaker, (msg.Flags & VoiceTransmitFlags.EndOfSpurt) != 0);
            return;
        }

        // Speaker changed body: start fresh.
        if (state.Entity != speaker)
        {
            if (state.Entity is { } old)
                _audibility.ForgetSpeaker(old);

            state.Entity = speaker;
            state.Local.Clear();
            state.RecipientsComputed = TimeSpan.Zero;

            // New body, new radio stream ids, so the old and new body can't be linked by stream id.
            ReleaseRoutes(state);
        }

        state.LastPacket = now;

        if (now - state.RecipientsComputed > _interval)
            UpdateLocalRecipients(state, speakerComp);

        var radio = (msg.Flags & VoiceTransmitFlags.Radio) != 0 && _radioEnabled && !speakerComp.Ghost;
        var endOfSpurt = (msg.Flags & VoiceTransmitFlags.EndOfSpurt) != 0;

        SendLocal(session, state, speaker, speakerComp, msg, radio ? _radioLocalGain : 1f, endOfSpurt);

        if (radio)
            SendRadio(session, state, speaker, speakerComp, msg, now, endOfSpurt);
        else if (_radioEnabled && _radioMicrophones && !speakerComp.Ghost)
            SendMicrophones(session, state, speaker, msg, now, endOfSpurt);
    }

    /// <summary>
    /// Validates a packet's structure and rate. Never trusts anything the client says about who it is.
    /// </summary>
    private bool Validate(MsgVoiceTransmit msg, SpeakerState state, TimeSpan now, out string reason)
    {
        var payload = msg.Payload;
        var endOfSpurt = (msg.Flags & VoiceTransmitFlags.EndOfSpurt) != 0;

        if (payload.Length > _maxPacketBytes)
        {
            reason = $"oversized packet ({payload.Length} bytes)";
            return false;
        }

        var audioMs = 0;
        if (payload.Length == 0)
        {
            if (!endOfSpurt)
            {
                reason = "empty packet";
                return false;
            }
        }
        else
        {
            var samples = OpusPacket.GetSampleCount(payload, VoiceConstants.SampleRate);
            if (samples <= 0 || samples > VoiceConstants.SamplesForMs(60))
            {
                reason = "malformed opus packet";
                return false;
            }

            audioMs = samples / (VoiceConstants.SampleRate / 1000);
        }

        if (msg.RadioChannel != null && (msg.Flags & VoiceTransmitFlags.Radio) == 0)
        {
            reason = "channel without radio flag";
            return false;
        }

        if (!state.Limiter.TryConsume(now, audioMs, _maxPacketsPerSecond, _maxBurstMs))
        {
            reason = "rate limited";
            return false;
        }

        if (!state.Sequence.Accept(msg.Sequence))
        {
            reason = "duplicate sequence";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void NoteAbuse(ICommonSession session, SpeakerState state, TimeSpan now, string reason)
    {
        if (now - state.AbuseWindowStart > TimeSpan.FromMinutes(1))
        {
            state.AbuseWindowStart = now;
            state.AbuseCount = 0;
        }

        state.AbuseCount++;

        if (state.AbuseCount == _abuseThreshold)
        {
            _adminLog.Add(LogType.Voice, LogImpact.Medium,
                $"{session:player} exceeded voice abuse threshold ({_abuseThreshold} rejected packets in a minute), last reason: {reason}");
            Log.Warning($"Voice packets from {session} repeatedly rejected: {reason}");
        }
    }

    #endregion

    #region Sending

    private void SendLocal(ICommonSession session, SpeakerState state, EntityUid speaker, VoiceSpeakerComponent comp,
        MsgVoiceTransmit msg, float gainMultiplier, bool endOfSpurt)
    {
        if (gainMultiplier <= 0)
            return;

        var outgoing = _outgoing;
        outgoing.Mode = VoiceReceiveMode.Local;
        outgoing.Flags = (endOfSpurt ? VoiceReceiveFlags.EndOfSpurt : 0) | (comp.Ghost ? VoiceReceiveFlags.Ghost : 0);
        outgoing.StreamId = state.LocalStreamId;
        outgoing.Sequence = msg.Sequence;
        outgoing.Speaker = GetNetEntity(speaker);
        outgoing.Channel = null;
        outgoing.SpeakerName = null;
        outgoing.Muffle = 0;
        outgoing.Payload = msg.Payload;

        foreach (var recipient in state.Local)
        {
            if (recipient.Session.Status != SessionStatus.InGame)
                continue;

            var volume = _moderation.GetListenerVolume(recipient.Session.UserId, session.UserId);
            if (volume <= 0)
            {
                Stats.PacketsSuppressed++;
                continue;
            }

            var gain = recipient.Gain * gainMultiplier * volume;
            if (gain < _minGain)
                continue;

            outgoing.Gain = Quantize(gain);
            outgoing.Muffle = Quantize(recipient.Muffle);
            if (TrySend(outgoing, recipient.Session))
                Stats.BytesSent += msg.Payload.Length + 12;
        }
    }

    private void SendRadio(ICommonSession session, SpeakerState state, EntityUid speaker, VoiceSpeakerComponent comp,
        MsgVoiceTransmit msg, TimeSpan now, bool endOfSpurt)
    {
        ProtoId<RadioChannelPrototype>? requested = msg.RadioChannel != null
            ? new ProtoId<RadioChannelPrototype>(msg.RadioChannel)
            : comp.DefaultRadioChannel;

        if (requested is not { } channel || !comp.RadioChannels.Contains(channel))
            return;

        if (!state.Radio.TryGetValue(channel, out var radio))
        {
            radio = new RadioRoute { StreamId = AllocateStreamId(), Channel = channel };
            state.Radio[channel] = radio;
        }

        if (now - radio.Computed > _interval)
        {
            radio.Computed = now;

            if (_radio.TryGetTransmitter(speaker, channel, out var radioSource))
                ComputeRoute(radio, radioSource, session, speaker);
            else
                ClearRoute(radio);
        }

        SendOnRoute(radio, session, msg, now, endOfSpurt);
    }

    /// <summary>
    /// Talking near a switched-on radio microphone (intercom, handheld radio) broadcasts on its channel, like text.
    /// </summary>
    private void SendMicrophones(ICommonSession session, SpeakerState state, EntityUid speaker, MsgVoiceTransmit msg,
        TimeSpan now, bool endOfSpurt)
    {
        if (now - state.MicrophonesComputed > _interval)
        {
            state.MicrophonesComputed = now;
            UpdateMicrophoneRoutes(session, state, speaker, now);
        }

        foreach (var route in state.Microphones.Values)
        {
            if (route.Active)
                SendOnRoute(route, session, msg, now, endOfSpurt);
        }
    }

    private void UpdateMicrophoneRoutes(ICommonSession session, SpeakerState state, EntityUid speaker, TimeSpan now)
    {
        foreach (var route in state.Microphones.Values)
        {
            route.Active = false;
        }

        _positionCache.Clear();
        _hearingCache.Clear();

        var speakerPos = GetPosition(speaker);
        if (speakerPos.Map.MapId != MapId.Nullspace)
        {
            _radio.CollectMicrophones(speakerPos.Map, MaxMicrophoneSearchRange, _microphoneScratch);

            // Loudest microphone first; only one per channel, so two intercoms in a room don't double the audio.
            _microphoneCandidates.Clear();
            foreach (var mic in _microphoneScratch)
            {
                if (mic.Owner == speaker
                    || Vector2.Distance(speakerPos.Map.Position, _xform.GetMapCoordinates(mic.Owner).Position) > mic.Comp.ListenRange
                    || mic.Comp.UnobstructedRequired && !_interaction.InRangeUnobstructed(speaker, mic.Owner, 0)
                    || !_audibility.TryGetAudibility(speakerPos, GetPosition(mic.Owner), out var gain, out _)
                    || gain < _minGain)
                {
                    continue;
                }

                _microphoneCandidates.Add((mic, gain));
            }

            _microphoneCandidates.Sort((a, b) => b.Gain.CompareTo(a.Gain));
            _microphoneChannels.Clear();

            foreach (var (mic, _) in _microphoneCandidates)
            {
                var channel = mic.Comp.BroadcastChannel;
                if (!_microphoneChannels.Add(channel))
                    continue;

                if (!state.Microphones.TryGetValue(mic.Owner, out var route))
                {
                    route = new RadioRoute { StreamId = AllocateStreamId() };
                    state.Microphones[mic.Owner] = route;
                }

                route.Channel = channel;
                route.Active = true;
                route.Computed = now;
                ComputeRoute(route, mic.Owner, session, speaker);
            }
        }

        // Forget microphones this speaker hasn't used for a while.
        _expiredMicrophones.Clear();
        foreach (var (mic, route) in state.Microphones)
        {
            if (!route.Active && now - route.Computed > MicrophoneRouteTimeout)
                _expiredMicrophones.Add(mic);
        }

        foreach (var mic in _expiredMicrophones)
        {
            if (state.Microphones.Remove(mic, out var route))
                ReleaseRoute(route);
        }
    }

    /// <summary>
    /// Works out who hears a radio transmission from <paramref name="radioSource"/>: players with their own receiver,
    /// and players near a device playing it out loud.
    /// </summary>
    private void ComputeRoute(RadioRoute route, EntityUid radioSource, ICommonSession sender, EntityUid speaker)
    {
        _radio.CollectListeners(radioSource, route.Channel, route.Listeners, _loudspeakerScratch);
        route.Listeners.Remove(sender);
        UpdateLoudspeakerRecipients(route, sender);

        var nameEv = new TransformSpeakerNameEvent(speaker, Name(speaker));
        RaiseLocalEvent(speaker, nameEv);
        route.Name = nameEv.VoiceName;
    }

    private void ClearRoute(RadioRoute route)
    {
        route.Listeners.Clear();
        route.Loudspeakers.Clear();
    }

    /// <summary>
    /// For each player without their own receiver for this transmission, the loudest device playing it near them.
    /// Uses the same acoustics as people talking: walls, doors, vacuum, distance.
    /// </summary>
    private void UpdateLoudspeakerRecipients(RadioRoute route, ICommonSession sender)
    {
        route.Loudspeakers.Clear();

        if (_loudspeakerGain > 0 && _loudspeakerScratch.Count > 0)
        {
            _positionCache.Clear();
            _hearingCache.Clear();

            foreach (var session in _players.Sessions)
            {
                if (session == sender
                    || session.Status != SessionStatus.InGame
                    || session.AttachedEntity is not { } listener
                    || route.Listeners.Contains(session)
                    || _ghostQuery.HasComp(listener))
                {
                    continue;
                }

                var listenerMap = _xform.GetMapCoordinates(listener);
                EntityUid? bestDevice = null;
                var bestGain = 0f;
                var bestMuffle = 0f;

                foreach (var device in _loudspeakerScratch)
                {
                    var devicePos = GetPosition(device);
                    if (devicePos.Map.MapId != listenerMap.MapId
                        || (devicePos.Map.Position - listenerMap.Position).LengthSquared() > 50f * 50f
                        || !_audibility.TryGetAudibility(devicePos, GetPosition(listener), out var gain, out var muffle))
                    {
                        continue;
                    }

                    if (gain > bestGain)
                    {
                        bestDevice = device;
                        bestGain = gain;
                        bestMuffle = muffle;
                    }
                }

                if (bestDevice is not { } chosen)
                    continue;

                var hearing = GetHearing(listener);
                if (hearing.Deaf)
                    continue;

                bestGain *= _loudspeakerGain * hearing.Gain;
                if (bestGain < _minGain)
                    continue;

                if (!route.DeviceStreams.TryGetValue(chosen, out var streamId))
                {
                    streamId = AllocateStreamId();
                    route.DeviceStreams[chosen] = streamId;
                }

                route.Loudspeakers.Add(new LoudspeakerRecipient(session, chosen, streamId, bestGain,
                    Acoustics.AcousticMath.CombineMuffle(bestMuffle, hearing.Muffle)));
            }
        }

        // Stream ids of devices that stopped playing this transmission.
        _expiredDevices.Clear();
        foreach (var device in route.DeviceStreams.Keys)
        {
            if (!_loudspeakerScratch.Contains(device))
                _expiredDevices.Add(device);
        }

        foreach (var device in _expiredDevices)
        {
            if (route.DeviceStreams.Remove(device, out var id))
                _usedStreamIds.Remove(id);
        }
    }

    private void SendOnRoute(RadioRoute route, ICommonSession session, MsgVoiceTransmit msg, TimeSpan now, bool endOfSpurt)
    {
        var channel = route.Channel.Id;
        var outgoing = _outgoing;
        outgoing.Mode = VoiceReceiveMode.Radio;
        outgoing.Flags = endOfSpurt ? VoiceReceiveFlags.EndOfSpurt : 0;
        outgoing.StreamId = route.StreamId;
        outgoing.Sequence = msg.Sequence;
        outgoing.Speaker = NetEntity.Invalid;
        outgoing.Channel = channel;
        outgoing.Muffle = 0;
        outgoing.Payload = msg.Payload;

        foreach (var listener in route.Listeners)
        {
            if (!TryGetRouteVolume(listener, session, channel, out var volume))
                continue;

            outgoing.SpeakerName = NameFor(route, listener, now);
            outgoing.Gain = Quantize(volume);
            if (TrySend(outgoing, listener))
                Stats.BytesSent += msg.Payload.Length + 16;
        }

        if (route.Loudspeakers.Count == 0)
            return;

        outgoing.Mode = VoiceReceiveMode.Loudspeaker;
        foreach (var recipient in route.Loudspeakers)
        {
            if (!TryGetRouteVolume(recipient.Session, session, channel, out var volume))
                continue;

            var gain = recipient.Gain * volume;
            if (gain < _minGain)
                continue;

            outgoing.StreamId = recipient.StreamId;
            outgoing.Speaker = GetNetEntity(recipient.Device);
            outgoing.SpeakerName = NameFor(route, recipient.Session, now);
            outgoing.Gain = Quantize(gain);
            outgoing.Muffle = Quantize(recipient.Muffle);
            if (TrySend(outgoing, recipient.Session))
                Stats.BytesSent += msg.Payload.Length + 20;
        }
    }

    /// <summary>
    /// Whether <paramref name="listener"/> gets radio voice from <paramref name="sender"/> on this channel at all, and
    /// at what volume (their own per-player volume).
    /// </summary>
    private bool TryGetRouteVolume(ICommonSession listener, ICommonSession sender, string channel, out float volume)
    {
        volume = 0;
        if (listener.Status != SessionStatus.InGame)
            return false;

        if (IsRadioChannelMuted(listener.UserId, channel))
        {
            Stats.PacketsSuppressed++;
            return false;
        }

        volume = _moderation.GetListenerVolume(listener.UserId, sender.UserId);
        if (volume <= 0)
        {
            Stats.PacketsSuppressed++;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The speaker's name goes with the first packets of a transmission and periodically after.
    /// </summary>
    private static string? NameFor(RadioRoute route, ICommonSession listener, TimeSpan now)
    {
        if (route.NameSent.TryGetValue(listener, out var sentAt) && now - sentAt <= NameInterval)
            return null;

        route.NameSent[listener] = now;
        return route.Name;
    }

    private bool TrySend(MsgVoiceReceive message, ICommonSession session)
    {
        try
        {
            _net.ServerSendMessage(message, session.Channel);
            Stats.PacketsSent++;
            return true;
        }
        catch (Exception e)
        {
            // A bad recipient must never stop delivery to everyone else.
            Stats.SendFailures++;
            Log.Debug($"Failed to send voice to {session}: {e.Message}");
            return false;
        }
    }

    private static byte Quantize(float value)
    {
        return (byte) Math.Clamp((int) MathF.Round(value * 255f), 0, 255);
    }

    #endregion

    #region Recipients

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + _interval;

        if (!_enabled || _speakers.Count == 0)
            return;

        _positionCache.Clear();
        _hearingCache.Clear();

        var any = false;
        foreach (var (_, state) in _speakers)
        {
            if (state.Entity is not { } speaker || now - state.LastPacket > ActiveWindow)
            {
                state.Local.Clear();
                continue;
            }

            if (!_speakerQuery.TryComp(speaker, out var comp) || Deleted(speaker))
            {
                state.Local.Clear();
                continue;
            }

            UpdateLocalRecipients(state, comp, cached: true);
            any = true;
        }

        if (any)
            EnforceStreamCap();
    }

    /// <summary>
    /// Recomputes who hears <paramref name="state"/>'s local voice and how well.
    /// </summary>
    private void UpdateLocalRecipients(SpeakerState state, VoiceSpeakerComponent comp, bool cached = false)
    {
        if (!cached)
        {
            _positionCache.Clear();
            _hearingCache.Clear();
        }

        state.RecipientsComputed = _timing.RealTime;
        state.Local.Clear();

        if (state.Entity is not { } speaker || TerminatingOrDeleted(speaker))
            return;

        var start = _timing.RealTime;
        var speakerPos = GetPosition(speaker);
        if (speakerPos.Map.MapId == MapId.Nullspace)
            return;

        var speakerGhost = comp.Ghost;

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame
                || session.AttachedEntity is not { } listener
                || listener == speaker)
            {
                continue;
            }

            // Cheap map/range rejection before anything expensive.
            var listenerMap = _xform.GetMapCoordinates(listener);
            if (listenerMap.MapId != speakerPos.Map.MapId
                || (listenerMap.Position - speakerPos.Map.Position).LengthSquared() > 50f * 50f)
            {
                continue;
            }

            var listenerGhost = _ghostQuery.HasComp(listener);
            float gain;
            var muffle = 0f;

            if (speakerGhost)
            {
                if (listenerGhost ? !_ghostVoice : !_livingHearGhosts)
                    continue;

                if (!_audibility.TryGetObserverAudibility(speakerPos, GetPosition(listener), out gain))
                    continue;

                if (!listenerGhost)
                    muffle = 0.3f;
            }
            else if (listenerGhost)
            {
                if (!_ghostsHearLiving || !_ghostHearingQuery.HasComp(listener))
                    continue;

                if (!_audibility.TryGetObserverAudibility(speakerPos, GetPosition(listener), out gain))
                    continue;
            }
            else
            {
                if (!_audibility.TryGetAudibility(speakerPos, GetPosition(listener), out gain, out muffle))
                    continue;

                var hearing = GetHearing(listener);
                if (hearing.Deaf)
                    continue;

                gain *= hearing.Gain;
                muffle = Acoustics.AcousticMath.CombineMuffle(muffle, hearing.Muffle);
            }

            if (gain < _minGain)
                continue;

            state.Local.Add(new LocalRecipient(session, gain, muffle));
        }

        Stats.RecipientUpdates++;
        Stats.RecipientTime += _timing.RealTime - start;
    }

    /// <summary>
    /// Computes who would currently hear <paramref name="speaker"/> talking locally, and how well.
    /// For debugging tools and tests; routing uses its own cached lists.
    /// </summary>
    public List<(ICommonSession Session, float Gain, float Muffle)> GetLocalListeners(EntityUid speaker)
    {
        var state = new SpeakerState { Entity = speaker };
        var result = new List<(ICommonSession, float, float)>();
        if (!_speakerQuery.TryComp(speaker, out var comp))
            return result;

        UpdateLocalRecipients(state, comp);
        foreach (var recipient in state.Local)
        {
            result.Add((recipient.Session, recipient.Gain, recipient.Muffle));
        }

        return result;
    }

    /// <summary>
    /// Limits how many speakers a single listener receives at once, keeping the loudest.
    /// </summary>
    private void EnforceStreamCap()
    {
        foreach (var list in _listenerLoad.Values)
        {
            list.Clear();
        }

        foreach (var state in _speakers.Values)
        {
            for (var i = 0; i < state.Local.Count; i++)
            {
                var session = state.Local[i].Session;
                if (!_listenerLoad.TryGetValue(session, out var list))
                {
                    list = new List<(SpeakerState, int)>();
                    _listenerLoad[session] = list;
                }

                list.Add((state, i));
            }
        }

        foreach (var (session, list) in _listenerLoad)
        {
            if (list.Count <= _maxStreams)
                continue;

            list.Sort((a, b) => b.Speaker.Local[b.Index].Gain.CompareTo(a.Speaker.Local[a.Index].Gain));
            for (var i = _maxStreams; i < list.Count; i++)
            {
                var (speaker, index) = list[i];
                speaker.Local[index] = speaker.Local[index] with { Gain = 0 };
                Stats.StreamsCapped++;
            }
        }

        // Drop listener entries for sessions that are gone.
        if (_listenerLoad.Count > _players.PlayerCount * 2 + 16)
            _listenerLoad.Clear();
    }

    private AcousticPosition GetPosition(EntityUid uid)
    {
        if (_positionCache.TryGetValue(uid, out var pos))
            return pos;

        pos = _audibility.GetAcousticPosition(uid);
        _positionCache[uid] = pos;
        return pos;
    }

    private VoiceHearingEvent GetHearing(EntityUid uid)
    {
        if (_hearingCache.TryGetValue(uid, out var hearing))
            return hearing;

        hearing = _audibility.GetHearing(uid);
        _hearingCache[uid] = hearing;
        return hearing;
    }

    #endregion

    #region State

    private SpeakerState GetState(ICommonSession session)
    {
        if (_speakers.TryGetValue(session, out var state))
            return state;

        state = new SpeakerState { LocalStreamId = AllocateStreamId() };
        _speakers[session] = state;
        return state;
    }

    private ushort AllocateStreamId()
    {
        // Random so stream IDs reveal nothing about join order or identity.
        for (var i = 0; i < 100; i++)
        {
            var id = (ushort) _random.Next(1, ushort.MaxValue);
            if (_usedStreamIds.Add(id))
                return id;
        }

        throw new InvalidOperationException("Ran out of voice stream IDs.");
    }

    private void ReleaseState(SpeakerState state)
    {
        _usedStreamIds.Remove(state.LocalStreamId);
        ReleaseRoutes(state);

        if (state.Entity is { } entity)
            _audibility.ForgetSpeaker(entity);
    }

    private void ReleaseRoutes(SpeakerState state)
    {
        foreach (var route in state.Radio.Values)
        {
            ReleaseRoute(route);
        }

        foreach (var route in state.Microphones.Values)
        {
            ReleaseRoute(route);
        }

        state.Radio.Clear();
        state.Microphones.Clear();
    }

    private void ReleaseRoute(RadioRoute route)
    {
        _usedStreamIds.Remove(route.StreamId);
        foreach (var id in route.DeviceStreams.Values)
        {
            _usedStreamIds.Remove(id);
        }

        route.DeviceStreams.Clear();
    }

    /// <summary>
    /// Number of speakers that transmitted recently.
    /// </summary>
    public int ActiveSpeakers
    {
        get
        {
            var now = _timing.RealTime;
            var count = 0;
            foreach (var state in _speakers.Values)
            {
                if (now - state.LastPacket < ActiveWindow)
                    count++;
            }

            return count;
        }
    }

    private sealed class SpeakerState
    {
        public readonly VoiceRateLimiter Limiter = new();
        public readonly VoiceSequenceWindow Sequence = new();
        public readonly List<LocalRecipient> Local = new();
        public readonly Dictionary<ProtoId<RadioChannelPrototype>, RadioRoute> Radio = new();

        /// <summary>
        /// Radio microphones (intercoms, handheld radios) picking this speaker up, by microphone entity.
        /// </summary>
        public readonly Dictionary<EntityUid, RadioRoute> Microphones = new();
        public TimeSpan MicrophonesComputed;
        public ushort LocalStreamId;
        public EntityUid? Entity;
        public TimeSpan LastPacket;
        public TimeSpan RecipientsComputed;
        public TimeSpan AbuseWindowStart;
        public int AbuseCount;
    }

    /// <summary>
    /// One radio transmission path: a speaker on a channel through their own radio, or through a microphone.
    /// </summary>
    private sealed class RadioRoute
    {
        public ushort StreamId;
        public ProtoId<RadioChannelPrototype> Channel;
        public TimeSpan Computed;
        public string Name = string.Empty;

        /// <summary>
        /// For microphone routes: whether the speaker is currently in range of it.
        /// </summary>
        public bool Active;

        public readonly HashSet<ICommonSession> Listeners = new();
        public readonly List<LoudspeakerRecipient> Loudspeakers = new();

        /// <summary>
        /// Stream id per device playing this route out loud, so each device is its own positional sound.
        /// </summary>
        public readonly Dictionary<EntityUid, ushort> DeviceStreams = new();

        public readonly Dictionary<ICommonSession, TimeSpan> NameSent = new();
    }

    private readonly record struct LoudspeakerRecipient(ICommonSession Session, EntityUid Device, ushort StreamId,
        float Gain, float Muffle);

    private readonly record struct LocalRecipient(ICommonSession Session, float Gain, float Muffle);

    #endregion
}

public struct VoiceStats
{
    public long PacketsReceived;
    public long PacketsRejected;
    public long PacketsMuted;
    public long PacketsSent;
    public long BytesSent;
    public long RecipientUpdates;
    public TimeSpan RecipientTime;
    public long StreamsCapped;
    public long PacketsSuppressed;

    /// <summary>
    /// Frames from creatures nobody can understand, replaced by their speech sound.
    /// </summary>
    public long PacketsReplaced;
    public long SendFailures;
}

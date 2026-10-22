using Content.Server.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Ghost.Components;
using Content.Shared.Radio;
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
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private VoiceAudibilitySystem _audibility = default!;
    [Dependency] private VoiceRadioSystem _radio = default!;
    [Dependency] private VoiceModerationSystem _moderation = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>
    /// How long after its last packet a speaker keeps getting its recipients refreshed.
    /// </summary>
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How often radio recipients re-receive the speaker's display name.
    /// </summary>
    private static readonly TimeSpan NameInterval = TimeSpan.FromSeconds(2);

    private EntityQuery<VoiceSpeakerComponent> _speakerQuery;
    private EntityQuery<GhostComponent> _ghostQuery;
    private EntityQuery<GhostHearingComponent> _ghostHearingQuery;

    private readonly Dictionary<ICommonSession, SpeakerState> _speakers = new();
    private readonly HashSet<ushort> _usedStreamIds = new();

    // Scratch collections reused between updates.
    private readonly Dictionary<EntityUid, AcousticPosition> _positionCache = new();
    private readonly Dictionary<EntityUid, VoiceHearingEvent> _hearingCache = new();
    private readonly Dictionary<ICommonSession, List<(SpeakerState Speaker, int Index)>> _listenerLoad = new();
    private readonly MsgVoiceReceive _outgoing = new();

    private bool _enabled;
    private bool _radioEnabled;
    private int _maxPacketBytes;
    private float _maxPacketsPerSecond;
    private int _maxBurstMs;
    private int _abuseThreshold;
    private int _maxStreams;
    private float _minGain;
    private float _radioLocalGain;
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

        _net.RegisterNetMessage<MsgVoiceTransmit>(OnTransmit);
        _net.RegisterNetMessage<MsgVoiceReceive>();

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
        Subs.CVar(_cfg, CCVars.VoiceGhostsHearLiving, v => _ghostsHearLiving = v, true);
        Subs.CVar(_cfg, CCVars.VoiceGhostVoice, v => _ghostVoice = v, true);
        Subs.CVar(_cfg, CCVars.VoiceLivingHearGhosts, v => _livingHearGhosts = v, true);
        Subs.CVar(_cfg, CCVars.VoiceAudibilityInterval, v => _interval = TimeSpan.FromSeconds(Math.Clamp(v, 0.05f, 2f)), true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus != SessionStatus.Disconnected)
            return;

        if (_speakers.Remove(e.Session, out var state))
            ReleaseState(state);
    }

    #region Receiving

    private void OnTransmit(MsgVoiceTransmit msg)
    {
        Stats.PacketsReceived++;

        if (!_enabled)
            return;

        if (!_players.TryGetSessionByChannel(msg.MsgChannel, out var session)
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

        // Speaker changed body: start fresh.
        if (state.Entity != speaker)
        {
            if (state.Entity is { } old)
                _audibility.ForgetSpeaker(old);

            state.Entity = speaker;
            state.Local.Clear();
            state.RecipientsComputed = TimeSpan.Zero;
            state.Radio.Clear();
        }

        state.LastPacket = now;

        if (now - state.RecipientsComputed > _interval)
            UpdateLocalRecipients(state, speakerComp);

        var radio = (msg.Flags & VoiceTransmitFlags.Radio) != 0 && _radioEnabled && !speakerComp.Ghost;
        var endOfSpurt = (msg.Flags & VoiceTransmitFlags.EndOfSpurt) != 0;

        SendLocal(session, state, speaker, speakerComp, msg, radio ? _radioLocalGain : 1f, endOfSpurt);

        if (radio)
            SendRadio(session, state, speaker, speakerComp, msg, now, endOfSpurt);
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
            var gain = recipient.Gain * gainMultiplier * volume;
            if (gain < _minGain)
                continue;

            outgoing.Gain = Quantize(gain);
            outgoing.Muffle = Quantize(recipient.Muffle);
            _net.ServerSendMessage(outgoing, recipient.Session.Channel);
            Stats.PacketsSent++;
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
            radio = new RadioRoute { StreamId = AllocateStreamId() };
            state.Radio[channel] = radio;
        }

        if (now - radio.Computed > _interval)
        {
            radio.Computed = now;
            radio.Listeners.Clear();

            if (_radio.TryGetTransmitter(speaker, channel, out var radioSource))
                _radio.CollectListeners(radioSource, channel, radio.Listeners);

            radio.Listeners.Remove(session);

            var nameEv = new TransformSpeakerNameEvent(speaker, Name(speaker));
            RaiseLocalEvent(speaker, nameEv);
            radio.Name = nameEv.VoiceName;
        }

        var outgoing = _outgoing;
        outgoing.Mode = VoiceReceiveMode.Radio;
        outgoing.Flags = endOfSpurt ? VoiceReceiveFlags.EndOfSpurt : 0;
        outgoing.StreamId = radio.StreamId;
        outgoing.Sequence = msg.Sequence;
        outgoing.Speaker = NetEntity.Invalid;
        outgoing.Channel = channel.Id;
        outgoing.Muffle = 0;
        outgoing.Payload = msg.Payload;

        foreach (var listener in radio.Listeners)
        {
            if (listener.Status != SessionStatus.InGame)
                continue;

            var volume = _moderation.GetListenerVolume(listener.UserId, session.UserId);
            if (volume <= 0)
                continue;

            // Send the name with the first packets of a transmission and periodically after.
            if (!radio.NameSent.TryGetValue(listener, out var sentAt) || now - sentAt > NameInterval)
            {
                outgoing.SpeakerName = radio.Name;
                radio.NameSent[listener] = now;
            }
            else
            {
                outgoing.SpeakerName = null;
            }

            outgoing.Gain = Quantize(volume);
            _net.ServerSendMessage(outgoing, listener.Channel);
            Stats.PacketsSent++;
            Stats.BytesSent += msg.Payload.Length + 16;
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
        foreach (var route in state.Radio.Values)
        {
            _usedStreamIds.Remove(route.StreamId);
        }

        if (state.Entity is { } entity)
            _audibility.ForgetSpeaker(entity);
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
        public ushort LocalStreamId;
        public EntityUid? Entity;
        public TimeSpan LastPacket;
        public TimeSpan RecipientsComputed;
        public TimeSpan AbuseWindowStart;
        public int AbuseCount;
    }

    private sealed class RadioRoute
    {
        public ushort StreamId;
        public TimeSpan Computed;
        public string Name = string.Empty;
        public readonly HashSet<ICommonSession> Listeners = new();
        public readonly Dictionary<ICommonSession, TimeSpan> NameSent = new();
    }

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
}

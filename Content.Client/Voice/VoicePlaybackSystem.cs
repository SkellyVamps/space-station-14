using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Voice;
using Content.Shared.Voice.Dsp;
using Robust.Client.Audio.Realtime;
using Robust.Client.Graphics;
using Robust.Shared.Audio.Codecs;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// PLAYBACK layer: turns voice packets from the server into sound.
/// </summary>
/// <remarks>
/// Each stream (a speaker heard locally, or a speaker heard on one radio channel) gets its own jitter buffer, Opus
/// decoder, receive-side DSP and streaming audio source. Local streams are positioned at the speaking entity so they
/// move with it; radio streams are non-positional and go through the radio effect chain.
/// The server already decided gain and muffling (walls, doors, distance...); this just renders it smoothly.
/// </remarks>
public sealed partial class VoicePlaybackSystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private VoiceNetManager _voiceNet = default!;
    [Dependency] private IRealtimeAudioManager _realtime = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>
    /// Built-in filter applied to ghosts heard by the living (only when the server allows that).
    /// </summary>
    public static readonly ProtoId<VoiceFilterPrototype> GhostFilter = "GhostVoice";

    private const int MaxStreams = 32;
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SpeakingWindow = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<(VoiceReceiveMode, ushort), VoiceStream> _streams = new();
    private readonly List<(VoiceReceiveMode, ushort)> _toRemove = new();
    private readonly Dictionary<NetEntity, float> _playerVolumes = new();

    private bool _enabled;
    private float _localVolume;
    private float _radioVolume;
    private int _jitterMs;
    private string _radioFilter = string.Empty;

    public int StreamCount => _streams.Count;

    /// <summary>
    /// Total voice packets received from the server. Diagnostics.
    /// </summary>
    public long PacketsReceived { get; private set; }

    /// <summary>
    /// The most recent packet received. Diagnostics and tests.
    /// </summary>
    public (VoiceReceiveMode Mode, NetEntity Speaker, string? Channel, byte Gain, byte Muffle)? LastPacket { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        _voiceNet.VoiceReceived += OnReceive;
        SubscribeNetworkEvent<VoicePlayerVolumeChangedEvent>(OnPlayerVolumeChanged);

        Subs.CVar(_cfg, CCVars.VoiceClientEnabled, v => { _enabled = v; if (!v) ClearStreams(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceEnabled, v => { if (!v) ClearStreams(); });
        Subs.CVar(_cfg, CCVars.VoiceOutputVolume, v => _localVolume = Math.Clamp(v, 0f, 2f), true);
        Subs.CVar(_cfg, CCVars.VoiceRadioVolume, v => _radioVolume = Math.Clamp(v, 0f, 2f), true);
        Subs.CVar(_cfg, CCVars.VoiceJitterBufferMs, v => _jitterMs = Math.Clamp(v, 20, 400), true);
        Subs.CVar(_cfg, CCVars.VoiceRadioFilter, v => _radioFilter = v, true);

        _net.Disconnect += OnDisconnect;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _voiceNet.VoiceReceived -= OnReceive;
        _net.Disconnect -= OnDisconnect;
        ClearStreams();
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        ClearStreams();
    }

    private void OnPlayerVolumeChanged(VoicePlayerVolumeChangedEvent ev)
    {
        _playerVolumes[ev.Target] = ev.Volume;
    }

    /// <summary>
    /// Volume the local player set for the player controlling <paramref name="entity"/> (1 = default, 0 = muted).
    /// The server enforces it; this is for UI.
    /// </summary>
    public float GetPlayerVolume(NetEntity entity)
    {
        return _playerVolumes.GetValueOrDefault(entity, 1f);
    }

    public void SetPlayerVolume(NetEntity entity, float volume)
    {
        _playerVolumes[entity] = volume;
        RaiseNetworkEvent(new VoiceSetPlayerVolumeEvent(entity, volume));
    }

    #region Receive

    private void OnReceive(MsgVoiceReceive msg)
    {
        PacketsReceived++;
        LastPacket = (msg.Mode, msg.Speaker, msg.Channel, msg.Gain, msg.Muffle);

        if (!_enabled)
            return;

        try
        {
            var key = (msg.Mode, msg.StreamId);
            if (!_streams.TryGetValue(key, out var stream))
            {
                if (_streams.Count >= MaxStreams || msg.Payload.Length == 0)
                    return;

                stream = CreateStream(msg);
                if (stream == null)
                    return;

                _streams[key] = stream;
            }

            if (msg.Mode == VoiceReceiveMode.Local)
            {
                stream.Speaker = msg.Speaker;

                // Muted locally (the server stops sending shortly after, this covers the gap).
                if (_playerVolumes.TryGetValue(msg.Speaker, out var volume) && volume <= 0)
                    return;
            }
            else
            {
                stream.Channel = msg.Channel;
                if (msg.SpeakerName != null)
                    stream.Name = msg.SpeakerName;
            }

            stream.TargetGain = msg.Gain / 255f;
            stream.TargetMuffle = msg.Muffle / 255f;
            stream.LastPacket = _timing.RealTime;
            stream.Jitter.Push(msg.Sequence, msg.Payload, (msg.Flags & VoiceReceiveFlags.EndOfSpurt) != 0, _timing.RealTime);
        }
        catch (Exception e)
        {
            Log.Error($"Error receiving voice packet: {e}");
        }
    }

    private VoiceStream? CreateStream(MsgVoiceReceive msg)
    {
        var source = _realtime.CreateStreamingSource(VoiceConstants.SampleRate, 32);
        if (source == null)
            return null;

        // The server already applied distance falloff; OpenAL only pans.
        source.RolloffFactor = 0f;
        source.ReferenceDistance = 1f;
        source.MaxDistance = 1000f;
        source.Occlusion = 0f;
        source.Global = msg.Mode == VoiceReceiveMode.Radio;

        var frameMs = _cfg.GetCVar(CCVars.VoiceFrameMs);
        var depth = Math.Max(1, (int) MathF.Ceiling(_jitterMs / (float) Math.Max(10, frameMs)));

        ProcessorChain? effects = null;
        var seed = msg.StreamId;
        if (msg.Mode == VoiceReceiveMode.Radio)
        {
            if (_proto.TryIndex<VoiceFilterPrototype>(_radioFilter, out var radio))
                effects = VoiceChainBuilder.Build(radio, seed);
        }
        else if ((msg.Flags & VoiceReceiveFlags.Ghost) != 0 && _proto.TryIndex(GhostFilter, out var ghost))
        {
            effects = VoiceChainBuilder.Build(ghost, seed);
        }

        return new VoiceStream(msg.Mode, source, new OpusVoiceDecoder(VoiceConstants.SampleRate),
            new VoiceJitterBuffer(depth), effects);
    }

    #endregion

    #region Playback

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_streams.Count == 0)
            return;

        var now = _timing.RealTime;
        var listenerMap = _eye.CurrentEye.Position.MapId;

        // Keep enough audio queued to survive until the next frame, plus a margin.
        var queueTarget = (int) (VoiceConstants.SampleRate * (Math.Min(frameTime, 0.25f) * 1.5f + 0.03f));

        foreach (var (key, stream) in _streams)
        {
            if (now - stream.LastPacket > StreamTimeout)
            {
                _toRemove.Add(key);
                continue;
            }

            try
            {
                UpdatePosition(stream, listenerMap);
                stream.Volume = stream.Mode == VoiceReceiveMode.Radio ? _radioVolume : _localVolume;
                stream.Fill(now, queueTarget);
            }
            catch (Exception e)
            {
                Log.Error($"Voice playback error: {e}");
                _toRemove.Add(key);
            }
        }

        foreach (var key in _toRemove)
        {
            if (_streams.Remove(key, out var stream))
                stream.Dispose();
        }

        _toRemove.Clear();
    }

    private void UpdatePosition(VoiceStream stream, Robust.Shared.Map.MapId listenerMap)
    {
        if (stream.Mode == VoiceReceiveMode.Radio)
            return;

        // Follow the speaking entity. If we can't see it (outside PVS), fall back to non-positional.
        if (TryGetEntity(stream.Speaker, out var speaker) && !TerminatingOrDeleted(speaker))
        {
            var pos = _xform.GetMapCoordinates(speaker.Value);
            if (pos.MapId == listenerMap)
            {
                stream.Source.Global = false;
                stream.Source.Position = pos.Position;
                return;
            }
        }

        stream.Source.Global = true;
        stream.Source.Position = Vector2.Zero;
    }

    private void ClearStreams()
    {
        foreach (var stream in _streams.Values)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }

    #endregion

    #region Queries

    /// <summary>
    /// Whether <paramref name="entity"/> is currently heard speaking locally.
    /// </summary>
    public bool IsSpeaking(EntityUid entity)
    {
        var net = GetNetEntity(entity);
        var now = _timing.RealTime;
        foreach (var stream in _streams.Values)
        {
            if (stream.Mode == VoiceReceiveMode.Local && stream.Speaker == net && now - stream.LastPacket < SpeakingWindow)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Entities heard speaking locally right now.
    /// </summary>
    public void GetLocalSpeakers(HashSet<EntityUid> output)
    {
        var now = _timing.RealTime;
        foreach (var stream in _streams.Values)
        {
            if (stream.Mode == VoiceReceiveMode.Local
                && now - stream.LastPacket < SpeakingWindow
                && TryGetEntity(stream.Speaker, out var uid))
            {
                output.Add(uid.Value);
            }
        }
    }

    /// <summary>
    /// Radio transmissions heard right now, as (channel, speaker name).
    /// </summary>
    public void GetRadioSpeakers(List<(string Channel, string Name)> output)
    {
        var now = _timing.RealTime;
        foreach (var stream in _streams.Values)
        {
            if (stream.Mode == VoiceReceiveMode.Radio && now - stream.LastPacket < SpeakingWindow)
                output.Add((stream.Channel ?? string.Empty, stream.Name ?? string.Empty));
        }
    }

    #endregion
}

/// <summary>
/// One incoming voice stream: jitter buffer -> decoder -> effects -> streaming source.
/// </summary>
public sealed class VoiceStream : IDisposable
{
    private const float MuffleSmoothing = 0.25f;

    public readonly VoiceReceiveMode Mode;
    public readonly IStreamingAudioSource Source;
    public readonly VoiceJitterBuffer Jitter;

    private readonly OpusVoiceDecoder _decoder;
    private readonly ProcessorChain? _effects;
    private readonly Biquad _muffleFilter = new(BiquadType.LowPass, VoiceConstants.SampleRate, 20000f);
    private readonly Biquad _muffleFilter2 = new(BiquadType.LowPass, VoiceConstants.SampleRate, 20000f);
    private readonly short[] _pcm = new short[VoiceConstants.SamplesForMs(60)];
    private readonly float[] _float = new float[VoiceConstants.SamplesForMs(60)];
    private int _lastFrameSamples = VoiceConstants.SamplesForMs(20);

    private float _gain;
    private float _muffle;
    private float _appliedCutoff = -1;

    public NetEntity Speaker;
    public string? Channel;
    public string? Name;
    public float TargetGain;
    public float TargetMuffle;
    public float Volume = 1f;
    public TimeSpan LastPacket;

    public int DecodeErrors { get; private set; }

    public VoiceStream(VoiceReceiveMode mode, IStreamingAudioSource source, OpusVoiceDecoder decoder,
        VoiceJitterBuffer jitter, ProcessorChain? effects)
    {
        Mode = mode;
        Source = source;
        _decoder = decoder;
        Jitter = jitter;
        _effects = effects;
    }

    /// <summary>
    /// Decodes and queues frames until the source has at least <paramref name="targetSamples"/> queued.
    /// </summary>
    public void Fill(TimeSpan now, int targetSamples)
    {
        // Bounded so a flood of packets can't stall the frame.
        for (var guard = 0; guard < 12 && Source.QueuedSamples < targetSamples; guard++)
        {
            var result = Jitter.Pop(now, out var payload);
            int count;

            switch (result)
            {
                case JitterResult.Idle:
                    if (!Jitter.Playing)
                        _effectsIdle = true;
                    return;
                case JitterResult.Packet:
                    count = _decoder.Decode(payload, _pcm);
                    break;
                case JitterResult.LostWithFec:
                    count = _decoder.Decode(payload, _pcm.AsSpan(0, _lastFrameSamples), decodeFec: true);
                    if (count <= 0)
                        count = _decoder.DecodeLost(_pcm.AsSpan(0, _lastFrameSamples));
                    break;
                default:
                    count = _decoder.DecodeLost(_pcm.AsSpan(0, _lastFrameSamples));
                    break;
            }

            if (count <= 0)
            {
                DecodeErrors++;
                continue;
            }

            _lastFrameSamples = count;
            Render(count);

            if (!Source.QueueSamples(_pcm.AsSpan(0, count)))
                return;
        }
    }

    private bool _effectsIdle;

    private void Render(int count)
    {
        var buffer = _float.AsSpan(0, count);
        VoiceDsp.ShortToFloat(_pcm.AsSpan(0, count), buffer);

        if (_effectsIdle)
        {
            // New talk spurt: don't let the previous one's echo/filter state leak in.
            _effects?.Reset();
            _effectsIdle = false;
        }

        // Environmental muffling: low-pass that closes down from 20 kHz (clear) to ~350 Hz (through a wall).
        _muffle += (TargetMuffle - _muffle) * MuffleSmoothing;
        var cutoff = _muffle < 0.02f ? 20000f : 20000f * MathF.Pow(350f / 20000f, _muffle);
        if (MathF.Abs(cutoff - _appliedCutoff) > 1f)
        {
            _muffleFilter.Set(BiquadType.LowPass, VoiceConstants.SampleRate, cutoff);
            _muffleFilter2.Set(BiquadType.LowPass, VoiceConstants.SampleRate, cutoff);
            _appliedCutoff = cutoff;
        }

        if (_muffle >= 0.02f)
        {
            _muffleFilter.Process(buffer);
            _muffleFilter2.Process(buffer);
        }

        _effects?.Process(buffer);

        // Ramp gain across the block to avoid zipper noise when audibility changes.
        var target = TargetGain * Volume;
        var step = (target - _gain) / count;
        for (var i = 0; i < count; i++)
        {
            _gain += step;
            buffer[i] *= _gain;
        }

        _gain = target;
        VoiceDsp.FloatToShort(buffer, _pcm.AsSpan(0, count));
    }

    public void Dispose()
    {
        Source.Dispose();
    }
}

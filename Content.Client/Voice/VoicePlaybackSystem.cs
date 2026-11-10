using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Voice;
using Content.Shared.Voice.Dsp;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// PLAYBACK layer: turns voice packets from the server into sound.
/// </summary>
/// <remarks>
/// Each stream (a speaker heard locally, or a speaker heard on one radio channel) gets its own jitter buffer, decoder,
/// receive-side DSP and playback (<see cref="VoiceStream"/>). Local streams are positioned at the speaking entity so they
/// move with it; radio streams are non-positional and go through the radio effect chain.
/// The server already decided gain and muffling (walls, doors, distance...); this just renders it smoothly.
/// </remarks>
public sealed partial class VoicePlaybackSystem : EntitySystem
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private VoiceNetManager _voiceNet = default!;
    [Dependency] private IAudioManager _audio = default!;

    private EngineVoiceClipPlayer? _clipPlayer;
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
    private readonly HashSet<string> _mutedRadioChannels = new();
    private readonly Dictionary<string, float> _channelVolumes = new();

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
        Subs.CVar(_cfg, CCVars.VoiceRadioMutedChannels, OnMutedRadioChannelsChanged, true);
        Subs.CVar(_cfg, CCVars.VoiceRadioChannelVolumes, v => ParseChannelVolumes(v, _channelVolumes), true);

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

    private void OnMutedRadioChannelsChanged(string value)
    {
        _mutedRadioChannels.Clear();
        foreach (var channel in ParseChannelList(value))
        {
            _mutedRadioChannels.Add(channel);
        }

        // Stop anything already playing on a channel that was just muted.
        foreach (var (key, stream) in _streams)
        {
            if (stream.Mode == VoiceReceiveMode.Radio && stream.Channel != null && _mutedRadioChannels.Contains(stream.Channel))
                _toRemove.Add(key);
        }
    }

    /// <summary>
    /// Parses a comma separated channel list as stored in <see cref="CCVars.VoiceRadioMutedChannels"/>.
    /// </summary>
    public static IEnumerable<string> ParseChannelList(string value)
    {
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }

    public bool IsRadioChannelMuted(string channel) => _mutedRadioChannels.Contains(channel);

    /// <summary>
    /// Parses <see cref="CCVars.VoiceRadioChannelVolumes"/> ("Common=0.5,Security=1.5").
    /// </summary>
    public static void ParseChannelVolumes(string value, Dictionary<string, float> output)
    {
        output.Clear();
        foreach (var pair in ParseChannelList(value))
        {
            var split = pair.Split('=', 2);
            if (split.Length == 2
                && float.TryParse(split[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var volume)
                && float.IsFinite(volume))
            {
                output[split[0].Trim()] = Math.Clamp(volume, 0f, 2f);
            }
        }
    }

    /// <summary>
    /// The player's volume for a radio channel (1 if they haven't changed it).
    /// </summary>
    public float GetChannelVolume(string? channel)
    {
        return channel != null && _channelVolumes.TryGetValue(channel, out var volume) ? volume : 1f;
    }

    /// <summary>
    /// Whether anyone is audibly talking right now (for lowering other game sounds).
    /// </summary>
    public bool AnyVoicePlaying()
    {
        var now = _timing.RealTime;
        foreach (var stream in _streams.Values)
        {
            if (now - stream.LastPacket < SpeakingWindow && stream.TargetGain * stream.Volume > 0.05f)
                return true;
        }

        return false;
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

        // The server stops sending muted channels once it knows; this covers the gap.
        // Only our own radio: an intercom playing a channel out loud is heard in the room regardless.
        if (msg.Mode == VoiceReceiveMode.Radio && msg.Channel != null && _mutedRadioChannels.Contains(msg.Channel))
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
                stream.Range = (msg.Flags & VoiceReceiveFlags.Whisper) != 0 ? VoiceRangeMode.Whisper
                    : (msg.Flags & VoiceReceiveFlags.Shout) != 0 ? VoiceRangeMode.Shout
                    : VoiceRangeMode.Talk;

                // Muted locally (the server stops sending shortly after, this covers the gap).
                if (_playerVolumes.TryGetValue(msg.Speaker, out var volume) && volume <= 0)
                    return;
            }
            else
            {
                // Loudspeakers play from the device.
                if (msg.Mode == VoiceReceiveMode.Loudspeaker)
                    stream.Speaker = msg.Speaker;

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
        // Voice frames are always 20 ms.
        var depth = Math.Max(1, (int) MathF.Ceiling(_jitterMs / 20f));

        ProcessorChain? effects = null;
        var seed = msg.StreamId;
        if (msg.Mode != VoiceReceiveMode.Local)
        {
            if (_proto.TryIndex<VoiceFilterPrototype>(_radioFilter, out var radio))
                effects = VoiceChainBuilder.Build(radio, seed);
        }
        else if ((msg.Flags & VoiceReceiveFlags.Ghost) != 0 && _proto.TryIndex(GhostFilter, out var ghost))
        {
            effects = VoiceChainBuilder.Build(ghost, seed);
        }

        _clipPlayer ??= new EngineVoiceClipPlayer(_audio);
        return new VoiceStream(msg.Mode, _clipPlayer, new VoiceJitterBuffer(depth), effects);
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
                // Intercoms playing out loud are sounds in the room, like people talking.
                stream.Volume = stream.Mode == VoiceReceiveMode.Radio
                    ? _radioVolume * GetChannelVolume(stream.Channel)
                    : _localVolume;
                stream.Update(now, frameTime);
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
                stream.Global = false;
                stream.Position = pos.Position;
                return;
            }
        }

        stream.Global = true;
        stream.Position = Vector2.Zero;
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
    /// Everyone being heard right now (or just finished, while their indicator fades), for the speaking indicators.
    /// </summary>
    public void GetHeardVoices(List<HeardVoice> output)
    {
        var now = _timing.RealTime;
        foreach (var ((mode, id), stream) in _streams)
        {
            var talking = now - stream.LastPacket < SpeakingWindow;
            if (!talking && stream.Levels.IsSilent)
                continue;

            output.Add(new HeardVoice(mode, id, stream.Speaker, stream.Channel, stream.Name, stream.Levels, talking,
                stream.Range));
        }
    }

    #endregion
}

/// <param name="Mode">How it's heard: in person, over the radio, or from a device playing the radio out loud.</param>
/// <param name="StreamId">Identifies the stream; stays the same while the same person keeps talking the same way.</param>
/// <param name="Speaker">The person talking (in person), or the device playing it (loudspeaker). Unset for radio.</param>
/// <param name="Name">Radio only: the name the server says the speaker goes by.</param>
/// <param name="Talking">Audio is still arriving (as opposed to the indicator fading out).</param>
/// <param name="Range">In person: whether they're whispering, talking or shouting.</param>
public readonly record struct HeardVoice(
    VoiceReceiveMode Mode,
    ushort StreamId,
    NetEntity Speaker,
    string? Channel,
    string? Name,
    VoiceLevels Levels,
    bool Talking,
    VoiceRangeMode Range);

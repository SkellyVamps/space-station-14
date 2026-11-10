using System.Numerics;
using Content.Shared.Voice;
using Content.Shared.Voice.Codecs;
using Content.Shared.Voice.Dsp;
using Robust.Client.Audio;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Network;

namespace Content.Client.Voice;

/// <summary>
/// One incoming voice stream: jitter buffer, decoder, effects, and playback as a chain of short overlapping clips.
/// </summary>
/// <remarks>
/// <para>
/// The engine plays whole sounds, not live streams, so voice is played as clips of <see cref="Body"/> samples, each
/// with an extra <see cref="Fade"/> of the audio that follows. Each clip fades in over the end of the previous one, whose
/// last part fades out over the same samples: the two add back up to the original.
/// </para>
/// <para>
/// Each clip is timed from how far the previous one has really played (the audio device's clock, not the game's, so
/// they never drift apart). Since the game only gets a chance every frame, a clip is started up to a frame early with
/// that much silence in front, so its audio begins exactly when the previous one hands over; if a frame comes late
/// anyway, the clip starts that far in instead.
/// </para>
/// </remarks>
public sealed class VoiceStream : IDisposable
{
    private const int Rate = VoiceConstants.SampleRate;
    private const int FrameSamples = ImaAdpcm.FrameSamples * 3;

    /// <summary>
    /// 40 ms of each clip that is its own.
    /// </summary>
    private const int Body = Rate * 40 / 1000;

    /// <summary>
    /// 30 ms crossfade into the next clip.
    /// </summary>
    private const int Fade = Rate * 30 / 1000;

    /// <summary>
    /// A sentence starts playing once this much more than one clip has arrived, so later clips find their audio there
    /// in time even when packets arrive a little unevenly.
    /// </summary>
    private const int StartMargin = Rate * 50 / 1000;

    /// <summary>
    /// Short ramps at the very start and end of a spurt, so it doesn't click.
    /// </summary>
    private const int Ramp = Rate * 3 / 1000;

    /// <summary>
    /// More than this waiting means playback stalled (no audio device...): drop the oldest instead of growing.
    /// </summary>
    private const int MaxPending = Rate;

    private const float MuffleSmoothing = 0.25f;

    public readonly VoiceReceiveMode Mode;
    public readonly VoiceJitterBuffer Jitter;

    private readonly IVoiceClipPlayer _audio;
    private readonly ProcessorChain? _effects;
    private readonly Biquad _muffleFilter = new(BiquadType.LowPass, Rate, 20000f);
    private readonly Biquad _muffleFilter2 = new(BiquadType.LowPass, Rate, 20000f);
    private readonly Biquad _smooth1 = new(BiquadType.LowPass, Rate, 7000f);
    private readonly Biquad _smooth2 = new(BiquadType.LowPass, Rate, 7000f);
    private readonly short[] _decoded = new short[ImaAdpcm.FrameSamples];
    private readonly short[] _lastDecoded = new short[ImaAdpcm.FrameSamples];
    private readonly float[] _float = new float[FrameSamples];
    private readonly short[] _pending = new short[MaxPending + Body + Fade];
    private readonly short[] _clip = new short[MaxLead + Body + Fade];

    /// <summary>
    /// Clips are started up to this early, padded with silence, so the next game frame being late doesn't matter.
    /// </summary>
    private const int MaxLead = Rate * 100 / 1000;
    private readonly List<Clip> _clips = new();

    private readonly VoiceLevelAnalyzer _analyzer = new();
    private readonly VoiceLevelTimeline _timeline = new();

    private int _pendingCount;
    private float _lastSample;
    private int _concealed;
    private Clip? _current;
    private bool _spurtEnded;
    private bool _effectsIdle;
    private VoiceLevels _blockLevels;
    private float _longFrame;

    private float _gain;
    private float _muffle;
    private float _appliedCutoff = -1;

    public NetEntity Speaker;
    public VoiceRangeMode Range = VoiceRangeMode.Talk;
    public string? Channel;
    public string? Name;
    public float TargetGain;
    public float TargetMuffle;
    public float Volume = 1f;
    public TimeSpan LastPacket;

    /// <summary>
    /// Plays without position (radio), or at <see cref="Position"/>.
    /// </summary>
    public bool Global;

    public Vector2 Position;

    /// <summary>
    /// How loud what's playing right now is (smoothed), for the speaking indicators.
    /// </summary>
    public VoiceLevels Levels;

    public int DecodeErrors { get; private set; }

    public VoiceStream(VoiceReceiveMode mode, IVoiceClipPlayer audio, VoiceJitterBuffer jitter, ProcessorChain? effects)
    {
        Mode = mode;
        _audio = audio;
        Jitter = jitter;
        _effects = effects;
        Global = mode == VoiceReceiveMode.Radio;
    }

    /// <summary>
    /// Decodes what's needed to keep playing smoothly until the next frame, and starts the next clip when it's time.
    /// </summary>
    public void Update(TimeSpan now, float frameTime)
    {
        CleanUpClips();

        // How much is already lined up: what's waiting, plus what the current clip still has to play.
        var ahead = _pendingCount + CurrentRemaining();
        var target = Body + Fade + MaxLead + (int) (Rate * (Math.Min(frameTime, 0.25f) * 1.5f + 0.02f));

        // Bounded so a flood of packets can't stall the frame.
        for (var guard = 0; guard < 12 && ahead < target; guard++)
        {
            // Running ahead of the network: wait for the next packet rather than fill in for it, unless what's lined
            // up is about to run out.
            if (Jitter.Playing && Jitter.Buffered == 0 && ahead >= FrameSamples * 2)
                break;

            var result = Jitter.Pop(now, out var payload);
            if (result == JitterResult.Idle)
            {
                if (!Jitter.Playing)
                {
                    _spurtEnded = true;
                    _effectsIdle = true;
                }

                break;
            }

            _spurtEnded = false;
            if (result == JitterResult.Packet && ImaAdpcm.Decode(payload, _decoded))
            {
                Array.Copy(_decoded, _lastDecoded, _decoded.Length);
                _concealed = 0;
            }
            else
            {
                if (result == JitterResult.Packet)
                    DecodeErrors++;

                // Lost: repeat the last frame, quieter each time, so a dropped packet is a blip, not a click.
                _concealed++;
                var fade = MathF.Pow(0.5f, _concealed);
                for (var i = 0; i < _decoded.Length; i++)
                {
                    _decoded[i] = (short) (_lastDecoded[i] * fade);
                }
            }

            Render();

            // Heard once everything already lined up has played.
            _timeline.Add(now + TimeSpan.FromSeconds(ahead / (double) Rate), _blockLevels);
            Append(_float);
            ahead += FrameSamples;
        }

        ScheduleClips(frameTime);

        var levels = _timeline.Get(now, TimeSpan.FromMilliseconds(20));
        Levels = Levels.SmoothTowards(levels, frameTime);

        foreach (var clip in _clips)
        {
            clip.Source.Global = Global;
            if (!Global)
                clip.Source.Position = Position;
        }
    }

    #region Decoding

    /// <summary>
    /// Turns the decoded 16 kHz frame into 48 kHz audio with the listener-side effects (radio, walls) and volume.
    /// </summary>
    private void Render()
    {
        // 16 kHz -> 48 kHz: linear interpolation smoothed by a low-pass.
        var previous = _lastSample;
        for (var i = 0; i < _decoded.Length; i++)
        {
            var current = _decoded[i] / 32768f;
            for (var j = 0; j < 3; j++)
            {
                var sample = previous + (current - previous) * ((j + 1) / 3f);
                _float[i * 3 + j] = _smooth2.ProcessSample(_smooth1.ProcessSample(sample));
            }

            previous = current;
        }

        _lastSample = previous;
        var buffer = _float.AsSpan();

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
            _muffleFilter.Set(BiquadType.LowPass, Rate, cutoff);
            _muffleFilter2.Set(BiquadType.LowPass, Rate, cutoff);
            _appliedCutoff = cutoff;
        }

        if (_muffle >= 0.02f)
        {
            _muffleFilter.Process(buffer);
            _muffleFilter2.Process(buffer);
        }

        _effects?.Process(buffer);

        // Measured before the player's own volume setting: that shouldn't change how loud someone looks.
        _blockLevels = _analyzer.Analyze(buffer, TargetGain);

        // Ramp gain across the block to avoid zipper noise when audibility changes.
        var target = TargetGain * Volume;
        var step = (target - _gain) / buffer.Length;
        for (var i = 0; i < buffer.Length; i++)
        {
            _gain += step;
            buffer[i] *= _gain;
        }

        _gain = target;
    }

    private void Append(ReadOnlySpan<float> samples)
    {
        if (_pendingCount + samples.Length > _pending.Length)
        {
            // Playback isn't keeping up: forget the oldest rather than grow.
            var drop = _pendingCount + samples.Length - MaxPending;
            Array.Copy(_pending, drop, _pending, 0, _pendingCount - drop);
            _pendingCount -= drop;
        }

        VoiceDsp.FloatToShort(samples, _pending.AsSpan(_pendingCount, samples.Length));
        _pendingCount += samples.Length;
    }

    #endregion

    #region Clips

    private int CurrentRemaining()
    {
        if (_current is not { } clip || !clip.Source.Playing)
            return 0;

        return Math.Max(0, clip.Body - (int) (clip.Source.PlaybackPosition * Rate));
    }

    /// <param name="frameTime">How long until we get another chance, roughly: the last frame's length.</param>
    private void ScheduleClips(float frameTime)
    {
        // Frames vary: plan for the longest one lately, forgetting it slowly.
        _longFrame = Math.Max(frameTime, _longFrame * 0.995f);

        var current = _current;
        var overlapping = current != null && current.Source.Playing;
        var offset = 0;
        var lead = 0;

        if (overlapping)
        {
            var position = (int) (current!.Source.PlaybackPosition * Rate);
            var untilTakeover = current.Body - position;

            // Not yet, if the next frame comes in time.
            var nextChance = (int) (Rate * (Math.Min(_longFrame, 0.1f) * 1.25f + 0.005f));
            if (untilTakeover > nextChance)
                return;

            if (untilTakeover > 0)
            {
                // Early: pad with silence so the audio starts exactly when the previous clip hands over.
                lead = Math.Min(untilTakeover, MaxLead);
            }
            else
            {
                // Late after all: start the next clip that far in.
                offset = Math.Min(-untilTakeover, Fade);
            }
        }

        var length = Math.Min(_pendingCount, Body + Fade);
        var last = _spurtEnded && length < Body + Fade;
        if (length == 0 || length < Body + Fade && !last)
            return;

        // Starting a sentence: a little extra in hand first.
        if (!overlapping && !_spurtEnded && _pendingCount < Body + Fade + StartMargin)
            return;

        _clip.AsSpan(0, lead).Clear();
        _pending.AsSpan(0, length).CopyTo(_clip.AsSpan(lead));
        var clip = _clip.AsSpan(lead, length);

        // Fade in over the previous clip's fade out, or just a short ramp when starting fresh.
        var fadeIn = overlapping ? Math.Min(Fade, length) : Math.Min(Ramp, length);
        for (var i = 0; i < fadeIn; i++)
        {
            clip[i] = (short) (clip[i] * (i / (float) fadeIn));
        }

        // Fade out into the next clip, or a short ramp at the end of the spurt.
        var fadeOut = last ? Math.Min(Ramp, length) : Fade;
        for (var i = 0; i < fadeOut; i++)
        {
            var at = length - fadeOut + i;
            clip[at] = (short) (clip[at] * (1f - (i + 1) / (float) fadeOut));
        }

        var consumed = last ? length : Body;
        Array.Copy(_pending, consumed, _pending, 0, _pendingCount - consumed);
        _pendingCount -= consumed;

        var source = _audio.Play(_clip.AsSpan(0, lead + length), Rate, offset / (float) Rate, Global, Position);
        if (source == null)
            return;

        _current = new Clip(source, lead + (last ? length : Body));
        _clips.Add(_current);
    }

    private void CleanUpClips()
    {
        for (var i = _clips.Count - 1; i >= 0; i--)
        {
            var clip = _clips[i];
            if (clip.Source.Playing)
                continue;

            clip.Dispose();
            _clips.RemoveAt(i);
            if (_current == clip)
                _current = null;
        }
    }

    #endregion

    public void Dispose()
    {
        foreach (var clip in _clips)
        {
            clip.Dispose();
        }

        _clips.Clear();
        _current = null;
    }

    private sealed class Clip(IVoiceClip source, int body) : IDisposable
    {
        public readonly IVoiceClip Source = source;

        /// <summary>
        /// Where the next clip takes over.
        /// </summary>
        public readonly int Body = body;

        public void Dispose()
        {
            Source.Dispose();
        }
    }
}

/// <summary>
/// Plays the clips a <see cref="VoiceStream"/> is made of. <see cref="EngineVoiceClipPlayer"/> in the game; tests use
/// their own to check the clips join up.
/// </summary>
public interface IVoiceClipPlayer
{
    /// <summary>
    /// Starts playing <paramref name="samples"/> (mono, copied), <paramref name="startAt"/> seconds in.
    /// </summary>
    /// <returns>Null if no more sounds can play right now.</returns>
    IVoiceClip? Play(ReadOnlySpan<short> samples, int sampleRate, float startAt, bool global, Vector2 position);
}

public interface IVoiceClip : IDisposable
{
    bool Playing { get; }

    /// <summary>
    /// How far it has played, in seconds, by the audio device's clock.
    /// </summary>
    float PlaybackPosition { get; }

    bool Global { set; }
    Vector2 Position { set; }
}

/// <summary>
/// Clips as engine sounds: an audio buffer and a source each.
/// </summary>
public sealed class EngineVoiceClipPlayer(IAudioManager audio) : IVoiceClipPlayer
{
    public IVoiceClip? Play(ReadOnlySpan<short> samples, int sampleRate, float startAt, bool global, Vector2 position)
    {
        var stream = audio.LoadAudioRaw(samples, 1, sampleRate);
        var source = audio.CreateAudioSource(stream);
        if (source == null)
        {
            stream.Dispose();
            return null;
        }

        // The server already applied distance falloff; OpenAL only pans.
        source.RolloffFactor = 0f;
        source.ReferenceDistance = 1f;
        source.MaxDistance = 1000f;
        source.Occlusion = 0f;
        source.Gain = 1f;
        source.Global = global;
        if (!global)
            source.Position = position;

        if (startAt > 0f)
            source.PlaybackPosition = startAt;

        source.StartPlaying();
        return new EngineClip(stream, source);
    }

    private sealed class EngineClip(AudioStream stream, IAudioSource source) : IVoiceClip
    {
        public bool Playing => source.Playing;
        public float PlaybackPosition => source.PlaybackPosition;

        public bool Global
        {
            set => source.Global = value;
        }

        public Vector2 Position
        {
            set => source.Position = value;
        }

        public void Dispose()
        {
            source.Dispose();
            stream.Dispose();
        }
    }
}

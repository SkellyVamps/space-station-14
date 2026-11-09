using Content.Shared.Voice;
using Content.Shared.Voice.Dsp;

namespace Content.Client.Voice;

/// <summary>
/// How loud a voice is right now, overall and in three frequency bands, each 0-1. Drives the level bars over speakers'
/// heads and the waveforms in the voice panel.
/// </summary>
public record struct VoiceLevels(float Low, float Mid, float High, float Overall)
{
    public bool IsSilent => Overall <= 0.01f;

    /// <summary>
    /// Moves towards <paramref name="target"/>: quickly when getting louder, more slowly when getting quieter, so the
    /// bars jump with syllables and settle smoothly.
    /// </summary>
    public VoiceLevels SmoothTowards(VoiceLevels target, float frameTime)
    {
        var attack = 1f - MathF.Exp(-frameTime / 0.03f);
        var release = 1f - MathF.Exp(-frameTime / 0.18f);
        return new VoiceLevels(
            Smooth(Low, target.Low, attack, release),
            Smooth(Mid, target.Mid, attack, release),
            Smooth(High, target.High, attack, release),
            Smooth(Overall, target.Overall, attack, release));
    }

    private static float Smooth(float current, float target, float attack, float release)
    {
        return current + (target - current) * (target > current ? attack : release);
    }
}

/// <summary>
/// Measures <see cref="VoiceLevels"/> from audio: the overall level plus bass (below ~400 Hz), middle (~1 kHz) and
/// treble (above ~2.5 kHz).
/// </summary>
public sealed class VoiceLevelAnalyzer
{
    /// <summary>
    /// Levels are shown from this quiet (dBFS)...
    /// </summary>
    private const float FloorDb = -52f;

    /// <summary>
    /// ...up to this loud.
    /// </summary>
    private const float CeilingDb = -12f;

    private readonly Biquad _low = new(BiquadType.LowPass, VoiceConstants.SampleRate, 400f);
    private readonly Biquad _mid = new(BiquadType.BandPass, VoiceConstants.SampleRate, 1000f, 0.8f);
    private readonly Biquad _high = new(BiquadType.HighPass, VoiceConstants.SampleRate, 2500f);

    /// <param name="gain">Volume the audio is heard at (distance, walls), applied to the result.</param>
    public VoiceLevels Analyze(ReadOnlySpan<float> buffer, float gain = 1f)
    {
        if (buffer.Length == 0)
            return default;

        double all = 0, low = 0, mid = 0, high = 0;
        foreach (var sample in buffer)
        {
            all += sample * sample;

            var l = _low.ProcessSample(sample);
            var m = _mid.ProcessSample(sample);
            var h = _high.ProcessSample(sample);
            low += l * l;
            mid += m * m;
            high += h * h;
        }

        var n = buffer.Length / (gain * gain + 1e-12);
        // The narrower bands carry less energy than the whole signal; boost them so they fill the bars similarly.
        return new VoiceLevels(
            Normalize(low / n, 1.4f),
            Normalize(mid / n, 2.5f),
            Normalize(high / n, 4f),
            Normalize(all / n, 1f));
    }

    public void Reset()
    {
        _low.Reset();
        _mid.Reset();
        _high.Reset();
    }

    private static float Normalize(double meanSquare, float boost)
    {
        var db = VoiceDsp.LinearToDb((float) Math.Sqrt(meanSquare) * boost);
        return Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
    }
}

/// <summary>
/// Remembers when each measured block of audio will actually be heard, so the bars move with what you hear rather
/// than with what was just decoded (which is queued slightly ahead).
/// </summary>
public sealed class VoiceLevelTimeline
{
    private const int Capacity = 32;

    private readonly TimeSpan[] _times = new TimeSpan[Capacity];
    private readonly VoiceLevels[] _levels = new VoiceLevels[Capacity];
    private int _head;
    private int _count;

    public void Add(TimeSpan playsAt, VoiceLevels levels)
    {
        var index = (_head + _count) % Capacity;
        if (_count == Capacity)
        {
            _head = (_head + 1) % Capacity;
            _count--;
        }

        _times[index] = playsAt;
        _levels[index] = levels;
        _count++;
    }

    /// <summary>
    /// The levels of the audio playing at <paramref name="now"/>, or silence if nothing is.
    /// </summary>
    /// <param name="hold">How long one block of audio lasts.</param>
    public VoiceLevels Get(TimeSpan now, TimeSpan hold)
    {
        // Skip blocks that a newer, already started block replaced.
        while (_count > 1 && _times[(_head + 1) % Capacity] <= now)
        {
            _head = (_head + 1) % Capacity;
            _count--;
        }

        if (_count == 0 || _times[_head] > now)
            return default;

        if (now - _times[_head] < hold)
            return _levels[_head];

        // Finished and nothing after it: silence.
        _head = (_head + 1) % Capacity;
        _count--;
        return default;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}

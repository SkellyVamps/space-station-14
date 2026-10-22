namespace Content.Shared.Voice.Dsp;

/// <summary>
/// Multiplies the signal by a constant.
/// </summary>
public sealed class GainProcessor(float gain) : IVoiceProcessor
{
    public float Gain = gain;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= Gain;
        }
    }

    public void Reset()
    {
    }
}

/// <summary>
/// Follows the amplitude envelope of a signal with separate attack and release times.
/// </summary>
public struct EnvelopeFollower
{
    private readonly float _attack;
    private readonly float _release;
    public float Value;

    public EnvelopeFollower(int sampleRate, float attackMs, float releaseMs)
    {
        _attack = Coefficient(sampleRate, attackMs);
        _release = Coefficient(sampleRate, releaseMs);
        Value = 0;
    }

    public static float Coefficient(int sampleRate, float ms)
    {
        return ms <= 0 ? 0 : MathF.Exp(-1f / (sampleRate * ms / 1000f));
    }

    public float Next(float x)
    {
        var level = MathF.Abs(x);
        var coef = level > Value ? _attack : _release;
        Value = coef * Value + (1 - coef) * level;
        return Value;
    }
}

/// <summary>
/// Feed-forward compressor with a soft knee and makeup gain. With a high ratio it doubles as a limiter.
/// </summary>
public sealed class CompressorProcessor : IVoiceProcessor
{
    private readonly float _thresholdDb;
    private readonly float _ratio;
    private readonly float _kneeDb;
    private readonly float _makeup;
    private EnvelopeFollower _env;
    private readonly int _sampleRate;
    private readonly float _attackMs;
    private readonly float _releaseMs;

    public CompressorProcessor(int sampleRate, float thresholdDb, float ratio, float attackMs, float releaseMs,
        float makeupDb = 0, float kneeDb = 6)
    {
        _sampleRate = sampleRate;
        _attackMs = attackMs;
        _releaseMs = releaseMs;
        _thresholdDb = thresholdDb;
        _ratio = MathF.Max(1f, ratio);
        _kneeDb = MathF.Max(0f, kneeDb);
        _makeup = VoiceDsp.DbToLinear(makeupDb);
        _env = new EnvelopeFollower(sampleRate, attackMs, releaseMs);
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var level = _env.Next(buffer[i]);
            var levelDb = VoiceDsp.LinearToDb(level);
            var over = levelDb - _thresholdDb;
            float reductionDb;

            if (over <= -_kneeDb / 2)
                reductionDb = 0;
            else if (over >= _kneeDb / 2)
                reductionDb = over * (1 - 1 / _ratio);
            else
            {
                var x = over + _kneeDb / 2;
                reductionDb = (1 - 1 / _ratio) * x * x / (2 * _kneeDb);
            }

            buffer[i] *= VoiceDsp.DbToLinear(-reductionDb) * _makeup;
        }
    }

    public void Reset()
    {
        _env = new EnvelopeFollower(_sampleRate, _attackMs, _releaseMs);
    }
}

/// <summary>
/// Soft (tanh) or hard clipping distortion.
/// </summary>
public sealed class DistortionProcessor(float drive, float mix, bool hard) : IVoiceProcessor
{
    private readonly float _drive = MathF.Max(1f, drive);
    private readonly float _norm = 1f / MathF.Tanh(MathF.Max(1f, drive));

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            var y = hard
                ? Math.Clamp(x * _drive, -1f, 1f)
                : MathF.Tanh(x * _drive) * _norm;
            buffer[i] = x + (y - x) * mix;
        }
    }

    public void Reset()
    {
    }
}

/// <summary>
/// Reduces bit depth and sample rate for a lo-fi digital sound.
/// </summary>
public sealed class BitCrushProcessor(int bits, int downsample, float mix) : IVoiceProcessor
{
    private readonly float _levels = MathF.Pow(2, Math.Clamp(bits, 1, 16) - 1);
    private readonly int _downsample = Math.Max(1, downsample);
    private int _counter;
    private float _held;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (_counter++ % _downsample == 0)
                _held = MathF.Round(buffer[i] * _levels) / _levels;

            buffer[i] += (_held - buffer[i]) * mix;
        }
    }

    public void Reset()
    {
        _counter = 0;
        _held = 0;
    }
}

/// <summary>
/// Multiplies the signal by a sine carrier: metallic, robotic or alien tones.
/// </summary>
public sealed class RingModulatorProcessor(int sampleRate, float frequency, float mix) : IVoiceProcessor
{
    private readonly float _step = 2f * MathF.PI * frequency / sampleRate;
    private float _phase;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            buffer[i] = x + (x * MathF.Sin(_phase) - x) * mix;
            _phase += _step;
            if (_phase > 2f * MathF.PI)
                _phase -= 2f * MathF.PI;
        }
    }

    public void Reset()
    {
        _phase = 0;
    }
}

/// <summary>
/// Periodic amplitude modulation. With jitter it becomes an irregular "rough" or "gravelly" quality.
/// </summary>
public sealed class TremoloProcessor(int sampleRate, float rate, float depth, float jitter, int seed) : IVoiceProcessor
{
    private readonly float _baseStep = 2f * MathF.PI * rate / sampleRate;
    private VoiceRandom _random = new(seed);
    private float _phase;
    private float _rateMod = 1f;
    private float _rateTarget = 1f;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (jitter > 0 && (i & 63) == 0)
            {
                if (MathF.Abs(_rateMod - _rateTarget) < 0.01f)
                    _rateTarget = 1f + _random.NextBipolar() * jitter;

                _rateMod += (_rateTarget - _rateMod) * 0.05f;
            }

            var mod = 1f - depth * (0.5f + 0.5f * MathF.Sin(_phase));
            buffer[i] *= mod;
            _phase += _baseStep * _rateMod;
            if (_phase > 2f * MathF.PI)
                _phase -= 2f * MathF.PI;
        }
    }

    public void Reset()
    {
        _phase = 0;
    }
}

/// <summary>
/// Fractional delay line used by echo, comb, chorus and vibrato effects.
/// </summary>
public sealed class DelayLine
{
    private readonly float[] _buffer;
    private int _write;

    public DelayLine(int maxSamples)
    {
        _buffer = new float[Math.Max(2, maxSamples + 2)];
    }

    public void Write(float x)
    {
        _buffer[_write] = x;
        _write = (_write + 1) % _buffer.Length;
    }

    /// <summary>
    /// Reads the sample written <paramref name="delay"/> samples ago (1 = most recent), linearly interpolated.
    /// </summary>
    public float Read(float delay)
    {
        delay = Math.Clamp(delay, 1f, _buffer.Length - 2);
        var pos = _write - delay;
        if (pos < 0)
            pos += _buffer.Length;

        var i0 = (int) pos;
        var frac = pos - i0;
        var i1 = (i0 + 1) % _buffer.Length;
        return _buffer[i0] * (1 - frac) + _buffer[i1] * frac;
    }

    public void Clear()
    {
        Array.Clear(_buffer);
    }
}

/// <summary>
/// Feedback echo.
/// </summary>
public sealed class EchoProcessor(int sampleRate, float delayMs, float feedback, float mix) : IVoiceProcessor
{
    private readonly DelayLine _line = new((int) (sampleRate * delayMs / 1000f));
    private readonly float _delay = MathF.Max(1, sampleRate * delayMs / 1000f);
    private readonly float _feedback = Math.Clamp(feedback, 0f, 0.95f);

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            var delayed = _line.Read(_delay);
            _line.Write(x + delayed * _feedback);
            buffer[i] = x + delayed * mix;
        }
    }

    public void Reset()
    {
        _line.Clear();
    }
}

/// <summary>
/// Short feedback comb filter: metallic, tubular resonance (helmets, speakers, robots).
/// </summary>
public sealed class CombProcessor(int sampleRate, float delayMs, float feedback, float mix) : IVoiceProcessor
{
    private readonly DelayLine _line = new((int) (sampleRate * delayMs / 1000f) + 1);
    private readonly float _delay = MathF.Max(1, sampleRate * delayMs / 1000f);
    private readonly float _feedback = Math.Clamp(feedback, -0.95f, 0.95f);

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            var y = x + _line.Read(_delay) * _feedback;
            _line.Write(y);
            buffer[i] = x + (y - x) * mix;
        }
    }

    public void Reset()
    {
        _line.Clear();
    }
}

/// <summary>
/// Modulated delay: chorus when mixed with the dry signal, vibrato (pitch wobble) when fully wet.
/// </summary>
public sealed class ModulatedDelayProcessor(int sampleRate, float baseDelayMs, float depthMs, float rate, float mix)
    : IVoiceProcessor
{
    private readonly DelayLine _line = new((int) (sampleRate * (baseDelayMs + depthMs) / 1000f) + 2);
    private readonly float _base = sampleRate * baseDelayMs / 1000f;
    private readonly float _depth = sampleRate * depthMs / 1000f;
    private readonly float _step = 2f * MathF.PI * rate / sampleRate;
    private float _phase;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            _line.Write(x);
            var delayed = _line.Read(_base + _depth * (0.5f + 0.5f * MathF.Sin(_phase)));
            _phase += _step;
            if (_phase > 2f * MathF.PI)
                _phase -= 2f * MathF.PI;

            buffer[i] = x + (delayed - x) * mix;
        }
    }

    public void Reset()
    {
        _line.Clear();
        _phase = 0;
    }
}

/// <summary>
/// Adds noise. Optionally the noise follows the voice's loudness (breathiness) instead of being constant (hiss).
/// </summary>
public sealed class NoiseProcessor : IVoiceProcessor
{
    private readonly float _level;
    private readonly bool _followSignal;
    private readonly Biquad _color;
    private readonly int _seed;
    private VoiceRandom _random;
    private EnvelopeFollower _env;
    private readonly int _sampleRate;

    public NoiseProcessor(int sampleRate, float level, bool followSignal, float lowCut, float highCut, int seed)
    {
        _sampleRate = sampleRate;
        _level = level;
        _followSignal = followSignal;
        _seed = seed;
        _random = new VoiceRandom(seed);
        _env = new EnvelopeFollower(sampleRate, 5, 60);

        var center = MathF.Sqrt(MathF.Max(lowCut, 20) * MathF.Max(highCut, lowCut + 1));
        var q = center / MathF.Max(1, highCut - lowCut);
        _color = new Biquad(BiquadType.BandPass, sampleRate, center, MathF.Max(q, 0.3f));
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            var noise = _color.ProcessSample(_random.NextBipolar()) * 2f;
            var amount = _followSignal ? _env.Next(x) * 4f : 1f;
            buffer[i] = x + noise * _level * amount;
        }
    }

    public void Reset()
    {
        _random = new VoiceRandom(_seed);
        _env = new EnvelopeFollower(_sampleRate, 5, 60);
        _color.Reset();
    }
}

/// <summary>
/// Random clicks and pops, like a bad connection or old speaker.
/// </summary>
public sealed class CrackleProcessor(int sampleRate, float ratePerSecond, float level, int seed) : IVoiceProcessor
{
    private VoiceRandom _random = new(seed);
    private readonly float _chance = ratePerSecond / sampleRate;
    private readonly float _decay = EnvelopeFollower.Coefficient(sampleRate, 1.5f);
    private float _impulse;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (_random.NextFloat() < _chance)
                _impulse = _random.NextBipolar() * level;

            buffer[i] += _impulse * _random.NextBipolar();
            _impulse *= _decay;
        }
    }

    public void Reset()
    {
        _impulse = 0;
    }
}

/// <summary>
/// Channel vocoder: imposes the voice's band envelopes onto a buzzy carrier at a fixed pitch.
/// Produces the classic monotone robot voice while keeping words intelligible.
/// </summary>
public sealed class VocoderProcessor : IVoiceProcessor
{
    private readonly Biquad[] _analysis;
    private readonly Biquad[] _synthesis;
    private readonly EnvelopeFollower[] _envelopes;
    private readonly float _carrierStep;
    private readonly float _mix;
    private readonly float _noise;
    private readonly int _sampleRate;
    private VoiceRandom _random = new(1);
    private float _carrierPhase;

    public VocoderProcessor(int sampleRate, int bands, float carrierFrequency, float mix, float noise)
    {
        _sampleRate = sampleRate;
        bands = Math.Clamp(bands, 4, 32);
        _analysis = new Biquad[bands];
        _synthesis = new Biquad[bands];
        _envelopes = new EnvelopeFollower[bands];
        _carrierStep = carrierFrequency / sampleRate;
        _mix = mix;
        _noise = noise;

        // Log-spaced bands across the speech range.
        const float low = 120f;
        const float high = 7000f;
        var ratio = MathF.Pow(high / low, 1f / bands);
        var q = MathF.Sqrt(ratio) / (ratio - 1f);

        for (var b = 0; b < bands; b++)
        {
            var center = low * MathF.Pow(ratio, b + 0.5f);
            _analysis[b] = new Biquad(BiquadType.BandPass, sampleRate, center, q);
            _synthesis[b] = new Biquad(BiquadType.BandPass, sampleRate, center, q);
            _envelopes[b] = new EnvelopeFollower(sampleRate, 3, 25);
        }
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];

            // Sawtooth carrier plus a little noise so unvoiced consonants survive.
            var carrier = 2f * _carrierPhase - 1f;
            carrier = carrier * (1 - _noise) + _random.NextBipolar() * _noise;
            _carrierPhase += _carrierStep;
            if (_carrierPhase >= 1f)
                _carrierPhase -= 1f;

            var y = 0f;
            for (var b = 0; b < _analysis.Length; b++)
            {
                var level = _envelopes[b].Next(_analysis[b].ProcessSample(x));
                y += _synthesis[b].ProcessSample(carrier) * level;
            }

            y *= 6f;
            buffer[i] = x + (y - x) * _mix;
        }
    }

    public void Reset()
    {
        for (var b = 0; b < _analysis.Length; b++)
        {
            _analysis[b].Reset();
            _synthesis[b].Reset();
            _envelopes[b] = new EnvelopeFollower(_sampleRate, 3, 25);
        }

        _carrierPhase = 0;
    }
}

/// <summary>
/// Removes low-level background noise by closing when the signal falls below a threshold.
/// </summary>
public sealed class NoiseGateProcessor(int sampleRate, float thresholdDb, float holdMs = 120, float releaseMs = 80)
    : IVoiceProcessor
{
    private readonly float _threshold = VoiceDsp.DbToLinear(thresholdDb);
    private readonly int _holdSamples = (int) (sampleRate * holdMs / 1000f);
    private readonly float _releaseCoef = EnvelopeFollower.Coefficient(sampleRate, releaseMs);
    private readonly float _attackCoef = EnvelopeFollower.Coefficient(sampleRate, 2);
    private EnvelopeFollower _env = new(sampleRate, 1, 30);
    private int _hold;
    private float _gain;

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var level = _env.Next(buffer[i]);
            if (level >= _threshold)
                _hold = _holdSamples;
            else if (_hold > 0)
                _hold--;

            var target = _hold > 0 ? 1f : 0f;
            var coef = target > _gain ? _attackCoef : _releaseCoef;
            _gain = coef * _gain + (1 - coef) * target;
            buffer[i] *= _gain;
        }
    }

    public void Reset()
    {
        _hold = 0;
        _gain = 0;
        _env.Value = 0;
    }
}

/// <summary>
/// Runs several processors in series.
/// </summary>
public sealed class ProcessorChain : IVoiceProcessor
{
    private readonly List<IVoiceProcessor> _processors;

    public int Count => _processors.Count;

    public ProcessorChain(List<IVoiceProcessor> processors)
    {
        _processors = processors;
    }

    public void Process(Span<float> buffer)
    {
        foreach (var processor in _processors)
        {
            processor.Process(buffer);
        }
    }

    public void Reset()
    {
        foreach (var processor in _processors)
        {
            processor.Reset();
        }
    }
}

public static class VoiceDsp
{
    public static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);

    public static float LinearToDb(float linear) => 20f * MathF.Log10(MathF.Max(linear, 1e-6f));

    /// <summary>
    /// RMS level of a block in dBFS.
    /// </summary>
    public static float RmsDb(ReadOnlySpan<float> buffer)
    {
        if (buffer.Length == 0)
            return -120f;

        var sum = 0.0;
        foreach (var s in buffer)
        {
            sum += s * s;
        }

        return LinearToDb((float) Math.Sqrt(sum / buffer.Length));
    }

    public static void ShortToFloat(ReadOnlySpan<short> source, Span<float> dest)
    {
        for (var i = 0; i < source.Length; i++)
        {
            dest[i] = source[i] / 32768f;
        }
    }

    public static void FloatToShort(ReadOnlySpan<float> source, Span<short> dest)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var s = source[i];
            if (!float.IsFinite(s))
                s = 0;

            dest[i] = (short) Math.Clamp(s * 32767f, -32768f, 32767f);
        }
    }
}

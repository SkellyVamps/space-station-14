namespace Content.Shared.Voice.Dsp;

public enum BiquadType : byte
{
    LowPass,
    HighPass,
    BandPass,
    Notch,
    Peaking,
    LowShelf,
    HighShelf,
}

/// <summary>
/// Second order IIR filter (RBJ audio EQ cookbook), transposed direct form II.
/// </summary>
public sealed class Biquad : IVoiceProcessor
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _z1, _z2;

    public Biquad(BiquadType type, int sampleRate, float frequency, float q = 0.7071f, float gainDb = 0f)
    {
        Set(type, sampleRate, frequency, q, gainDb);
    }

    public void Set(BiquadType type, int sampleRate, float frequency, float q = 0.7071f, float gainDb = 0f)
    {
        frequency = Math.Clamp(frequency, 10f, sampleRate * 0.49f);
        q = MathF.Max(q, 0.05f);

        var w0 = 2f * MathF.PI * frequency / sampleRate;
        var cos = MathF.Cos(w0);
        var sin = MathF.Sin(w0);
        var alpha = sin / (2f * q);
        var a = MathF.Pow(10f, gainDb / 40f);

        float b0, b1, b2, a0, a1, a2;

        switch (type)
        {
            case BiquadType.LowPass:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case BiquadType.HighPass:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case BiquadType.BandPass:
                // Constant 0 dB peak gain.
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case BiquadType.Notch:
                b0 = 1; b1 = -2 * cos; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case BiquadType.Peaking:
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
            case BiquadType.LowShelf:
            {
                var sq = 2 * MathF.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cos + sq);
                b1 = 2 * a * ((a - 1) - (a + 1) * cos);
                b2 = a * ((a + 1) - (a - 1) * cos - sq);
                a0 = (a + 1) + (a - 1) * cos + sq;
                a1 = -2 * ((a - 1) + (a + 1) * cos);
                a2 = (a + 1) + (a - 1) * cos - sq;
                break;
            }
            case BiquadType.HighShelf:
            {
                var sq = 2 * MathF.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cos + sq);
                b1 = -2 * a * ((a - 1) + (a + 1) * cos);
                b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq;
                a1 = 2 * ((a - 1) - (a + 1) * cos);
                a2 = (a + 1) - (a - 1) * cos - sq;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public float ProcessSample(float x)
    {
        var y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ProcessSample(buffer[i]);
        }

        // Flush denormals / recover from blow-ups caused by bad input.
        if (!float.IsFinite(_z1) || !float.IsFinite(_z2) || MathF.Abs(_z1) < 1e-20f)
        {
            _z1 = 0;
            _z2 = 0;
        }
    }

    public void Reset()
    {
        _z1 = 0;
        _z2 = 0;
    }
}

namespace Content.Shared.Voice.Dsp;

/// <summary>
/// Phase vocoder that shifts pitch and formants independently.
/// </summary>
/// <remarks>
/// <para>
/// Each analysis frame is split into a smooth spectral envelope (formants, roughly "who is talking") and a fine
/// excitation (harmonics, roughly "the note being spoken"). The excitation is moved by the pitch ratio while the
/// envelope is re-applied warped by the formant ratio. Equal ratios reduce to a classic pitch shifter where the voice
/// gets "chipmunked"; a pitch ratio with a formant ratio of 1 changes pitch while keeping the voice's character.
/// </para>
/// <para>
/// Based on the well known public domain-style phase vocoder description by S. M. Bernsee ("Pitch Shifting Using
/// The Fourier Transform"); written from scratch here. Latency is <c>FrameSize - Hop</c> samples (16 ms at 48 kHz).
/// </para>
/// </remarks>
public sealed class SpectralShifter : IVoiceProcessor
{
    private const int FrameSize = 1024;
    private const int Oversample = 4;
    private const int Hop = FrameSize / Oversample;
    private const int Half = FrameSize / 2;
    private const int Latency = FrameSize - Hop;

    // Half width (in bins) of the moving average used to estimate the spectral envelope.
    private const int EnvelopeRadius = 6;

    private readonly Fft _fft = new(FrameSize);
    private readonly float[] _window = new float[FrameSize];

    private readonly float[] _inFifo = new float[FrameSize];
    private readonly float[] _outFifo = new float[FrameSize];
    private readonly float[] _outAccum = new float[FrameSize * 2];
    private readonly float[] _re = new float[FrameSize];
    private readonly float[] _im = new float[FrameSize];
    private readonly float[] _lastPhase = new float[Half + 1];
    private readonly float[] _sumPhase = new float[Half + 1];
    private readonly float[] _anaMag = new float[Half + 1];
    private readonly float[] _anaFreq = new float[Half + 1];
    private readonly float[] _synMag = new float[Half + 1];
    private readonly float[] _synFreq = new float[Half + 1];
    private readonly float[] _envelope = new float[Half + 1];
    private readonly float[] _prefix = new float[Half + 2];

    private int _rover = Latency;

    public float PitchRatio { get; set; }
    public float FormantRatio { get; set; }

    public SpectralShifter(float pitchRatio, float formantRatio)
    {
        PitchRatio = pitchRatio;
        FormantRatio = formantRatio;

        for (var i = 0; i < FrameSize; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / FrameSize);
        }
    }

    public static SpectralShifter FromSemitones(float pitch, float formant)
    {
        return new SpectralShifter(MathF.Pow(2f, pitch / 12f), MathF.Pow(2f, formant / 12f));
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            _inFifo[_rover] = buffer[i];
            buffer[i] = _outFifo[_rover - Latency];
            _rover++;

            if (_rover < FrameSize)
                continue;

            _rover = Latency;
            ProcessFrame();

            Array.Copy(_outAccum, _outFifo, Hop);
            Array.Copy(_outAccum, Hop, _outAccum, 0, FrameSize);
            Array.Clear(_outAccum, FrameSize, Hop);
            Array.Copy(_inFifo, Hop, _inFifo, 0, Latency);
        }
    }

    private void ProcessFrame()
    {
        const float expected = 2f * MathF.PI * Hop / FrameSize;

        for (var k = 0; k < FrameSize; k++)
        {
            _re[k] = _inFifo[k] * _window[k];
            _im[k] = 0;
        }

        _fft.Transform(_re, _im, false);

        // Analysis: magnitude and true frequency (in bins) of every bin.
        for (var k = 0; k <= Half; k++)
        {
            var re = _re[k];
            var im = _im[k];
            var mag = 2f * MathF.Sqrt(re * re + im * im);
            var phase = MathF.Atan2(im, re);

            var delta = phase - _lastPhase[k];
            _lastPhase[k] = phase;
            delta -= k * expected;
            delta = WrapPhase(delta);

            _anaMag[k] = mag;
            _anaFreq[k] = k + delta * Oversample / (2f * MathF.PI);
        }

        // Spectral envelope: moving average of the magnitude across bins smooths out individual harmonics.
        _prefix[0] = 0;
        for (var k = 0; k <= Half; k++)
        {
            _prefix[k + 1] = _prefix[k] + _anaMag[k];
        }

        for (var k = 0; k <= Half; k++)
        {
            var lo = Math.Max(0, k - EnvelopeRadius);
            var hi = Math.Min(Half, k + EnvelopeRadius);
            _envelope[k] = MathF.Max((_prefix[hi + 1] - _prefix[lo]) / (hi - lo + 1), 1e-9f);
        }

        Array.Clear(_synMag);
        Array.Clear(_synFreq);

        // Move the excitation (magnitude divided by envelope) by the pitch ratio.
        var pitch = PitchRatio;
        for (var k = 0; k <= Half; k++)
        {
            var j = (int) (k * pitch + 0.5f);
            if (j > Half)
                break;

            _synMag[j] += _anaMag[k] / _envelope[k];
            _synFreq[j] = _anaFreq[k] * pitch;
        }

        // Re-apply the envelope, warped by the formant ratio.
        var formant = FormantRatio;
        for (var j = 0; j <= Half; j++)
        {
            var src = j / formant;
            float env;
            if (src >= Half)
            {
                env = 0;
            }
            else
            {
                var idx = (int) src;
                var frac = src - idx;
                env = _envelope[idx] * (1 - frac) + _envelope[idx + 1] * frac;
            }

            _synMag[j] *= env;
        }

        // Synthesis: accumulate phase for each output bin from its frequency.
        for (var j = 0; j <= Half; j++)
        {
            var deviation = _synFreq[j] - j;
            var phaseAdvance = deviation * 2f * MathF.PI / Oversample + j * expected;
            _sumPhase[j] = WrapPhase(_sumPhase[j] + phaseAdvance);

            _re[j] = _synMag[j] * MathF.Cos(_sumPhase[j]);
            _im[j] = _synMag[j] * MathF.Sin(_sumPhase[j]);
        }

        for (var k = Half + 1; k < FrameSize; k++)
        {
            _re[k] = 0;
            _im[k] = 0;
        }

        _fft.Transform(_re, _im, true);

        // Analysis and synthesis are both Hann windowed; Hann squared at 4x overlap sums to 1.5.
        const float scale = 2f / (Half * Oversample * 1.5f);
        for (var k = 0; k < FrameSize; k++)
        {
            _outAccum[k] += scale * _window[k] * _re[k];
        }
    }

    private static float WrapPhase(float phase)
    {
        // Wrap into [-pi, pi].
        phase -= 2f * MathF.PI * MathF.Floor((phase + MathF.PI) / (2f * MathF.PI));
        return phase;
    }

    public void Reset()
    {
        Array.Clear(_inFifo);
        Array.Clear(_outFifo);
        Array.Clear(_outAccum);
        Array.Clear(_lastPhase);
        Array.Clear(_sumPhase);
        _rover = Latency;
    }
}

namespace Content.Shared.Voice.Dsp;

/// <summary>
/// Removes steady background noise (fans, hum, hiss, computer noise) from the microphone while keeping speech.
/// </summary>
/// <remarks>
/// <para>
/// Short-time spectral subtraction: the signal is split into overlapping 512-sample frames (sqrt-Hann analysis and
/// synthesis windows at 50% overlap, which reconstruct the input exactly when nothing is changed). For every frequency
/// bin a noise floor is tracked: it follows the signal down immediately and creeps up slowly, so it settles on the
/// steady part of the sound and ignores speech, which comes and goes. Each bin is then attenuated in proportion to how
/// much of it is noise, with a floor so it never goes fully silent, and smoothed over time to avoid "musical" noise.
/// </para>
/// <para>
/// Latency is one frame (512 samples, 10.7 ms at 48 kHz).
/// </para>
/// </remarks>
public sealed class NoiseSuppressor : IVoiceProcessor
{
    private const int FrameSize = 512;
    private const int Hop = FrameSize / 2;
    private const int Bins = FrameSize / 2 + 1;

    /// <summary>
    /// How fast the noise floor may rise per frame when the signal stays above it (about 3 dB per second).
    /// </summary>
    private const float FloorRise = 1.0035f;

    /// <summary>
    /// The minimum of smoothed noise power sits below its average; this brings the estimate back up to it.
    /// </summary>
    private const float FloorBias = 4.0f;

    /// <summary>
    /// Per-frame smoothing of the power used for the noise floor, so its minimum isn't a random dip.
    /// </summary>
    private const float PowerSmoothing = 0.8f;

    /// <summary>
    /// Frames used to learn the initial noise floor before suppressing anything.
    /// </summary>
    private const int WarmupFrames = 8;

    private readonly Fft _fft = new(FrameSize);
    private readonly float[] _window = new float[FrameSize];
    private readonly float[] _input = new float[FrameSize];
    private readonly float[] _output = new float[FrameSize];
    private readonly float[] _re = new float[FrameSize];
    private readonly float[] _im = new float[FrameSize];
    private readonly float[] _noise = new float[Bins];
    private readonly float[] _smoothed = new float[Bins];
    private readonly float[] _gain = new float[Bins];

    private float _strength;
    private float _overSubtraction;
    private float _minGain;
    private int _filled;
    private int _frames;

    /// <param name="strength">0 = off, 1 = as strong as it goes.</param>
    public NoiseSuppressor(float strength)
    {
        for (var i = 0; i < FrameSize; i++)
        {
            _window[i] = MathF.Sqrt(0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / FrameSize));
        }

        Strength = strength;
        Reset();
    }

    /// <summary>
    /// 0 = off, 1 = as strong as it goes. Stronger removes more noise but can make speech sound thinner.
    /// </summary>
    public float Strength
    {
        get => _strength;
        set
        {
            _strength = Math.Clamp(value, 0f, 1f);
            _overSubtraction = 1f + 2f * _strength;
            // Up to about 30 dB of noise reduction.
            _minGain = MathF.Pow(10f, -30f * _strength / 20f);
        }
    }

    public void Process(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            _input[Hop + _filled] = buffer[i];
            buffer[i] = _output[_filled];
            _filled++;

            if (_filled < Hop)
                continue;

            _filled = 0;
            ProcessFrame();
        }
    }

    private void ProcessFrame()
    {
        for (var i = 0; i < FrameSize; i++)
        {
            _re[i] = _input[i] * _window[i];
            _im[i] = 0f;
        }

        _fft.Transform(_re, _im, false);
        _frames++;

        for (var k = 0; k < Bins; k++)
        {
            var power = _re[k] * _re[k] + _im[k] * _im[k];

            // Noise floor: follows the smoothed power down right away, rises slowly.
            _smoothed[k] = _frames == 1 ? power : _smoothed[k] * PowerSmoothing + power * (1f - PowerSmoothing);
            if (_frames == 1 || _smoothed[k] < _noise[k])
                _noise[k] = _smoothed[k];
            else
                _noise[k] = Math.Min(_smoothed[k], _noise[k] * FloorRise);

            float gain;
            if (_frames <= WarmupFrames || power <= 1e-12f)
            {
                gain = 1f;
            }
            else
            {
                gain = Math.Max(_minGain, 1f - _overSubtraction * FloorBias * _noise[k] / power);
            }

            // Open quickly when speech starts, close more gently.
            _gain[k] = gain > _gain[k] ? gain : _gain[k] * 0.6f + gain * 0.4f;
        }

        for (var k = 0; k < Bins; k++)
        {
            var g = _gain[k];
            _re[k] *= g;
            _im[k] *= g;

            // Keep the spectrum conjugate-symmetric so the result stays real.
            if (k > 0 && k < Bins - 1)
            {
                _re[FrameSize - k] = _re[k];
                _im[FrameSize - k] = -_im[k];
            }
        }

        _fft.Transform(_re, _im, true);

        // Overlap-add (the inverse transform is unscaled): the first half completes the previous frame's second half.
        const float scale = 1f / FrameSize;
        for (var i = 0; i < Hop; i++)
        {
            _output[i] = _output[Hop + i] + _re[i] * _window[i] * scale;
            _output[Hop + i] = _re[Hop + i] * _window[Hop + i] * scale;
        }

        // Slide the input along by one hop.
        Array.Copy(_input, Hop, _input, 0, Hop);
    }

    public void Reset()
    {
        Array.Clear(_input);
        Array.Clear(_output);
        Array.Clear(_noise);
        Array.Clear(_smoothed);
        Array.Fill(_gain, 1f);
        _filled = 0;
        _frames = 0;
    }
}

namespace Content.Shared.Voice.Dsp;

/// <summary>
/// In-place iterative radix-2 complex FFT on split real/imaginary arrays.
/// </summary>
public sealed class Fft
{
    private readonly int _size;
    private readonly int[] _bitReverse;
    private readonly float[] _cos;
    private readonly float[] _sin;

    public int Size => _size;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentException("FFT size must be a power of two.", nameof(size));

        _size = size;
        _bitReverse = new int[size];
        var bits = 0;
        while (1 << bits < size)
            bits++;

        for (var i = 0; i < size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
            {
                if ((i & (1 << b)) != 0)
                    r |= 1 << (bits - 1 - b);
            }

            _bitReverse[i] = r;
        }

        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            _cos[i] = MathF.Cos(2 * MathF.PI * i / size);
            _sin[i] = MathF.Sin(2 * MathF.PI * i / size);
        }
    }

    /// <summary>
    /// Forward transform (inverse = false) or unscaled inverse transform (inverse = true).
    /// </summary>
    public void Transform(Span<float> re, Span<float> im, bool inverse)
    {
        var n = _size;

        for (var i = 0; i < n; i++)
        {
            var j = _bitReverse[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        var sign = inverse ? 1f : -1f;

        for (var len = 2; len <= n; len <<= 1)
        {
            var half = len >> 1;
            var step = n / len;
            for (var start = 0; start < n; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = sign * _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}

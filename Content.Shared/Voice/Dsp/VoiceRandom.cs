namespace Content.Shared.Voice.Dsp;

/// <summary>
/// Small, fast, deterministic PRNG (xorshift32) for audio noise generation.
/// </summary>
public struct VoiceRandom
{
    private uint _state;

    public VoiceRandom(int seed)
    {
        _state = (uint) seed * 2654435761u;
        if (_state == 0)
            _state = 0x9E3779B9u;
    }

    public uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>
    /// Uniform float in [0, 1).
    /// </summary>
    public float NextFloat()
    {
        return (NextUInt() >> 8) * (1f / 16777216f);
    }

    /// <summary>
    /// Uniform float in [-1, 1).
    /// </summary>
    public float NextBipolar()
    {
        return NextFloat() * 2f - 1f;
    }
}

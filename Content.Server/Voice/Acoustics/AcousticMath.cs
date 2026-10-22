namespace Content.Server.Voice.Acoustics;

public static class AcousticMath
{
    /// <summary>
    /// Muffling from two independent sources: each lets through (1 - muffle) of the "clarity".
    /// </summary>
    public static float CombineMuffle(float a, float b)
    {
        return 1f - (1f - Math.Clamp(a, 0f, 1f)) * (1f - Math.Clamp(b, 0f, 1f));
    }

    /// <summary>
    /// Volume at an (effective) distance: full up to <paramref name="fullRange"/>, then falling to 0 at
    /// <paramref name="maxRange"/> with the given curve exponent.
    /// </summary>
    public static float Falloff(float distance, float fullRange, float maxRange, float exponent)
    {
        if (distance <= fullRange)
            return 1f;

        if (distance >= maxRange || maxRange <= fullRange)
            return 0f;

        var t = (distance - fullRange) / (maxRange - fullRange);
        return MathF.Pow(1f - t, MathF.Max(0.1f, exponent));
    }

    /// <summary>
    /// How well sound carries at a pressure: 0 below <paramref name="min"/>, 1 at or above <paramref name="full"/>.
    /// </summary>
    public static float PressureFactor(float pressure, float min, float full)
    {
        if (pressure < min)
            return 0f;

        if (pressure >= full || full <= min)
            return 1f;

        return (pressure - min) / (full - min);
    }
}

using Content.Shared.Voice.Dsp;
using Robust.Shared.Serialization;

namespace Content.Shared.Voice;

/// <summary>
/// Procedural description of how a character's voice is coloured. This never models the real player's voice; it is a
/// set of DSP parameters applied on top of whatever the microphone picks up, so the same human can sound different
/// as different characters.
/// </summary>
/// <remarks>
/// Also used as an <i>offset</i>: species, equipment and disguises add their own profile to the character's.
/// </remarks>
[DataDefinition, Serializable, NetSerializable]
public partial struct VoiceProfile : IEquatable<VoiceProfile>
{
    /// <summary>
    /// Pitch offset in semitones.
    /// </summary>
    [DataField]
    public float Pitch;

    /// <summary>
    /// Formant (vocal tract size) offset in semitones. Positive sounds smaller, negative sounds larger.
    /// </summary>
    [DataField]
    public float Formant;

    /// <summary>
    /// Spectral tilt, -1 (dark/warm) to 1 (bright/thin).
    /// </summary>
    [DataField]
    public float Brightness;

    /// <summary>
    /// Gravelly irregularity, 0 to 1.
    /// </summary>
    [DataField]
    public float Roughness;

    /// <summary>
    /// Breath noise mixed into the voice, 0 to 1.
    /// </summary>
    [DataField]
    public float Breathiness;

    public static readonly VoiceProfile Neutral = default;

    public VoiceProfile(float pitch, float formant, float brightness = 0, float roughness = 0, float breathiness = 0)
    {
        Pitch = pitch;
        Formant = formant;
        Brightness = brightness;
        Roughness = roughness;
        Breathiness = breathiness;
    }

    public readonly bool IsNeutral => this == Neutral;

    public readonly bool NeedsShifter => MathF.Abs(Pitch) > 0.05f || MathF.Abs(Formant) > 0.05f;

    /// <summary>
    /// Stacks an offset profile on top of this one.
    /// </summary>
    public readonly VoiceProfile Add(VoiceProfile other)
    {
        return new VoiceProfile(
            Pitch + other.Pitch,
            Formant + other.Formant,
            Brightness + other.Brightness,
            Roughness + other.Roughness,
            Breathiness + other.Breathiness);
    }

    /// <summary>
    /// Clamps every parameter into a safe, intelligible range.
    /// </summary>
    public readonly VoiceProfile Clamped(float maxPitch, float maxFormant)
    {
        return new VoiceProfile(
            Clean(Pitch, maxPitch),
            Clean(Formant, maxFormant),
            Clean(Brightness, 1f),
            Math.Clamp(Clean(Roughness, 1f), 0f, 1f),
            Math.Clamp(Clean(Breathiness, 1f), 0f, 1f));
    }

    private static float Clean(float value, float max)
    {
        if (!float.IsFinite(value))
            return 0;

        return Math.Clamp(value, -max, max);
    }

    /// <summary>
    /// Deterministic pseudo-random profile used for voice disguises. The same seed always gives the same voice.
    /// </summary>
    /// <param name="strength">0-1 scale on how far from neutral the voice may be pushed.</param>
    public static VoiceProfile FromSeed(int seed, float strength)
    {
        var rng = new VoiceRandom(seed);
        // Bias pitch and formant away from zero so a disguise is always noticeably different.
        var pitchSign = rng.NextFloat() < 0.5f ? -1f : 1f;
        var formantSign = rng.NextFloat() < 0.5f ? -1f : 1f;
        return new VoiceProfile(
            pitchSign * (1.5f + rng.NextFloat() * 3f) * strength,
            formantSign * (1f + rng.NextFloat() * 2f) * strength,
            rng.NextBipolar() * 0.5f * strength,
            rng.NextFloat() * 0.35f * strength,
            rng.NextFloat() * 0.25f * strength);
    }

    /// <summary>
    /// Builds the DSP stages for this profile.
    /// </summary>
    public readonly void CreateProcessors(int sampleRate, int seed, List<IVoiceProcessor> output)
    {
        if (NeedsShifter)
            output.Add(SpectralShifter.FromSemitones(Pitch, Formant));

        if (MathF.Abs(Brightness) > 0.01f)
        {
            output.Add(new Biquad(BiquadType.HighShelf, sampleRate, 2500f, 0.7f, Brightness * 6f));
            output.Add(new Biquad(BiquadType.LowShelf, sampleRate, 300f, 0.7f, -Brightness * 3f));
        }

        if (Roughness > 0.01f)
        {
            output.Add(new TremoloProcessor(sampleRate, 45f, Roughness * 0.45f, 0.6f, seed));
            output.Add(new DistortionProcessor(1f + Roughness * 2.5f, Roughness * 0.5f, false));
        }

        if (Breathiness > 0.01f)
            output.Add(new NoiseProcessor(sampleRate, Breathiness * 0.12f, true, 2000f, 9000f, seed + 1));
    }

    public readonly bool Equals(VoiceProfile other)
    {
        return Pitch.Equals(other.Pitch)
               && Formant.Equals(other.Formant)
               && Brightness.Equals(other.Brightness)
               && Roughness.Equals(other.Roughness)
               && Breathiness.Equals(other.Breathiness);
    }

    public override readonly bool Equals(object? obj) => obj is VoiceProfile other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Pitch, Formant, Brightness, Roughness, Breathiness);

    public static bool operator ==(VoiceProfile a, VoiceProfile b) => a.Equals(b);

    public static bool operator !=(VoiceProfile a, VoiceProfile b) => !a.Equals(b);

    public override readonly string ToString()
    {
        return $"pitch {Pitch:0.##}, formant {Formant:0.##}, brightness {Brightness:0.##}, roughness {Roughness:0.##}, breath {Breathiness:0.##}";
    }
}

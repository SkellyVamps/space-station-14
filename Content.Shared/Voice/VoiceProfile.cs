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

    /// <summary>
    /// Nasal resonance ("talking through the nose"), 0 to 1.
    /// </summary>
    [DataField]
    public float Nasality;

    /// <summary>
    /// Shaky, wavering voice (pitch and volume wobble), 0 to 1.
    /// </summary>
    [DataField]
    public float Tremble;

    public static readonly VoiceProfile Neutral = default;

    public VoiceProfile(float pitch, float formant, float brightness = 0, float roughness = 0, float breathiness = 0,
        float nasality = 0, float tremble = 0)
    {
        Pitch = pitch;
        Formant = formant;
        Brightness = brightness;
        Roughness = roughness;
        Breathiness = breathiness;
        Nasality = nasality;
        Tremble = tremble;
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
            Breathiness + other.Breathiness,
            Nasality + other.Nasality,
            Tremble + other.Tremble);
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
            Math.Clamp(Clean(Breathiness, 1f), 0f, 1f),
            Math.Clamp(Clean(Nasality, 1f), 0f, 1f),
            Math.Clamp(Clean(Tremble, 1f), 0f, 1f));
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
            rng.NextFloat() * 0.25f * strength,
            rng.NextFloat() * 0.3f * strength);
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

        if (Nasality > 0.01f)
        {
            // A nose resonance around 1 kHz with less chest.
            output.Add(new Biquad(BiquadType.Peaking, sampleRate, 1100f, 1.4f, Nasality * 9f));
            output.Add(new Biquad(BiquadType.LowShelf, sampleRate, 250f, 0.7f, -Nasality * 5f));
        }

        if (Tremble > 0.01f)
        {
            // Pitch wobble (a few cents' vibrato from a modulated delay) plus a little volume wobble.
            output.Add(new ModulatedDelayProcessor(sampleRate, 2f, Tremble * 0.9f, 5.5f, 1f));
            output.Add(new TremoloProcessor(sampleRate, 5.5f, Tremble * 0.25f, 0.3f, seed + 2));
        }
    }

    private const string CodePrefix = "VOICE1:";

    /// <summary>
    /// A short text form of this voice that players can copy and paste, e.g. to share a voice with a friend.
    /// </summary>
    public readonly string ToCode()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        // An explicit array: a params call would compile to an inline array, which the client sandbox rejects.
        return CodePrefix + string.Join(";", new[]
        {
            Pitch.ToString("0.##", culture),
            Formant.ToString("0.##", culture),
            Brightness.ToString("0.##", culture),
            Roughness.ToString("0.##", culture),
            Breathiness.ToString("0.##", culture),
            Nasality.ToString("0.##", culture),
            Tremble.ToString("0.##", culture),
        });
    }

    /// <summary>
    /// Reads a voice written by <see cref="ToCode"/>. Missing trailing values (codes from before a parameter existed)
    /// count as 0. The result is not clamped; callers clamp to the server's limits.
    /// </summary>
    public static bool TryParseCode(string? code, out VoiceProfile profile)
    {
        profile = Neutral;
        code = code?.Trim();
        if (code == null || !code.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = code[CodePrefix.Length..].Split(';');
        if (parts.Length < 5 || parts.Length > 16)
            return false;

        // No stackalloc: shared code runs in the client sandbox, which rejects it.
        var values = new float[7];
        for (var i = 0; i < values.Length && i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out values[i])
                || !float.IsFinite(values[i]))
            {
                return false;
            }
        }

        profile = new VoiceProfile(values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
        return true;
    }

    public readonly bool Equals(VoiceProfile other)
    {
        return Pitch.Equals(other.Pitch)
               && Formant.Equals(other.Formant)
               && Brightness.Equals(other.Brightness)
               && Roughness.Equals(other.Roughness)
               && Breathiness.Equals(other.Breathiness)
               && Nasality.Equals(other.Nasality)
               && Tremble.Equals(other.Tremble);
    }

    public override readonly bool Equals(object? obj) => obj is VoiceProfile other && Equals(other);

    public override readonly int GetHashCode()
        => HashCode.Combine(Pitch, Formant, Brightness, Roughness, Breathiness, Nasality, Tremble);

    public static bool operator ==(VoiceProfile a, VoiceProfile b) => a.Equals(b);

    public static bool operator !=(VoiceProfile a, VoiceProfile b) => !a.Equals(b);

    public override readonly string ToString()
    {
        return $"pitch {Pitch:0.##}, formant {Formant:0.##}, brightness {Brightness:0.##}, roughness {Roughness:0.##}, breath {Breathiness:0.##}, nasal {Nasality:0.##}, tremble {Tremble:0.##}";
    }
}

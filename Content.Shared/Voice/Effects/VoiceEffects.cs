using Content.Shared.Voice.Dsp;

namespace Content.Shared.Voice.Effects;

/// <summary>
/// Constant volume change.
/// </summary>
public sealed partial class GainVoiceEffect : VoiceEffect
{
    [DataField]
    public float Db;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new GainProcessor(VoiceDsp.DbToLinear(Db));
    }
}

/// <summary>
/// Any biquad filter: lowPass, highPass, bandPass, notch, peaking, lowShelf, highShelf.
/// </summary>
public sealed partial class FilterVoiceEffect : VoiceEffect
{
    [DataField]
    public BiquadType Type = BiquadType.LowPass;

    [DataField]
    public float Frequency = 1000f;

    [DataField]
    public float Q = 0.7071f;

    /// <summary>
    /// Gain for peaking and shelf filters.
    /// </summary>
    [DataField]
    public float GainDb;

    /// <summary>
    /// Number of identical filters in series, for steeper slopes.
    /// </summary>
    [DataField]
    public int Stages = 1;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        var stages = Math.Clamp(Stages, 1, 4);
        if (stages == 1)
            return new Biquad(Type, sampleRate, Frequency, Q, GainDb);

        var list = new List<IVoiceProcessor>(stages);
        for (var i = 0; i < stages; i++)
        {
            list.Add(new Biquad(Type, sampleRate, Frequency, Q, GainDb));
        }

        return new ProcessorChain(list);
    }
}

/// <summary>
/// Dynamic range compression.
/// </summary>
public sealed partial class CompressorVoiceEffect : VoiceEffect
{
    [DataField]
    public float ThresholdDb = -24f;

    [DataField]
    public float Ratio = 4f;

    [DataField]
    public float AttackMs = 5f;

    [DataField]
    public float ReleaseMs = 80f;

    [DataField]
    public float MakeupDb = 6f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new CompressorProcessor(sampleRate, ThresholdDb, Ratio, AttackMs, ReleaseMs, MakeupDb);
    }
}

public sealed partial class DistortionVoiceEffect : VoiceEffect
{
    [DataField]
    public float Drive = 3f;

    [DataField]
    public float Mix = 0.5f;

    [DataField]
    public bool Hard;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new DistortionProcessor(Drive, Math.Clamp(Mix, 0f, 1f), Hard);
    }
}

public sealed partial class BitCrushVoiceEffect : VoiceEffect
{
    [DataField]
    public int Bits = 8;

    [DataField]
    public int Downsample = 2;

    [DataField]
    public float Mix = 1f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new BitCrushProcessor(Bits, Downsample, Math.Clamp(Mix, 0f, 1f));
    }
}

public sealed partial class RingModulatorVoiceEffect : VoiceEffect
{
    [DataField]
    public float Frequency = 40f;

    [DataField]
    public float Mix = 0.5f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new RingModulatorProcessor(sampleRate, Frequency, Math.Clamp(Mix, 0f, 1f));
    }
}

/// <summary>
/// Amplitude modulation; with jitter it reads as roughness or a failing speaker.
/// </summary>
public sealed partial class TremoloVoiceEffect : VoiceEffect
{
    [DataField]
    public float Rate = 6f;

    [DataField]
    public float Depth = 0.3f;

    [DataField]
    public float Jitter;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new TremoloProcessor(sampleRate, Rate, Math.Clamp(Depth, 0f, 1f), Math.Clamp(Jitter, 0f, 1f), seed);
    }
}

public sealed partial class EchoVoiceEffect : VoiceEffect
{
    [DataField]
    public float DelayMs = 120f;

    [DataField]
    public float Feedback = 0.3f;

    [DataField]
    public float Mix = 0.3f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new EchoProcessor(sampleRate, Math.Clamp(DelayMs, 1f, 1000f), Feedback, Math.Clamp(Mix, 0f, 1f));
    }
}

/// <summary>
/// Short resonant comb: metallic, enclosed, "inside a helmet" sound.
/// </summary>
public sealed partial class CombVoiceEffect : VoiceEffect
{
    [DataField]
    public float DelayMs = 3f;

    [DataField]
    public float Feedback = 0.5f;

    [DataField]
    public float Mix = 0.5f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new CombProcessor(sampleRate, Math.Clamp(DelayMs, 0.1f, 50f), Feedback, Math.Clamp(Mix, 0f, 1f));
    }
}

/// <summary>
/// Chorus (mix below 1) or vibrato (mix 1).
/// </summary>
public sealed partial class ModulationVoiceEffect : VoiceEffect
{
    [DataField]
    public float DelayMs = 8f;

    [DataField]
    public float DepthMs = 2f;

    [DataField]
    public float Rate = 0.8f;

    [DataField]
    public float Mix = 0.5f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new ModulatedDelayProcessor(sampleRate, Math.Clamp(DelayMs, 0.5f, 50f), Math.Clamp(DepthMs, 0f, 20f),
            Rate, Math.Clamp(Mix, 0f, 1f));
    }
}

/// <summary>
/// Hiss/static (constant) or breath (follows the voice).
/// </summary>
public sealed partial class NoiseVoiceEffect : VoiceEffect
{
    [DataField]
    public float Level = 0.02f;

    [DataField]
    public bool FollowSignal;

    [DataField]
    public float LowCut = 500f;

    [DataField]
    public float HighCut = 6000f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new NoiseProcessor(sampleRate, Math.Clamp(Level, 0f, 0.5f), FollowSignal, LowCut, HighCut, seed);
    }
}

public sealed partial class CrackleVoiceEffect : VoiceEffect
{
    [DataField]
    public float Rate = 4f;

    [DataField]
    public float Level = 0.1f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new CrackleProcessor(sampleRate, Math.Clamp(Rate, 0f, 200f), Math.Clamp(Level, 0f, 1f), seed);
    }
}

/// <summary>
/// Pitch and/or formant shift in semitones.
/// </summary>
public sealed partial class PitchShiftVoiceEffect : VoiceEffect
{
    [DataField]
    public float Pitch;

    [DataField]
    public float Formant;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return SpectralShifter.FromSemitones(Math.Clamp(Pitch, -12f, 12f), Math.Clamp(Formant, -12f, 12f));
    }
}

/// <summary>
/// Monotone robot voice.
/// </summary>
public sealed partial class VocoderVoiceEffect : VoiceEffect
{
    [DataField]
    public int Bands = 16;

    [DataField]
    public float CarrierFrequency = 110f;

    [DataField]
    public float Mix = 0.8f;

    /// <summary>
    /// Noise mixed into the carrier, keeps "s" and "t" sounds intelligible.
    /// </summary>
    [DataField]
    public float Noise = 0.15f;

    public override IVoiceProcessor CreateProcessor(int sampleRate, int seed)
    {
        return new VocoderProcessor(sampleRate, Bands, Math.Clamp(CarrierFrequency, 30f, 1000f),
            Math.Clamp(Mix, 0f, 1f), Math.Clamp(Noise, 0f, 1f));
    }
}

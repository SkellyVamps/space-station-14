using System;
using Content.Shared.Voice;
using Content.Shared.Voice.Dsp;
using Content.Shared.Voice.Effects;
using NUnit.Framework;

namespace Content.Tests.Shared.Voice;

[TestFixture, Parallelizable]
[TestOf(typeof(SpectralShifter))]
[TestOf(typeof(VoiceProfile))]
public sealed class VoiceDspTest
{
    private const int Rate = VoiceConstants.SampleRate;

    private static float[] Tone(float freq, int samples, float amplitude = 0.3f)
    {
        var data = new float[samples];
        for (var i = 0; i < samples; i++)
        {
            data[i] = amplitude * MathF.Sin(2 * MathF.PI * freq * i / Rate);
        }

        return data;
    }

    private static float Rms(ReadOnlySpan<float> data)
    {
        var sum = 0.0;
        foreach (var s in data)
        {
            sum += s * s;
        }

        return (float) Math.Sqrt(sum / data.Length);
    }

    /// <summary>
    /// Dominant frequency by zero crossings, good enough for a pure tone.
    /// </summary>
    private static float ZeroCrossingFrequency(ReadOnlySpan<float> data)
    {
        var crossings = 0;
        for (var i = 1; i < data.Length; i++)
        {
            if (data[i - 1] < 0 && data[i] >= 0)
                crossings++;
        }

        return crossings * (float) Rate / data.Length;
    }

    private static float[] Run(IVoiceProcessor processor, float[] input, int block = 960)
    {
        var output = (float[]) input.Clone();
        for (var offset = 0; offset < output.Length; offset += block)
        {
            processor.Process(output.AsSpan(offset, Math.Min(block, output.Length - offset)));
        }

        return output;
    }

    [Test]
    public void ShifterUnityPreservesSignal()
    {
        var input = Tone(220f, Rate);
        var output = Run(new SpectralShifter(1f, 1f), input);

        // Skip the latency/warm-up.
        var tail = output.AsSpan(Rate / 2);
        Assert.That(Rms(tail), Is.EqualTo(Rms(input)).Within(Rms(input) * 0.25f));
        Assert.That(ZeroCrossingFrequency(tail), Is.EqualTo(220f).Within(10f));
    }

    [Test]
    [TestCase(5f)]
    [TestCase(-5f)]
    [TestCase(12f)]
    public void ShifterChangesPitch(float semitones)
    {
        var input = Tone(220f, Rate);
        var output = Run(SpectralShifter.FromSemitones(semitones, semitones), input);
        var expected = 220f * MathF.Pow(2f, semitones / 12f);
        Assert.That(ZeroCrossingFrequency(output.AsSpan(Rate / 2)), Is.EqualTo(expected).Within(expected * 0.06f));
    }

    [Test]
    public void ProcessorsAreStableWithExtremeInput()
    {
        var effects = new VoiceEffect[]
        {
            new FilterVoiceEffect { Type = BiquadType.HighPass, Frequency = 300, Stages = 4 },
            new FilterVoiceEffect { Type = BiquadType.Peaking, Frequency = 1000, GainDb = 12 },
            new CompressorVoiceEffect(),
            new DistortionVoiceEffect { Drive = 50, Mix = 1 },
            new BitCrushVoiceEffect { Bits = 2, Downsample = 8 },
            new RingModulatorVoiceEffect(),
            new TremoloVoiceEffect { Jitter = 1 },
            new EchoVoiceEffect { Feedback = 2 },
            new CombVoiceEffect { Feedback = 2 },
            new ModulationVoiceEffect(),
            new NoiseVoiceEffect { FollowSignal = true },
            new CrackleVoiceEffect { Rate = 100 },
            new PitchShiftVoiceEffect { Pitch = 12, Formant = -12 },
            new VocoderVoiceEffect(),
            new GainVoiceEffect { Db = 6 },
        };

        var rng = new Random(7);
        var input = new float[Rate];
        for (var i = 0; i < input.Length; i++)
        {
            // Full-scale noise with occasional clipping spikes and NaN-free garbage.
            input[i] = (float) (rng.NextDouble() * 2 - 1) * (i % 4800 == 0 ? 50f : 1f);
        }

        foreach (var effect in effects)
        {
            var output = Run(effect.CreateProcessor(Rate, 1), input, 333);
            foreach (var s in output)
            {
                Assert.That(float.IsFinite(s), $"{effect.GetType().Name} produced a non-finite sample");
            }
        }
    }

    [Test]
    public void SilenceStaysQuiet()
    {
        // Effects shouldn't hiss loudly over silence (except deliberate static).
        var silence = new float[Rate];
        var chain = new ProcessorChain(new()
        {
            new Biquad(BiquadType.LowPass, Rate, 3000),
            new CompressorProcessor(Rate, -24, 4, 5, 80, 6),
            new NoiseProcessor(Rate, 0.05f, true, 2000, 8000, 1),
            SpectralShifter.FromSemitones(3, 1),
        });

        var output = Run(chain, silence);
        Assert.That(Rms(output), Is.LessThan(1e-4f));
    }

    [Test]
    public void ProfileClampingAndComposition()
    {
        var character = new VoiceProfile(4f, 2f, 0.5f, 0.2f, 0.1f);
        var species = new VoiceProfile(0f, -0.5f);
        var helmet = new VoiceProfile(0f, 0f, -0.3f);

        var combined = character.Add(species).Add(helmet);
        Assert.That(combined.Pitch, Is.EqualTo(4f));
        Assert.That(combined.Formant, Is.EqualTo(1.5f));
        Assert.That(combined.Brightness, Is.EqualTo(0.2f).Within(0.0001f));

        var wild = new VoiceProfile(40f, -40f, 9f, 9f, -9f).Clamped(6f, 4f);
        Assert.That(wild.Pitch, Is.EqualTo(6f));
        Assert.That(wild.Formant, Is.EqualTo(-4f));
        Assert.That(wild.Brightness, Is.EqualTo(1f));
        Assert.That(wild.Roughness, Is.EqualTo(1f));
        Assert.That(wild.Breathiness, Is.EqualTo(0f));

        var nan = new VoiceProfile(float.NaN, float.PositiveInfinity).Clamped(6f, 4f);
        Assert.That(nan.Pitch, Is.Zero);
        Assert.That(nan.Formant, Is.Zero);
    }

    [Test]
    public void DisguiseIsStableAndDistinct()
    {
        var a1 = VoiceProfile.FromSeed(1234, 1f);
        var a2 = VoiceProfile.FromSeed(1234, 1f);
        var b = VoiceProfile.FromSeed(98765, 1f);

        Assert.That(a1, Is.EqualTo(a2), "same seed, same voice every time");
        Assert.That(a1, Is.Not.EqualTo(b));
        Assert.That(MathF.Abs(a1.Pitch), Is.GreaterThanOrEqualTo(1.5f), "a disguise is always noticeable");
        Assert.That(MathF.Abs(a1.Pitch), Is.LessThanOrEqualTo(4.5f), "and still intelligible");
    }

    [Test]
    public void NeutralProfileHasNoProcessing()
    {
        var list = new System.Collections.Generic.List<IVoiceProcessor>();
        VoiceProfile.Neutral.CreateProcessors(Rate, 1, list);
        Assert.That(list, Is.Empty);
    }

    [Test]
    public void NoiseSuppressorOffIsTransparent()
    {
        // At strength 0 the overlapping windows reconstruct the input exactly, one frame (512 samples) late.
        var suppressor = new NoiseSuppressor(0f);
        var input = Tone(440, Rate / 2);
        var output = (float[]) input.Clone();
        suppressor.Process(output);

        for (var i = 1024; i < input.Length; i++)
        {
            Assert.That(output[i], Is.EqualTo(input[i - 512]).Within(1e-4f), $"sample {i}");
        }
    }

    [Test]
    public void NoiseSuppressorRemovesSteadyNoiseKeepsSpeech()
    {
        // Two seconds of steady hiss with a "word" (a tone) in the middle second.
        var random = new Random(1234);
        var length = Rate * 2;
        var noise = new float[length];
        for (var i = 0; i < length; i++)
        {
            noise[i] = (float) (random.NextDouble() * 2 - 1) * 0.02f;
        }

        var tone = Tone(300, length, 0.2f);
        var mixed = new float[length];
        for (var i = 0; i < length; i++)
        {
            var speaking = i >= Rate && i < Rate * 3 / 2;
            mixed[i] = noise[i] + (speaking ? tone[i] : 0f);
        }

        var output = (float[]) mixed.Clone();
        var suppressor = new NoiseSuppressor(1f);
        for (var offset = 0; offset < length; offset += 960)
        {
            suppressor.Process(output.AsSpan(offset, Math.Min(960, length - offset)));
        }

        // Noise only, after the floor has settled: at least 12 dB quieter.
        var noiseBefore = Rms(mixed.AsSpan(Rate / 2, Rate / 4));
        var noiseAfter = Rms(output.AsSpan(Rate / 2 + 512, Rate / 4));
        Assert.That(noiseAfter, Is.LessThan(noiseBefore / 4f));

        // The word keeps most of its level.
        var speechBefore = Rms(mixed.AsSpan(Rate + Rate / 8, Rate / 4));
        var speechAfter = Rms(output.AsSpan(Rate + Rate / 8 + 512, Rate / 4));
        Assert.That(speechAfter, Is.GreaterThan(speechBefore * 0.8f));
    }
}

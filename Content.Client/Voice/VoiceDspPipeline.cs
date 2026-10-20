using Content.Shared.Voice;

namespace Content.Client.Voice;

/// <summary>Allocation-free procedural pitch, low-pass, soft drive and ring modulation.</summary>
public sealed class VoiceDspPipeline
{
    private readonly float[] _delay = new float[4096];
    private int _write;
    private float _phase;
    private float _robotPhase;
    private float _filter;

    public void Process(Span<short> pcm, ActiveVoiceProfileComponent profile)
    {
        var ratio = Math.Clamp(profile.Pitch, 0.75f, 1.35f);
        var coefficient = 1 - MathF.Exp(-2 * MathF.PI * Math.Clamp(profile.LowPass, 800, 16000) / 48000);
        var robot = Math.Clamp(profile.Robot, 0, 0.6f);
        var drive = 1 + 3 * Math.Clamp(profile.Drive, 0, 1);
        for (var i = 0; i < pcm.Length; i++)
        {
            var sample = pcm[i] / 32768f;
            _delay[_write] = sample;
            if (MathF.Abs(ratio - 1) > 0.001f)
            {
                var other = (_phase + 0.5f) % 1;
                var weight = MathF.Sin(MathF.PI * _phase);
                weight *= weight;
                sample = Read(128 + _phase * 1024) * weight + Read(128 + other * 1024) * (1 - weight);
                _phase = (_phase + (1 - ratio) / 1024 + 1) % 1;
            }
            _write = (_write + 1) & 4095;
            _filter += coefficient * (sample - _filter);
            sample = _filter;
            sample *= 1 - robot + robot * MathF.Sin(_robotPhase);
            _robotPhase = (_robotPhase + 2 * MathF.PI * 75 / 48000) % (2 * MathF.PI);
            if (drive > 1)
                sample = MathF.Tanh(sample * drive) / MathF.Tanh(drive);
            pcm[i] = (short) Math.Clamp(sample * 32767, short.MinValue, short.MaxValue);
        }
    }

    private float Read(float delay)
    {
        var position = (_write - delay + 4096) % 4096;
        var first = (int) position;
        var fraction = position - first;
        return _delay[first] * (1 - fraction) + _delay[(first + 1) & 4095] * fraction;
    }
}

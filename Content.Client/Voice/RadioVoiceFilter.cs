namespace Content.Client.Voice;

/// <summary>Intelligible receive-side radio coloration; never duplicates Opus encoding.</summary>
public sealed class RadioVoiceFilter
{
    private float _low;
    private float _bass;
    private uint _noise = 0x4137;
    public void Process(Span<float> samples)
    {
        const float highCoefficient = 0.037f; // approximately 300 Hz at 48 kHz
        const float lowCoefficient = 0.342f; // approximately 3200 Hz
        for (var i = 0; i < samples.Length; i++)
        {
            _bass += highCoefficient * (samples[i] - _bass);
            _low += lowCoefficient * (samples[i] - _bass - _low);
            _noise = unchecked(_noise * 1664525 + 1013904223);
            var noise = ((_noise >> 16) / 32768f - 1) * 0.001f;
            samples[i] = MathF.Tanh(_low * 1.4f) / 1.4f + noise;
        }
    }
}

using Content.Shared.Voice.Dsp;

namespace Content.Shared.Voice.Effects;

/// <summary>
/// Data describing one stage of a voice effect chain. Each concrete effect knows how to build its own processor,
/// so adding a new effect is just adding a new subclass.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class VoiceEffect
{
    /// <param name="sampleRate">Sample rate of the audio that will be processed.</param>
    /// <param name="seed">Stable seed for effects with randomness.</param>
    public abstract IVoiceProcessor CreateProcessor(int sampleRate, int seed);
}

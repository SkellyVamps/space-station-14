using Content.Shared.Voice.Dsp;
using Robust.Shared.Prototypes;

namespace Content.Shared.Voice;

/// <summary>
/// PROCESSING layer helper: turns a profile plus a list of filter prototypes into one processing chain.
/// </summary>
public static class VoiceChainBuilder
{
    /// <param name="seed">Stable per-speaker seed for random-ish effects.</param>
    public static ProcessorChain Build(
        IPrototypeManager proto,
        VoiceProfile profile,
        IEnumerable<ProtoId<VoiceFilterPrototype>> filters,
        int seed,
        int sampleRate = VoiceConstants.SampleRate)
    {
        var processors = new List<IVoiceProcessor>();
        profile.CreateProcessors(sampleRate, seed, processors);

        var i = 0;
        foreach (var filterId in filters)
        {
            if (!proto.TryIndex(filterId, out var filter))
                continue;

            AddFilter(processors, filter, sampleRate, seed + 101 * ++i);
        }

        return new ProcessorChain(processors);
    }

    public static ProcessorChain Build(VoiceFilterPrototype filter, int seed, int sampleRate = VoiceConstants.SampleRate)
    {
        var processors = new List<IVoiceProcessor>();
        AddFilter(processors, filter, sampleRate, seed);
        return new ProcessorChain(processors);
    }

    private static void AddFilter(List<IVoiceProcessor> processors, VoiceFilterPrototype filter, int sampleRate, int seed)
    {
        var j = 0;
        foreach (var effect in filter.Effects)
        {
            processors.Add(effect.CreateProcessor(sampleRate, seed + j++));
        }
    }
}

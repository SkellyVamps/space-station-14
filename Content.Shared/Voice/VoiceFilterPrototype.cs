using Content.Shared.Voice.Effects;
using Robust.Shared.Prototypes;

namespace Content.Shared.Voice;

/// <summary>
/// A reusable chain of voice effects, e.g. "gas mask", "radio", "synthetic larynx".
/// Species, equipment, status effects and radio playback all refer to these.
/// </summary>
[Prototype]
public sealed partial class VoiceFilterPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Effects, applied in order.
    /// </summary>
    [DataField(required: true)]
    public List<VoiceEffect> Effects = new();

    /// <summary>
    /// Ordering when several filters stack on one speaker; lower runs first.
    /// Character/species colouring should come before "something in front of the mouth" effects like masks.
    /// </summary>
    [DataField]
    public int Order;
}

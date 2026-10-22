using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared.Voice.Components;

/// <summary>
/// Alters the voice of whoever has it. Works innately (put it on a species' mob prototype) and on equipment (gas
/// masks, helmets, voice boxes): worn items apply it when equipped in a slot matching <see cref="Slots"/>.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceModifierComponent : Component
{
    /// <summary>
    /// Effect chains added to the voice.
    /// </summary>
    [DataField]
    public List<ProtoId<VoiceFilterPrototype>> Filters = new();

    /// <summary>
    /// Offset added to the character's procedural profile.
    /// </summary>
    [DataField]
    public VoiceProfile Profile;

    /// <summary>
    /// For equipment: slots in which the modifier applies. Default: anything worn except pockets.
    /// </summary>
    [DataField]
    public SlotFlags Slots = SlotFlags.WITHOUT_POCKET;

    /// <summary>
    /// For masks: ignore this modifier while the mask is pulled down.
    /// </summary>
    [DataField]
    public bool RequireMaskUp = true;

    [DataField]
    public bool Enabled = true;
}

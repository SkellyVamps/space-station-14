using Content.Shared.Inventory;

namespace Content.Shared.Voice.Components;

/// <summary>
/// Changes how well its holder hears local voice: earmuffs, sealed helmets, hearing damage, etc.
/// Works innately or on worn equipment.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceHearingModifierComponent : Component
{
    [DataField]
    public float GainMultiplier = 1f;

    /// <summary>
    /// Muffling 0-1 added to everything heard.
    /// </summary>
    [DataField]
    public float Muffle;

    /// <summary>
    /// Cannot hear local voice at all. Radio through a headset still works.
    /// </summary>
    [DataField]
    public bool Deafens;

    [DataField]
    public SlotFlags Slots = SlotFlags.WITHOUT_POCKET;
}

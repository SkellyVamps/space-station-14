namespace Content.Shared.Voice.Components;

/// <summary>
/// Equipment that conceals the wearer's real voice behind a stable, pseudo-random procedural voice.
/// Combined with a voice mask, the disguise is active whenever the voice mask is active.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceDisguiseComponent : Component
{
    /// <summary>
    /// Seed of the disguise voice. Randomised when the item is created, so each disguise item sounds different but
    /// always the same.
    /// </summary>
    [DataField]
    public int Seed;

    /// <summary>
    /// 0-1, how far the disguise may move the voice.
    /// </summary>
    [DataField]
    public float Strength = 1f;

    /// <summary>
    /// If true the disguise replaces the wearer's own profile instead of adding to it, so two different wearers of
    /// the same item sound alike. This is what makes the disguise actually hide identity.
    /// </summary>
    [DataField]
    public bool ReplaceProfile = true;

    /// <summary>
    /// Filters added while disguised (e.g. a slight synthetic sheen).
    /// </summary>
    [DataField]
    public List<Robust.Shared.Prototypes.ProtoId<VoiceFilterPrototype>> Filters = new();

    [DataField]
    public bool Enabled = true;
}

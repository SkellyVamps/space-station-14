using Robust.Shared.Prototypes;

namespace Content.Shared.Voice;

/// <summary>
/// A ready-made character voice players can pick in the character editor as a starting point.
/// </summary>
[Prototype]
public sealed partial class VoicePresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public VoiceProfile Profile;

    /// <summary>
    /// Position in the preset list, lowest first.
    /// </summary>
    [DataField]
    public int Order;
}

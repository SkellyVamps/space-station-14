using Robust.Shared.Prototypes;

namespace Content.Shared.Voice.Components;

/// <summary>
/// Advanced voice changer that can record a character's procedural voice and play it back as the wearer's voice.
/// Impersonation activates when the item's voice mask name matches a recorded sample's name.
/// This copies a character's in-game procedural profile; it never touches real player audio.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceImpersonatorComponent : Component
{
    /// <summary>
    /// Recorded voices, by the name they were recorded under.
    /// </summary>
    [DataField]
    public Dictionary<string, VoiceSample> Samples = new();

    [DataField]
    public int MaxSamples = 3;

    /// <summary>
    /// How long it takes to record someone's voice.
    /// </summary>
    [DataField]
    public TimeSpan SampleTime = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How close the target must be while recording.
    /// </summary>
    [DataField]
    public float SampleRange = 2f;
}

/// <summary>
/// A recorded character voice.
/// </summary>
[DataDefinition]
public sealed partial class VoiceSample
{
    [DataField]
    public VoiceProfile Profile;

    [DataField]
    public List<ProtoId<VoiceFilterPrototype>> Filters = new();
}

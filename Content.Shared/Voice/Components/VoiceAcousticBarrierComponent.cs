namespace Content.Shared.Voice.Components;

/// <summary>
/// How an air-blocking structure (wall, door, window...) affects voice passing through its tile while it blocks air.
/// Structures that block air without this component use the server's default barrier settings.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceAcousticBarrierComponent : Component
{
    /// <summary>
    /// Extra effective distance, in tiles, for sound passing through.
    /// </summary>
    [DataField]
    public float Penalty = 4f;

    /// <summary>
    /// Muffling 0-1 applied to sound passing through.
    /// </summary>
    [DataField]
    public float Muffle = 0.6f;

    /// <summary>
    /// Blocks voice entirely.
    /// </summary>
    [DataField]
    public bool Blocks;
}

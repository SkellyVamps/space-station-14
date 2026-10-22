namespace Content.Shared.Voice.Components;

/// <summary>
/// How voice escapes from (or gets into) an entity that contains people, e.g. lockers, crates, disposal units, mechs.
/// Containers without this component use the server's default container settings.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceContainerAcousticsComponent : Component
{
    /// <summary>
    /// Extra effective distance in tiles.
    /// </summary>
    [DataField]
    public float Penalty = 1.5f;

    /// <summary>
    /// Muffling 0-1.
    /// </summary>
    [DataField]
    public float Muffle = 0.6f;

    /// <summary>
    /// Blocks voice entirely (airtight sealed containers).
    /// </summary>
    [DataField]
    public bool Blocks;

    /// <summary>
    /// Only applies while closed (for entity storage like lockers). Open lockers don't muffle.
    /// </summary>
    [DataField]
    public bool OnlyWhenClosed = true;
}

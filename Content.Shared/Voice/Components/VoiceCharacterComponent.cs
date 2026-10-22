namespace Content.Shared.Voice.Components;

/// <summary>
/// The character's own procedural voice, from their character profile. Added when a player character spawns.
/// </summary>
[RegisterComponent]
public sealed partial class VoiceCharacterComponent : Component
{
    [DataField]
    public VoiceProfile Profile;
}

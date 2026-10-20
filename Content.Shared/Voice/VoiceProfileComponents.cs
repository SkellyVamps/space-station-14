using Robust.Shared.GameStates;

namespace Content.Shared.Voice;

/// <summary>Character/species base profile. Attach to a mob prototype or customize on the server.</summary>
[RegisterComponent]
public sealed partial class VoiceProfileComponent : Component
{
    [DataField] public float Pitch = 1;
    [DataField] public float LowPass = 12000;
    [DataField] public float Drive;
    [DataField] public float Robot;
}

/// <summary>Reusable equipped/status modifier. Only worn mask/head/outer-clothing slots are considered.</summary>
[RegisterComponent]
public sealed partial class VoiceModifierComponent : Component
{
    [DataField] public float Pitch = 1;
    [DataField] public float LowPass = 12000;
    [DataField] public float Drive;
    [DataField] public float Robot;
    [DataField] public bool Enabled = true;
}

/// <summary>Server-composed profile. Clients wait for this before opening their microphone.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveVoiceProfileComponent : Component
{
    [DataField, AutoNetworkedField] public uint Revision;
    [DataField, AutoNetworkedField] public float Pitch = 1;
    [DataField, AutoNetworkedField] public float LowPass = 12000;
    [DataField, AutoNetworkedField] public float Drive;
    [DataField, AutoNetworkedField] public float Robot;
}

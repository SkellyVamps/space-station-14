namespace Content.Shared.Voice;

/// <summary>Prototype override for doors, windows, blast shutters, and sealed containers.</summary>
[RegisterComponent]
public sealed partial class VoiceAcousticBarrierComponent : Component
{
    [DataField] public bool Blocks;
    [DataField] public float ExtraDistance = 3;
}

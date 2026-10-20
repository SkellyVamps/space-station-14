using Robust.Shared.Prototypes;

namespace Content.Shared.Voice;

/// <summary>Composable procedural layer. Species{SpeciesId} and Voice{EmoteVoiceId} are resolved by the server.</summary>
[Prototype]
public sealed partial class VoiceFilterPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public float Pitch = 1;
    [DataField] public float LowPass = 12000;
    [DataField] public float Drive;
    [DataField] public float Robot;
}

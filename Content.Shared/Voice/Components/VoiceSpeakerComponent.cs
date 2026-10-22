using Content.Shared.Radio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Voice.Components;

/// <summary>
/// Server-authoritative voice state for a player-controlled entity, sent only to its controlling player.
/// The client uses it to build the effect chain it applies to its own microphone before encoding, and to know which
/// radio channels it can talk on.
/// </summary>
/// <remarks>
/// A modified client can of course ignore this; the server only trusts it for things the client cannot fake
/// (routing, audibility, permissions).
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VoiceSpeakerComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>
    /// Final procedural profile: character profile + species + equipment/disguise offsets (or an impersonated
    /// character's profile).
    /// </summary>
    [DataField, AutoNetworkedField]
    public VoiceProfile Profile;

    /// <summary>
    /// Filters to apply after the profile, already sorted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<VoiceFilterPrototype>> Filters = new();

    /// <summary>
    /// Whether the entity can currently speak at all (alive, not muted, not gagged...).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanSpeak = true;

    /// <summary>
    /// Radio channels the entity can currently transmit on.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<ProtoId<RadioChannelPrototype>> RadioChannels = new();

    /// <summary>
    /// Channel used by the radio push-to-talk key when the player hasn't picked one.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<RadioChannelPrototype>? DefaultRadioChannel;

    /// <summary>
    /// Speaking as a ghost: only other ghosts (and possibly the living, depending on server rules) hear it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Ghost;

    /// <summary>
    /// Stable seed for effects with randomness, so a character's static/crackle is consistent.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Seed;
}

using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Voice;

/// <summary>
/// Raised on a speaker (and relayed to worn equipment) to collect everything that modifies their voice.
/// Status effects, implants or traits can subscribe to add their own filters.
/// </summary>
public sealed class VoiceModifiersEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    /// <summary>
    /// Only collect the body's own voice (species, implants), ignoring equipment, disguises and status.
    /// Used when recording someone's voice for impersonation.
    /// </summary>
    public bool InnateOnly;

    /// <summary>
    /// Summed offsets added to the base profile.
    /// </summary>
    public VoiceProfile ProfileOffset;

    /// <summary>
    /// If set, replaces the character's own base profile (disguises, impersonation).
    /// Highest priority wins.
    /// </summary>
    public VoiceProfile? ProfileOverride;

    public int OverridePriority = int.MinValue;

    /// <summary>
    /// Filters from the speaker's own body (species, implants, status effects). Replaced by
    /// <see cref="FilterOverride"/> when impersonating someone else.
    /// </summary>
    public List<ProtoId<VoiceFilterPrototype>> InnateFilters = new();

    /// <summary>
    /// Filters from things in front of the mouth: masks, helmets, voice boxes. Always applied.
    /// </summary>
    public List<ProtoId<VoiceFilterPrototype>> Filters = new();

    /// <summary>
    /// Set by an override that also replaces innate filters (impersonating another species' voice).
    /// </summary>
    public List<ProtoId<VoiceFilterPrototype>>? FilterOverride;

    public void Override(VoiceProfile profile, int priority, List<ProtoId<VoiceFilterPrototype>>? filters = null)
    {
        if (priority < OverridePriority)
            return;

        ProfileOverride = profile;
        OverridePriority = priority;
        FilterOverride = filters;
    }
}

/// <summary>
/// Raised on a listener (and relayed to worn equipment) to find out how well they hear local voice.
/// </summary>
public sealed class VoiceHearingEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    public float Gain = 1f;

    /// <summary>
    /// Combined muffling, 0-1. Use <see cref="AddMuffle"/>.
    /// </summary>
    public float Muffle;

    public bool Deaf;

    public void AddMuffle(float amount)
    {
        Muffle = 1f - (1f - Muffle) * (1f - Math.Clamp(amount, 0f, 1f));
    }
}

/// <summary>
/// Client asks the server to locally mute or change the volume of the player controlling an entity. The server keeps
/// this per account, so it persists across that player's bodies, and simply stops sending their voice when muted.
/// The client never learns who the player is.
/// </summary>
[Serializable, NetSerializable]
public sealed class VoiceSetPlayerVolumeEvent(NetEntity target, float volume) : EntityEventArgs
{
    public NetEntity Target = target;

    /// <summary>
    /// 0 mutes; 1 is normal; up to 2.
    /// </summary>
    public float Volume = volume;
}

/// <summary>
/// Server tells the client its per-player volume settings changed for an entity, so UI can reflect it.
/// </summary>
[Serializable, NetSerializable]
public sealed class VoicePlayerVolumeChangedEvent(NetEntity target, float volume) : EntityEventArgs
{
    public NetEntity Target = target;
    public float Volume = volume;
}

/// <summary>
/// Server tells a client whether it is voice muted by an admin, so the UI can explain why it can't talk.
/// </summary>
[Serializable, NetSerializable]
public sealed class VoiceAdminMuteStateEvent(bool muted, TimeSpan? expires) : EntityEventArgs
{
    public bool Muted = muted;
    public TimeSpan? Expires = expires;
}

/// <summary>
/// Recording someone's voice into a <see cref="Components.VoiceImpersonatorComponent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class VoiceSampleDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Client tells the server which radio channels it doesn't want to hear voice on. The server stops sending radio
/// voice on those channels to this player. Text radio is unaffected.
/// </summary>
[Serializable, NetSerializable]
public sealed class VoiceSetRadioMutedChannelsEvent(List<string> channels) : EntityEventArgs
{
    public List<string> Channels = channels;
}

/// <summary>
/// Raised on a speaker to find out whether anyone can understand what they say. When not, their voice is never sent
/// to anyone and their creature's speech sound plays instead, matching what text chat does with them.
/// </summary>
[ByRefEvent]
public record struct VoiceIntelligibilityEvent(bool Unintelligible = false);

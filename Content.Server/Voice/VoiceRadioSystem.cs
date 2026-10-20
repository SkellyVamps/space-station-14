using Content.Server.Radio.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Voice;

/// <summary>Headset voice permissions. No receiving channel or radio entity is trusted from the client.</summary>
public sealed partial class VoiceRadioSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public bool TryGetTransmitter(EntityUid speaker, string requested, out EntityUid transmitter,
        out RadioChannelPrototype channel)
    {
        channel = default!;
        transmitter = default;
        if (!TryGetHeadset(speaker, out transmitter) ||
            !TryComp<EncryptionKeyHolderComponent>(transmitter, out var keys))
            return false;
        var id = requested.Length == 0 ? keys.DefaultChannel : requested;
        if (id == null || !keys.Channels.Contains(id) ||
            !_prototypes.TryIndex<RadioChannelPrototype>(id, out var resolved))
            return false;
        channel = resolved;
        return true;
    }

    public bool CanReceive(EntityUid listener, EntityUid transmitter, RadioChannelPrototype channel) =>
        TryGetHeadset(listener, out var receiver) && _radio.CanRelayVoice(transmitter, receiver, channel);

    private bool TryGetHeadset(EntityUid wearer, out EntityUid headset)
    {
        var slots = _inventory.GetSlotEnumerator(wearer, SlotFlags.EARS);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } item || !TryComp<HeadsetComponent>(item, out var component) ||
                !component.Enabled || !component.IsEquipped || !HasComp<ActiveRadioComponent>(item))
                continue;
            headset = item;
            return true;
        }
        headset = default;
        return false;
    }
}

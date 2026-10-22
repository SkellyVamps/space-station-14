using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Content.Shared.VoiceMask;
using Robust.Shared.Random;

namespace Content.Server.Voice;

/// <summary>
/// Gameplay sources of voice modification: species (innate <see cref="VoiceModifierComponent"/>), worn equipment,
/// disguises and impersonation, and hearing modifiers.
/// </summary>
/// <remarks>
/// Nothing here knows about specific items; everything is driven by components on prototypes.
/// </remarks>
public sealed partial class VoiceModifierSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;

    /// <summary>
    /// Override priorities: an impersonation beats a generic disguise.
    /// </summary>
    public const int DisguisePriority = 10;
    public const int ImpersonationPriority = 20;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VoiceModifierComponent, VoiceModifiersEvent>(OnInnateModifier);
        SubscribeLocalEvent<VoiceModifierComponent, InventoryRelayedEvent<VoiceModifiersEvent>>(OnWornModifier);

        SubscribeLocalEvent<VoiceDisguiseComponent, MapInitEvent>(OnDisguiseMapInit);
        SubscribeLocalEvent<VoiceDisguiseComponent, VoiceModifiersEvent>((uid, comp, ev) => ApplyDisguise(uid, comp, ev));
        SubscribeLocalEvent<VoiceDisguiseComponent, InventoryRelayedEvent<VoiceModifiersEvent>>((uid, comp, ev) => ApplyDisguise(uid, comp, ev.Args));

        SubscribeLocalEvent<VoiceImpersonatorComponent, VoiceModifiersEvent>((uid, comp, ev) => ApplyImpersonation(uid, comp, ev));
        SubscribeLocalEvent<VoiceImpersonatorComponent, InventoryRelayedEvent<VoiceModifiersEvent>>((uid, comp, ev) => ApplyImpersonation(uid, comp, ev.Args));

        SubscribeLocalEvent<VoiceHearingModifierComponent, VoiceHearingEvent>((_, comp, ev) => ApplyHearing(comp, ev));
        SubscribeLocalEvent<VoiceHearingModifierComponent, InventoryRelayedEvent<VoiceHearingEvent>>(OnWornHearing);
    }

    private void OnInnateModifier(Entity<VoiceModifierComponent> ent, ref VoiceModifiersEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        args.ProfileOffset = args.ProfileOffset.Add(ent.Comp.Profile);
        args.InnateFilters.AddRange(ent.Comp.Filters);
    }

    private void OnWornModifier(Entity<VoiceModifierComponent> ent, ref InventoryRelayedEvent<VoiceModifiersEvent> args)
    {
        if (!ent.Comp.Enabled || !InActiveSlot(ent, ent.Comp.Slots))
            return;

        if (ent.Comp.RequireMaskUp && TryComp<MaskComponent>(ent, out var mask) && mask.IsToggled)
            return;

        args.Args.ProfileOffset = args.Args.ProfileOffset.Add(ent.Comp.Profile);
        args.Args.Filters.AddRange(ent.Comp.Filters);
    }

    private bool InActiveSlot(EntityUid item, SlotFlags slots)
    {
        return _inventory.TryGetContainingSlot(item, out var slot) && (slot.SlotFlags & slots) != 0;
    }

    private void OnDisguiseMapInit(Entity<VoiceDisguiseComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Seed == 0)
            ent.Comp.Seed = _random.Next(1, int.MaxValue);
    }

    /// <summary>
    /// A disguise is active when enabled and, if the item is also a voice mask, while the voice mask is active.
    /// </summary>
    private bool DisguiseActive(EntityUid uid, bool enabled)
    {
        if (!enabled)
            return false;

        if (TryComp<VoiceMaskComponent>(uid, out var voiceMask) && !voiceMask.Active)
            return false;

        if (TryComp<MaskComponent>(uid, out var mask) && mask.IsToggled)
            return false;

        return true;
    }

    private void ApplyDisguise(EntityUid uid, VoiceDisguiseComponent comp, VoiceModifiersEvent ev)
    {
        if (!DisguiseActive(uid, comp.Enabled))
            return;

        var disguise = VoiceProfile.FromSeed(comp.Seed, comp.Strength);
        if (comp.ReplaceProfile)
        {
            // Also hide species colouring, otherwise a lizard in a disguise is still obviously a lizard.
            ev.Override(disguise, DisguisePriority, new List<Robust.Shared.Prototypes.ProtoId<VoiceFilterPrototype>>());
        }
        else
        {
            ev.ProfileOffset = ev.ProfileOffset.Add(disguise);
        }

        ev.Filters.AddRange(comp.Filters);
    }

    private void ApplyImpersonation(EntityUid uid, VoiceImpersonatorComponent comp, VoiceModifiersEvent ev)
    {
        if (!TryComp<VoiceMaskComponent>(uid, out var voiceMask)
            || !DisguiseActive(uid, true)
            || voiceMask.VoiceMaskName is not { } name
            || !comp.Samples.TryGetValue(name, out var sample))
        {
            return;
        }

        ev.Override(sample.Profile, ImpersonationPriority, new(sample.Filters));
    }

    private void OnWornHearing(Entity<VoiceHearingModifierComponent> ent, ref InventoryRelayedEvent<VoiceHearingEvent> args)
    {
        if (!InActiveSlot(ent, ent.Comp.Slots))
            return;

        ApplyHearing(ent.Comp, args.Args);
    }

    private static void ApplyHearing(VoiceHearingModifierComponent comp, VoiceHearingEvent ev)
    {
        if (comp.Deafens)
            ev.Deaf = true;

        ev.Gain *= comp.GainMultiplier;
        ev.AddMuffle(comp.Muffle);
    }
}

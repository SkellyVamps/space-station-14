using Content.Shared.Inventory;
using Content.Shared.Humanoid;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Content.Shared.Clothing.Components;
using Content.Shared.Voice;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>Composes server gameplay state; the microphone client is not the equipment authority.</summary>
public sealed partial class VoiceProfileSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IGameTiming _timing = default!;
    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_config.GetCVar(CCVars.VoiceEnabled) || _timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromMilliseconds(100);
        foreach (var session in _players.Sessions)
            if (session.AttachedEntity is { } entity && !Deleted(entity))
                Refresh(entity);
    }

    /// <summary>Also called on ingress so equipment changes cannot authorize an obsolete normal-client profile.</summary>
    public uint Refresh(EntityUid entity)
    {
        var pitch = 1f;
        var lowPass = 12000f;
        var drive = 0f;
        var robot = 0f;
        if (TryComp<VoiceProfileComponent>(entity, out var character))
        {
            pitch = character.Pitch;
            lowPass = character.LowPass;
            drive = character.Drive;
            robot = character.Robot;
        }
        if (TryComp<HumanoidProfileComponent>(entity, out var humanoid))
        {
            ApplyPreset("Species" + humanoid.Species.Id);
            ApplyPreset("Voice" + humanoid.Voice.Id);
        }
        if (TryComp<VoiceModifierComponent>(entity, out var status))
            Apply(status);
        var slots = _inventory.GetSlotEnumerator(entity, SlotFlags.MASK | SlotFlags.HEAD | SlotFlags.OUTERCLOTHING);
        while (slots.MoveNext(out var slot))
            if (slot.ContainedEntity is { } item &&
                !(TryComp<MaskComponent>(item, out var mask) && mask.IsToggled) &&
                TryComp<VoiceModifierComponent>(item, out var modifier))
                Apply(modifier);
        pitch = Safe(pitch, 0.75f, 1.35f, 1);
        lowPass = Safe(lowPass, 800, 16000, 12000);
        drive = Safe(drive, 0, 1, 0);
        robot = Safe(robot, 0, 0.6f, 0);
        var active = EnsureComp<ActiveVoiceProfileComponent>(entity);
        if (active.Revision == 0 || active.Pitch != pitch || active.LowPass != lowPass ||
            active.Drive != drive || active.Robot != robot)
        {
            active.Pitch = pitch;
            active.LowPass = lowPass;
            active.Drive = drive;
            active.Robot = robot;
            active.Revision++;
            if (active.Revision == 0)
                active.Revision = 1;
            Dirty(entity, active);
        }
        return active.Revision;

        void ApplyPreset(string id)
        {
            if (!_prototypes.TryIndex<VoiceFilterPrototype>(id, out var preset))
                return;
            pitch *= preset.Pitch;
            lowPass = Math.Min(lowPass, preset.LowPass);
            drive += preset.Drive;
            robot += preset.Robot;
        }

        void Apply(VoiceModifierComponent modifier)
        {
            if (!modifier.Enabled)
                return;
            pitch *= modifier.Pitch;
            lowPass = Math.Min(lowPass, modifier.LowPass);
            drive += modifier.Drive;
            robot += modifier.Robot;
        }
    }

    private static float Safe(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

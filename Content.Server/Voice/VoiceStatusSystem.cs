using Content.Server.Body.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Drowsiness;
using Content.Shared.Drunk;
using Content.Shared.Jittering;
using Content.Shared.StatusEffectNew;
using Content.Shared.Temperature.Components;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;

namespace Content.Server.Voice;

/// <summary>
/// How the state of someone's body changes their voice: drunk, shaking, out of breath, suffocating, freezing,
/// drowsy. Applied on top of the character, species and equipment, and refreshed with them a couple of times a second.
/// </summary>
public sealed partial class VoiceStatusSystem : EntitySystem
{
    [Dependency] private StatusEffectsSystem _status = default!;

    /// <summary>
    /// Shivering starts this far (kelvin) above the temperature where cold starts to hurt.
    /// </summary>
    private const float ShiverMargin = 25f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VoiceSpeakerComponent, VoiceModifiersEvent>(OnModifiers);
    }

    private void OnModifiers(EntityUid uid, VoiceSpeakerComponent component, VoiceModifiersEvent args)
    {
        // Recording someone's voice captures their voice, not their hangover.
        if (args.InnateOnly)
            return;

        var offset = VoiceProfile.Neutral;

        // Drunk: slurred and darker.
        if (_status.HasEffectComp<DrunkStatusEffectComponent>(uid))
            offset = offset.Add(new VoiceProfile(-0.5f, 0, brightness: -0.35f, roughness: 0.1f, tremble: 0.25f));

        // Drowsy: flat and dull.
        if (_status.HasEffectComp<DrowsinessStatusEffectComponent>(uid))
            offset = offset.Add(new VoiceProfile(-0.5f, 0, brightness: -0.25f, breathiness: 0.15f));

        // Electrocuted, shaking, withdrawal...
        if (HasComp<JitteringComponent>(uid))
            offset = offset.Add(new VoiceProfile(0, 0, tremble: 0.6f));

        // Out of breath from running or a stun baton: breathier as stamina runs out.
        if (TryComp<StaminaComponent>(uid, out var stamina) && stamina.CritThreshold > 0)
        {
            var tired = Math.Clamp(stamina.StaminaDamage / stamina.CritThreshold, 0f, 1f);
            if (stamina.Critical)
                tired = 1f;

            if (tired > 0.4f)
                offset = offset.Add(new VoiceProfile(0, 0, breathiness: (tired - 0.4f) * 1.2f));
        }

        // Suffocating: strained, breathy, thin.
        if (TryComp<RespiratorComponent>(uid, out var respirator)
            && respirator.Saturation < respirator.SuffocationThreshold)
        {
            offset = offset.Add(new VoiceProfile(0, 0, brightness: 0.2f, roughness: 0.2f, breathiness: 0.45f));
        }

        // Freezing: shivering, worse the colder it gets.
        if (TryComp<TemperatureComponent>(uid, out var temperature)
            && TryComp<TemperatureDamageComponent>(uid, out var damage))
        {
            var cold = damage.ColdDamageThreshold + ShiverMargin;
            if (temperature.Temperature < cold)
            {
                var shiver = Math.Clamp((cold - temperature.Temperature) / ShiverMargin, 0f, 1f);
                offset = offset.Add(new VoiceProfile(0, 0, tremble: 0.2f + 0.5f * shiver));
            }
        }

        if (!offset.IsNeutral)
            args.ProfileOffset = args.ProfileOffset.Add(offset);
    }
}

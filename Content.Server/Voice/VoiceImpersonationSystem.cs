using Content.Server.Popups;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Verbs;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;

namespace Content.Server.Voice;

/// <summary>
/// Recording characters' procedural voices into impersonation devices. Playback happens in
/// <see cref="VoiceModifierSystem"/> when the device's voice mask name matches a recording.
/// </summary>
/// <remarks>
/// This copies the target CHARACTER's procedural voice parameters. It never records or reproduces any real player's
/// audio.
/// </remarks>
public sealed partial class VoiceImpersonationSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private VoiceProfileSystem _profiles = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VoiceImpersonatorComponent, GetVerbsEvent<UtilityVerb>>(OnUtilityVerb);
        SubscribeLocalEvent<VoiceImpersonatorComponent, VoiceSampleDoAfterEvent>(OnSampleDoAfter);
        SubscribeLocalEvent<VoiceImpersonatorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnUtilityVerb(Entity<VoiceImpersonatorComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using != ent.Owner
            || args.Target == args.User || !HasComp<HumanoidProfileComponent>(args.Target))
        {
            return;
        }

        var user = args.User;
        var target = args.Target;
        args.Verbs.Add(new UtilityVerb
        {
            Text = Loc.GetString("voice-impersonator-verb-record"),
            Message = Loc.GetString("voice-impersonator-verb-record-message"),
            Act = () => StartSample(ent, user, target),
        });
    }

    private void StartSample(Entity<VoiceImpersonatorComponent> ent, EntityUid user, EntityUid target)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.SampleTime, new VoiceSampleDoAfterEvent(),
            ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = ent.Comp.SampleRange,
        });

        _popup.PopupEntity(Loc.GetString("voice-impersonator-recording"), user, user);
    }

    private void OnSampleDoAfter(Entity<VoiceImpersonatorComponent> ent, ref VoiceSampleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;

        var name = Name(target);
        var (profile, filters) = _profiles.GetInnateVoice(target);

        if (!ent.Comp.Samples.ContainsKey(name) && ent.Comp.Samples.Count >= ent.Comp.MaxSamples)
        {
            // Forget the first recording to make room.
            foreach (var key in ent.Comp.Samples.Keys)
            {
                ent.Comp.Samples.Remove(key);
                break;
            }
        }

        ent.Comp.Samples[name] = new VoiceSample { Profile = profile, Filters = filters };

        _popup.PopupEntity(Loc.GetString("voice-impersonator-recorded", ("name", name)), args.User, args.User);
        _adminLog.Add(LogType.Voice, LogImpact.Low,
            $"{ToPrettyString(args.User):user} recorded the character voice of {ToPrettyString(target):target} with {ToPrettyString(ent):device}");
    }

    private void OnExamined(Entity<VoiceImpersonatorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.Samples.Count == 0)
            return;

        args.PushMarkup(Loc.GetString("voice-impersonator-examine",
            ("names", string.Join(", ", ent.Comp.Samples.Keys))));
    }
}

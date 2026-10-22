using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Ghost.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Radio;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Content.Shared.VoiceMask;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>
/// Works out, from gameplay state, how each player's voice should sound and what they may do with it, and publishes
/// it to their client via <see cref="VoiceSpeakerComponent"/>.
/// </summary>
/// <remarks>
/// Composition order: character profile (or a disguise/impersonation override) + species/innate filters (or the
/// impersonated speaker's) + equipment filters + status filters. The server decides; the client merely applies it.
/// Re-evaluated immediately on equipment and mob state changes and periodically as a safety net.
/// </remarks>
public sealed partial class VoiceProfileSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private VoiceRadioSystem _radio = default!;

    /// <summary>
    /// Hard safety limits for the final profile, regardless of what stacks up.
    /// </summary>
    private const float AbsoluteMaxPitch = 12f;
    private const float AbsoluteMaxFormant = 8f;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.5);

    private readonly List<ProtoId<RadioChannelPrototype>> _channels = new();
    private readonly List<ProtoId<VoiceFilterPrototype>> _filters = new();
    private TimeSpan _nextRefresh;
    private bool _ghostVoice;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActorComponent, ComponentStartup>(OnActorStartup);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnDetached);

        // Things that change the voice: refresh right away so removing a mask is instant.
        SubscribeLocalEvent<VoiceSpeakerComponent, DidEquipEvent>((uid, comp, _) => Refresh(uid, comp));
        SubscribeLocalEvent<VoiceSpeakerComponent, DidUnequipEvent>((uid, comp, _) => Refresh(uid, comp));
        SubscribeLocalEvent<VoiceSpeakerComponent, MobStateChangedEvent>((uid, comp, _) => Refresh(uid, comp));
        SubscribeLocalEvent<VoiceSpeakerComponent, VoiceMaskToggledEvent>((uid, comp, _) => Refresh(uid, comp));
        SubscribeLocalEvent<ItemMaskToggledEvent>(OnMaskToggled);

        Subs.CVar(_cfg, CCVars.VoiceGhostVoice, v => _ghostVoice = v, true);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        // The character's own procedural voice comes from their character profile.
        SetCharacterVoice(ev.Mob, ev.Profile.CharacterVoice);
    }

    /// <summary>
    /// Sets the base (character) voice of an entity, e.g. when a character spawns or is cloned.
    /// </summary>
    public void SetCharacterVoice(EntityUid uid, VoiceProfile profile)
    {
        var character = EnsureComp<VoiceCharacterComponent>(uid);
        character.Profile = profile.Clamped(_cfg.GetCVar(CCVars.VoiceProfileMaxPitch), _cfg.GetCVar(CCVars.VoiceProfileMaxFormant));

        if (TryComp<VoiceSpeakerComponent>(uid, out var speaker))
            Refresh(uid, speaker);
    }

    private void OnActorStartup(Entity<ActorComponent> ent, ref ComponentStartup args)
    {
        EnsureSpeaker(ent);
    }

    private void OnAttached(PlayerAttachedEvent ev)
    {
        EnsureSpeaker(ev.Entity);
    }

    private void OnDetached(PlayerDetachedEvent ev)
    {
        RemCompDeferred<VoiceSpeakerComponent>(ev.Entity);
    }

    private void OnMaskToggled(ref ItemMaskToggledEvent ev)
    {
        if (ev.Wearer is { } wearer && TryComp<VoiceSpeakerComponent>(wearer, out var comp))
            Refresh(wearer, comp);
    }

    private void EnsureSpeaker(EntityUid uid)
    {
        var comp = EnsureComp<VoiceSpeakerComponent>(uid);
        comp.Seed = StableHash(Name(uid));
        Refresh(uid, comp, force: true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshInterval;

        var query = EntityQueryEnumerator<VoiceSpeakerComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            Refresh(uid, comp);
        }
    }

    /// <summary>
    /// Computes the final voice profile and filters for a speaker without touching any component.
    /// </summary>
    public (VoiceProfile Profile, List<ProtoId<VoiceFilterPrototype>> Filters) Compose(EntityUid uid)
    {
        var ev = new VoiceModifiersEvent();
        RaiseLocalEvent(uid, ev);

        var baseProfile = ev.ProfileOverride
                          ?? (TryComp<VoiceCharacterComponent>(uid, out var character) ? character.Profile : VoiceProfile.Neutral);

        var profile = baseProfile.Add(ev.ProfileOffset).Clamped(AbsoluteMaxPitch, AbsoluteMaxFormant);

        var filters = new List<ProtoId<VoiceFilterPrototype>>();
        AddFilters(filters, ev.FilterOverride ?? ev.InnateFilters);
        AddFilters(filters, ev.Filters);

        filters.Sort((a, b) =>
        {
            var orderA = _proto.TryIndex(a, out var protoA) ? protoA.Order : 0;
            var orderB = _proto.TryIndex(b, out var protoB) ? protoB.Order : 0;
            return orderA != orderB ? orderA.CompareTo(orderB) : string.CompareOrdinal(a.Id, b.Id);
        });

        return (profile, filters);
    }

    /// <summary>
    /// The voice someone's body produces on its own (character + species), without equipment or disguises.
    /// </summary>
    public (VoiceProfile Profile, List<ProtoId<VoiceFilterPrototype>> Filters) GetInnateVoice(EntityUid uid)
    {
        var ev = new VoiceModifiersEvent { InnateOnly = true };
        RaiseLocalEvent(uid, ev);

        var baseProfile = TryComp<VoiceCharacterComponent>(uid, out var character) ? character.Profile : VoiceProfile.Neutral;
        var filters = new List<ProtoId<VoiceFilterPrototype>>();
        AddFilters(filters, ev.InnateFilters);
        return (baseProfile.Add(ev.ProfileOffset), filters);
    }

    private void AddFilters(List<ProtoId<VoiceFilterPrototype>> into, List<ProtoId<VoiceFilterPrototype>> from)
    {
        foreach (var filter in from)
        {
            if (!into.Contains(filter) && _proto.HasIndex(filter))
                into.Add(filter);
        }
    }

    public void Refresh(EntityUid uid, VoiceSpeakerComponent? comp = null, bool force = false)
    {
        if (!Resolve(uid, ref comp, false) || TerminatingOrDeleted(uid))
            return;

        var ghost = HasComp<GhostComponent>(uid);
        var canSpeak = ghost ? _ghostVoice : _actionBlocker.CanSpeak(uid);

        var (profile, filters) = ghost
            ? (VoiceProfile.Neutral, new List<ProtoId<VoiceFilterPrototype>>())
            : Compose(uid);

        _channels.Clear();
        ProtoId<RadioChannelPrototype>? defaultChannel = null;
        if (!ghost)
            _radio.GetTransmitChannels(uid, _channels, out defaultChannel);

        var changed = force
                      || comp.Ghost != ghost
                      || comp.CanSpeak != canSpeak
                      || comp.Profile != profile
                      || comp.DefaultRadioChannel != defaultChannel
                      || !SequenceEqual(comp.Filters, filters)
                      || !SequenceEqual(comp.RadioChannels, _channels);

        if (!changed)
            return;

        comp.Ghost = ghost;
        comp.CanSpeak = canSpeak;
        comp.Profile = profile;
        comp.Filters = filters;
        comp.RadioChannels = new List<ProtoId<RadioChannelPrototype>>(_channels);
        comp.DefaultRadioChannel = defaultChannel;
        Dirty(uid, comp);
    }

    private static bool SequenceEqual<T>(List<T> a, List<T> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// FNV-1a; string.GetHashCode is randomized per process and so useless for "same voice every round".
    /// </summary>
    public static int StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int) hash;
        }
    }
}

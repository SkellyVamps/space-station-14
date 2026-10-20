using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.Doors;
using Content.Shared.Mobs.Systems;
using Content.Shared.Voice;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>
/// Authenticated local voice relay. Audio is never decoded, logged, or replay-recorded here.
/// Acoustic searches are bounded and shared by all listeners for a speaker in an epoch.
/// </summary>
public sealed partial class VoiceRoutingSystem : EntitySystem
{
    [Dependency] private VoiceModerationSystem _moderation = default!;
    [Dependency] private VoiceRadioSystem _radio = default!;
    [Dependency] private VoiceProfileSystem _profiles = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private ActionBlockerSystem _actions = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    private readonly Dictionary<INetChannel, VoiceIngressGuard> _guards = new();
    private readonly Dictionary<EntityUid, AcousticCache> _cache = new();
    private TimeSpan _nextCleanup;
    private readonly VoiceStreamRegistry _streams = new();

    private sealed record AcousticCache(EntityUid Grid, VoiceAcousticTile Origin, TimeSpan Expires,
        Dictionary<VoiceAcousticTile, float> Costs);

    public override void Initialize()
    {
        base.Initialize();
        _net.RegisterNetMessage<MsgVoiceFrame>(Receive, accept: NetMessageAccept.Server);
        _net.RegisterNetMessage<MsgVoicePlayback>(accept: NetMessageAccept.Client);
        SubscribeLocalEvent<DoorComponent, DoorStateChangedEvent>(OnDoorChanged);
    }

    private void OnDoorChanged(EntityUid uid, DoorComponent component, DoorStateChangedEvent args)
    {
        _cache.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCleanup)
            return;
        _nextCleanup = _timing.CurTime + TimeSpan.FromSeconds(1);
        _streams.Retain(new HashSet<NetUserId>(_players.Sessions.Select(s => s.UserId)));
        // Bounded to authenticated connections, never client-supplied stream IDs.
        foreach (var channel in new List<INetChannel>(_guards.Keys))
            if (!_players.TryGetSessionByChannel(channel, out var session) || session.Channel != channel)
                _guards.Remove(channel);
        foreach (var entity in new List<EntityUid>(_cache.Keys))
            if (_cache[entity].Expires < _timing.CurTime)
                _cache.Remove(entity);
    }

    private void Receive(MsgVoiceFrame message)
    {
        if (!_players.TryGetSessionByChannel(message.MsgChannel, out var session) ||
            session.Channel != message.MsgChannel || session.Status != SessionStatus.InGame)
            return;
        if (!_guards.TryGetValue(message.MsgChannel, out var guard))
            _guards.Add(message.MsgChannel, guard = new VoiceIngressGuard());
        var permitted = _config.GetCVar(CCVars.VoiceEnabled) && !_moderation.IsMuted(session.UserId) && session.AttachedEntity is { } speaker &&
                        CanSpeak(speaker);
        if (!guard.TryAccept(message.Sequence, message.Payload.Length, _timing.CurTime, permitted) ||
            session.AttachedEntity is not { } source)
            return;
        if (!VoiceOpusPacket.IsValid(message.Payload) || _profiles.Refresh(source) != message.ProfileRevision)
            return;
        var delivered = new HashSet<NetUserId>();
        if (message.Transmission != VoiceTransmission.Radio)
            RouteLocal(session, source, message, delivered);
        if (message.Transmission != VoiceTransmission.Local &&
            _radio.TryGetTransmitter(source, message.RadioChannel, out var transmitter, out var channel))
        {
            foreach (var listener in _players.Sessions)
            {
                if (listener == session || delivered.Contains(listener.UserId) || listener.Status != SessionStatus.InGame ||
                    listener.AttachedEntity is not { } target || !CanListen(target) ||
                    !_radio.CanReceive(target, transmitter, channel))
                    continue;
                _net.ServerSendMessage(new MsgVoicePlayback
                {
                    Speaker = _streams.Get(listener.UserId, session.UserId, source, message.ProfileRevision),
                    Entity = NetEntity.Invalid, Sequence = message.Sequence,
                    Gain = 1, Radio = true, Payload = message.Payload,
                }, listener.Channel);
            }
        }
    }

    private void RouteLocal(ICommonSession session, EntityUid source, MsgVoiceFrame message,
        HashSet<NetUserId> delivered)
    {
        if (!TryContainerGain(source, out var sourceContainerGain) ||
            (_atmos.GetContainingMixture(source)?.Pressure ?? 0) < _config.GetCVar(CCVars.VoiceMinimumPressure))
            return;
        var xform = Transform(source);
        // Cross-grid portals need a dedicated adapter. Until then, fail closed.
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;
        var tile = _transform.GetGridOrMapTilePosition(source, xform);
        var origin = new VoiceAcousticTile(tile.X, tile.Y);
        var range = Math.Clamp(_config.GetCVar(CCVars.VoiceRange), 1, 32);
        if (!_cache.TryGetValue(source, out var cache) || cache.Grid != grid || cache.Origin != origin ||
            cache.Expires <= _timing.CurTime)
        {
            var pressure = Math.Max(0.1f, _config.GetCVar(CCVars.VoiceMinimumPressure));
            var costs = VoiceAcousticSearch.Search(origin, range, pressure, 2048,
                point => Sample(grid, mapGrid, xform.MapUid, point));
            cache = new AcousticCache(grid, origin, _timing.CurTime + TimeSpan.FromMilliseconds(100), costs);
            _cache[source] = cache;
        }
        // Iterate connected sessions, never every world entity. A spatial session index can replace
        // this when profiling justifies it; the expensive search above is independent of listener count.
        foreach (var listener in _players.Sessions)
        {
            if (listener == session || listener.Status != SessionStatus.InGame ||
                listener.AttachedEntity is not { } target || !CanListen(target) ||
                !TryContainerGain(target, out var targetContainerGain))
                continue;
            var targetXform = Transform(target);
            if (targetXform.MapID != xform.MapID || targetXform.GridUid != grid)
                continue;
            var targetTile = _transform.GetGridOrMapTilePosition(target, targetXform);
            if (!cache.Costs.TryGetValue(new VoiceAcousticTile(targetTile.X, targetTile.Y), out var cost))
                continue;
            var gain = Math.Clamp(1 - Math.Max(0, cost - 2) / Math.Max(1, range - 2), 0, 1) *
                       sourceContainerGain * targetContainerGain;
            if (gain <= 0)
                continue;
            delivered.Add(listener.UserId);
            _net.ServerSendMessage(new MsgVoicePlayback
            {
                Speaker = _streams.Get(listener.UserId, session.UserId, source, message.ProfileRevision),
                Entity = GetNetEntity(source),
                Sequence = message.Sequence,
                Gain = gain,
                Payload = message.Payload,
            }, listener.Channel);
        }
    }

    private bool CanSpeak(EntityUid entity) => !Deleted(entity) && _mob.IsAlive(entity) &&
        _actions.CanSpeak(entity) && !HasComp<VoiceMutedComponent>(entity);

    private bool CanListen(EntityUid entity) => !Deleted(entity) && _mob.IsAlive(entity) &&
        !HasComp<VoiceDeafComponent>(entity);

    private bool TryContainerGain(EntityUid entity, out float gain)
    {
        gain = 1;
        for (var depth = 0; depth < 8; depth++)
        {
            if (!_containers.TryGetContainingContainer(entity, out var container))
                return true;
            if (!TryComp<VoiceAcousticBarrierComponent>(container.Owner, out var barrier) || barrier.Blocks)
                return false;
            gain *= MathF.Exp(-Math.Clamp(barrier.ExtraDistance, 0, 32) * 0.25f);
            entity = container.Owner;
        }
        return false;
    }

    private VoiceAcousticMedium Sample(EntityUid grid, MapGridComponent mapGrid, EntityUid? map,
        VoiceAcousticTile tile)
    {
        var indices = new Vector2i(tile.X, tile.Y);
        var blocked = false;
        var extra = 0f;
        var barrierTile = false;
        foreach (var entity in _maps.GetAnchoredEntities(grid, mapGrid, indices))
        {
            if (TryComp<DoorComponent>(entity, out var door))
            {
                if (door.State == DoorState.Open)
                    continue;
                barrierTile = true;
                if (TryComp<VoiceAcousticBarrierComponent>(entity, out var doorBarrier))
                {
                    blocked |= doorBarrier.Blocks;
                    extra = Math.Max(extra, doorBarrier.ExtraDistance);
                }
                else
                    extra = Math.Max(extra, 4);
                continue;
            }
            if (TryComp<VoiceAcousticBarrierComponent>(entity, out var barrier))
            {
                barrierTile = true;
                blocked |= barrier.Blocks;
                extra = Math.Max(extra, barrier.ExtraDistance);
            }
            else if (TryComp<AirtightComponent>(entity, out var airtight) && airtight.AirBlocked)
                blocked = true;
        }
        var air = _atmos.GetTileMixture(grid, map, indices);
        var pressure = air?.Pressure ?? 0;
        // Airtight barrier tiles may deliberately have no tile mixture. Transmission through
        // a material uses adjacent real atmosphere, while the path still requires air on both sides.
        if (barrierTile && pressure < 0.1f)
        {
            pressure = Math.Max(pressure, _atmos.GetTileMixture(grid, map, indices + new Vector2i(1, 0))?.Pressure ?? 0);
            pressure = Math.Max(pressure, _atmos.GetTileMixture(grid, map, indices + new Vector2i(-1, 0))?.Pressure ?? 0);
            pressure = Math.Max(pressure, _atmos.GetTileMixture(grid, map, indices + new Vector2i(0, 1))?.Pressure ?? 0);
            pressure = Math.Max(pressure, _atmos.GetTileMixture(grid, map, indices + new Vector2i(0, -1))?.Pressure ?? 0);
        }
        return new VoiceAcousticMedium(blocked, pressure, extra);
    }
}

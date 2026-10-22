using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Voice.Acoustics;
using Content.Shared.Bed.Sleep;
using Content.Shared.CCVar;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>
/// Answers "how well does listener X hear speaker Y talking normally right now" using the station's actual layout:
/// walls, doors, windows, vacuum, containers and the listener's hearing.
/// </summary>
/// <remarks>
/// <para>
/// This is the AUDIBILITY layer. It never sees voice packets. The routing layer asks it for audibility a few times a
/// second per active speaker and caches the answers; packets then just use the cached recipient lists.
/// </para>
/// <para>
/// Propagation is a shortest-path search over tiles from the speaker (<see cref="AcousticField"/>). Fields are cached
/// per speaker and only rebuilt when the speaker changes tile, when an airtight structure or tile changes on that grid,
/// or when the field is older than <see cref="CCVars.VoiceFieldMaxAge"/> (to pick up pressure changes).
/// Per-tile data is cached per grid and invalidated by the same events.
/// </para>
/// </remarks>
public sealed partial class VoiceAudibilitySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private EntityQuery<AirtightComponent> _airtightQuery;
    private EntityQuery<VoiceAcousticBarrierComponent> _barrierQuery;
    private EntityQuery<VoiceContainerAcousticsComponent> _containerAcousticsQuery;
    private EntityQuery<EntityStorageComponent> _storageQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<SleepingComponent> _sleepingQuery;

    private readonly Dictionary<EntityUid, GridAcoustics> _grids = new();
    private readonly Dictionary<EntityUid, SpeakerField> _fields = new();

    private bool _acoustics;
    private float _range;
    private float _fullRange;
    private float _falloff;
    private float _minPressure;
    private float _fullPressure;
    private float _crossGridRange;
    private float _defaultBarrierPenalty;
    private float _defaultBarrierMuffle;
    private float _defaultContainerPenalty;
    private float _defaultContainerMuffle;
    private TimeSpan _fieldMaxAge;
    private float _sleepingMuffle;
    private bool _critCanHear;

    /// <summary>
    /// Statistics for profiling / the voicestats command.
    /// </summary>
    public int FieldsComputed { get; private set; }
    public int FieldCacheHits { get; private set; }
    public TimeSpan FieldComputeTime { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        _airtightQuery = GetEntityQuery<AirtightComponent>();
        _barrierQuery = GetEntityQuery<VoiceAcousticBarrierComponent>();
        _containerAcousticsQuery = GetEntityQuery<VoiceContainerAcousticsComponent>();
        _storageQuery = GetEntityQuery<EntityStorageComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _sleepingQuery = GetEntityQuery<SleepingComponent>();

        SubscribeLocalEvent<AirtightComponent, AirtightChanged>(OnAirtightChanged);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);

        Subs.CVar(_cfg, CCVars.VoiceAcoustics, v => _acoustics = v, true);
        Subs.CVar(_cfg, CCVars.VoiceRange, v => { _range = v; InvalidateAll(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceFullVolumeRange, v => _fullRange = v, true);
        Subs.CVar(_cfg, CCVars.VoiceFalloffExponent, v => _falloff = v, true);
        Subs.CVar(_cfg, CCVars.VoiceMinPressure, v => { _minPressure = v; InvalidateAll(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceFullPressure, v => _fullPressure = v, true);
        Subs.CVar(_cfg, CCVars.VoiceCrossGridRange, v => _crossGridRange = v, true);
        Subs.CVar(_cfg, CCVars.VoiceDefaultBarrierPenalty, v => { _defaultBarrierPenalty = v; InvalidateAll(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceDefaultBarrierMuffle, v => { _defaultBarrierMuffle = v; InvalidateAll(); }, true);
        Subs.CVar(_cfg, CCVars.VoiceDefaultContainerPenalty, v => _defaultContainerPenalty = v, true);
        Subs.CVar(_cfg, CCVars.VoiceDefaultContainerMuffle, v => _defaultContainerMuffle = v, true);
        Subs.CVar(_cfg, CCVars.VoiceFieldMaxAge, v => _fieldMaxAge = TimeSpan.FromSeconds(v), true);
        Subs.CVar(_cfg, CCVars.VoiceSleepingMuffle, v => _sleepingMuffle = v, true);
        Subs.CVar(_cfg, CCVars.VoiceCritCanHear, v => _critCanHear = v, true);
    }

    #region Invalidation

    private void OnAirtightChanged(Entity<AirtightComponent> ent, ref AirtightChanged args)
    {
        // AirtightSystem raises this for both the old and new position when an airtight entity moves.
        InvalidateTile(args.Position.Grid, args.Position.Tile);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            InvalidateTile(args.Entity, change.GridIndices);
        }
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        _grids.Remove(ev.EntityUid);
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        _grids.Remove(args.Grid);
        foreach (var grid in args.NewGrids)
        {
            _grids.Remove(grid);
        }
    }

    private void InvalidateTile(EntityUid grid, Vector2i tile)
    {
        if (!_grids.TryGetValue(grid, out var data))
            return;

        data.Tiles.Remove(tile);
        data.Version++;
    }

    public void InvalidateAll()
    {
        _grids.Clear();
        _fields.Clear();
    }

    /// <summary>
    /// Drops cached data for a speaker (e.g. on detach).
    /// </summary>
    public void ForgetSpeaker(EntityUid speaker)
    {
        _fields.Remove(speaker);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Where sound from (or to) an entity effectively originates: the outermost thing containing it, plus the
    /// containers it has to pass through on the way.
    /// </summary>
    public AcousticPosition GetAcousticPosition(EntityUid uid)
    {
        var containers = new List<(EntityUid Owner, AcousticBarrier Barrier)>();
        var current = uid;

        // Bounded to avoid pathological container nesting.
        for (var depth = 0; depth < 8; depth++)
        {
            if (!_container.TryGetContainingContainer(current, out var container))
                break;

            current = container.Owner;
            containers.Add((current, GetContainerBarrier(current)));
        }

        var xform = Transform(current);
        var mapPos = _xform.GetMapCoordinates(current, xform);
        var grid = xform.GridUid;
        var tile = Vector2i.Zero;

        if (grid != null && _gridQuery.TryComp(grid, out var gridComp))
            tile = _map.TileIndicesFor(grid.Value, gridComp, xform.Coordinates);

        return new AcousticPosition(uid, current, mapPos, grid, tile, containers);
    }

    /// <summary>
    /// How strongly the listener's hearing is affected (deafness, sleep, earmuffs...).
    /// </summary>
    public VoiceHearingEvent GetHearing(EntityUid listener)
    {
        var ev = new VoiceHearingEvent();

        if (_mobState.IsDead(listener))
        {
            ev.Deaf = true;
            return ev;
        }

        if (_mobState.IsCritical(listener))
        {
            if (!_critCanHear)
            {
                ev.Deaf = true;
                return ev;
            }

            ev.AddMuffle(0.8f);
            ev.Gain *= 0.5f;
        }

        if (_sleepingQuery.HasComp(listener))
        {
            if (_sleepingMuffle >= 1f)
            {
                ev.Deaf = true;
                return ev;
            }

            ev.AddMuffle(_sleepingMuffle);
        }

        RaiseLocalEvent(listener, ev);
        return ev;
    }

    /// <summary>
    /// Straight-line falloff only, for observers (ghosts) who aren't bound by the station's acoustics.
    /// </summary>
    public bool TryGetObserverAudibility(in AcousticPosition speaker, in AcousticPosition listener, out float gain)
    {
        gain = 0;
        if (speaker.Map.MapId != listener.Map.MapId || speaker.Map.MapId == MapId.Nullspace)
            return false;

        var distance = Vector2.Distance(speaker.Map.Position, listener.Map.Position);
        gain = AcousticMath.Falloff(distance, _fullRange, _range, _falloff);
        return gain > 0;
    }

    /// <summary>
    /// Physical audibility of speech from <paramref name="speaker"/> at <paramref name="listener"/>, without the
    /// listener's hearing modifiers.
    /// </summary>
    /// <returns>False if the listener cannot hear the speaker at all.</returns>
    public bool TryGetAudibility(in AcousticPosition speaker, in AcousticPosition listener, out float gain, out float muffle)
    {
        gain = 0;
        muffle = 0;

        if (speaker.Map.MapId != listener.Map.MapId || speaker.Map.MapId == MapId.Nullspace)
            return false;

        var euclid = Vector2.Distance(speaker.Map.Position, listener.Map.Position);
        if (euclid > _range)
            return false;

        // Containers between the two. Containers both are inside of don't separate them.
        var shared = CountSharedContainers(speaker.Containers, listener.Containers);
        var distance = 0f;

        for (var i = 0; i < speaker.Containers.Count - shared; i++)
        {
            var barrier = speaker.Containers[i].Barrier;
            if (barrier.Blocks)
                return false;

            distance += barrier.Penalty;
            muffle = AcousticMath.CombineMuffle(muffle, barrier.Muffle);
        }

        for (var i = 0; i < listener.Containers.Count - shared; i++)
        {
            var barrier = listener.Containers[i].Barrier;
            if (barrier.Blocks)
                return false;

            distance += barrier.Penalty;
            muffle = AcousticMath.CombineMuffle(muffle, barrier.Muffle);
        }

        var speakerPressure = GetPressureFactor(speaker);
        var listenerPressure = GetPressureFactor(listener);

        // Both inside the very same container: pressed up against each other.
        if (shared > 0 && shared == speaker.Containers.Count && shared == listener.Containers.Count)
        {
            gain = speakerPressure;
            return gain > 0;
        }

        if (speakerPressure <= 0 || listenerPressure <= 0)
            return false;

        if (!_acoustics)
        {
            distance += euclid;
        }
        else if (speaker.Grid != null && speaker.Grid == listener.Grid)
        {
            var field = GetField(speaker);
            if (!field.TryGetNode(listener.Tile, out var node))
                return false;

            distance += MathF.Max(node.Distance, euclid);
            muffle = AcousticMath.CombineMuffle(muffle, node.Muffle);
        }
        else
        {
            // Different grids, or open map space: only across a short gap (e.g. a docking airlock).
            if (euclid > _crossGridRange)
                return false;

            distance += euclid + 1f;
            muffle = AcousticMath.CombineMuffle(muffle, 0.3f);
        }

        gain = AcousticMath.Falloff(distance, _fullRange, _range, _falloff) * speakerPressure * listenerPressure;
        return gain > 0;
    }

    #endregion

    #region Internals

    private static int CountSharedContainers(
        List<(EntityUid Owner, AcousticBarrier Barrier)> a,
        List<(EntityUid Owner, AcousticBarrier Barrier)> b)
    {
        // Container lists go innermost -> outermost, so compare from the end.
        var shared = 0;
        while (shared < a.Count && shared < b.Count
               && a[a.Count - 1 - shared].Owner == b[b.Count - 1 - shared].Owner)
        {
            shared++;
        }

        return shared;
    }

    private AcousticBarrier GetContainerBarrier(EntityUid owner)
    {
        if (_containerAcousticsQuery.TryComp(owner, out var acoustics))
        {
            if (acoustics.OnlyWhenClosed && _storageQuery.TryComp(owner, out var storage) && storage.Open)
                return AcousticBarrier.None;

            return new AcousticBarrier(acoustics.Penalty, acoustics.Muffle, acoustics.Blocks);
        }

        if (_storageQuery.TryComp(owner, out var openable) && openable.Open)
            return AcousticBarrier.None;

        return new AcousticBarrier(_defaultContainerPenalty, _defaultContainerMuffle, false);
    }

    private float GetPressureFactor(in AcousticPosition pos)
    {
        // Includes the air inside lockers and similar.
        var mixture = _atmos.GetContainingMixture(pos.Entity);
        return AcousticMath.PressureFactor(mixture?.Pressure ?? 0f, _minPressure, _fullPressure);
    }

    private AcousticField GetField(in AcousticPosition speaker)
    {
        var grid = speaker.Grid!.Value;
        var gridData = GetGridData(grid);
        var now = _timing.CurTime;

        if (_fields.TryGetValue(speaker.Entity, out var cached)
            && cached.Grid == grid
            && cached.Field.Origin == speaker.Tile
            && cached.Version == gridData.Version
            && now - cached.Computed < _fieldMaxAge)
        {
            FieldCacheHits++;
            return cached.Field;
        }

        cached ??= new SpeakerField();
        _fields[speaker.Entity] = cached;

        var start = _timing.RealTime;
        gridData.Source.Now = now;
        cached.Field.Compute(gridData.Source, speaker.Tile, new AcousticSettings(_range, _minPressure));
        cached.Grid = grid;
        cached.Version = gridData.Version;
        cached.Computed = now;

        FieldsComputed++;
        FieldComputeTime += _timing.RealTime - start;
        return cached.Field;
    }

    private GridAcoustics GetGridData(EntityUid grid)
    {
        if (_grids.TryGetValue(grid, out var data))
            return data;

        data = new GridAcoustics(this, grid);
        _grids[grid] = data;
        return data;
    }

    /// <summary>
    /// Reads acoustic data for one tile from the game world.
    /// </summary>
    private AcousticTile ReadTile(EntityUid grid, Vector2i indices)
    {
        if (!_gridQuery.TryComp(grid, out var gridComp)
            || !_map.TryGetTileRef(grid, gridComp, indices, out var tileRef)
            || tileRef.Tile.IsEmpty)
        {
            return AcousticTile.Closed;
        }

        var tile = new AcousticTile { Open = true };

        var anchored = _map.GetAnchoredEntities(grid, gridComp, indices);
        while (anchored.MoveNext(out var ent))
        {
            if (!_airtightQuery.TryComp(ent, out var airtight) || !airtight.AirBlocked)
                continue;

            var dirs = airtight.AirBlockedDirection;
            if (dirs == 0)
                continue;

            var barrier = GetBarrier(ent.Value);

            if (dirs == Shared.Atmos.AtmosDirection.All)
            {
                tile.Fill = tile.Fill.Combine(barrier);
                continue;
            }

            if ((dirs & Shared.Atmos.AtmosDirection.North) != 0)
                tile.SetEdge(AcousticDirection.North, barrier);
            if ((dirs & Shared.Atmos.AtmosDirection.East) != 0)
                tile.SetEdge(AcousticDirection.East, barrier);
            if ((dirs & Shared.Atmos.AtmosDirection.South) != 0)
                tile.SetEdge(AcousticDirection.South, barrier);
            if ((dirs & Shared.Atmos.AtmosDirection.West) != 0)
                tile.SetEdge(AcousticDirection.West, barrier);
        }

        if (tile.Fill.IsNone)
        {
            var mixture = _atmos.GetTileMixture(grid, Transform(grid).MapUid, indices);
            tile.Pressure = mixture?.Pressure ?? 0f;
        }

        return tile;
    }

    private AcousticBarrier GetBarrier(EntityUid uid)
    {
        if (_barrierQuery.TryComp(uid, out var barrier))
            return new AcousticBarrier(barrier.Penalty, barrier.Muffle, barrier.Blocks);

        return _defaultBarrierPenalty < 0
            ? new AcousticBarrier(0, 1, true)
            : new AcousticBarrier(_defaultBarrierPenalty, _defaultBarrierMuffle, false);
    }

    private sealed class SpeakerField
    {
        public readonly AcousticField Field = new();
        public EntityUid Grid;
        public int Version;
        public TimeSpan Computed;
    }

    /// <summary>
    /// Per-grid tile cache. Tiles are read lazily and invalidated individually.
    /// </summary>
    private sealed class GridAcoustics
    {
        public readonly Dictionary<Vector2i, (AcousticTile Tile, TimeSpan Read)> Tiles = new();
        public readonly TileSource Source;
        public int Version;

        public GridAcoustics(VoiceAudibilitySystem system, EntityUid grid)
        {
            Source = new TileSource(system, grid, this);
        }
    }

    private sealed class TileSource(VoiceAudibilitySystem system, EntityUid grid, GridAcoustics data) : IAcousticTileSource
    {
        public TimeSpan Now;

        public AcousticTile GetTile(Vector2i position)
        {
            if (data.Tiles.TryGetValue(position, out var cached) && Now - cached.Read < system._fieldMaxAge)
                return cached.Tile;

            var tile = system.ReadTile(grid, position);
            data.Tiles[position] = (tile, Now);
            return tile;
        }
    }

    #endregion
}

/// <summary>
/// Where an entity is, acoustically.
/// </summary>
/// <param name="Entity">The entity itself (speaker or listener).</param>
/// <param name="Outermost">The outermost entity containing it (itself if not contained).</param>
/// <param name="Containers">Containers it is inside, innermost first, with their acoustic effect.</param>
public readonly record struct AcousticPosition(
    EntityUid Entity,
    EntityUid Outermost,
    MapCoordinates Map,
    EntityUid? Grid,
    Vector2i Tile,
    List<(EntityUid Owner, AcousticBarrier Barrier)> Containers);

using System.Numerics;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Ghost.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mind.Components;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Teleportation.Components;
using Content.Shared.Tutorial;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Tutorial;

/// <summary>
/// Handles the Tutorial Station's per-room reset buttons. See <see cref="TutorialRoomResetComponent"/>.
/// </summary>
public sealed partial class TutorialRoomResetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TutorialRoomResetComponent, ActivateInWorldEvent>(OnActivate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Snapshot each room once, on the first tick after its map has initialized.
        var query = EntityQueryEnumerator<TutorialRoomResetComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var comp, out var meta))
        {
            if (comp.Snapshot != null || meta.EntityLifeStage < EntityLifeStage.MapInitialized)
                continue;
            if (Paused(uid))
                continue;

            TakeSnapshot((uid, comp));
        }
    }

    private void OnActivate(Entity<TutorialRoomResetComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        if (ent.Comp.Snapshot == null)
            return;

        if (_timing.CurTime < ent.Comp.NextUse)
        {
            _popup.PopupEntity(Loc.GetString("tutorial-room-reset-cooldown"), ent, args.User);
            return;
        }

        ent.Comp.NextUse = _timing.CurTime + ent.Comp.Cooldown;
        ResetRoom(ent);
        _popup.PopupEntity(Loc.GetString("tutorial-room-reset-done"), ent, args.User);
    }

    /// <summary>
    /// Records the room's tiles and contents so <see cref="ResetRoom"/> can rebuild them.
    /// </summary>
    public void TakeSnapshot(Entity<TutorialRoomResetComponent> ent)
    {
        var snapshot = new List<TutorialRoomSnapshotEntry>();
        ent.Comp.Snapshot = snapshot;
        ent.Comp.Tiles.Clear();

        if (Transform(ent).GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        foreach (var index in TilesIn(ent.Comp.Bounds))
        {
            var tile = _map.GetTileRef(gridUid, grid, index).Tile;
            ent.Comp.Tiles[index] = _tileDefs[tile.TypeId].ID;
        }

        foreach (var child in ChildrenInRoom(gridUid, ent))
        {
            var meta = MetaData(child);
            if (meta.EntityPrototype is not { } proto)
                continue;

            var xform = Transform(child);
            var name = meta.EntityName != proto.Name ? meta.EntityName : null;
            var content = TryComp<PaperComponent>(child, out var paper) ? paper.Content : null;
            snapshot.Add(new TutorialRoomSnapshotEntry(proto.ID, xform.LocalPosition, xform.LocalRotation, name, content));
        }
    }

    /// <summary>
    /// Deletes everything resettable in the room, restores its floor, and respawns the snapshot.
    /// </summary>
    public void ResetRoom(Entity<TutorialRoomResetComponent> ent)
    {
        if (ent.Comp.Snapshot == null ||
            Transform(ent).GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        foreach (var child in ChildrenInRoom(gridUid, ent))
        {
            QueueDel(child);
        }

        var tiles = new List<(Vector2i, Tile)>();
        foreach (var (index, tileId) in ent.Comp.Tiles)
        {
            if (_tileDefs.TryGetDefinition(tileId, out var def))
                tiles.Add((index, new Tile(def.TileId)));
        }

        _map.SetTiles((gridUid, grid), tiles);

        foreach (var entry in ent.Comp.Snapshot)
        {
            var spawned = SpawnAttachedTo(entry.Prototype, new EntityCoordinates(gridUid, entry.Position));
            _xform.SetLocalRotation(spawned, entry.Rotation);

            if (entry.Name != null)
                _meta.SetEntityName(spawned, entry.Name);

            if (entry.PaperContent != null && TryComp<PaperComponent>(spawned, out var paper))
                _paper.SetContent((spawned, paper), entry.PaperContent);
        }
    }

    /// <summary>
    /// Things standing directly on the grid inside the room that a reset may delete and respawn.
    /// </summary>
    private List<EntityUid> ChildrenInRoom(EntityUid gridUid, Entity<TutorialRoomResetComponent> button)
    {
        var found = new List<EntityUid>();
        var children = Transform(gridUid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (child == button.Owner || !button.Comp.Bounds.Contains(Transform(child).LocalPosition))
                continue;

            if (IsProtected(child))
                continue;

            found.Add(child);
        }

        return found;
    }

    /// <summary>
    /// Never touched by a reset: players and anything they control, ghosts, portals (their links
    /// would break) and linked machines such as consoles, telepads and lights (their links too).
    /// </summary>
    private bool IsProtected(EntityUid uid)
    {
        if (HasComp<ActorComponent>(uid) || HasComp<GhostComponent>(uid) || HasComp<PortalComponent>(uid))
            return true;

        if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            return true;

        return HasComp<DeviceLinkSourceComponent>(uid) || HasComp<DeviceLinkSinkComponent>(uid);
    }

    private static IEnumerable<Vector2i> TilesIn(Box2 bounds)
    {
        for (var x = (int) MathF.Floor(bounds.Left); x < (int) MathF.Ceiling(bounds.Right); x++)
        {
            for (var y = (int) MathF.Floor(bounds.Bottom); y < (int) MathF.Ceiling(bounds.Top); y++)
            {
                yield return new Vector2i(x, y);
            }
        }
    }
}

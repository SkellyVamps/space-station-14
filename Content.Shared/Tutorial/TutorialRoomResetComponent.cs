using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared.Tutorial;

/// <summary>
/// A button that puts one room of the Tutorial Station back the way it was at round start:
/// its floor tiles and every loose or anchored thing inside it.
/// Players, ghosts, portals and linked machines are left alone.
/// </summary>
[RegisterComponent]
public sealed partial class TutorialRoomResetComponent : Component
{
    /// <summary>
    /// The room's floor, in grid-local tile coordinates: bottom-left inclusive, top-right exclusive.
    /// </summary>
    [DataField(required: true)]
    public Box2 Bounds;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    [ViewVariables]
    public TimeSpan NextUse;

    /// <summary>
    /// What was in the room at round start. Taken on the first tick after map init, so random
    /// spawners and container fills have already run.
    /// </summary>
    [ViewVariables]
    public List<TutorialRoomSnapshotEntry>? Snapshot;

    [ViewVariables]
    public Dictionary<Vector2i, string> Tiles = new();
}

public readonly record struct TutorialRoomSnapshotEntry(
    EntProtoId Prototype,
    Vector2 Position,
    Angle Rotation,
    string? Name,
    string? PaperContent);

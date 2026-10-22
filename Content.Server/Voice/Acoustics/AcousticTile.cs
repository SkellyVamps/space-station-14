namespace Content.Server.Voice.Acoustics;

/// <summary>
/// Effect of an obstacle on sound crossing it.
/// </summary>
public record struct AcousticBarrier(float Penalty, float Muffle, bool Blocks)
{
    public static readonly AcousticBarrier None = default;

    public readonly bool IsNone => Penalty <= 0 && Muffle <= 0 && !Blocks;

    /// <summary>
    /// Combines two barriers in series.
    /// </summary>
    public readonly AcousticBarrier Combine(AcousticBarrier other)
    {
        return new AcousticBarrier(
            Penalty + other.Penalty,
            AcousticMath.CombineMuffle(Muffle, other.Muffle),
            Blocks || other.Blocks);
    }
}

/// <summary>
/// Everything the propagation algorithm needs to know about one tile.
/// </summary>
public record struct AcousticTile
{
    /// <summary>
    /// The tile exists (has a floor) and sound can enter it at all.
    /// Space tiles and tiles in vacuum are not open.
    /// </summary>
    public bool Open;

    /// <summary>
    /// Pressure in kPa. Below the minimum, sound does not travel through the tile.
    /// Ignored for tiles fully occupied by a barrier (closed doors etc. have no air of their own).
    /// </summary>
    public float Pressure;

    /// <summary>
    /// A barrier filling the whole tile (wall, closed door, full window). Paid once when entering the tile.
    /// </summary>
    public AcousticBarrier Fill;

    /// <summary>
    /// Barriers on the edges of the tile (directional windows), indexed by <see cref="AcousticDirection"/>.
    /// </summary>
    public AcousticBarrier EdgeNorth, EdgeEast, EdgeSouth, EdgeWest;

    public static readonly AcousticTile Closed = default;

    public readonly AcousticBarrier GetEdge(AcousticDirection dir)
    {
        return dir switch
        {
            AcousticDirection.North => EdgeNorth,
            AcousticDirection.East => EdgeEast,
            AcousticDirection.South => EdgeSouth,
            _ => EdgeWest,
        };
    }

    public void SetEdge(AcousticDirection dir, AcousticBarrier barrier)
    {
        switch (dir)
        {
            case AcousticDirection.North: EdgeNorth = EdgeNorth.Combine(barrier); break;
            case AcousticDirection.East: EdgeEast = EdgeEast.Combine(barrier); break;
            case AcousticDirection.South: EdgeSouth = EdgeSouth.Combine(barrier); break;
            default: EdgeWest = EdgeWest.Combine(barrier); break;
        }
    }

    public readonly bool HasEdges => !EdgeNorth.IsNone || !EdgeEast.IsNone || !EdgeSouth.IsNone || !EdgeWest.IsNone;
}

public enum AcousticDirection : byte
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// Supplies tile data to <see cref="AcousticField"/>. Lets the algorithm run against the real game world or
/// a synthetic grid in tests.
/// </summary>
public interface IAcousticTileSource
{
    AcousticTile GetTile(Vector2i position);
}

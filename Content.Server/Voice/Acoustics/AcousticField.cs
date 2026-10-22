namespace Content.Server.Voice.Acoustics;

/// <summary>
/// Result of propagating sound from one tile: the effective acoustic distance and muffling to every reachable tile.
/// </summary>
public record struct AcousticNode(float Distance, float Muffle);

/// <summary>
/// Settings for <see cref="AcousticField.Compute"/>.
/// </summary>
public record struct AcousticSettings(float MaxDistance, float MinPressure)
{
    /// <summary>
    /// Safety cap on visited tiles.
    /// </summary>
    public int MaxNodes = 4096;
}

/// <summary>
/// Sound propagation from a single origin tile across a grid, as a shortest-path search (Dijkstra) where the cost is
/// the effective acoustic distance: path length plus penalties for every barrier crossed. Walls that block, vacuum and
/// space stop propagation; doors, windows and similar add distance and muffling.
/// </summary>
/// <remarks>
/// The result only depends on the origin and the tile data, so it is computed once per speaker position and reused
/// for every listener and every voice packet until something relevant changes.
/// Cost is O(n log n) in the number of tiles within range (about 300-400 for a 10 tile range).
/// </remarks>
public sealed class AcousticField
{
    private static readonly float Diagonal = MathF.Sqrt(2f);

    private readonly Dictionary<Vector2i, AcousticNode> _nodes = new();
    private readonly Dictionary<Vector2i, AcousticTile> _tileCache = new();
    private readonly PriorityQueue<Vector2i, float> _queue = new();

    public Vector2i Origin { get; private set; }

    public int NodeCount => _nodes.Count;

    public IReadOnlyDictionary<Vector2i, AcousticNode> Nodes => _nodes;

    public bool TryGetNode(Vector2i tile, out AcousticNode node)
    {
        return _nodes.TryGetValue(tile, out node);
    }

    public void Clear()
    {
        _nodes.Clear();
    }

    public void Compute(IAcousticTileSource source, Vector2i origin, AcousticSettings settings)
    {
        _nodes.Clear();
        _tileCache.Clear();
        _queue.Clear();
        Origin = origin;

        var originTile = GetTile(source, origin);

        // Speaker is in vacuum or inside a solid object: nothing propagates.
        if (!originTile.Open || (originTile.Fill.IsNone && originTile.Pressure < settings.MinPressure))
            return;

        _nodes[origin] = new AcousticNode(0, 0);
        _queue.Enqueue(origin, 0);

        while (_queue.TryDequeue(out var pos, out var dist))
        {
            var node = _nodes[pos];
            if (dist > node.Distance)
                continue; // stale queue entry

            if (_nodes.Count >= settings.MaxNodes)
                break;

            var tile = GetTile(source, pos);

            // Cardinal neighbours.
            for (var d = 0; d < 4; d++)
            {
                var dir = (AcousticDirection) d;
                var next = pos + Offset(dir);
                var nextTile = GetTile(source, next);

                if (!CanEnter(nextTile, settings))
                    continue;

                var crossing = tile.GetEdge(dir)
                    .Combine(nextTile.GetEdge(Opposite(dir)))
                    .Combine(nextTile.Fill);

                if (crossing.Blocks)
                    continue;

                Relax(next, node.Distance + 1f + crossing.Penalty, AcousticMath.CombineMuffle(node.Muffle, crossing.Muffle), settings);
            }

            // Diagonal neighbours: only through open, barrier-free corners so sound can't squeeze between walls.
            if (!tile.Fill.IsNone && pos != origin || tile.HasEdges)
                continue;

            for (var d = 0; d < 4; d++)
            {
                var a = (AcousticDirection) d;
                var b = (AcousticDirection) ((d + 1) % 4);
                var sideA = pos + Offset(a);
                var sideB = pos + Offset(b);
                var corner = pos + Offset(a) + Offset(b);

                if (!IsClear(GetTile(source, sideA), settings) || !IsClear(GetTile(source, sideB), settings))
                    continue;

                if (!IsClear(GetTile(source, corner), settings))
                    continue;

                Relax(corner, node.Distance + Diagonal, node.Muffle, settings);
            }
        }
    }

    private void Relax(Vector2i pos, float distance, float muffle, AcousticSettings settings)
    {
        if (distance > settings.MaxDistance)
            return;

        if (_nodes.TryGetValue(pos, out var existing))
        {
            if (existing.Distance < distance || existing.Distance == distance && existing.Muffle <= muffle)
                return;
        }

        _nodes[pos] = new AcousticNode(distance, muffle);
        _queue.Enqueue(pos, distance);
    }

    private static bool CanEnter(AcousticTile tile, AcousticSettings settings)
    {
        if (!tile.Open)
            return false;

        // Barrier tiles (closed doors...) carry sound by their own rules; air tiles need enough pressure.
        return !tile.Fill.IsNone || tile.Pressure >= settings.MinPressure;
    }

    private static bool IsClear(AcousticTile tile, AcousticSettings settings)
    {
        return tile.Open && tile.Fill.IsNone && !tile.HasEdges && tile.Pressure >= settings.MinPressure;
    }

    private AcousticTile GetTile(IAcousticTileSource source, Vector2i pos)
    {
        if (_tileCache.TryGetValue(pos, out var tile))
            return tile;

        tile = source.GetTile(pos);
        _tileCache[pos] = tile;
        return tile;
    }

    public static Vector2i Offset(AcousticDirection dir)
    {
        return dir switch
        {
            AcousticDirection.North => new Vector2i(0, 1),
            AcousticDirection.East => new Vector2i(1, 0),
            AcousticDirection.South => new Vector2i(0, -1),
            _ => new Vector2i(-1, 0),
        };
    }

    public static AcousticDirection Opposite(AcousticDirection dir)
    {
        return (AcousticDirection) (((int) dir + 2) % 4);
    }
}

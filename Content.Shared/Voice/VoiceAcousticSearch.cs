using System;
using System.Collections.Generic;

namespace Content.Shared.Voice;

/// <summary>A snapshot node: map/grid identity belongs to the adapter, never just world coordinates.</summary>
public readonly record struct VoiceAcousticTile(int X, int Y);
public readonly record struct VoiceAcousticMedium(bool Blocked, float Pressure, float ExtraDistance);

/// <summary>
/// Bounded four-neighbour Dijkstra search. Run once per active speaker per routing epoch,
/// never per recipient or audio packet. Vacuum and unknown atmosphere fail closed.
/// Each result contains shortest acoustic cost, including configured barrier losses.
/// </summary>
public static class VoiceAcousticSearch
{
    public static Dictionary<VoiceAcousticTile, float> Search(
        VoiceAcousticTile origin,
        float range,
        float minimumPressure,
        int nodeBudget,
        Func<VoiceAcousticTile, VoiceAcousticMedium> sample)
    {
        var result = new Dictionary<VoiceAcousticTile, float>();
        if (!float.IsFinite(range) || range <= 0 || range > 64 ||
            !float.IsFinite(minimumPressure) || minimumPressure < 0 || nodeBudget <= 0)
            return result;
        var source = sample(origin);
        if (!Passes(source, minimumPressure))
            return result;
        var queue = new Robust.Shared.Utility.PriorityQueue<(VoiceAcousticTile Tile, float Cost)>(
            Comparer<(VoiceAcousticTile Tile, float Cost)>.Create((a, b) => b.Cost.CompareTo(a.Cost)));
        result.Add(origin, 0);
        queue.Add((origin, 0));
        var visited = 0;
        while (queue.Count > 0 && visited++ < nodeBudget)
        {
            var (tile, cost) = queue.Take();
            if (cost != result[tile])
                continue;
            Visit(new VoiceAcousticTile(tile.X + 1, tile.Y));
            Visit(new VoiceAcousticTile(tile.X - 1, tile.Y));
            Visit(new VoiceAcousticTile(tile.X, tile.Y + 1));
            Visit(new VoiceAcousticTile(tile.X, tile.Y - 1));
            void Visit(VoiceAcousticTile next)
            {
                // Manhattan radius limits both sampling and allocations.
                if (Math.Abs(next.X - origin.X) + Math.Abs(next.Y - origin.Y) > range)
                    return;
                // All added losses are non-negative; skip already cheaper routes before ECS sampling.
                if (result.TryGetValue(next, out var previous) && previous <= cost + 1)
                    return;
                var medium = sample(next);
                if (!Passes(medium, minimumPressure))
                    return;
                var candidate = cost + 1 + medium.ExtraDistance;
                if (candidate > range || (result.TryGetValue(next, out var old) && old <= candidate))
                    return;
                result[next] = candidate;
                queue.Add((next, candidate));
            }
        }
        // Every discovered route is valid; exhausted budgets may conservatively miss routes.
        return result;
    }

    private static bool Passes(VoiceAcousticMedium medium, float minimumPressure) =>
        !medium.Blocked && float.IsFinite(medium.Pressure) && medium.Pressure >= minimumPressure &&
        float.IsFinite(medium.ExtraDistance) && medium.ExtraDistance >= 0;
}

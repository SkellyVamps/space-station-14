using System;
using System.Collections.Generic;
using Content.Server.Voice.Acoustics;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Server.Voice;

/// <summary>
/// Acoustic propagation against synthetic maps. Map legend:
/// '.' pressurized floor, '#' wall (blocks), 'D' closed door, 'W' window, ' ' space/no tile,
/// 'v' floor in vacuum, 'B' blast door, '|' floor with a directional window on its east edge.
/// Row 0 of the string array is the top (highest y).
/// </summary>
[TestFixture, Parallelizable]
[TestOf(typeof(AcousticField))]
public sealed class AcousticFieldTest
{
    private const float Range = 10f;
    private const float MinPressure = 10f;

    private static readonly AcousticBarrier Door = new(3f, 0.7f, false);
    private static readonly AcousticBarrier Window = new(2.5f, 0.6f, false);
    private static readonly AcousticBarrier Wall = new(0f, 1f, true);
    private static readonly AcousticBarrier Thindow = new(1.5f, 0.45f, false);

    private sealed class MapSource : IAcousticTileSource
    {
        private readonly Dictionary<Vector2i, AcousticTile> _tiles = new();
        public int Reads;

        public MapSource(string[] rows)
        {
            for (var row = 0; row < rows.Length; row++)
            {
                var y = rows.Length - 1 - row;
                for (var x = 0; x < rows[row].Length; x++)
                {
                    var tile = rows[row][x] switch
                    {
                        '.' => new AcousticTile { Open = true, Pressure = 101f },
                        'v' => new AcousticTile { Open = true, Pressure = 0f },
                        '#' => new AcousticTile { Open = true, Fill = Wall },
                        'D' => new AcousticTile { Open = true, Fill = Door },
                        'W' => new AcousticTile { Open = true, Fill = Window },
                        'B' => new AcousticTile { Open = true, Fill = new AcousticBarrier(0, 1, true) },
                        '|' => new AcousticTile { Open = true, Pressure = 101f, EdgeEast = Thindow },
                        _ => AcousticTile.Closed,
                    };
                    _tiles[new Vector2i(x, y)] = tile;
                }
            }
        }

        public AcousticTile GetTile(Vector2i position)
        {
            Reads++;
            return _tiles.GetValueOrDefault(position, AcousticTile.Closed);
        }
    }

    private static AcousticField Compute(string[] rows, Vector2i origin)
    {
        var field = new AcousticField();
        field.Compute(new MapSource(rows), origin, new AcousticSettings(Range, MinPressure));
        return field;
    }

    [Test]
    public void OpenRoomDistanceIsPathLength()
    {
        var field = Compute(new[]
        {
            "............",
            "............",
            "............",
        }, new Vector2i(0, 1));

        Assert.That(field.TryGetNode(new Vector2i(5, 1), out var node));
        Assert.That(node.Distance, Is.EqualTo(5f).Within(0.01f));
        Assert.That(node.Muffle, Is.Zero);

        // Diagonal steps are allowed in open space.
        Assert.That(field.TryGetNode(new Vector2i(2, 2), out var diag));
        Assert.That(diag.Distance, Is.EqualTo(1f + MathF.Sqrt(2f)).Within(0.01f));

        // Beyond max range: nothing.
        Assert.That(field.TryGetNode(new Vector2i(11, 1), out _), Is.False);
    }

    [Test]
    public void SolidWallBlocks()
    {
        // Scenario B: listener is 2 tiles away in a straight line, but behind a wall with no way around.
        var field = Compute(new[]
        {
            "..#..",
            "..#..",
            "..#..",
        }, new Vector2i(1, 1));

        Assert.That(field.TryGetNode(new Vector2i(3, 1), out _), Is.False);
    }

    [Test]
    public void SoundGoesAroundWalls()
    {
        // Wall with a gap at the top: the path around is longer than the straight line.
        var field = Compute(new[]
        {
            ".....",
            "..#..",
            "..#..",
        }, new Vector2i(1, 0));

        Assert.That(field.TryGetNode(new Vector2i(3, 0), out var node));
        Assert.That(node.Distance, Is.GreaterThan(3.5f));
        Assert.That(node.Muffle, Is.Zero);
    }

    [Test]
    public void ClosedDoorMuffles()
    {
        // Scenario C: closed door adds distance and muffling.
        var field = Compute(new[]
        {
            "##.##",
            "..D..",
            "##.##",
        }, new Vector2i(0, 1));

        // Top/bottom middle tiles are floor too, but enclosed by walls apart from the door tile; the only
        // route from left to right is through the door.
        Assert.That(field.TryGetNode(new Vector2i(4, 1), out var node));
        Assert.That(node.Distance, Is.EqualTo(4f + Door.Penalty).Within(0.01f));
        Assert.That(node.Muffle, Is.EqualTo(Door.Muffle).Within(0.001f));
    }

    [Test]
    public void OpenDoorPassesFreely()
    {
        // Scenario C (open): the door tile is just floor while open.
        var field = Compute(new[]
        {
            "#####",
            ".....",
            "#####",
        }, new Vector2i(0, 1));

        Assert.That(field.TryGetNode(new Vector2i(4, 1), out var node));
        Assert.That(node.Distance, Is.EqualTo(4f).Within(0.01f));
        Assert.That(node.Muffle, Is.Zero);
    }

    [Test]
    public void BlastDoorBlocks()
    {
        var field = Compute(new[]
        {
            "#####",
            "..B..",
            "#####",
        }, new Vector2i(0, 1));

        Assert.That(field.TryGetNode(new Vector2i(4, 1), out _), Is.False);
    }

    [Test]
    public void WindowTransmitsMoreThanWall()
    {
        var window = Compute(new[] { "..W.." }, new Vector2i(0, 0));
        var wall = Compute(new[] { "..#.." }, new Vector2i(0, 0));

        Assert.That(window.TryGetNode(new Vector2i(4, 0), out var node));
        Assert.That(node.Muffle, Is.EqualTo(Window.Muffle).Within(0.001f));
        Assert.That(wall.TryGetNode(new Vector2i(4, 0), out _), Is.False);
    }

    [Test]
    public void DirectionalWindowOnlyAffectsItsEdge()
    {
        // '|' has a window on its east edge.
        var field = Compute(new[] { ".|..." }, new Vector2i(0, 0));

        Assert.That(field.TryGetNode(new Vector2i(1, 0), out var onTile));
        Assert.That(onTile.Muffle, Is.Zero, "Entering the tile from the west isn't through the window.");

        Assert.That(field.TryGetNode(new Vector2i(2, 0), out var through));
        Assert.That(through.Muffle, Is.EqualTo(Thindow.Muffle).Within(0.001f));
        Assert.That(through.Distance, Is.EqualTo(2f + Thindow.Penalty).Within(0.01f));
    }

    [Test]
    public void VacuumGapBlocks()
    {
        // Scenario D: two pressurized rooms separated by a vacuum corridor.
        var field = Compute(new[] { "..vv.." }, new Vector2i(0, 0));
        Assert.That(field.TryGetNode(new Vector2i(5, 0), out _), Is.False);

        // And open space (no tiles at all).
        var space = Compute(new[] { "..  .." }, new Vector2i(0, 0));
        Assert.That(space.TryGetNode(new Vector2i(5, 0), out _), Is.False);
    }

    [Test]
    public void SpeakerInVacuumIsSilent()
    {
        var field = Compute(new[] { "v...." }, new Vector2i(0, 0));
        Assert.That(field.NodeCount, Is.Zero);
    }

    [Test]
    public void SealedRoomDoesNotLeakIntoVacuum()
    {
        // Pressurized room sealed by a door, vacuum outside: nothing outside the door is reachable.
        var field = Compute(new[]
        {
            "#####",
            "#..Dvvv",
            "#####",
        }, new Vector2i(1, 1));

        Assert.That(field.TryGetNode(new Vector2i(5, 1), out _), Is.False);
        Assert.That(field.TryGetNode(new Vector2i(3, 1), out _), Is.True, "The door itself still conducts.");
    }

    [Test]
    public void NoCornerCutting()
    {
        // Two walls touching diagonally must not let sound slip through the corner.
        var field = Compute(new[]
        {
            ".#",
            "#.",
        }, new Vector2i(0, 1));

        Assert.That(field.TryGetNode(new Vector2i(1, 0), out _), Is.False);
    }

    [Test]
    public void TilesAreReadOncePerCompute()
    {
        var source = new MapSource(new[]
        {
            "..........",
            "..........",
            "..........",
            "..........",
        });

        var field = new AcousticField();
        field.Compute(source, new Vector2i(0, 0), new AcousticSettings(Range, MinPressure));
        Assert.That(source.Reads, Is.LessThanOrEqualTo(40 + 4 * 14), "Each tile (plus the border) should be read once.");
    }

    [Test]
    public void FalloffCurve()
    {
        Assert.That(AcousticMath.Falloff(1f, 2f, 10f, 1.6f), Is.EqualTo(1f));
        Assert.That(AcousticMath.Falloff(2f, 2f, 10f, 1.6f), Is.EqualTo(1f));
        Assert.That(AcousticMath.Falloff(10f, 2f, 10f, 1.6f), Is.EqualTo(0f));

        var mid = AcousticMath.Falloff(5f, 2f, 10f, 1.6f);
        var far = AcousticMath.Falloff(8f, 2f, 10f, 1.6f);
        var edge = AcousticMath.Falloff(9.5f, 2f, 10f, 1.6f);
        Assert.That(mid, Is.InRange(0.4f, 0.6f), "3-5 tiles: normal falloff");
        Assert.That(far, Is.InRange(0.05f, 0.2f), "6-8 tiles: quiet");
        Assert.That(edge, Is.InRange(0.001f, 0.05f), "9-10 tiles: barely audible");
    }

    [Test]
    public void PressureFactor()
    {
        Assert.That(AcousticMath.PressureFactor(0f, 10f, 70f), Is.Zero);
        Assert.That(AcousticMath.PressureFactor(101f, 10f, 70f), Is.EqualTo(1f));
        Assert.That(AcousticMath.PressureFactor(40f, 10f, 70f), Is.EqualTo(0.5f).Within(0.001f));
    }

    [Test]
    public void MuffleCombines()
    {
        Assert.That(AcousticMath.CombineMuffle(0.5f, 0.5f), Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(AcousticMath.CombineMuffle(0f, 0.3f), Is.EqualTo(0.3f).Within(0.001f));
        Assert.That(AcousticMath.CombineMuffle(1f, 0.3f), Is.EqualTo(1f).Within(0.001f));
    }
}

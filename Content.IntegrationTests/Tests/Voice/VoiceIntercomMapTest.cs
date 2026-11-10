using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.Configuration;
using Content.Server.GameTicking;
using Content.Shared.Maps;
using Content.Server.Voice;
using Robust.Shared.EntitySerialization;
using Content.Shared.CCVar;
using Content.Shared.Radio.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Voice;

/// <summary>
/// Every intercom on real station maps, mounted on walls in every direction, must be heard by someone standing on the
/// floor in front of it. (Sound can't start inside a wall: there's no air there.)
/// </summary>
[TestFixture]
[TestOf(typeof(VoiceAudibilitySystem))]
public sealed class VoiceIntercomMapTest : GameTest
{
    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    [TestCase("Box")]
    [TestCase("Bagel")]
    [TestCase("Packed")]
    [TestCase("Saltern")]
    public async Task EveryIntercomIsHeardInFrontOfIt(string mapProto)
    {
        var server = Pair.Server;
        var ent = server.EntMan;
        var audibility = ent.System<VoiceAudibilitySystem>();
        var mapSystem = ent.System<SharedMapSystem>();

        var mapId = MapId.Nullspace;
        await server.WaitAssertion(() =>
        {
            // As a round would, with the map initialised (air, power...).
            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ent.System<ServerGameTicker>().LoadGameMap(server.ProtoMan.Index<GameMapPrototype>(mapProto), out mapId, opts);
        });

        await RunTicksSync(5);

        var failures = new List<string>();
        var checkedCount = 0;

        await server.WaitAssertion(() =>
        {
            var intercoms = new List<EntityUid>();
            var query = ent.EntityQueryEnumerator<IntercomComponent, TransformComponent>();
            var total = 0;
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                // Only this map's: the test server may still have earlier tests' maps loaded.
                if (xform.MapID != mapId)
                    continue;

                total++;
                if (xform.GridUid != null)
                    intercoms.Add(uid);
            }

            TestContext.Out.WriteLine($"{total} intercoms, {intercoms.Count} on grids");

            foreach (var uid in intercoms)
            {
                var from = audibility.GetAcousticPosition(uid);
                if (from.Grid is not { } grid || !ent.TryGetComponent(grid, out MapGridComponent gridComp))
                    continue;

                checkedCount++;

                // Someone standing on the tile the intercom sounds on.
                var listener = ent.SpawnAtPosition(null, mapSystem.GridTileToLocal(grid, gridComp, from.Tile));
                var to = audibility.GetAcousticPosition(listener);

                if (!audibility.TryGetAudibility(from, to, out var gain, out _) || gain <= 0)
                {
                    var rotation = ent.GetComponent<TransformComponent>(uid).LocalRotation;
                    var atmos = ent.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
                    var own = mapSystem.TileIndicesFor(grid, gridComp, ent.GetComponent<TransformComponent>(uid).Coordinates);
                    foreach (var d in new[] { new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(0, -1), new Vector2i(-1, 0) })
                    {
                        var air = atmos.GetTileMixture(grid, null, own + d);
                        TestContext.Out.WriteLine($"  side {d}: {air?.Pressure ?? -1:0.0} kPa, anchored: " +
                            string.Join(",", mapSystem.GetAnchoredEntities(grid, gridComp, own + d).Select(e => ent.ToPrettyString(e).Prototype)));
                    }

                    failures.Add($"{ent.ToPrettyString(uid)} at {ent.GetComponent<TransformComponent>(uid).Coordinates} " +
                                 $"(rotation {rotation.Degrees:0}, sounding on tile {from.Tile})");
                }

                ent.DeleteEntity(listener);
            }
        });

        Assert.That(checkedCount, Is.GreaterThan(0), "the map has intercoms");
        Assert.That(failures, Is.Empty,
            $"{failures.Count} of {checkedCount} intercoms can't be heard in front of them:\n" + string.Join("\n", failures));
    }
}

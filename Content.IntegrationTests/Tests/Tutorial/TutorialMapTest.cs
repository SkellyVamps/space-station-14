using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Shared.CCVar;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Content.Shared.Paper;
using Content.Shared.Teleportation.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Tutorial;

/// <summary>
/// Runs the Tutorial Station for a while and checks that a solo player can actually use it:
/// everything is powered, gravity is on, the air is breathable and every portal has a partner.
/// </summary>
[TestFixture]
public sealed class TutorialMapTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    // The Electrical Workshop is off the grid on purpose: the student wires up this light.
    private const string WorkshopTargetLight = "workshop target light";

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task TutorialStationWorks()
    {
        var server = Pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var ticker = entMan.System<ServerGameTicker>();
        var atmos = entMan.System<AtmosphereSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        var mapId = MapId.Nullspace;
        await server.WaitPost(() =>
        {
            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            ticker.LoadGameMap(protoMan.Index<GameMapPrototype>("Tutorial"), out mapId, opts);
        });

        // Let RTGs ramp up, APCs charge and atmos settle.
        await Pair.RunSeconds(20);

        await server.WaitAssertion(() =>
        {
            var grid = mapSys.GetAllGrids(mapId).Single().Owner;

            Assert.That(entMan.GetComponent<GravityComponent>(grid).Enabled, "Gravity is off.");

            // Power: every powered machine outside the Electrical Workshop should be running.
            var unpowered = new List<string>();
            var query = entMan.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out var receiver, out var xform, out var meta))
            {
                if (xform.GridUid != grid || !xform.Anchored || !receiver.NeedsPower || receiver.PowerDisabled || receiver.Powered)
                    continue;
                if (meta.EntityName == WorkshopTargetLight)
                    continue;
                unpowered.Add($"{meta.EntityPrototype?.ID} at {xform.Coordinates.Position}");
            }

            Assert.That(unpowered, Is.Empty, $"Unpowered on the Tutorial Station:\n{string.Join("\n", unpowered.Take(30))}");

            // Air: breathable at every lesson paper, which sits just inside each room's door.
            var stale = new List<string>();
            var papers = entMan.EntityQueryEnumerator<PaperComponent, TransformComponent>();
            while (papers.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != grid)
                    continue;
                var mix = atmos.GetTileMixture((uid, xform));
                if (mix == null || mix.Pressure < 90 || mix.Pressure > 115)
                    stale.Add($"{xform.Coordinates.Position}: {mix?.Pressure ?? 0:F0} kPa");
            }

            Assert.That(stale, Is.Empty, $"Bad air pressure:\n{string.Join("\n", stale.Take(30))}");

            // Portals: each one has exactly one partner, and the partner points back.
            var portals = entMan.EntityQueryEnumerator<PortalComponent, LinkedEntityComponent, MetaDataComponent>();
            var count = 0;
            while (portals.MoveNext(out var uid, out _, out var link, out var meta))
            {
                count++;
                Assert.That(link.LinkedEntities, Has.Count.EqualTo(1), $"Portal '{meta.EntityName}' should have one partner.");
                var partner = link.LinkedEntities.First();
                Assert.That(entMan.GetComponent<LinkedEntityComponent>(partner).LinkedEntities, Does.Contain(uid),
                    $"Portal '{meta.EntityName}' is not linked back.");
            }

            Assert.That(count, Is.GreaterThanOrEqualTo(100), "Expected a portal pair for every room.");
        });
    }
}

#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using Content.Shared.Tutorial;
using Content.Shared.Decals;
using Content.Server.Decals;
using Content.Shared.Teleportation.Systems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Tutorial;

/// <summary>
/// Builds Resources/Maps/tutorial.yml from the layout JSON written by Tools/tutorial_map/generate.py.
/// This is a tool, not a regular test: it only runs when asked for by name, e.g.
/// <c>dotnet test Content.IntegrationTests --filter "FullyQualifiedName~TutorialMapBuilder"</c>
/// with TUTORIAL_LAYOUT and TUTORIAL_OUTPUT set to file paths.
/// </summary>
[TestFixture]
[Explicit("Map generation tool; run on demand only.")]
public sealed class TutorialMapBuilder : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private sealed record LayoutEntity(string P, float X, float Y, float R, string? Paper, string? Name, string? Key, string? LinkTo, string? Portal, int[]? Reset);

    private sealed record LayoutDecal(string Id, float X, float Y, string? Color, float R);

    private sealed record Layout(string StationId, string GridName, List<List<JsonElement>> Tiles, List<LayoutEntity> Ents, List<LayoutDecal>? Decals);

    [Test]
    [EnsureCVar(Side.Server, typeof(CCVars), nameof(CCVars.GridFill), false)]
    public async Task BuildTutorialMap()
    {
        var layoutPath = Environment.GetEnvironmentVariable("TUTORIAL_LAYOUT");
        var outputPath = Environment.GetEnvironmentVariable("TUTORIAL_OUTPUT");
        Assert.That(layoutPath, Is.Not.Null.And.Not.Empty, "Set TUTORIAL_LAYOUT to the layout JSON path.");
        Assert.That(outputPath, Is.Not.Null.And.Not.Empty, "Set TUTORIAL_OUTPUT to the map YAML path to write.");

        var layout = JsonSerializer.Deserialize<Layout>(
            File.ReadAllText(layoutPath!),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var server = Pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var paperSys = entMan.System<PaperSystem>();
        var atmosSys = entMan.System<AtmosphereSystem>();
        var metaSys = entMan.System<MetaDataSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var decalSys = entMan.System<DecalSystem>();
        var linkSys = entMan.System<DeviceLinkSystem>();
        var portalLinkSys = entMan.System<LinkedEntitySystem>();

        // Fail early, with every bad id at once, instead of on the first spawn.
        var unknown = layout.Ents.Select(e => e.P).Distinct().Where(p => !protoMan.HasIndex<EntityPrototype>(p)).ToList();
        var unknownTiles = layout.Tiles.Select(t => t[2].GetString()!).Distinct().Where(t => !tileDefs.TryGetDefinition(t, out _)).ToList();
        Assert.That(unknown, Is.Empty, $"Unknown entity prototypes: {string.Join(", ", unknown)}");
        Assert.That(unknownTiles, Is.Empty, $"Unknown tiles: {string.Join(", ", unknownTiles)}");
        var unknownDecals = (layout.Decals ?? new()).Select(d => d.Id).Distinct().Where(d => !protoMan.HasIndex<DecalPrototype>(d)).ToList();
        Assert.That(unknownDecals, Is.Empty, $"Unknown decals: {string.Join(", ", unknownDecals)}");

        await server.WaitAssertion(() =>
        {
            var mapUid = mapSys.CreateMap(out var mapId, runMapInit: false);
            var grid = mapSys.CreateGridEntity(mapId);
            metaSys.SetEntityName(grid, layout.GridName);

            var tiles = layout.Tiles
                .Select(t => (new Vector2i(t[0].GetInt32(), t[1].GetInt32()), new Tile(tileDefs[t[2].GetString()!].TileId)))
                .ToList();
            mapSys.SetTiles(grid, tiles);

            var keyed = new Dictionary<string, EntityUid>();
            var links = new List<(EntityUid Source, string Sink)>();
            var portals = new List<(EntityUid A, string B)>();

            foreach (var e in layout.Ents)
            {
                var uid = entMan.SpawnEntity(e.P, new EntityCoordinates(grid, new Vector2(e.X, e.Y)));
                if (e.R != 0)
                    xformSys.SetLocalRotation(uid, new Angle(e.R));
                if (e.Name != null)
                    metaSys.SetEntityName(uid, e.Name);
                if (e.Paper != null && entMan.TryGetComponent(uid, out PaperComponent? paper))
                    paperSys.SetContent((uid, paper), e.Paper);
                if (e.Key != null)
                    keyed[e.Key] = uid;
                if (e.LinkTo != null)
                    links.Add((uid, e.LinkTo));
                if (e.Portal != null)
                    portals.Add((uid, e.Portal));
                if (e.Reset is { Length: 4 } r)
                    entMan.GetComponent<TutorialRoomResetComponent>(uid).Bounds = new Box2(r[0], r[1], r[2] + 1, r[3] + 1);
            }

            // Consoles such as cargo orders or artifact analysis only work when linked to their machine.
            foreach (var (source, sink) in links)
            {
                Assert.That(keyed.ContainsKey(sink), $"Link target {sink} was never spawned.");
                linkSys.LinkDefaults(null, source, keyed[sink]);
            }

            foreach (var d in layout.Decals ?? new())
            {
                var color = d.Color != null ? Color.FromHex(d.Color) : (Color?) null;
                Assert.That(decalSys.TryAddDecal(d.Id, new EntityCoordinates(grid, new Vector2(d.X, d.Y)), out _, color, new Angle(d.R), cleanable: true),
                    $"Could not place decal {d.Id} at {d.X},{d.Y}.");
            }

            // Portal pairs, linked both ways like the debug map's gateways.
            foreach (var (a, b) in portals)
            {
                Assert.That(keyed.ContainsKey(b), $"Portal partner {b} was never spawned.");
                Assert.That(portalLinkSys.TryLink(a, keyed[b]), $"Could not link portal to {b}.");
            }

            // BecomesStation is "mapping only"; this tool is a mapper, so it sets the id directly.
#pragma warning disable RA0002
            entMan.EnsureComponent<BecomesStationComponent>(grid).Id = layout.StationId;
#pragma warning restore RA0002
            entMan.EnsureComponent<GravityComponent>(grid);
            var atmos = entMan.EnsureComponent<GridAtmosphereComponent>(grid);
            entMan.EnsureComponent<GasTileOverlayComponent>(grid);
            atmosSys.RebuildGridAtmosphere((grid.Owner, atmos, entMan.GetComponent<MapGridComponent>(grid)));

            using var writer = new StreamWriter(outputPath!);
            Assert.That(loader.TrySaveMap(mapUid, writer), "Saving the tutorial map failed.");
        });
    }
}

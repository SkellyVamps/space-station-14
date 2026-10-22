using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Doors.Systems;
using Content.Server.Voice;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Voice;

/// <summary>
/// Runs the real voice audibility system against a real grid: walls, airlocks, lockers, atmosphere.
/// Covers scenarios A-D from the design (proximity, wall, door, vacuum).
/// </summary>
[TestFixture]
[TestOf(typeof(VoiceAudibilitySystem))]
public sealed class VoiceAudibilityTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: VoiceTestDummy
  components:
  - type: Physics
    bodyType: Dynamic
";

    private EntityUid _map;
    private Entity<MapGridComponent> _grid;

    private IEntityManager Ent => Pair.Server.EntMan;

    /// <summary>
    /// Builds a 15x3 corridor of plating (x = 0..14, y = -1..1) with breathable map atmosphere.
    /// </summary>
    private async Task Setup()
    {
        var mapData = await Pair.CreateTestMap();
        _map = mapData.MapUid;
        _grid = mapData.Grid;

        await Pair.Server.WaitPost(() =>
        {
            var mapSys = Ent.System<SharedMapSystem>();
            var tile = mapData.Tile.Tile;
            for (var x = 0; x <= 14; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    mapSys.SetTile(_grid.Owner, _grid.Comp, new Vector2i(x, y), tile);
                }
            }

            SetAir(true);
        });

        await RunTicksSync(5);
    }

    private void SetAir(bool breathable)
    {
        var atmos = Ent.System<AtmosphereSystem>();
        var gridAtmos = Ent.GetComponent<GridAtmosphereComponent>(_grid);

        if (breathable)
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            atmos.SetMapAtmosphere(_map, false, air);
            atmos.RebuildGridAtmosphere((_grid.Owner, gridAtmos, _grid.Comp));
        }
        else
        {
            atmos.SetMapAtmosphere(_map, true, GasMixture.SpaceGas);
            foreach (var tile in gridAtmos.Tiles.Values)
            {
                if (tile.Air is { Immutable: false } mix)
                    mix.Clear();
            }
        }

        Ent.System<VoiceAudibilitySystem>().InvalidateAll();
    }

    private EntityUid SpawnAt(string proto, int x, int y)
    {
        return Ent.SpawnAtPosition(proto, new EntityCoordinates(_grid.Owner, new Vector2(x + 0.5f, y + 0.5f)));
    }

    private bool Hear(EntityUid speaker, EntityUid listener, out float gain, out float muffle)
    {
        var sys = Ent.System<VoiceAudibilitySystem>();
        var s = sys.GetAcousticPosition(speaker);
        var l = sys.GetAcousticPosition(listener);
        return sys.TryGetAudibility(s, l, out gain, out muffle);
    }

    [Test]
    public async Task ProximityFalloff()
    {
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            var speaker = SpawnAt("VoiceTestDummy", 0, 0);
            var near = SpawnAt("VoiceTestDummy", 1, 0);
            var mid = SpawnAt("VoiceTestDummy", 5, 0);
            var far = SpawnAt("VoiceTestDummy", 9, 0);
            var outOfRange = SpawnAt("VoiceTestDummy", 12, 0);

            Assert.That(Hear(speaker, near, out var gNear, out var mNear));
            Assert.That(Hear(speaker, mid, out var gMid, out _));
            Assert.That(Hear(speaker, far, out var gFar, out _));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gNear, Is.EqualTo(1f).Within(0.01f));
                Assert.That(mNear, Is.Zero);
                Assert.That(gMid, Is.LessThan(gNear).And.GreaterThan(0.2f));
                Assert.That(gFar, Is.LessThan(gMid).And.GreaterThan(0f));
                Assert.That(Hear(speaker, outOfRange, out _, out _), Is.False, "beyond max range: not delivered");
            }
        });
    }

    [Test]
    public async Task WallBlocksDoorMuffles()
    {
        await Setup();

        EntityUid speaker = default, listener = default, airlock = default;

        await Pair.Server.WaitAssertion(() =>
        {
            speaker = SpawnAt("VoiceTestDummy", 1, 0);
            listener = SpawnAt("VoiceTestDummy", 5, 0);

            // A full-height wall at x = 3...
            SpawnAt("WallSolid", 3, -1);
            SpawnAt("WallSolid", 3, 1);
            var middle = SpawnAt("WallSolid", 3, 0);

            Assert.That(Hear(speaker, listener, out _, out _), Is.False, "Scenario B: wall blocks speech");

            // ...then replace its middle with an airlock.
            Ent.DeleteEntity(middle);
            airlock = SpawnAt("Airlock", 3, 0);
        });

        await RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(Ent.GetComponent<DoorComponent>(airlock).State, Is.EqualTo(DoorState.Closed));
            Assert.That(Hear(speaker, listener, out var gain, out var muffle), "closed door: muffled, not blocked");
            Assert.That(muffle, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(gain, Is.GreaterThan(0f));

            Ent.System<DoorSystem>().StartOpening(airlock);
        });

        await PoolManager.WaitUntil(Pair.Server, () => Ent.GetComponent<DoorComponent>(airlock).State == DoorState.Open);
        await RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(Hear(speaker, listener, out var gain, out var muffle), "Scenario C: open door passes speech");
            Assert.That(muffle, Is.LessThan(0.05f));
            Assert.That(gain, Is.GreaterThan(0.3f));
        });
    }

    [Test]
    public async Task VacuumBlocks()
    {
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            var speaker = SpawnAt("VoiceTestDummy", 1, 0);
            var listener = SpawnAt("VoiceTestDummy", 3, 0);
            Assert.That(Hear(speaker, listener, out _, out _));

            SetAir(false);
            Assert.That(Hear(speaker, listener, out _, out _), Is.False, "Scenario D: no speech through vacuum");
        });
    }

    [Test]
    public async Task SpaceGapBlocks()
    {
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            // Remove a full column of floor: the two halves are separated by open space.
            var mapSys = Ent.System<SharedMapSystem>();
            for (var y = -1; y <= 1; y++)
            {
                mapSys.SetTile(_grid.Owner, _grid.Comp, new Vector2i(4, y), Tile.Empty);
            }

            var speaker = SpawnAt("VoiceTestDummy", 2, 0);
            var listener = SpawnAt("VoiceTestDummy", 6, 0);
            Assert.That(Hear(speaker, listener, out _, out _), Is.False);
        });
    }

    [Test]
    public async Task LockerMuffles()
    {
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            var speaker = SpawnAt("VoiceTestDummy", 1, 0);
            var listener = SpawnAt("VoiceTestDummy", 3, 0);
            var locker = SpawnAt("ClosetEmergency", 1, 0);

            Assert.That(Hear(speaker, listener, out _, out var openMuffle));
            Assert.That(openMuffle, Is.Zero);

            var storage = Ent.System<SharedEntityStorageSystem>();
            Assert.That(storage.Insert(speaker, locker), "insert into locker");
            storage.CloseStorage(locker);

            Assert.That(Hear(speaker, listener, out _, out var muffle), "trapped in a locker: muffled");
            Assert.That(muffle, Is.GreaterThanOrEqualTo(0.5f));

            // Two people in the same locker hear each other fine.
            var cellmate = SpawnAt("VoiceTestDummy", 1, 0);
            Assert.That(storage.Insert(cellmate, locker));
            Assert.That(Hear(speaker, cellmate, out var gain, out var sharedMuffle));
            Assert.That(sharedMuffle, Is.Zero);
            Assert.That(gain, Is.GreaterThan(0.9f));
        });
    }

    [Test]
    public async Task DifferentMapsNeverHear()
    {
        await Setup();
        var other = await Pair.CreateTestMap();

        await Pair.Server.WaitAssertion(() =>
        {
            var speaker = SpawnAt("VoiceTestDummy", 1, 0);
            var listener = Ent.SpawnAtPosition("VoiceTestDummy", new EntityCoordinates(other.Grid.Owner, new Vector2(1.5f, 0.5f)));
            Assert.That(Hear(speaker, listener, out _, out _), Is.False);
        });
    }
}

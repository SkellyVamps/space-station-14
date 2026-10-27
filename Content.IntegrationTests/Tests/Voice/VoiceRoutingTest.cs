using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Voice;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Voice;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Radio;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Content.Shared.VoiceMask;
using Robust.Shared.Audio.Codecs;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Voice;

/// <summary>
/// End-to-end voice routing: a dummy player "talks", the real client should (or should not) receive it.
/// Also covers packet validation through the real network and equipment-driven voice changes.
/// </summary>
[TestFixture]
[TestOf(typeof(VoiceSystem))]
public sealed class VoiceRoutingTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: VoiceTestRadioMob
  parent: MobHuman
  components:
  - type: IntrinsicRadioTransmitter
    channels: [ Common ]
  - type: IntrinsicRadioReceiver
  - type: ActiveRadio
    channels: [ Common ]
  - type: TelecomExempt

- type: entity
  id: VoiceTestHandheldTalker
  parent: MobHuman
  components:
  - type: IntrinsicRadioTransmitter
    channels: [ Handheld ]
  - type: TelecomExempt

- type: entity
  id: VoiceTestHandheldListener
  parent: VoiceTestHandheldTalker
  components:
  - type: IntrinsicRadioReceiver
  - type: ActiveRadio
    channels: [ Handheld ]
";

    public override PoolSettings PoolSettings => new() { DummyTicker = false, Connected = true, Dirty = true };

    private EntityUid _map;
    private Entity<MapGridComponent> _grid;
    private ICommonSession _clientSession = default!;
    private ICommonSession _dummy = default!;
    private EntityUid _listener;
    private EntityUid _speaker;
    private ushort _nextSequence = 1;

    private IEntityManager Ent => Pair.Server.EntMan;

    private byte[] _opusFrame = default!;

    private async Task Setup(string mobProto = "MobHuman", int speakerX = 3)
    {
        var mapData = await Pair.CreateTestMap();
        _map = mapData.MapUid;
        _grid = mapData.Grid;

        // A real encoded frame of a quiet tone.
        var encoder = new OpusVoiceEncoder(VoiceConstants.SampleRate);
        var pcm = new short[960];
        for (var i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (short) (MathF.Sin(i * 0.05f) * 3000);
        }

        var buffer = new byte[400];
        var length = encoder.Encode(pcm, buffer);
        _opusFrame = buffer[..length];

        _clientSession = Pair.Client.Session!;
        _dummy = await Pair.Server.AddDummySession();

        await Pair.Server.WaitPost(() =>
        {
            var mapSys = Ent.System<SharedMapSystem>();
            var tile = mapData.Tile.Tile;
            for (var x = -2; x <= 14; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    mapSys.SetTile(_grid.Owner, _grid.Comp, new Vector2i(x, y), tile);
                }
            }

            var atmos = Ent.System<AtmosphereSystem>();
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            atmos.SetMapAtmosphere(_map, false, air);
            atmos.RebuildGridAtmosphere((_grid.Owner, Ent.GetComponent<GridAtmosphereComponent>(_grid), _grid.Comp));

            var minds = Ent.System<SharedMindSystem>();
            _listener = Ent.SpawnAtPosition(mobProto, new EntityCoordinates(_grid.Owner, new Vector2(1.5f, 0.5f)));
            var mind = minds.CreateMind(_clientSession.UserId, "VoiceListener");
            minds.TransferTo(mind, _listener, mind: mind.Comp);
            Pair.Server.PlayerMan.SetAttachedEntity(_clientSession, _listener);

            _speaker = Ent.SpawnAtPosition(mobProto, new EntityCoordinates(_grid.Owner, new Vector2(speakerX + 0.5f, 0.5f)));
            Pair.Server.PlayerMan.JoinGame(_dummy);
            Pair.Server.PlayerMan.SetAttachedEntity(_dummy, _speaker);

            Ent.System<VoiceAudibilitySystem>().InvalidateAll();
        });

        await RunUntilSynced();
        await RunTicksSync(10);
    }

    private async Task Speak(ICommonSession session, bool radio = false, ushort? seq = null)
    {
        await Pair.Server.WaitPost(() =>
        {
            Ent.System<VoiceSystem>().HandleTransmit(session, new MsgVoiceTransmit
            {
                Flags = radio ? VoiceTransmitFlags.Radio : VoiceTransmitFlags.None,
                // Counting up like a real client; random numbers occasionally land in the "too old" range.
                Sequence = seq ?? _nextSequence++,
                Payload = _opusFrame,
            });
        });

        await RunTicksSync(5);
    }

    private long ClientReceived => Pair.Client.EntMan.System<VoicePlaybackSystem>().PacketsReceived;

    [Test]
    public async Task NearbyPlayerHearsAndCanMute()
    {
        await Setup();

        // Both got speaker state.
        Assert.That(Ent.HasComponent<VoiceSpeakerComponent>(_listener));
        Assert.That(Ent.HasComponent<VoiceSpeakerComponent>(_speaker));
        await Pair.Client.WaitAssertion(() =>
        {
            var local = Pair.Client.Session!.AttachedEntity;
            Assert.That(local, Is.Not.Null);
            Assert.That(Pair.Client.EntMan.HasComponent<VoiceSpeakerComponent>(local!.Value),
                "The owner's client receives its own voice state.");
        });

        // Scenario A: two players close together hear each other.
        var before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before + 1));

        var last = Pair.Client.EntMan.System<VoicePlaybackSystem>().LastPacket!.Value;
        Assert.That(last.Mode, Is.EqualTo(VoiceReceiveMode.Local));
        Assert.That(last.Speaker, Is.EqualTo(Ent.GetNetEntity(_speaker)));
        Assert.That(last.Gain, Is.GreaterThan(200));

        // Scenario H: the client mutes the speaker's player. Nothing more arrives.
        await Pair.Server.WaitPost(() =>
            Ent.System<VoiceModerationSystem>().SetListenerVolume(_clientSession.UserId, _dummy.UserId, 0f));

        before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before), "muted players' voice isn't even sent");

        // Unmute.
        await Pair.Server.WaitPost(() =>
            Ent.System<VoiceModerationSystem>().SetListenerVolume(_clientSession.UserId, _dummy.UserId, 1f));
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before + 1));

        // Admin voice mute stops the speaker entirely.
        await Pair.Server.WaitPost(() =>
            Ent.System<VoiceModerationSystem>().AdminMute(_dummy.UserId, _dummy.Name, TimeSpan.FromMinutes(5), "test", null));
        before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before));
    }

    [Test]
    public async Task OutOfRangeAndWallsAreNotSent()
    {
        await Setup(speakerX: 12);

        // Scenario A: beyond max range, no packets are delivered.
        var before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before));

        await Pair.Server.WaitPost(() =>
        {
            Ent.System<SharedTransformSystem>().SetCoordinates(_speaker, new EntityCoordinates(_grid.Owner, new Vector2(4.5f, 0.5f)));
            for (var y = -2; y <= 2; y++)
            {
                Ent.SpawnAtPosition("WallSolid", new EntityCoordinates(_grid.Owner, new Vector2(2.5f, y + 0.5f)));
            }
        });
        await RunTicksSync(5);

        // Scenario B: separated by a solid wall.
        await Pair.Server.WaitAssertion(() =>
        {
            var listeners = Ent.System<VoiceSystem>().GetLocalListeners(_speaker);
            Assert.That(listeners.Any(l => l.Session == _clientSession), Is.False);
        });

        before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before));
    }

    [Test]
    public async Task RadioReachesFarAwayListeners()
    {
        // Scenario E: far apart (and the speaker is out in open space), but both on the Common channel.
        await Setup("VoiceTestRadioMob");

        await Pair.Server.WaitPost(() =>
        {
            Ent.System<SharedTransformSystem>().SetMapCoordinates(_speaker, new MapCoordinates(new Vector2(200, 200), Ent.GetComponent<TransformComponent>(_listener).MapID));
        });
        await RunTicksSync(30);

        Assert.That(Ent.GetComponent<VoiceSpeakerComponent>(_speaker).RadioChannels.Select(c => c.Id), Does.Contain("Common"));

        var before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before), "too far for local voice");

        await Speak(_dummy, radio: true);
        Assert.That(ClientReceived, Is.EqualTo(before + 1));

        var last = Pair.Client.EntMan.System<VoicePlaybackSystem>().LastPacket!.Value;
        Assert.That(last.Mode, Is.EqualTo(VoiceReceiveMode.Radio));
        Assert.That(last.Channel, Is.EqualTo("Common"));
        Assert.That(last.Speaker, Is.EqualTo(NetEntity.Invalid), "radio never reveals the speaking entity");

        // The radio panel lists what each player can hear: their channels, or everything for an observer.
        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(Ent.GetComponent<VoiceSpeakerComponent>(_listener).ListenChannels.Select(c => c.Id),
                Does.Contain("Common"));

            var observer = Ent.SpawnEntity("MobObserver", MapCoordinates.Nullspace);
            var channels = new List<ProtoId<RadioChannelPrototype>>();
            Ent.System<VoiceRadioSystem>().GetReceiveChannels(observer, channels);
            Assert.That(channels, Has.Count.EqualTo(Pair.Server.ProtoMan.EnumeratePrototypes<RadioChannelPrototype>().Count()));
            Ent.DeleteEntity(observer);
        });

        // The listener turns off listening to Common in the radio panel: the server stops sending it.
        await Pair.Client.WaitPost(() =>
            Pair.Client.EntMan.System<VoiceIndicatorSystem>().SetListening("Common", false));
        await RunTicksSync(5);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(Ent.System<VoiceSystem>().IsRadioChannelMuted(_clientSession.UserId, "Common")));

        before = ClientReceived;
        await Speak(_dummy, radio: true);
        Assert.That(ClientReceived, Is.EqualTo(before), "muted radio channels aren't sent");

        await Pair.Client.WaitPost(() =>
            Pair.Client.EntMan.System<VoiceIndicatorSystem>().SetListening("Common", true));
        await RunTicksSync(5);

        await Speak(_dummy, radio: true);
        Assert.That(ClientReceived, Is.EqualTo(before + 1));
    }

    [Test]
    public async Task CreaturesThatCantTalkAreNotTransmitted()
    {
        await Setup();

        var before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before + 1), "a person is heard");

        // Text speech fully replaced by "Squeak!": nobody can understand them, so no audio is sent at all.
        await Pair.Server.WaitPost(() => Ent.System<ReplacementAccentSystem>().ApplyAccent(_speaker, "mouse"));

        var replaced = Ent.System<VoiceSystem>().Stats.PacketsReplaced;
        before = ClientReceived;
        await Speak(_dummy);
        await Speak(_dummy, radio: true);
        Assert.That(ClientReceived, Is.EqualTo(before), "a creature's voice never reaches anyone");
        Assert.That(Ent.System<VoiceSystem>().Stats.PacketsReplaced, Is.EqualTo(replaced + 2));

        // Word replacements (pirate...) are still understandable.
        await Pair.Server.WaitPost(() => Ent.System<ReplacementAccentSystem>().ApplyAccent(_speaker, "pirate"));
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before + 1));

        // An actual hamster.
        await Pair.Server.WaitAssertion(() =>
        {
            var hamster = Ent.SpawnEntity("MobHamster", MapCoordinates.Nullspace);
            Assert.That(Ent.System<VoiceCreatureSystem>().IsUnintelligible(hamster));
            Ent.DeleteEntity(hamster);
        });
    }

    [Test]
    public async Task RadiosWithSpeakersPlayOutLoud()
    {
        // Nobody here has a radio receiver; the speaker is far away.
        await Setup("VoiceTestHandheldTalker");

        EntityUid radio = default;
        await Pair.Server.WaitPost(() =>
        {
            Ent.System<SharedTransformSystem>().SetMapCoordinates(_speaker,
                new MapCoordinates(new Vector2(200, 200), Ent.GetComponent<TransformComponent>(_listener).MapID));

            radio = Ent.SpawnAtPosition("RadioHandheld", new EntityCoordinates(_grid.Owner, new Vector2(2.5f, 0.5f)));
            Ent.System<RadioDeviceSystem>().SetSpeakerEnabled(radio, null, true, quiet: true);
        });
        await RunTicksSync(10);

        var before = ClientReceived;
        await Speak(_dummy, radio: true);
        Assert.That(ClientReceived, Is.EqualTo(before + 1), "a handheld radio with its speaker on plays it");

        var last = Pair.Client.EntMan.System<VoicePlaybackSystem>().LastPacket!.Value;
        Assert.That(last.Mode, Is.EqualTo(VoiceReceiveMode.Loudspeaker));
        Assert.That(last.Speaker, Is.EqualTo(Ent.GetNetEntity(radio)), "it plays from the radio");
        Assert.That(last.Channel, Is.EqualTo("Handheld"));

        // Speaker off: silence (once the route is recomputed, a few times a second).
        await Pair.Server.WaitPost(() => Ent.System<RadioDeviceSystem>().SetSpeakerEnabled(radio, null, false, quiet: true));
        await Task.Delay(300);
        await RunTicksSync(10);
        before = ClientReceived;
        await Speak(_dummy, radio: true);
        Assert.That(ClientReceived, Is.EqualTo(before));
    }

    [Test]
    public async Task TalkingNearRadioMicrophonesBroadcasts()
    {
        await Setup("VoiceTestHandheldListener");

        // The listener is far away and only reachable over the Handheld channel.
        await Pair.Server.WaitPost(() =>
        {
            Ent.System<SharedTransformSystem>().SetMapCoordinates(_listener,
                new MapCoordinates(new Vector2(200, 200), Ent.GetComponent<TransformComponent>(_speaker).MapID));
        });
        await RunTicksSync(10);

        var before = ClientReceived;
        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before), "too far, and no microphone yet");

        EntityUid radio = default;
        await Pair.Server.WaitPost(() =>
        {
            radio = Ent.SpawnAtPosition("RadioHandheld", new EntityCoordinates(_grid.Owner, new Vector2(4.5f, 0.5f)));
            Ent.System<RadioDeviceSystem>().SetMicrophoneEnabled(radio, null, true, quiet: true);
        });
        await Task.Delay(300); // microphones near a speaker are looked up a few times a second
        await RunTicksSync(10);

        await Speak(_dummy);
        Assert.That(ClientReceived, Is.EqualTo(before + 1), "the microphone picked it up");

        var last = Pair.Client.EntMan.System<VoicePlaybackSystem>().LastPacket!.Value;
        Assert.That(last.Mode, Is.EqualTo(VoiceReceiveMode.Radio));
        Assert.That(last.Channel, Is.EqualTo("Handheld"));
    }

    [Test]
    public async Task EquipmentChangesVoice()
    {
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            var inventory = Ent.System<InventorySystem>();
            var speaker = Ent.GetComponent<VoiceSpeakerComponent>(_speaker);
            Assert.That(speaker.Filters.Select(f => f.Id), Does.Not.Contain("GasMask"));

            // Scenario F: gas mask muffles the voice, removing it restores it immediately.
            var mask = Ent.SpawnAtPosition("ClothingMaskGas", Ent.GetComponent<TransformComponent>(_speaker).Coordinates);
            Assert.That(inventory.TryEquip(_speaker, mask, "mask", silent: true, force: true));
            Assert.That(speaker.Filters.Select(f => f.Id), Does.Contain("GasMask"));

            Assert.That(inventory.TryUnequip(_speaker, "mask", silent: true, force: true));
            Assert.That(speaker.Filters.Select(f => f.Id), Does.Not.Contain("GasMask"));

            // A voice-changing mask: inactive does nothing, active disguises the voice.
            var voiceMask = Ent.SpawnAtPosition("ClothingMaskGasVoiceChameleon", Ent.GetComponent<TransformComponent>(_speaker).Coordinates);
            Assert.That(inventory.TryEquip(_speaker, voiceMask, "mask", silent: true, force: true));
            var profileBefore = speaker.Profile;

            Ent.GetComponent<VoiceMaskComponent>(voiceMask).Active = true;
            Ent.System<VoiceProfileSystem>().Refresh(_speaker, speaker);
            Assert.That(speaker.Profile, Is.Not.EqualTo(profileBefore));
            Assert.That(speaker.Filters.Select(f => f.Id), Does.Contain("DisguiseSheen"));
            var disguised = speaker.Profile;

            // Impersonation: recorded voice + matching mask name.
            var impersonator = Ent.GetComponent<VoiceImpersonatorComponent>(voiceMask);
            var captain = new VoiceProfile(-3f, -2f, 0.2f);
            impersonator.Samples["Captain Test"] = new VoiceSample { Profile = captain };
            Ent.GetComponent<VoiceMaskComponent>(voiceMask).VoiceMaskName = "Captain Test";
            Ent.System<VoiceProfileSystem>().Refresh(_speaker, speaker);
            Assert.That(speaker.Profile, Is.EqualTo(captain));

            // Taking it off restores the character's own voice.
            Assert.That(inventory.TryUnequip(_speaker, "mask", silent: true, force: true));
            Assert.That(speaker.Profile, Is.EqualTo(profileBefore));
            Assert.That(speaker.Profile, Is.Not.EqualTo(disguised));
        });
    }

    [Test]
    public async Task SpeciesMasksAndStatusChangeVoice()
    {
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            var profiles = Ent.System<VoiceProfileSystem>();
            var coords = Ent.GetComponent<TransformComponent>(_speaker).Coordinates;

            // Every species except humans (the baseline) sounds a bit different.
            foreach (var mob in new[] { "MobVulpkanin", "MobGingerbread", "MobHorror" })
            {
                var uid = Ent.SpawnAtPosition(mob, coords);
                var (profile, filters) = profiles.Compose(uid);
                Assert.That(!profile.IsNeutral || filters.Count > 0, mob);
                Ent.DeleteEntity(uid);
            }

            var inventory = Ent.System<InventorySystem>();
            var speaker = Ent.GetComponent<VoiceSpeakerComponent>(_speaker);

            foreach (var (mask, filter) in new[]
                     {
                         ("ClothingMaskSterile", "ClothMask"),
                         ("ClothingMaskPlague", "PlagueMask"),
                         ("ClothingMaskFox", "HardMask"),
                     })
            {
                var item = Ent.SpawnAtPosition(mask, coords);
                Assert.That(inventory.TryEquip(_speaker, item, "mask", silent: true, force: true), mask);
                Assert.That(speaker.Filters.Select(f => f.Id), Does.Contain(filter), mask);
                Assert.That(inventory.TryUnequip(_speaker, "mask", silent: true, force: true), mask);
                Ent.DeleteEntity(item);
            }

            var clown = Ent.SpawnAtPosition("ClothingMaskClown", coords);
            var before = speaker.Profile;
            Assert.That(inventory.TryEquip(_speaker, clown, "mask", silent: true, force: true));
            Assert.That(speaker.Profile.Nasality, Is.GreaterThan(before.Nasality), "clown masks honk");
            Assert.That(inventory.TryUnequip(_speaker, "mask", silent: true, force: true));

            // Drunk and shaking change the voice; recording it for impersonation ignores that.
            var plain = profiles.Compose(_speaker).Profile;
            Assert.That(Ent.System<Content.Shared.StatusEffectNew.StatusEffectsSystem>()
                .TryAddStatusEffectDuration(_speaker, "StatusEffectDrunk", TimeSpan.FromMinutes(1)));
            Ent.EnsureComponent<Content.Shared.Jittering.JitteringComponent>(_speaker);

            var affected = profiles.Compose(_speaker).Profile;
            Assert.That(affected.Tremble, Is.GreaterThan(plain.Tremble));
            Assert.That(affected.Brightness, Is.LessThan(plain.Brightness));
            Assert.That(profiles.GetInnateVoice(_speaker).Profile, Is.EqualTo(plain));
        });
    }

    [Test]
    public async Task CharacterProfilesDiffer()
    {
        // Scenario G: different characters of the same human can have different voices.
        await Setup();

        await Pair.Server.WaitAssertion(() =>
        {
            var profiles = Ent.System<VoiceProfileSystem>();
            profiles.SetCharacterVoice(_listener, new VoiceProfile(4f, 1f));
            profiles.SetCharacterVoice(_speaker, new VoiceProfile(-4f, -1f, 0.5f));

            Assert.That(Ent.GetComponent<VoiceSpeakerComponent>(_listener).Profile.Pitch, Is.EqualTo(4f));
            Assert.That(Ent.GetComponent<VoiceSpeakerComponent>(_speaker).Profile.Pitch, Is.EqualTo(-4f));
        });
    }

    [Test]
    public async Task MalformedAndAbusivePacketsAreRejected()
    {
        // Scenario I, through the real client -> server network path.
        await Setup();

        var stats = () => Ent.System<VoiceSystem>().Stats;
        var net = Pair.Client.ResolveDependency<IClientNetManager>();
        var start = stats();

        await Pair.Client.WaitPost(() =>
        {
            // Oversized.
            net.ClientSendMessage(new MsgVoiceTransmit { Sequence = 1, Payload = new byte[900] });
            // Structurally invalid Opus (code 3 packet claiming 63 x 20 ms).
            net.ClientSendMessage(new MsgVoiceTransmit { Sequence = 2, Payload = new byte[] { 0xFB, 0x3F, 0, 0 } });
            // Empty without end-of-spurt.
            net.ClientSendMessage(new MsgVoiceTransmit { Sequence = 3, Payload = Array.Empty<byte>() });
            // Duplicate sequence.
            net.ClientSendMessage(new MsgVoiceTransmit { Sequence = 10, Payload = _opusFrame });
            net.ClientSendMessage(new MsgVoiceTransmit { Sequence = 10, Payload = _opusFrame });
        });
        await RunTicksSync(10);

        var afterBad = stats();
        Assert.That(afterBad.PacketsReceived - start.PacketsReceived, Is.EqualTo(5));
        Assert.That(afterBad.PacketsRejected - start.PacketsRejected, Is.EqualTo(4));

        // Flood: 5 seconds of audio in one go.
        await Pair.Client.WaitPost(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                net.ClientSendMessage(new MsgVoiceTransmit { Sequence = (ushort) (100 + i), Payload = _opusFrame });
            }
        });
        await RunTicksSync(10);

        var afterFlood = stats();
        Assert.That(afterFlood.PacketsRejected - afterBad.PacketsRejected, Is.GreaterThan(200), "flood is rate limited");

        // And the client is still connected: abuse is dropped, not crashed on.
        Assert.That(net.IsConnected);
        Assert.That(Pair.Server.PlayerMan.Sessions.Any(s => s.UserId == _clientSession.UserId));
    }
}

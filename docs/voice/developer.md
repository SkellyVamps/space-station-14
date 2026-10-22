# Voice chat: developer guide

## Layers

The system is split into layers that only talk to their neighbours, so each can be extended on its own.

| Layer | Responsibility | Where |
|---|---|---|
| CAPTURE | Microphone devices, opening/closing, reading PCM | engine `IRealtimeAudioManager`, `IAudioCaptureDevice`; `Content.Client/Voice/VoiceCaptureSystem.cs` |
| PROCESSING | Preprocessing (gain, rumble filter, noise gate) and the gameplay effect chain | `Content.Shared/Voice/Dsp`, `Effects`, `VoiceChainBuilder`; `VoiceTransmitSystem` |
| ENCODING | Opus encode/decode | engine `Robust.Shared.Audio.Codecs` |
| TRANSPORT | Wire messages | `MsgVoiceTransmit`, `MsgVoiceReceive`, `VoiceNetManager` |
| ROUTING | Validation, rate limiting, who receives a frame | `Content.Server/Voice/VoiceSystem.cs`, `VoiceRateLimiter.cs` |
| AUDIBILITY | Distance, walls, doors, vacuum, containers, hearing | `Content.Server/Voice/VoiceAudibilitySystem.cs`, `Acoustics/` |
| RADIO | Channels, transmitters, receivers | `Content.Server/Voice/VoiceRadioSystem.cs` |
| GAMEPLAY | Character/species/equipment/disguise/impersonation -> speaker state | `VoiceProfileSystem`, `VoiceModifierSystem`, `VoiceImpersonationSystem` |
| PLAYBACK | Jitter buffer, decode, receive DSP, positional sources | `Content.Client/Voice/VoicePlaybackSystem.cs`, `VoiceJitterBuffer.cs` |
| MODERATION | Admin mutes, personal mute/volume, logs | `VoiceModerationSystem`, `Commands/` |
| UI | Options tab, character editor, indicators | `Content.Client/Options/UI/Tabs/VoiceTab.*`, `Lobby/UI/HumanoidProfileEditor.CharacterVoice.cs`, `VoiceIndicator*` |

## Packet flow

```
 microphone
   │  IAudioCaptureDevice.ReadSamples (16-bit mono, 48 kHz)
 VoiceTransmitSystem (client, every frame)
   │  gain -> 80 Hz high-pass -> level meter / voice activation -> noise gate
   │  VoiceSpeakerComponent.Profile + Filters  (server-authoritative, sent only to the owner)
   │  limiter -> Opus (20 ms, VOIP, FEC)
   ▼
 MsgVoiceTransmit { flags, sequence, radio channel?, opus }      no speaker id: the server knows who sent it
   ▼
 VoiceSystem (server)
   │  validate: size, Opus TOC structure, audio-time + packet rate limits, duplicate window, admin mute, CanSpeak
   │  local recipients: cached list from VoiceAudibilitySystem, refreshed every voice.audibility_interval
   │  radio recipients: VoiceRadioSystem (same rules as text radio), cached per channel
   │  per listener: personal mute/volume
   ▼
 MsgVoiceReceive { mode, stream id, sequence, gain, muffle, speaker (local) | channel + name (radio), opus }
   ▼
 VoicePlaybackSystem (client)
   │  per stream: VoiceJitterBuffer -> OpusVoiceDecoder (FEC / concealment)
   │  muffle low-pass (from server) -> radio or ghost filter -> gain ramp
   ▼
 IStreamingAudioSource at the speaker's position (OpenAL pans; distance falloff already applied by the server)
```

Effects that depend on the speaker (character, species, masks, disguises) are applied **once, before encoding**,
on the speaker's client: one encode per speaker regardless of the number of listeners, no extra bandwidth.
Effects that depend on the listener (distance, walls, being in a locker, radio coloration, ghost reverb) are applied
**after decoding** on each listener's client, driven by two bytes (gain, muffle) the server adds per recipient.
The server never decodes or re-encodes audio.

### Trust model

* The speaker is always `session.AttachedEntity`. Packets don't contain a speaker id.
* Clients only receive voice they may hear; a modified client can't eavesdrop.
* Radio packets carry a per-speaker-per-channel random stream id, never the speaking entity, so radio voices can't be
  linked to bodies by a modified client.
* Personal mutes are stored by account on the server; clients never learn which account is behind an entity.
* The speaker's own client applies its gameplay effects. A modified client could skip them or send arbitrary audio.
  Nothing can cryptographically prove audio came unmodified from a microphone; the server only enforces what it can
  (who, where, how much, to whom).

## Engine modifications

All additive, in new files where possible (`Tools/voice/engine-patches/`):

* `Robust.Client/Audio/Realtime/IRealtimeAudioManager.cs` (+ `AudioManager.Realtime.cs`, headless stub, IoC
  registration): `GetCaptureDevices`, `OpenCaptureDevice`, `CreateStreamingSource`.
* `IAudioCaptureDevice` / `AudioCaptureDevice`: ALC capture, `Connected` via `ALC_EXT_disconnect`.
* `IStreamingAudioSource` / `StreamingAudioSource`: a pool of AL buffers, `QueueSamples`, auto restart after underrun.
* `Robust.Shared/Audio/Codecs`: `OpusVoiceEncoder`, `OpusVoiceDecoder` (never throw on bad data), `OpusPacket`
  (structural validation without decoding). Backed by Concentus, `PrivateAssets=compile` so content can't depend on it
  directly.
* Engine tests: `Robust.Shared.Tests/Audio/OpusVoiceCodecTest.cs`.

Apply with `Tools/voice/apply-engine-patches.sh` after `git submodule update`.

## Components and prototypes

| Component | On | Purpose |
|---|---|---|
| `VoiceSpeakerComponent` | player entities (added automatically) | Server-computed voice state, networked to the owner only |
| `VoiceCharacterComponent` | spawned characters | The character profile's procedural voice |
| `VoiceModifier` | species mobs (innate) or clothing | Adds filters and a profile offset. `slots`, `requireMaskUp`, `enabled` |
| `VoiceDisguise` | voice changers | Stable pseudo-random replacement voice; active with the voice mask |
| `VoiceImpersonator` | advanced voice changers | Recorded character voices, used when the voice mask name matches |
| `VoiceHearingModifier` | earmuffs, helmets, traits | Gain, muffle, deafness for local voice |
| `VoiceAcousticBarrier` | doors, windows, walls | How sound crosses the tile while it blocks air: `penalty`, `muffle`, `blocks` |
| `VoiceContainerAcoustics` | lockers, crates, disposals... | How sound escapes: `penalty`, `muffle`, `blocks`, `onlyWhenClosed` |

`voiceFilter` prototypes (`Resources/Prototypes/Voice/filters.yml`) are ordered effect lists:

```yaml
- type: voiceFilter
  id: GasMask
  order: 50          # lower runs first: species ~5, masks ~50, helmets ~60-70
  effects:
  - !type:FilterVoiceEffect { type: LowPass, frequency: 3200 }
  - !type:CombVoiceEffect { delayMs: 1.2, feedback: 0.2, mix: 0.25 }
```

Available effects (`Content.Shared/Voice/Effects/VoiceEffects.cs`): `GainVoiceEffect`, `FilterVoiceEffect`
(lowPass, highPass, bandPass, notch, peaking, lowShelf, highShelf, `stages`), `CompressorVoiceEffect`,
`DistortionVoiceEffect`, `BitCrushVoiceEffect`, `RingModulatorVoiceEffect`, `TremoloVoiceEffect`, `EchoVoiceEffect`,
`CombVoiceEffect`, `ModulationVoiceEffect` (chorus/vibrato), `NoiseVoiceEffect` (static or breath), `CrackleVoiceEffect`,
`PitchShiftVoiceEffect`, `VocoderVoiceEffect`.

### Voice composition

`VoiceProfileSystem.Compose` raises `VoiceModifiersEvent` on the entity (relayed to worn equipment):

```
base   = override ?? VoiceCharacterComponent.Profile
profile = base + Σ ProfileOffset                          (clamped to safe limits)
filters = (FilterOverride ?? InnateFilters) + Filters      (sorted by prototype order)
```

* Innate sources (species, implants, status effects) add to `InnateFilters`.
* Equipment adds to `Filters`.
* A disguise calls `Override(profile, priority 10, [])` (hides species too); impersonation uses priority 20 with the
  recorded character's innate filters.

The result is written to `VoiceSpeakerComponent` immediately on equip/unequip, mask toggle, voice mask toggle and
mob state change, and every 0.5 s as a safety net.

## How to...

### Add a new effect

1. Write an `IVoiceProcessor` in `Content.Shared/Voice/Dsp` (allocation-free `Process`, any block size, `Reset`).
   Content code runs in the client sandbox: no `stackalloc`, `unsafe`, pointers or P/Invoke, or the client refuses to
   load it. `SandboxTest` in the integration tests catches this.
2. Add a `VoiceEffect` subclass with `[DataField]`s that returns it from `CreateProcessor`.
3. Use it from YAML with `!type:YourVoiceEffect`. Add it to `VoiceDspTest.ProcessorsAreStableWithExtremeInput`.

### Make equipment modify voice

```yaml
- type: entity
  id: ClothingHeadHelmetFishbowl
  components:
  - type: VoiceModifier
    filters: [ SealedHelmet ]
    profile:            # optional offset
      brightness: -0.2
```

Masks are ignored while pulled down (`requireMaskUp: true` by default). Limit to slots with `slots: [HEAD]`.

### Add species voice colouring

Put an innate `VoiceModifier` on the species' mob prototype (see `MobReptilian`, `MobVox`). Keep pitch out of species
offsets; players choose pitch per character.

### Make something block or muffle sound

Give the structure `VoiceAcousticBarrier`. It applies only while the entity is air-blocking (so doors stop muffling
once open, using the existing `AirtightComponent` state). Air-blocking structures without the component use
`voice.default_barrier_penalty` (default: block completely, i.e. walls).

### Status effects / traits

Subscribe to `VoiceModifiersEvent` (or `VoiceHearingEvent`) on your component and add filters/offsets. Call
`VoiceProfileSystem.Refresh` when your state changes if it should apply instantly.

## Environmental audibility

`AcousticField` (pure, `Content.Server/Voice/Acoustics`) runs a Dijkstra search over the speaker's grid tiles where the
cost is *effective distance*: path length plus `penalty` of every barrier crossed. Muffle combines as
`1 - Π(1 - mᵢ)`. Tiles below `voice.min_pressure` and space tiles don't conduct; walls (blocking barriers) stop
propagation; diagonal steps can't cut wall corners. The final gain is:

```
gain = falloff(max(fieldDistance, straightLine) + containerPenalties) × pressure(speaker) × pressure(listener) × hearing
```

`falloff` is 1 up to `voice.full_volume_range`, then `(1 - t)^voice.falloff_exponent` to 0 at `voice.range`.

Performance design:

* Audibility is never computed per packet. Recipient lists are rebuilt every `voice.audibility_interval` (0.2 s) for
  speakers who talked in the last 1.5 s. Packets walk the cached list.
* A speaker's field is cached and only rebuilt when they change tile, an airtight structure or tile on that grid
  changes (`AirtightChanged`, `TileChangedEvent`), or it's older than `voice.field_max_age` (pressure changes).
* Tile data (barriers, pressure) is cached per grid and invalidated per tile.
* A field over a 10-tile range touches ~300 tiles.

Containers: an entity's acoustic position is its outermost container; containers both parties are inside don't
separate them. Grids: voice only crosses between grids or into open space within `voice.cross_grid_range`, enough for
docking airlocks.

## Ghosts and observers

| Speaker \ listener | Living | Ghost |
|---|---|---|
| Living | acoustics + hearing | if `voice.ghosts_hear_living` and ghost hearing is on: straight-line distance |
| Ghost | only if `voice.living_hear_ghosts` (ghost reverb applied) | if `voice.ghost_voice` |

Admin ghosts are ghosts. Dead bodies (not yet ghosted) can neither speak nor hear living voice; critical players
hear muffled (configurable); sleepers hear muffled.

## Testing

* Unit (`dotnet test Content.Tests --filter "FullyQualifiedName~Voice"`): DSP correctness and stability, pitch shift
  accuracy, profile clamping/composition, acoustic scenarios on synthetic maps, rate limiter, replay window, jitter
  buffer under simulated loss/jitter.
* Integration (`dotnet test Content.IntegrationTests --filter "FullyQualifiedName~Tests.Voice"`): real grids with
  walls/airlocks/lockers/atmosphere, and end-to-end routing to a real client (scenarios A-I).
* Engine (`dotnet test RobustToolbox/Robust.Shared.Tests --filter OpusVoiceCodecTest`).
* Benchmarks: `dotnet run -c Release --project Content.Benchmarks -- --filter '*VoiceBenchmark*'`.

Manual multi-client testing: `runserver`, then two `runclient` instances (`--username a`, `--username b`). Mic test
in Options -> Voice works without a server.

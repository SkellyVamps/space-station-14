# Voice chat: developer guide

## Layers

The system is split into layers that only talk to their neighbours, so each can be extended on its own.

| Layer | Responsibility | Where |
|---|---|---|
| CAPTURE | The microphone: the voice webpage or the Echoes Voice app, over a WebSocket on the status port | `Content.Server/Voice/External/` (`Web/`, `VoiceWebManager`), `EchoesVoice/` |
| KEYS | Push to talk, radio keys, intercoms, range, mute: what the game says about when you're heard | `VoiceTransmitSystem`, `VoiceExternalSystem` (client) |
| PROCESSING | The gameplay effect chain, applied on the server | `Content.Shared/Voice/Dsp`, `Effects`, `VoiceChainBuilder`; `VoiceExternalMicSystem` |
| ENCODING | 16 kHz IMA ADPCM, 20 ms frames | `Content.Shared/Voice/Codecs/ImaAdpcm.cs` (also used by the app; mirrored in `voice-worklet.js`) |
| TRANSPORT | Wire messages | `MsgVoiceReceive`, `VoiceNetManager` |
| ROUTING | Validation, rate limiting, who receives a frame | `Content.Server/Voice/VoiceSystem.cs`, `VoiceRateLimiter.cs` |
| AUDIBILITY | Distance, walls, doors, vacuum, containers, hearing | `Content.Server/Voice/VoiceAudibilitySystem.cs`, `Acoustics/` |
| RADIO | Channels, transmitters, receivers | `Content.Server/Voice/VoiceRadioSystem.cs` |
| GAMEPLAY | Character/species/equipment/disguise/impersonation -> speaker state | `VoiceProfileSystem`, `VoiceModifierSystem`, `VoiceImpersonationSystem` |
| PLAYBACK | Jitter buffer, decode, receive DSP, crossfaded clips | `Content.Client/Voice/VoicePlaybackSystem.cs`, `VoiceStream.cs`, `VoiceJitterBuffer.cs` |
| MODERATION | Admin mutes, personal mute/volume, logs | `VoiceModerationSystem`, `Commands/` |
| UI | Options tab, character editor, level meters, voice panel (speaker cards, range and mute buttons) | `Content.Client/Options/UI/Tabs/VoiceTab.*`, `Lobby/UI/HumanoidProfileEditor.CharacterVoice.cs`, `VoiceIndicator*`, `VoiceRadioPanel.cs` |

## Packet flow

```
 microphone (browser getUserMedia + AudioWorklet, or the app's OpenAL capture)
   │  16 kHz mono -> IMA ADPCM, 20 ms
   ▼
 WebSocket /voice/ws on the status port: hello {code}, then [0x01, seq, frame]
   ▼
 VoiceExternalMicSystem (server)
   │  gate with the player's key state (VoiceExternalKeyEvent from their game client)
   │  -> 48 kHz -> VoiceSpeakerComponent.Profile + Filters -> limiter -> back to 16 kHz ADPCM
   ▼
 VoiceSystem.HandleTransmit (server)
   │  validate: size, frame structure, admin mute, CanSpeak
   │  local recipients: cached list from VoiceAudibilitySystem, refreshed every voice.audibility_interval
   │  radio recipients: VoiceRadioSystem (same rules as text radio), cached per channel
   │  per listener: personal mute/volume; a copy goes to voice-to-text
   ▼
 MsgVoiceReceive { mode, stream id, sequence, gain, muffle, speaker (local) | channel + name (radio), frame }
   ▼
 VoiceStream (client)
   │  VoiceJitterBuffer -> ADPCM decode (concealment for lost frames) -> 48 kHz
   │  muffle low-pass (from server) -> radio or ghost filter -> gain ramp
   ▼
 crossfaded clips (IAudioManager.LoadAudioRaw + CreateAudioSource) at the speaker's position
```

Effects that depend on the speaker (character, species, masks, disguises) are applied **once, on the server**, before
the frame is routed: one pass per speaker regardless of the number of listeners. Effects that depend on the listener
(distance, walls, being in a locker, radio coloration, ghost reverb) are applied on each listener's client, driven by
two bytes (gain, muffle) the server adds per recipient.

### Playback without a streaming source

The official engine plays whole sounds, so `VoiceStream` plays voice as 40 ms clips, each with 30 ms more audio that
crossfades into the next one. The next clip is timed from the previous one's real playback position (the audio device
clock), started up to a frame early with silence in front so it lines up exactly, or that far in if a frame comes late.
Playback waits for about 120 ms of audio before starting a sentence. `VoiceStreamTest` rebuilds the heard signal from
the clips under uneven frame timing and network jitter and checks it's seamless.

### Trust model

* The speaker is always `session.AttachedEntity`. Packets don't contain a speaker id.
* Clients only receive voice they may hear; a modified client can't eavesdrop.
* Radio packets carry a per-speaker-per-channel random stream id, never the speaking entity, so radio voices can't be
  linked to bodies by a modified client.
* Personal mutes are stored by account on the server; clients never learn which account is behind an entity.
* Gameplay effects are applied by the server, so a modified client can't skip its character's voice. Nothing can
  prove audio came unmodified from a microphone; the server enforces who, where, when, how much, and to whom.
* Game clients never send audio; only a page or app holding the player's link code can, and only while that player's
  talk keys are held.

## Engine

None: it runs on official RobustToolbox. Content uses only public engine APIs (`IAudioManager.LoadAudioRaw`,
`CreateAudioSource`, `IAudioSource.PlaybackPosition`, `IStatusHost` handlers and WebSockets).

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
  buffer under simulated loss/jitter, the ADPCM codec matching the webpage's, seamless clip playback.
* Integration (`dotnet test Content.IntegrationTests --filter "FullyQualifiedName~Tests.Voice"`): real grids with
  walls/airlocks/lockers/atmosphere, and end-to-end routing to a real client (scenarios A-I).
* Benchmarks: `dotnet run -c Release --project Content.Benchmarks -- --filter '*VoiceBenchmark*'`.

Manual multi-client testing: `runserver`, then two `runclient` instances (`--username a`, `--username b`). Mic test
in Options -> Voice works without a server.

## Creatures that can't talk

`VoiceCreatureSystem` raises `VoiceIntelligibilityEvent` on a speaker for every frame. If anything sets
`Unintelligible`, the frame is dropped and the creature's `SpeechComponent` speech sound plays now and then instead.
Built in: a `ReplacementAccent` whose prototype has `fullReplacements`, `MonkeyAccent`, and the explicit
`VoiceUnintelligible` component for anything else. Subscribe to the event to add your own rules (a language system,
for example).

## Intercoms and handheld radios

Radio routing mirrors text radio (`VoiceRadioSystem.CollectListeners`). Receivers that belong to a player (headset,
intrinsic radio) get `VoiceReceiveMode.Radio`; enabled `RadioSpeaker` devices are collected separately, and each player
near one (headset or not) gets the loudest such device near them as `VoiceReceiveMode.Loudspeaker`: positional at the
device, radio filter, acoustics from the device to the listener (`voice.loudspeaker_gain`).

Speaking (not into a radio) near an enabled, powered `RadioMicrophone` within its `listenRange` that can acoustically
hear you transmits on its `broadcastChannel` with the microphone as radio source, one microphone per channel.
Routes are recomputed every `voice.audibility_interval` like local recipients. Intercoms are skipped here.

Intercoms transmit voice only while their window's transmit button is held: the client
(`VoiceTransmitSystem.SetDeviceTransmit`, from `IntercomBoundUserInterface`) sends with `VoiceTransmitFlags.Device` and
the intercom's `NetEntity`, needing no push to talk key. The server accepts it only if that intercom has its UI open by
the speaker, is tuned to a channel and is powered (`VoiceSystem.GetOperatedIntercom`), and sends it on a route keyed by
the intercom with it as the radio source. `Device` and `Radio` together are rejected.

## Client audio processing

The browser does its own echo cancellation, noise suppression and gain control; the app runs the shared
`NoiseSuppressor` (spectral subtraction with a tracked per-bin noise floor) and a volume control. The server runs
the gameplay chain and a limiter (`VoiceExternalMicSystem`).

`VoiceDuckingSystem` lowers other game sounds while voice is audible (`voice.ducking`): it runs after the engine's
`AudioSystem` and offsets every playing `AudioComponent`'s volume; the engine restores them on its next update.
Per-channel radio volume (`voice.radio_channel_volumes`) multiplies the radio volume on the client.

## Character voices

`VoiceProfile` (pitch, formant, brightness, roughness, breathiness, nasality, tremble) is stored per character
(`profile.voice_*` columns) and clamped by `voice.profile_max_pitch` / `_formant`. Presets are `voicePreset`
prototypes (`Resources/Prototypes/Voice/voice_presets.yml`); add one with a localized name and a profile. Voice codes
are `VOICE1:` followed by the values separated by `;`; missing trailing values read as 0, so new parameters stay
compatible with old codes.

## Status effects

`VoiceStatusSystem` adds profile offsets from the speaker's state when voices are composed (every 0.5 s): drunk and
drowsy status effects, `JitteringComponent`, stamina damage, respirator saturation below its suffocation threshold,
and body temperature within 25 K of the cold damage threshold. Skipped for `InnateOnly` (impersonation recordings).
Add new states there, or subscribe to `VoiceModifiersEvent` in your own system.

## Whisper, talk, shout

The client sends `VoiceSetRangeEvent` when the player switches (and again on attach). The server keeps it per session
(`VoiceSystem.SetRange`) and passes a range scale to `VoiceAudibilitySystem.TryGetAudibility`, which divides the
acoustic distance by it: the same falloff, stretched or shrunk (`voice.whisper_range_scale`,
`voice.shout_range_scale`). Acoustic fields are computed out to `voice.range × shout scale`. Local packets carry
`VoiceReceiveFlags.Whisper` / `Shout` so listeners can colour the indicators.

## Speaking indicators

`VoiceStream` measures what it plays with `VoiceLevelAnalyzer` (overall level plus bass, middle and treble bands) and
schedules each block on a `VoiceLevelTimeline` for when it will actually be heard, so the meters move with the audio.
`VoiceTransmitSystem.SelfLevels` does the same for your own voice. `VoiceIndicatorOverlay` draws the three-bar meters in
world space above heads; `VoiceRadioPanel` shows a `VoiceSpeakerCard` per voice from
`VoicePlaybackSystem.GetHeardVoices`. Who may be shown or named goes through `VoiceIndicatorSystem.CanSee`.

## Voice-to-text

`VoiceTranscriptionSystem` receives every accepted frame from `VoiceSystem.HandleTransmit` with a description of how it
was said, decodes it per player, downsamples to 16 kHz and cuts it into spurts (end of spurt, a route change, 30
seconds, or a second of silence). Spurts that are too short or too quiet are dropped; the rest go to one background
worker running an `IVoiceTranscriber` (`WhisperVoiceTranscriber`, Whisper.net), and results are logged on the main
thread as `LogType.VoiceTranscript`. Tests set `TranscriberOverride` and listen to `Transcribed`; the integration test
pool turns `voice.transcribe` off so the real model is never loaded there.

## Voice webpage and app (external microphone)

`VoiceWebManager` (IoC, server) adds a status host handler for `/voice`: the page (`Content.Server/Voice/External/Web`,
embedded resources), `/voice/downloads` (JSON) and `/voice/download/<file>` (files in `<data>/voice-app/`), and the
WebSocket `/voice/ws`. Each WebSocket runs on its own task so it doesn't hold one of the status host's few request
slots.

Protocol: the client sends `{"type":"hello","code":"...","kind":"web"|"app"}`, then binary audio messages: `0x01`,
sequence (uint16 LE), one `ImaAdpcm` frame (20 ms of 16 kHz mono, 163 bytes). `{"type":"ping"}` keeps it alive. The
server sends `welcome`, `state` (`talking`, `name`, `adminMuted`) and `pong`, and closes with 4001 (bad code), 4002
(replaced / switched back), 4003 (left the game), 4004 (shutdown) or 4005 (disabled), with a reason to show.

`VoiceExternalMicSystem` (server) drains frames on the main thread, gates them with the key state the game client
reports (`VoiceExternalKeyEvent`), upsamples to 48 kHz, runs the speaker's voice chain, re-encodes to ADPCM and calls
`VoiceSystem.HandleTransmit(..., fromServer: true)`. `VoiceExternalSystem` (client) gets the link and sends key
state; `VoiceTransmitSystem` stops using the local microphone while connected. `ImaAdpcm` is mirrored by
`voice-worklet.js`; `ImaAdpcmTest` checks both produce identical bytes.

The standalone app (`EchoesVoice/`, Avalonia, not in the game solution) speaks the same protocol with `kind: "app"`. It
records with OpenAL Soft (`Robust.Natives.OpenAL`) at 16 kHz, runs the shared `NoiseSuppressor`, encodes with the
shared `ImaAdpcm`, and registers the `echoesvoice://connect?server=...&code=...` link for the voice page's
"Open in the app" button (Windows registry, Linux .desktop, macOS Info.plist). Links that arrive from outside for a
server the player hasn't approved before always ask first.

## Wall-mounted devices

Intercoms and other `WallMount` entities sit inside a wall tile, which has no air, so sound couldn't start or arrive
there. `VoiceAudibilitySystem.GetAcousticPosition` places them on the tile they face (`WallMount.Direction` plus the
entity's rotation, so any placement direction works), or, if that's solid or airless (mounted between windows), the
nearest open air: the other sides, two tiles out, then the diagonals. `VoiceIntercomMapTest` loads real stations and
checks every intercom on them is heard in front of it.

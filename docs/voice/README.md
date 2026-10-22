# Proximity voice chat

Server-distributed, deeply integrated voice chat for Space Station 14: push to talk (or voice activation), Opus,
positional playback that follows the speaker, acoustics that respect walls, doors, windows, vacuum and lockers,
radio over headsets, per-character procedural voices, species colouring, masks/helmets, disguises and voice
impersonation, per-player muting, admin moderation.

| Document | For |
|---|---|
| [players.md](players.md) | Players: setup, push to talk, muting, troubleshooting |
| [server-owners.md](server-owners.md) | Server owners: enabling, configuration, moderation, **client distribution** |
| [developer.md](developer.md) | Developers: architecture, packet flow, DSP, acoustics, extending it |
| [THIRD-PARTY.md](THIRD-PARTY.md) | Third-party libraries and licences |

## Read this first: the engine dependency

Voice chat needs three small engine primitives that upstream RobustToolbox does not have yet:
microphone capture, a streaming audio source, and an Opus codec. Content code is sandboxed and cannot reach
OpenAL or native libraries itself, so these must live in the engine
([`Tools/voice/engine-patches/`](../../Tools/voice/engine-patches)).

The official SS14 launcher downloads the **content** (all of `Content.*`, including every voice system here) from your
server automatically, but it downloads the **engine** by version number from the official Space Wizards build
servers. A server cannot ship its own engine through the official launcher. Consequences:

* Everything works today in development (`runclient`/`runserver` with the patched submodule) and in the test suite.
* **Do not deploy this branch to a public server yet.** Clients connecting through the official launcher would run
  the official engine, which lacks the new engine types, and the client would fail to load the content.
* To make it work through the normal launcher with zero manual installs, the engine patch has to ship in an official
  RobustToolbox release. It was written to be upstreamable for that reason: it is additive, self-contained, and
  generic (nothing voice-chat specific). See [server-owners.md](server-owners.md#client-distribution) for the
  options and exact steps.

## Status

Implemented and tested:

- [x] Engine: microphone capture with device enumeration and disconnect detection; streaming PCM audio sources;
      Opus encoder/decoder (Concentus, managed) with packet inspection
- [x] Push to talk (local + radio keys, rebindable), voice activation with threshold/hold, noise gate
- [x] Opus at 48 kHz mono, 20 ms frames (server configurable), in-band FEC
- [x] Client -> server -> client transport; server never decodes audio
- [x] Adaptive jitter buffer, FEC recovery and packet loss concealment
- [x] Positional playback attached to the speaking entity
- [x] Acoustic propagation: walls, doors (open/closed, per door type), windows, directional windows, blast doors,
      vacuum/low pressure, space gaps, containers (lockers, crates, disposals, cryo, body bags, mechs), cross-grid gaps
- [x] Hearing: dead, critical, sleeping, ghosts (configurable rules), deafness via `VoiceHearingModifier`
- [x] Radio: headsets and intrinsic radios, channels from encryption keys, telecom servers, long range channels,
      jammers, radio effect chain on the receiving side, radio doesn't reveal the speaking entity
- [x] Character voice profiles (pitch, formant, brightness, roughness, breathiness) saved per character, editor with
      live preview
- [x] Species voice layers, masks, helmets, hardsuit speakers, muzzles, borg voice box, all via prototypes
- [x] Voice-changing disguise (stable per item) and character-voice impersonation (record, then use)
- [x] Per-player mute/volume (by account, enforced server side), admin voice mute (timed), admin logs, `voicestats`
- [x] Options tab: device, gain, level meter, mic test, mode, thresholds, volumes, network buffer, indicators
- [x] Speaking indicators and on-screen radio speaker list
- [x] Packet validation: size, Opus structure, audio-time rate limiting, packet flood limiting, duplicate/replay
      window, muted users, maximum streams per listener
- [x] Unit tests (DSP, acoustics, rate limiting, jitter buffer, codec) and integration tests for scenarios A-I

Not yet done / known limitations:

- [ ] **Engine patch must be upstreamed** before players can use it through the official launcher (see above).
- [ ] Intercoms and handheld radio speakers don't play voice positionally yet (headsets and intrinsic radios do).
- [ ] Voice between grids only crosses short gaps (docking airlocks); no full multi-grid acoustic graph.
- [ ] Personal mutes are kept in server memory (survive reconnects, not restarts) and admin voice mutes are not yet
      stored in the database.
- [ ] Cloning/body swaps keep the new body's voice; `VoiceProfileSystem.SetCharacterVoice` exists for hooking them up.
- [ ] Capture runs on the game thread at frame rate; at very low FPS latency rises (no audio is lost).
- [ ] Not tested on real audio hardware in this environment (headless CI). The OpenAL capture path is standard ALC
      capture, but needs a manual test on Windows/macOS/Linux with real microphones before release.

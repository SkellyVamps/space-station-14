# Proximity voice chat

Server-distributed, deeply integrated voice chat for Space Station 14: push to talk (or voice activation),
positional playback that follows the speaker, acoustics that respect walls, doors, windows, vacuum and lockers,
radio over headsets, per-character procedural voices, species colouring, masks/helmets, disguises and voice
impersonation, per-player muting, admin moderation.

| Document | For |
|---|---|
| [players.md](players.md) | Players: setup, push to talk, muting, troubleshooting |
| [server-owners.md](server-owners.md) | Server owners: enabling, the voice page and app, configuration, moderation |
| [developer.md](developer.md) | Developers: architecture, packet flow, DSP, acoustics, extending it |
| [THIRD-PARTY.md](THIRD-PARTY.md) | Third-party libraries and licences |

## Read this first: how it fits together

It runs on the **official engine and launcher**: players join from the official SS14 launcher like any other server,
and everything voice related comes from this server's game code.

The official engine can't record microphones, so players' microphones are the server's **voice webpage** (opened from
Options -> Voice, nothing to install) or the **Echoes Voice app** (a small optional download). Either one sends the
microphone to the game server; the game itself decides when you're heard (your push to talk keys), gives you your
character's voice, and plays everyone else's voice in game, positioned in the world.

The voice page is served by the game server itself on its web port; for players on other computers it needs an https
address (a Cloudflare Tunnel or reverse proxy). See [server-owners.md](server-owners.md#voice-webpage-and-app).

## Status

Implemented and tested:

- [x] Official engine and launcher: no forks
- [x] Microphone from the server's voice webpage or the Echoes Voice app (Windows, macOS, Linux)
- [x] Push to talk (local + radio keys, rebindable), voice activation with threshold/hold
- [x] 16 kHz IMA ADPCM, 20 ms frames; voice effects applied on the server
- [x] Adaptive jitter buffer, packet loss concealment, seamless playback as crossfaded clips
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
- [x] Options tab: microphone link, level meter, hearing yourself, mode, thresholds, volumes, network buffer, indicators
- [x] Speaking indicators and on-screen radio speaker list
- [x] Packet validation: size, frame structure, audio-time rate limiting, packet flood limiting, duplicate/replay
      window, muted users, maximum streams per listener
- [x] Unit tests (DSP, acoustics, rate limiting, jitter buffer, codec, seamless clip playback) and integration tests

Not yet done / known limitations:

- [ ] Voice between grids only crosses short gaps (docking airlocks); no full multi-grid acoustic graph.
- [ ] Personal mutes are kept in server memory (survive reconnects, not restarts) and admin voice mutes are not yet
      stored in the database.
- [ ] Cloning/body swaps keep the new body's voice; `VoiceProfileSystem.SetCharacterVoice` exists for hooking them up.
- [ ] Playback below about 20 FPS can have small gaps (clips are started from the game's frames).
- [ ] Not tested with real microphones and speakers in this environment (headless CI); needs a listen on real
      hardware before release.

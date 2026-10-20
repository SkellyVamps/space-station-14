# Integrated voice development branch

**Status: experimental development implementation, not a production release.**

This branch implements a first integrated capture/Opus/relay/playback path and gameplay
adapters. Automated checks do not establish that hardware capture, stock-launcher joining,
real station acoustics, or multiplayer operation work correctly. Voice defaults off at both
server and client. No audio content is intentionally written to logs or replay storage.

## Important distribution blocker

The official launcher obtains Robust engine versions from Space Wizards' build manifest.
A content server can advertise `engine_version`, but cannot supply an arbitrary replacement
engine URL or signature. The private engine patch here therefore **cannot currently be
installed simply by joining this server through the official launcher**.

Verified in launcher sources at investigation time:

- `SS14.Launcher/ConfigConstants.cs`: fixed Robust manifest sources.
- `SS14.Launcher/Models/EngineManager/EngineManagerDynamic.Manifest.cs`: manifest lookup.
- `SS14.Launcher/Models/EngineManager/EngineManagerDynamic.cs`: rejects missing engine versions.
- `SS14.Loader/Program.cs`: engine signature verification.

Inspected launcher commit: `a34ec36e404c8f62fe03e6e2853d5f50042d6841`.
Source: https://github.com/space-wizards/SS14.Launcher

Do not advertise a private version under an official engine version, disable signing for
players, promise that a content zip replaces the engine, or distribute a manual mod as though
it meets the original join-only requirement. The required decision is a supported engine/module
distribution route with the launcher maintainers, upstream exposure of the small engine APIs,
or a changed launcher requirement. Publishing content builds alone does not resolve this.

## Reproduce the development build

Base content commit: `81c150a7bd6002b838b1358a261785ae116bd5cf`.
Pinned engine commit: `de49cf724f8d8f3afa5c8bb3aaa5331a4cfac3b4`.

```sh
git clone --branch feature/integrated-voice --recurse-submodules https://github.com/SkellyVamps/space-station-14.git
cd space-station-14
python3 Tools/voice/prepare_engine.py
dotnet test Content.Tests/Content.Tests.csproj -m:1 --filter FullyQualifiedName~VoiceFoundationTest
```

Use the repository's .NET 10 SDK requirement. The engine changes live in
`Tools/voice/robust-voice.patch` so the content fork remains self-contained without pointing
its submodule at a nonexistent engine commit. The preparation script checks the engine pin,
recognizes an already-applied patch, and refuses conflicts. Rebase this patch explicitly when
updating RobustToolbox. Apply it before **any** content build or packaging workflow; the dedicated
`Voice development checks` workflow does this. Other upstream workflows have not all been adapted.

For development, build/run the content and patched engine together using this repository's
normal local client/server workflow. This is not a public player installation procedure.

## Server settings

In the server configuration:

```toml
[voice]
enabled = true
range = 10.0
minimum_pressure = 5.0
```

- Protocol: mono 48 kHz, one 20 ms Opus frame, managed encoder at 24 kbit/s.
- Maximum encoded payload: 512 bytes. Nominal 50 packets/s, burst budget 8 per connection.
- Local path searches: at most 2,048 expansions, maximum configured search radius 32 tiles.
- Acoustic snapshots expire after 100 ms; door-state events invalidate them immediately.
- Voice travels over existing SS14 networking; no separate voice service or port is introduced.
  Keep the normal SS14 game UDP and status/build HTTP endpoints reachable. Existing HTTP
  reverse proxies do not automatically carry the game's UDP traffic.
- `voice_mute <account UUID> <seconds>` requires Ban admin permission. Zero seconds removes a
  mute; up to seven days is accepted. Account mutes survive reconnect/respawn in this server
  process, but **do not survive restart**. Administrative changes are logged without speech.

## Player controls in a compatible development client

1. Open Options → Audio, opt into experimental voice, select your microphone, and apply.
2. Set local PTT and radio PTT in the normal Controls tab. They intentionally have no default
   key so joining a server cannot silently enable an existing key as a microphone trigger.
3. Use the local-only microphone test in Audio. It encodes/decodes locally and does not send
   test speech to the server. Stop the test or close the options panel to close capture.
4. Audio includes a mic level/status display, a player list with speaking indicators, and
   per-account volume/mute controls. Drag a volume slider and release to save. Refresh the
   list after players join. These preferences persist in the local client configuration.
5. Radio PTT speaks locally and on your equipped, enabled headset. The default channel is
   the headset's department/default channel. Developers can select another authorized channel
   with `cvar voice.radio_channel <prototype ID>`; an empty value restores the default.

The mic opens only during held PTT or an explicit local test. Capture stops on lost focus,
UI keyboard/modal focus for PTT, controlled-entity changes, disable, and shutdown. Device changes
reopen capture; a failed open has a retry delay. Device loss handling still needs physical testing.
Headphones are strongly recommended when testing local microphone loopback.

## Gameplay behavior implemented in code

- Server derives speaker from authenticated connection and controlled entity. Alive/speech
  permission, server mute, ingress rate/size/sequence, and effective-profile revision are checked.
- Local routing requires the same map and grid; unsupported cross-grid propagation fails closed.
- Four-neighbour acoustic path cost accounts for detours, barriers, actual atmosphere pressure,
  walls, closed doors, configurable windows, and explicitly configured containers.
- A closed normal door adds four distance units; open doors do not. A material override can
  block completely or set another penalty. Missing atmosphere fails closed. Barrier tiles which
  intentionally have no mixture sample real adjacent atmosphere, not invented gas.
- Standard full-tile windows have a transmission penalty; ordinary closets have a container
  penalty. Unknown nested containers block. Nested configured containers multiply attenuation.
- Existing headset/channel/encryption-key, telecom, map/long-range and radio attempt events
  govern radio permission. Nearby local delivery wins over radio to avoid duplicate playback.
  Intercoms, handheld-radio audio speakers and intrinsic radios are not yet integrated.
- Existing character voice selections and species resolve data-driven procedural presets.
  Character base + species + selected voice + status + worn equipment compose on the server.
  Pitch, low-pass, soft drive and robotic modulation run before encoding. No voice cloning.
- Gas mask and voice-changing chameleon-mask prototypes have modifiers. Pulled-down masks do
  not apply them. Other helmet/species/item presets can be added explicitly.
- Living characters only. Dead/ghost/spectator voice and observer eavesdropping are not enabled.
  `VoiceDeaf` is a voice-specific denial component; full hearing-impairment integration is pending.

## Architecture and extension points

- `Robust.Client.Audio`: microphone handles, public buffered audio API and sandbox-safe managed
  Opus wrappers. Capture and playback stay on the game thread. Headless capture returns unavailable.
- `Content.Shared.Voice`: versioned bounded packets, ingress guard, acoustic search, profile,
  permission and material components, plus procedural preset prototypes.
- `Content.Server.Voice`: identity/permission validation, bounded local acoustic snapshots,
  headset adapters, profile composition and moderation.
- `Content.Client.Voice`: opt-in PTT, pre-encode DSP, Opus, bounded jitter buffering/PLC,
  positional sources, radio coloration and controls.

The dedicated server never decodes/re-encodes audio. A missing jitter frame invokes Opus packet
loss concealment. In-band FEC is disabled; adaptive jitter timing is not implemented. Playback
supports up to 32 concurrent speaker streams and removes idle streams after 500 ms.

`VoiceProfileComponent` provides a base profile. `VoiceModifierComponent` on a mob applies a status
layer; on worn mask/head/outer-clothing it applies equipment coloration. Its `enabled` field can
be changed by server equipment logic. `voiceFilter` prototypes named `Species{SpeciesId}` and
`Voice{EmoteSoundsId}` provide species and existing-character-selector layers. Profile composition
is refreshed every 100 ms and on ingress, and replication sends the revision to the microphone
client. Stale revisions are rejected. This does not authenticate actual audio or stop a modified
client from submitting arbitrary audio.

`VoiceAcousticBarrier` fields: `blocks` and `extraDistance`. Use on a heavy door for `blocks: true`,
on a window for an added acoustic cost, and on a container for multiplicative attenuation.
Full-tile barriers are an approximation; directional barriers and detailed material frequency
responses require a directional edge/portal adapter. Do not treat this first implementation as
a physically accurate sound simulation. Distance-related receive occlusion uses existing audio
filtering; radio filtering runs after decode, without extra bandwidth or encoders.

The selected voice profile is procedural metadata, not a fingerprint. Authorized receivers can
save or alter audio in a modified client. Account IDs used for muting are sent to legitimate
listeners; anonymity-sensitive disguises need a privacy design for those IDs/UI before production.

## Validation and outstanding work

See [TODO.md](TODO.md) for the full readiness checklist and [VALIDATION.md](VALIDATION.md) for
commands/results. Synthetic tests do not substitute for two real clients with microphones.

New codec dependency: Concentus 2.2.2, permissive BSD-style license, managed implementation only.
Full notice is included in the engine patch at `ThirdParty/Concentus/LICENSE.txt`; include it in
binary distributions. Existing OpenTK/OpenAL dependencies are reused. No Figaro code or GPL DSP
was copied. No new AI models, virtual microphone drivers, or external voice clients are required
by the development implementation.

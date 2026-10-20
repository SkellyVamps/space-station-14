I want you to take ownership of a substantial software project and carry it through from architecture review to a working implementation.

# Project

Build a complete, server-distributed voice-chat system for Space Station 14:

https://github.com/space-wizards/space-station-14

The finished result should feel like:

**Space Station 14 + deeply integrated proximity voice chat**

Players should be able to join a compatible server through the normal SS14 launcher and automatically receive whatever custom client/content build is required.

Players should NOT need to separately install:

- Figaro
- VB-Cable
- Discord plugins
- virtual microphone software
- a separate voice-chat client
- any manual mod package

Everything should be integrated into the SS14/RobustToolbox client and server build distributed by the server.

Figaro can be used only as inspiration/reference for real-time voice effects:

https://github.com/MattMoony/figaro

Do not make Figaro itself a runtime dependency. Prefer a native C#/RobustToolbox implementation or suitably licensed native libraries.

This project is intended for our own SS14 fork/server. Do not optimize around getting these changes accepted upstream.

---

# Primary goal

Implement a production-quality voice system with all of the following major capabilities:

1. Microphone capture
2. Push-to-talk
3. Optional voice activation later
4. Opus voice compression
5. Client → server → client voice transport
6. Low-latency playback
7. Jitter buffering
8. Packet-loss tolerance
9. Positional/proximity voice
10. Advanced environmental acoustics
11. Radio communication
12. Character voice profiles
13. Species voice modifiers
14. Masks and helmets affecting voices
15. Voice-changing equipment
16. Disguised/obfuscated voices
17. Optional character-voice impersonation mechanics
18. Per-player volume/muting controls
19. Microphone/input-device settings
20. Speaking indicators
21. Server configuration and moderation tools
22. Good performance for realistic SS14 player counts

Do not stop at a simple proof of concept unless a lower-level blocker genuinely prevents further implementation. Work through the project systematically.

---

# Repository/engine investigation

Before changing code, inspect the current versions of:

- Space Station 14
- RobustToolbox
- the existing audio backend
- networking/message infrastructure
- entity/component systems
- transform/map/grid APIs
- atmosphere APIs
- door/wall systems
- radio/headset systems
- hearing/deafness mechanics
- player/session APIs
- settings/keybinding systems
- launcher/client distribution model

Determine where each part of the voice system belongs.

Expected separation will probably resemble:

```text
RobustToolbox
    low-level microphone capture
    input-device enumeration
    streaming/raw audio playback primitives
    low-level audio-buffer support

Content.Client
    microphone management
    push-to-talk
    voice DSP
    Opus encoding
    Opus decoding
    jitter buffering
    positional playback
    voice UI/settings

Content.Shared
    voice components
    prototypes
    messages
    shared state
    voice-profile definitions

Content.Server
    packet validation
    speaker identity
    proximity routing
    acoustic routing
    radio routing
    gameplay modifier selection
    moderation/rate limiting
```

Do not assume this structure is correct until the current codebase has been inspected.

Use the existing project conventions whenever practical.

---

# Core audio pipeline

The intended pipeline is approximately:

```text
Microphone
    ↓
Capture PCM
    ↓
Noise gate / microphone preprocessing
    ↓
Gameplay voice-effect chain
    ↓
Opus encoder
    ↓
Voice network packet
    ↓
Server routing
    ↓
Receiving clients
    ↓
Jitter buffer
    ↓
Opus decoder
    ↓
Environmental/radio playback processing
    ↓
3D positional playback
```

Target low latency.

Start with mono voice.

48 kHz is preferred if compatible with the chosen Opus implementation.

A 20 ms frame size is a reasonable starting point unless investigation shows a better choice.

---

# Voice networking

Design a dedicated voice packet/message format.

It should include only what is actually necessary.

Consider fields such as:

- voice session/stream ID
- sequence number
- timestamp/frame timing information
- encoded Opus payload
- transmission mode
- flags needed for radio/local communication

Do NOT trust a client-supplied speaker entity ID if the server can derive the speaker from the authenticated network session.

The server should determine the player's controlled entity.

The server must validate:

- player/session identity
- packet size
- packet rate
- transmission state
- voice permissions
- alive/dead state where applicable
- spam/rate limits

Do not send local voice packets to clients who cannot legitimately hear them.

This is both a gameplay rule and a privacy/bandwidth rule.

---

# Basic proximity voice

Implement local positional speech.

Example initial behavior:

```text
0-2 tiles:
    full conversational volume

3-5 tiles:
    normal falloff

6-8 tiles:
    quiet

9-10 tiles:
    barely audible

beyond configured maximum:
    packet is not delivered
```

Do not hard-code these exact values if prototypes/configuration would be better.

Distance/falloff should be configurable.

Attach playback to the actual speaking entity so spatial audio follows character movement.

---

# Advanced environmental acoustics

This is a core feature, not an optional future idea.

Local voice should interact with the SS14 world.

Investigate the current simulation and implement an acoustic/audibility system that accounts for as many of the following as reasonably practical:

## Walls

Walls should strongly block local speech.

Players should not clearly hear someone through solid station walls merely because Euclidean distance is short.

## Doors

Open doors should permit speech much more easily.

Closed doors should attenuate or block speech depending on their type/material/configuration.

Examples:

```text
open doorway:
    almost normal propagation

closed normal door:
    muffled/attenuated

heavy blast door:
    strongly blocked
```

Use existing entity/component/prototype information rather than hard-coded entity IDs wherever possible.

## Windows

Windows should behave differently from solid walls.

A closed window may reduce/muffle speech while allowing more transmission than a wall.

## Distance/pathing

Consider acoustic path distance rather than only straight-line distance where practical.

Do not implement a catastrophically expensive flood fill for every voice packet.

Design efficient caching or room/portal-style calculations if necessary.

## Atmosphere

Speech should interact with atmosphere.

At minimum investigate:

- pressure
- vacuum
- whether the speaker/listener share a connected atmosphere
- sealed spaces

Local spoken sound should not propagate normally through vacuum.

Examples:

```text
Speaker in pressurized hallway
Listener in same hallway
→ normal speech

Speaker and listener separated by vacuum
→ no normal local speech

Speaker inside sealed pressurized room
Listener outside in vacuum
→ local speech should not cross normally
```

Use existing SS14 atmosphere systems rather than creating an unrelated duplicate atmosphere simulation.

## Containers

Speech should be affected by being inside entities such as:

- lockers
- crates where relevant
- disposal units
- vehicles
- sealed containers

A character trapped inside a locker should sound muffled or potentially inaudible depending on configuration.

## Hearing state

Integrate with existing mechanics such as:

- deafness
- hearing impairment
- unconsciousness if appropriate
- ghost state
- admin/spectator behavior

Use game design sensibly and make unusual behaviors configurable.

---

# Acoustic performance

The advanced propagation system must be designed with server performance in mind.

Do not calculate expensive full-map acoustic simulation for every 20 ms voice packet.

Separate:

```text
audio packets
```

from:

```text
audibility state
```

Prefer an architecture where audibility relationships are recalculated only when relevant state changes or on a reasonable interval.

Potential triggers include:

- player movement
- doors opening/closing
- pressure/environment changes
- entering containers
- map/grid changes

Then voice packets can use cached routing/audibility data.

Profile this system.

---

# Radios

Integrate voice communication with SS14 radios/headsets.

Support at least:

- local voice only
- radio transmission
- local + radio if game rules require it
- radio channels
- headset state
- radio permissions
- encrypted/special channels where SS14 already supports them

Radio speech should not require physical acoustic proximity.

Radio audio should have a configurable audio effect chain such as:

- band-pass filtering
- compression
- subtle static
- clipping/distortion
- optional signal-quality effects

Do not overdo effects to the point of harming intelligibility.

Possible voice flow:

```text
Mic
 ↓
character/equipment effects
 ↓
Opus
 ↓
server
 ├── local acoustic recipients
 └── radio recipients
```

Determine whether radio effects belong before encoding or on receiving clients.

Choose an architecture that minimizes bandwidth/CPU duplication.

---

# Character voice profiles

Create a character-specific voice profile system.

The intention is NOT to AI-clone the real player's voice.

Use procedural DSP.

Potential properties:

- pitch
- formant
- resonance
- EQ
- roughness
- modulation
- compression
- subtle distortion
- species effects

A character should sound broadly consistent each round.

Where appropriate, expose controls during character setup.

Keep ranges safe and intelligible.

A player should be able to preview their character voice.

---

# Species voice effects

Species should be able to define additional voice-processing layers.

Examples could eventually include:

- robotic/vocoder characteristics
- unusual resonance
- pitch/formant ranges
- breathing/radio-like effects
- synthetic speech coloration

Implement this via prototypes/components rather than giant switch statements.

The processing chain should be composable:

```text
microphone
+
character profile
+
species profile
+
equipment modifiers
+
status modifiers
=
final local voice
```

---

# Masks, helmets and equipment

Equipment must be able to alter voice.

Examples:

## Gas mask

```text
slight muffling
reduced high frequencies
```

## Full helmet

Potentially stronger muffling.

## Powered/synthetic helmet

May add:

- speaker effect
- compression
- slight radio/synthetic processing

Implement generic reusable components/prototypes.

Conceptually something similar to:

```text
VoiceModifierComponent
VoiceFilterPrototype
```

but use naming/style appropriate for the actual SS14 codebase.

Do not hard-code individual equipment items into the central voice system.

---

# Voice-changing/disguise equipment

Support equipment specifically intended to conceal identity.

A disguise mask could:

- alter pitch
- alter formants
- change resonance
- add randomized but stable processing
- obscure recognition of the original speaker

The effect should remain understandable.

Server gameplay state determines which profile/effect is active.

The normal client should not be the authority deciding whether its mask is active.

---

# Character voice impersonation

Design support for advanced equipment capable of adopting another CHARACTER'S procedural voice profile.

This is character-profile impersonation, not AI cloning of the real human player.

Example:

```text
Traitor learns/captures Captain's character voice profile
        ↓
equips compatible voice changer
        ↓
server authorizes Captain-profile emulation
        ↓
DSP approximates Captain's configured procedural character voice
```

If this adds too much complexity to the first implementation, architect the profile system so this can be added cleanly after the foundational voice modifiers are functional.

---

# DSP/effect framework

Build a reusable real-time audio-processing pipeline.

Effects should be composable.

Desired effects include:

- gain
- noise gate
- pitch shifting
- formant shifting
- EQ
- high-pass/low-pass/band-pass
- compression
- distortion
- radio coloration
- vocoder/robot-style processing
- noise/static
- crackle
- echo where appropriate
- muffling
- modulation

Do not copy GPL Figaro source into an MIT/private fork without understanding licensing implications.

Use Figaro only for conceptual reference.

Prefer permissively licensed dependencies compatible with the project.

Document all new third-party libraries and their licenses.

Avoid heavyweight AI voice models.

Real-time DSP should run acceptably on ordinary gaming PCs.

---

# Voice settings UI

Add a proper voice settings section.

Include:

- enable/disable voice
- microphone input device
- microphone input gain
- push-to-talk keybind
- microphone level meter
- microphone test
- local voice output volume
- radio voice output volume if useful
- speaking indicator
- per-player mute
- per-player volume
- optional voice activation controls later

Changing microphones should not require restarting the entire game if avoidable.

Handle device disconnect/reconnect gracefully.

---

# Push-to-talk

PTT is the required initial transmission mode.

The key must be configurable using SS14's normal keybinding system.

Make input behavior robust when:

- game UI is focused
- chat is open
- menus are open
- player changes entity
- player disconnects
- microphone device disappears

Do not transmit when the player should not be transmitting.

---

# Voice activation

This is secondary to PTT.

Once the core system is stable, consider adding optional voice activation with:

- threshold
- attack/release
- noise gate
- visual indicator

PTT should remain available.

---

# Player controls

Players must be able to:

- mute individual players locally
- adjust individual player volume
- disable all voice
- control voice-output volume

Consider storing these preferences by account/user ID rather than temporary entity ID where appropriate.

---

# Moderation/admin tools

Design basic moderation support.

At minimum consider:

- server-wide disable
- player voice mute
- admin voice mute
- temporary mute
- permission checks
- logs of administrative voice actions

Do NOT record or store actual voice content by default.

Treat voice privacy seriously.

Do not introduce voice recording unless explicitly required later.

---

# Anti-abuse/network safeguards

Protect the server from malformed or abusive packets.

Include appropriate:

- packet-size limits
- packet-rate limits
- malformed Opus handling
- sequence sanity checks
- muted-user rejection
- maximum streams
- timeout handling

A malicious custom client will always be capable of sending arbitrary microphone/audio content.

Do not claim the system can cryptographically prove that transmitted speech is the player's unmodified real microphone.

The normal client should enforce gameplay effects, but design the server so a malicious client cannot hear people it was never sent packets for.

---

# Dead players / ghosts / spectators

Investigate how SS14 handles dead players and ghosts.

Design configurable behavior.

Possible rules could include:

```text
living players:
    hear living local voice

ghosts:
    hear ghosts
    optionally hear living voice based on server rules

living players:
    normally cannot hear ghosts

admins:
    configurable observer access
```

Do not impose these exact rules if existing SS14 conventions suggest something better.

---

# Maps and grids

Handle SS14's multi-map/multi-grid behavior correctly.

Never route local voice based only on world coordinate distance if entities are actually on unrelated maps/grids.

Voice between shuttles/grids should follow sensible spatial/acoustic rules.

---

# Client distribution

This is critical.

The system must fit SS14's normal custom-server distribution model.

The desired player experience is:

```text
Open SS14 launcher
      ↓
Join our server
      ↓
launcher downloads required build/content
      ↓
client launches
      ↓
voice chat available
```

No manual mod installation.

Document exactly how the custom client build must be published/configured for the server.

If engine changes require a custom RobustToolbox build, include those correctly in the server/client distribution process.

---

# Code organization

Use the current codebase's architecture and naming conventions.

Possible conceptual organization:

```text
Content.Client/Voice/
Content.Shared/Voice/
Content.Server/Voice/
```

plus RobustToolbox audio work where necessary.

Do not force this directory structure if current project conventions suggest better locations.

Keep systems modular.

Examples of conceptual responsibilities:

```text
VoiceCaptureSystem
VoiceEncodingSystem
VoicePlaybackSystem
VoiceJitterBuffer
VoiceProcessingSystem
VoiceRoutingSystem
VoiceAudibilitySystem
VoiceRadioSystem
VoiceProfileSystem
VoiceModerationSystem
```

Again, choose names based on existing conventions.

---

# Configuration/prototypes

Make important behavior configurable.

Examples:

```text
enable voice
local voice range
falloff
codec bitrate
frame duration
maximum packet size
maximum packet rate
environmental attenuation
door attenuation
wall attenuation
radio effect strength
voice-profile limits
```

Prefer CVars/prototypes/components according to how SS14 normally represents the relevant kind of setting.

---

# Testing

Build and test the code continuously.

Do not wait until the end to compile.

Create tests where practical for:

- packet validation
- routing logic
- distance checks
- map/grid separation
- muted players
- acoustic state
- door transitions
- vacuum transitions
- radio routing
- profile composition

Also perform integration testing with multiple local clients if the development setup allows it.

Important scenarios:

### Scenario A — basic proximity

Two players standing next to each other hear each other.

Walk away and volume decreases.

Walk beyond maximum range and packets stop arriving.

### Scenario B — wall

Two players separated by a solid wall cannot clearly hear each other.

### Scenario C — door

Open door:
speech passes.

Close door:
speech becomes muffled/blocked.

### Scenario D — vacuum

Players separated by vacuum cannot communicate through ordinary local speech.

### Scenario E — radio

Players far apart can communicate on an allowed radio channel.

### Scenario F — mask

Equip a voice-changing mask.

The transmitted voice changes automatically.

Remove it.

Normal character voice returns.

### Scenario G — character profile

Different characters belonging to the same human player can use different procedural voice characteristics.

### Scenario H — per-player mute

Client A mutes Client B.

Client B's voice is no longer played for Client A.

### Scenario I — malicious/invalid packets

Malformed/oversized/excessive voice packets are rejected safely.

---

# Performance testing

Voice chat must not destroy server tick performance.

Measure:

- server CPU
- client CPU
- allocations
- network bandwidth
- voice packet rate
- audibility calculation cost

Test with simulated realistic player counts where possible.

Optimize recipient lookup.

Avoid:

```text
for every voice packet
    iterate every entity in the game
```

Use SS14 spatial lookup systems and cached audibility relationships.

DSP should generally happen client-side.

The dedicated server should not decode/re-encode everyone's Opus audio unless an architectural requirement makes that unavoidable.

---

# Failure handling

Handle gracefully:

- no microphone
- permission denied
- disconnected microphone
- Opus decoder failure
- malformed packet
- temporary packet loss
- player disconnect
- entity deletion
- map transfer
- respawn
- server voice disabled

Voice-system failure should not crash the game.

---

# Documentation

Create documentation for:

## Developers

- architecture
- packet flow
- DSP pipeline
- engine modifications
- components/prototypes
- how to add new voice effects
- how to make equipment modify voice
- how to add species profiles
- how environmental audibility works

## Server owners

- enabling voice
- configuring ranges
- codec settings
- moderation
- client build distribution
- firewall/network considerations

## Players

- enabling microphone
- selecting device
- configuring PTT
- microphone test
- muting players
- troubleshooting

---

# Implementation workflow

Do not respond with only an architecture document.

I want implementation work.

Start by:

1. Inspecting the repositories.
2. Mapping current audio/network APIs.
3. Identifying whether microphone capture requires RobustToolbox changes.
4. Identifying an appropriate Opus library and its license.
5. Producing a concise implementation plan based on the ACTUAL code.
6. Creating the foundational voice subsystem.
7. Building it.
8. Fixing build errors.
9. Testing it.
10. Iterating feature-by-feature through the requirements above.

Maintain a project TODO/checklist while working.

When a decision is uncertain, inspect existing SS14/RobustToolbox patterns before inventing a new architecture.

Do not repeatedly stop to ask me minor implementation questions. Make reasonable engineering decisions and document them.

Ask me only when a decision materially changes the desired gameplay or requires information you genuinely cannot derive from the repository/project.

If some requested advanced feature cannot reasonably be implemented yet because of an engine limitation, finish everything that can be completed, clearly identify the blocker, and leave the architecture prepared for the missing feature.

---

# Git practices

Work in a dedicated feature branch.

Make logical commits by subsystem.

Do not overwrite unrelated changes.

Keep upstream merging in mind because this will be maintained as a fork of SS14.

Avoid unnecessarily invasive changes.

Where possible, isolate RobustToolbox modifications so future upstream merges are manageable.

---

# Important design rule

Keep these layers separate:

```text
CAPTURE
microphone acquisition

PROCESSING
character/species/equipment DSP

ENCODING
Opus

TRANSPORT
network packets

ROUTING
who receives packets

AUDIBILITY
distance/environment/doors/atmosphere

RADIO
non-local communication

PLAYBACK
jitter buffer + spatial audio
```

This separation is important because I eventually want to expand the system significantly.

---

# Final target

The finished experience should eventually allow this:

A player walks through a station hallway and talks normally.

Nearby characters hear their voice spatially.

As they walk farther away, the voice becomes quieter.

A closed door muffles or blocks them.

Opening the door lets the voice through.

A solid wall blocks speech.

A vacuum between players prevents normal spoken communication.

A player trapped in a locker sounds muffled.

Players using headsets can communicate over radio channels regardless of local distance.

Radio speech has an appropriate radio effect.

A character's selected procedural voice profile affects their normal speech.

Species can alter that profile.

Putting on a gas mask muffles the voice.

Putting on specialized voice-changing equipment alters or disguises the voice.

Removing the equipment immediately restores the correct character voice.

All of this happens automatically based on SS14 gameplay state.

And the player obtained the entire feature simply by joining the server through the normal SS14 launcher.

Start by inspecting the current Space Station 14 and RobustToolbox codebases and begin implementation.
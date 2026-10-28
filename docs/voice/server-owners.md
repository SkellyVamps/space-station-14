# Voice chat: server owner guide

## Enabling

Voice is on by default. Toggle it live with `cvar voice.enabled false`. Clients hide voice and the server drops all
voice packets when it's off.

Voice uses the normal game connection (the same UDP port as the game, `net.port`, default 1212). No extra ports,
relays or third-party services. Nothing is recorded; the server forwards Opus frames without decoding or storing them.

## Client distribution

SS14 delivers a server's build in two parts:

* **Content** (all `Content.*` code, prototypes, resources, every voice system) comes from your server, as usual.
* **Engine** (RobustToolbox) is fetched by the launcher **by version number** from an engine manifest, and the loader
  checks the engine's signature before running it.

Voice chat needs a modified engine, so this setup uses three repositories:

| Repository | Branch | Role |
|---|---|---|
| `SkellyVamps/space-station-14` | your server branch | Content. Its `RobustToolbox` submodule points at the engine fork |
| `SkellyVamps/RobustToolbox` | `voice-chat-engine` | Engine with voice primitives. Version `293.0.1.1` = upstream `293.0.1` + voice build 1. Tagging publishes signed builds and updates `manifest.json` on the `robust-builds` branch |
| `SkellyVamps/SS14.Launcher` | `voice-chat` | Launcher players install. Merges your engine manifest with the official one and trusts your signing key as well as the official one |

Official engine versions are never overridden, so the fork launcher keeps working for every official server. Official
launchers can't run the fork engine, so players must use your launcher for your server.

### One-time setup

1. **Create the signing key** on your own machine. Keep `engine_signing_key.pem` private: anyone with it can make
   engines your players' launchers will run.
   ```bash
   openssl genpkey -algorithm ed25519 -out engine_signing_key.pem
   openssl pkey -in engine_signing_key.pem -pubout -out signing_key_custom
   ```
2. **Store the private key** in `SkellyVamps/RobustToolbox` -> Settings -> Secrets and variables -> Actions -> New
   repository secret, named `ENGINE_SIGNING_KEY`, with the full contents of `engine_signing_key.pem`.
3. **Enable Actions on both forks.** GitHub disables workflows on new forks: open each fork's Actions tab and enable them.
4. **Add the public key to the launcher.** Commit `signing_key_custom` as `SS14.Launcher/signing_key_custom` on the
   launcher's `voice-chat` branch.

### Publishing an engine build

The engine version in `RobustToolbox/MSBuild/Robust.Engine.Version.props` must match the tag.

```bash
cd RobustToolbox
git tag v293.0.1.1
git push origin v293.0.1.1
```

`publish-fork-build.yml` builds Windows/macOS/Linux/FreeBSD clients, attaches them to a GitHub release, signs them and
adds them to `https://raw.githubusercontent.com/SkellyVamps/RobustToolbox/robust-builds/manifest.json`.
The engine build must be published **before** players join a server running that engine version.

### Publishing the launcher

On the launcher's `voice-chat` branch, push a tag (e.g. `v0.40.3-voice1`). `publish-release.yml` creates a GitHub
release with Windows, macOS and Linux zips. Point players at that release page. The launcher builds aren't
code-signed, so Windows SmartScreen and macOS Gatekeeper will warn on first launch.

### Running the server

Build and host the server from this repository as usual. The server advertises `build.engine_version` = `293.0.1.1`
automatically (from the engine fork's version), whether you host the client zip yourself or use a CDN.
Your server still appears on the official hub; players without your launcher get an "engine version not found" error.

### Updating to a newer upstream engine

1. In `SkellyVamps/RobustToolbox`, merge the upstream release tag into `voice-chat-engine`.
2. Set the version to `<upstream version>.1` (e.g. `294.0.0.1`), commit, tag `v294.0.0.1`, push the tag.
3. Bump the content repo's `RobustToolbox` submodule to that commit, as you would for an upstream engine update.

### Updating the launcher

Merge upstream SS14.Launcher into `voice-chat` (the fork's changes are small and marked "Voice chat fork"), then tag a
new release. See `VOICE_FORK.md` in the launcher fork.

## Configuration

All CVars are in `Content.Shared/CCVar/CCVars.Voice.cs`. Server ones (put them in `server_config.toml` under
`[voice]`):

| CVar | Default | Meaning |
|---|---|---|
| `voice.enabled` | `true` | Master switch |
| `voice.range` | `10` | Maximum acoustic distance in tiles. Nothing is sent beyond it |
| `voice.full_volume_range` | `2` | Full volume up to this distance |
| `voice.falloff_exponent` | `1.6` | Falloff curve (1 = linear, higher = drops off faster) |
| `voice.min_audible_gain` | `0.02` | Quieter than this: not sent |
| `voice.acoustics` | `true` | Walls/doors/vacuum. `false` = straight-line distance only |
| `voice.default_barrier_penalty` | `-1` | Air-blocking structures without `VoiceAcousticBarrier`: extra tiles of distance, negative = block |
| `voice.default_barrier_muffle` | `0.8` | ...and their muffling |
| `voice.min_pressure` | `10` | kPa below which sound doesn't travel |
| `voice.full_pressure` | `70` | kPa at which speech is at full volume |
| `voice.cross_grid_range` | `2.5` | Max gap (tiles) voice crosses between grids, e.g. docked shuttles |
| `voice.default_container_penalty` / `_muffle` | `1` / `0.3` | Containers without `VoiceContainerAcoustics` |
| `voice.audibility_interval` | `0.2` | Seconds between audibility recalculations per active speaker |
| `voice.field_max_age` | `1` | Seconds before an acoustic field is rebuilt anyway (pressure changes) |
| `voice.radio_enabled` | `true` | Voice over radio |
| `voice.loudspeaker_gain` | `0.8` | Volume of radio voice played out loud by intercoms and handheld radios (0 = off) |
| `voice.radio_microphones` | `true` | Handheld radios with their microphone on broadcast nearby voice (intercoms use their hold-to-talk button) |
| `voice.radio_local_gain` | `0.6` | How loud people near you hear you talking into your radio (0 = silent) |
| `voice.radio_filter` | `RadioDefault` | `voiceFilter` prototype for radio playback |
| `voice.ghosts_hear_living` | `true` | Ghosts (with ghost hearing on) hear living players nearby |
| `voice.ghost_voice` | `true` | Ghosts can talk to ghosts |
| `voice.living_hear_ghosts` | `false` | Living hear ghosts (spooky event option) |
| `voice.sleeping_muffle` | `0.7` | How muffled sleepers hear things (1 = they hear nothing) |
| `voice.crit_can_hear` | `true` | Critical players hear (heavily muffled) |
| `voice.creature_noise_delay` | `0.4` | Seconds of talking before a creature that can't speak makes its first noise |
| `voice.creature_noise_interval` | `1.5` | Average seconds between its noises while it keeps talking |
| `voice.profile_max_pitch` / `_formant` | `6` / `4` | Limits (semitones) for character voices |
| `voice.bitrate` | `24000` | Opus bitrate clients use |
| `voice.frame_ms` | `20` | Frame size: 10, 20, 40 or 60 |
| `voice.expected_loss` | `10` | Packet loss % the encoder protects against (FEC) |
| `voice.max_packet_bytes` | `400` | Largest accepted frame |
| `voice.max_packets_per_second` | `60` | Packet rate limit per client |
| `voice.max_burst_ms` | `400` | Audio a client may send ahead of real time |
| `voice.abuse_alert_threshold` | `200` | Rejected packets per minute before an admin log entry |
| `voice.max_streams_per_listener` | `12` | Max simultaneous speakers delivered to one player (quietest dropped) |

Acoustic properties of specific doors, windows and containers are prototype data (`VoiceAcousticBarrier`,
`VoiceContainerAcoustics`), not CVars; see [developer.md](developer.md).

## Moderation

| Action | How |
|---|---|
| Voice mute a player | `voicemute <player> [minutes, 0 = until restart] [reason]`, or right click -> Admin -> Voice mute |
| Lift it | `voiceunmute <player>` |
| List mutes | `voicemutes` |
| Server-wide off | `cvar voice.enabled false` |
| Stats | `voicestats` (active speakers, packets, rejections, timings) |

Requires the `MODERATOR` flag (`voicestats`: `DEBUG`). Mutes, unmutes, voice recordings for impersonation and repeated
packet abuse are written to the admin log under the `Voice` log type. Voice content is never logged or stored.

Muted players are told why they can't talk (Options -> Voice shows the status).

## Bandwidth and performance

* Per active stream: ~24 kbit/s Opus + headers, roughly 35-40 kbit/s on the wire.
* Each speaker uploads one stream; the server sends one copy per listener who can hear them.
  10 people talking at once, each heard by 8 others, is ~80 streams, about 3 Mbit/s outgoing.
* Server CPU: no audio processing. Recipient lists are rebuilt a few times a second per *active* speaker using cached
  acoustic fields, which take on the order of tens of microseconds each (see `voicestats` and the benchmark).
* Client CPU: one encoder and the speaker's effect chain when talking; one decoder plus light filtering per audible
  speaker.

## Network considerations

* Voice is sent unreliably (UDP, no retransmission) to keep latency low; the jitter buffer and Opus FEC hide moderate
  loss. Players on bad connections can raise "Network buffer" in Options -> Voice.
* Voice doesn't need any additional firewall rules beyond the game port.

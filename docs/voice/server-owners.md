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
| `voice.whisper_range_scale` | `0.3` | How far a whisper carries, as a fraction of the two ranges above |
| `voice.shout_range_scale` | `1.6` | How far a shout carries, as a multiple of the two ranges above |
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
| Voice mute a player | `voicemute <player> [minutes, 0 = until restart] [reason]`, right click -> Admin -> Voice mute, or the player panel's Voice row (pick a duration) |
| Lift it | `voiceunmute <player>` |
| List mutes | `voicemutes` |
| Server-wide off | `cvar voice.enabled false` |
| Stats | `voicestats` (active speakers, packets, rejections, timings) |

Requires the `MODERATOR` flag (`voicestats`: `DEBUG`). Mutes, unmutes, voice recordings for impersonation and repeated
packet abuse are written to the admin log under the `Voice` log type. Audio is never logged or stored; what people say
is logged as text, see below.

## Voice webpage and app

Players can use their browser, or the standalone Echoes Voice app, as their microphone instead of the game's. The
server itself serves the page at **`/voice`** on its web (status) port, the same port as the hub and launcher use, so
there's nothing else to run or open: it's part of the game server process.

* **HTTPS is required** for players connecting from other computers (browsers only allow microphones on secure
  pages; the same computer is fine without). With a Cloudflare Tunnel or reverse proxy that already serves the status
  port on a domain, set `voice.external_public_url` to that address, e.g. `https://ss14.example.com`. If unset,
  `hub.server_url` is used (`ss14s://` becomes `https://`), else the address the player connected to.
* WebSockets must pass through (Cloudflare Tunnels do by default).
* **The Echoes Voice app** (`EchoesVoice/`): run the **Publish Echoes Voice app** GitHub workflow with a tag like
  `voice-app-v1.0.0`; it builds Windows, macOS and Linux zips into a release. Then either set
  `voice.external_app_download_url` to that release page, or download the zips into `voice-app/` in the server's data
  folder and the voice page lists them, served by the game server itself (`.zip`, `.exe`, `.dmg`, `.AppImage`...).

| CVar | Default | Description |
|---|---|---|
| `voice.external_enabled` | `true` | Allow the voice webpage / app |
| `voice.external_public_url` | empty | Public https address of the server's web port |
| `voice.external_app_download_url` | empty | Where the page links for the app (empty: files in `voice-app/`) |

Linking is safe: the page or app proves who it is with a random code the game handed to that player over their own
logged-in connection, valid until they leave the server. The page only records; the server only lets the audio
through while the player's talk keys in the game say so, then gives it their character's voice and treats it like any
other voice (routing, radio, mutes, voice-to-text...).

## Voice-to-text

Everything players say over voice chat is converted to text on the server and written to the admin logs under its own
log type, **`VoiceTranscript`**, so it can be searched and filtered like text chat:

```
[Voice-to-text] Urist McHands (Tester) (radio, Security): "Someone's breaking into the armory."
```

It notes how it was said: `talking`, `whispering`, `shouting`, `radio, <channel>`, `intercom, <channel>` or `ghost`.
Each entry is one press of the talk key (long speeches are split every 30 seconds) and is logged a few seconds after it
was spoken, once recognition is done. Players see a notice in Options -> Voice that voice is converted to text for the
admin logs; mention it in your rules too.

Recognition is OpenAI's Whisper model running **locally** on the server (whisper.cpp via Whisper.net): no audio leaves
the server and nothing is paid for. It runs on a background thread, never the game's.

| CVar | Default | Description |
|---|---|---|
| `voice.transcribe` | `true` | On/off |
| `voice.transcribe_model` | `BaseEn` | `TinyEn` (fastest), `BaseEn`, `SmallEn` (more accurate, slower), `MediumEn`...; drop `En` for other languages |
| `voice.transcribe_model_path` | empty | Model file. Empty: `whisper/ggml-<model>.bin` in the server data folder |
| `voice.transcribe_auto_download` | `true` | Download the model from Hugging Face the first time if missing (Tiny ~75 MB, Base ~140 MB, Small ~470 MB) |
| `voice.transcribe_language` | `en` | Language code, or `auto` to detect |
| `voice.transcribe_threads` | `2` | CPU threads for recognition |
| `voice.transcribe_max_queue` | `32` | Speech waiting to be transcribed; beyond it new speech is skipped instead of falling behind |

The model is loaded (and downloaded if needed) when the server starts; the log says `Speech recognition ready`. How
fast it is depends on the CPU and model. If `voicestats` shows speech being skipped, raise `voice.transcribe_threads` or
use `TinyEn`.

**Pterodactyl / Docker:** the server data folder must be writable (it already is for the database), and the container
needs internet access the first time to download the model. Without it, download the `ggml-base.en.bin` file from
Hugging Face (`sandrohanea/whisper.net`) yourself and set `voice.transcribe_model_path` to it. Server builds made with
`Content.Packaging` include the speech recognition libraries for Windows, Linux and macOS.

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

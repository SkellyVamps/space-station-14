# Voice chat: server owner guide

## Enabling

Voice is on by default. Toggle it live with `cvar voice.enabled false`. Clients hide voice and the server drops all
voice packets when it's off.

Voice to players uses the normal game connection (the same UDP port as the game, `net.port`, default 1212).
Microphones connect to the server's web (status) port, the same port the hub and launcher use. No extra ports, relays
or third-party services. Audio is never recorded.

## Client distribution

Nothing special: the server runs on the **official** RobustToolbox engine, so players join with the **official
launcher**, from the hub or by address, and the game downloads this server's content as usual. There is no engine or
launcher to build or publish.

Players connect their microphone with the voice webpage or the Echoes Voice app (see below); the game tells them how
in Options -> Voice.

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

* Per active stream: 64 kbit/s ADPCM + headers, roughly 75 kbit/s on the wire.
* Each player's page or app uploads one stream (about 70 kbit/s, all the time while connected); the server sends one
  copy per listener who can hear them. 10 people talking at once, each heard by 8 others, is ~80 streams, about
  6 Mbit/s outgoing.
* Server CPU: each talking player's voice effects (a few percent of one core for several speakers), recipient lists
  rebuilt a few times a second per *active* speaker from cached acoustic fields (see `voicestats` and the
  benchmark), and voice-to-text if on.
* Client CPU: one decoder plus light filtering per audible speaker.

## Network considerations

* Voice is sent unreliably (UDP, no retransmission) to keep latency low; the jitter buffer and loss concealment hide
  moderate loss. Players on bad connections can raise "Network buffer" in Options -> Voice.
* Voice doesn't need any firewall rules beyond the game port and its web (status) port, which servers expose anyway.

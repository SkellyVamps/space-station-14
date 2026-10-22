# Voice chat: server owner guide

## Enabling

Voice is on by default. Toggle it live with `cvar voice.enabled false`. Clients hide voice and the server drops all
voice packets when it's off.

Voice uses the normal game connection (the same UDP port as the game, `net.port`, default 1212). No extra ports,
relays or third-party services. Nothing is recorded; the server forwards Opus frames without decoding or storing them.

## Client distribution

How SS14 delivers a server's build:

* **Content** (`Content.Client.dll`, `Content.Shared.dll`, resources/prototypes): delivered by your server (via
  `build.download_url` / manifest, or ACZ when the server hosts it itself). Every voice system lives here, so
  content-side updates reach players automatically.
* **Engine** (RobustToolbox): the launcher downloads it **by version number** (`build.engine_version`) from the
  official Space Wizards build servers. A server can't make the official launcher run its own engine build.

Voice chat needs three engine primitives (microphone capture, streaming audio, Opus) that aren't in an official engine
release yet. They're kept as a patch series in `Tools/voice/engine-patches/`.

### What this means today

| Setup | Works? |
|---|---|
| Local development (`runserver` + `runclient` with the patched submodule) | Yes |
| Integration tests | Yes |
| Public server + official launcher, unpatched official engine | **No: clients fail to load the content.** Don't deploy yet. |

### Getting to "join the server and it just works"

1. **Upstream the engine patch (recommended).** Submit `Tools/voice/engine-patches/*.patch` to
   [space-wizards/RobustToolbox](https://github.com/space-wizards/RobustToolbox) as a PR. It's additive, generic and
   has tests, which is what upstream usually needs. Once it's in a released engine version, bump the
   `RobustToolbox` submodule to that version, remove the patch step, and the official launcher delivers everything:
   no installs for players.
2. **Your own launcher + engine builds.** Fork RobustToolbox with the patch, publish engine builds with your own
   manifest and signing key, and ship a fork of SS14.Launcher pointed at them. Works immediately, but every player must
   install your launcher, which breaks the "normal launcher" goal.

Until (1) lands, keep voice on a test server where testers run the patched client from source.

### Building with the patch

```bash
git submodule update --init --recursive
Tools/voice/apply-engine-patches.sh     # idempotent
python RUN_THIS.py                      # as usual
dotnet build -c Release
```

If you create a RobustToolbox fork, push the patched branch there and point `.gitmodules` at it instead of running the
script; that keeps CI and packaging unchanged.

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
| `voice.radio_local_gain` | `0.6` | How loud people near you hear you talking into your radio (0 = silent) |
| `voice.radio_filter` | `RadioDefault` | `voiceFilter` prototype for radio playback |
| `voice.ghosts_hear_living` | `true` | Ghosts (with ghost hearing on) hear living players nearby |
| `voice.ghost_voice` | `true` | Ghosts can talk to ghosts |
| `voice.living_hear_ghosts` | `false` | Living hear ghosts (spooky event option) |
| `voice.sleeping_muffle` | `0.7` | How muffled sleepers hear things (1 = they hear nothing) |
| `voice.crit_can_hear` | `true` | Critical players hear (heavily muffled) |
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

# Two-player voice acceptance test

This is a private development test. Do not replace the production server yet. Use client and server
artifacts built from the **same commit**, and keep a note of that SHA and platform for every result.

## Obtain matching builds

On the feature branch, use these GitHub Actions runs:

- **Custom voice client development bundles**: Windows or Linux `SkellySS14-voice-dev-*` artifact.
- **Matching voice server development bundle**: `SkellySS14-voice-server-linux-x64` artifact.

GitHub wraps each artifact in a ZIP. Extract that, verify the included archive's SHA-256 against its
sidecar, then extract the actual game/server archive. These are unsigned development builds.
The server includes `voice-build-info.json`; the client includes `SkellySS14/build-info.json`.
Their `content_commit`, `engine_base` and `engine_patch_sha256` must match.

## Private server

The Linux server bundle includes its .NET runtime and prebuilt client archive for the launcher `/info`
endpoint. Native Linux dependencies may still be needed.
Extract to a new directory on the server host, separate from the existing server and saves:

```sh
chmod +x Robust.Server
./Robust.Server --config-file voice-test.toml --data-dir voice-test-data
```

The included preset enables voice, requires SS14 authentication, disables unauthenticated localhost
bypass and hub advertisement, and uses port 1212. Forward/allow **UDP 1212 for gameplay** and
**TCP 1212 for the launcher `/info` endpoint**. In Pterodactyl, allocate both protocols and use a
separate test server/container. If those ports are occupied, change the preset and allocation together.
Expose only to the test participants where practical. Never fix login failures by disabling auth.

The server address for players is `ss14://your-host:1212`. If `/info` is behind an HTTPS reverse proxy,
use an `ss14s://host:port` address and configure the server's advertised game `connect_address` to the
reachable UDP endpoint. A Cloudflare HTTP tunnel alone does not carry normal SS14 UDP gameplay.

## Players

Each player extracts the whole client bundle, starts `SkellyLauncher.exe` or `SkellyLauncher`, and
signs in using their own existing SS14 account and two-factor code if enabled. Enter the test server
address. In Options, enable voice, select a microphone, run the **local** microphone test, set input
and output gain, and bind local/radio push-to-talk under Controls. No voice keys are assigned by default.
Use headphones to avoid feedback. The launcher stays open while the game runs. Ready up/start the
round and spawn characters before testing multiplayer voice; lobby players cannot transmit.

## Record pass/fail and logs

| Scenario | Expected behavior |
| --- | --- |
| Account login, MFA and reconnect | Correct existing account; expired/invalid login refuses connection |
| Open room, nearby players | Clear local audio; gain falls with path distance |
| Beyond configured range / another map | No local audio |
| Closed door vs open door | Reduced/blocked path according to material; opening updates within bounded cache time |
| Solid wall / window / alternate corridor | Material rules and path detours apply; no straight-through wall leak |
| Vacuum gap | No local path through vacuum |
| Lockers and other containers | Prototype-based container policy applies |
| Matching headset channel/key with telecom | Radio audio works; local/radio reception does not duplicate audio |
| Wrong channel/key or disabled headset | No radio reception |
| Mask/equipment/species profile changes | Effects change; stream identity rotates; no account name appears in voice roster |
| Stream mute / volume | Only selected received stream changes; admin account mute survives disguise/reconnect |
| Alt-tab / focus loss / opening UI | Normal push-to-talk stops; no stuck transmission |
| Respawn, disconnect/reconnect | No old capture/playback or stale codec state |
| Microphone unplug/replug / device change | Game keeps running; capture recovers or reports error without a rapid retry loop |
| Leave game and close launcher | Clean graphical shutdown, no orphan game process |
| Signed update | Separate owner-configured release test; dev builds have updates disabled |

For an administrator, `voice_mute <account-UUID> <seconds>` mutes an account; `0` removes it. Current
admin mutes are in memory, so server restart persistence remains unfinished. Transcription is not
enabled, and no voice recordings are created by this implementation. Anonymous stream IDs do not
hide identity that can already be inferred from world/session data or voice recognition.

Record OS, input/output hardware, server/client commit, steps, observed behavior, timestamp and relevant
client/server logs for failures. Do not share authentication tokens/passwords. Check CPU, tick time and
network use with both players talking; a two-player pass does not establish high-player-count capacity.
Cross-grid acoustics, full formant processing, intercom routing and other items in TODO.md remain outside
the implemented foundation and must not be marked passed based on this test.

# Test voice on one computer

Use matching client and server builds from `feature/integrated-voice`. These are unsigned development
builds. A single computer can run the server and client; no router port forwarding or public hosting
is needed. Internet access is still needed for normal SS14 account authentication.

## 1. Download and extract (Windows)

In the fork's GitHub **Actions**, choose successful runs for the same commit:

- **Custom voice client development bundles**: download `SkellySS14-voice-dev-win-x64`.
- **Matching voice server development bundle**: download `SkellySS14-voice-server-win-x64`.

Extract each GitHub artifact ZIP. Inside each is the actual bundle ZIP and a `.zip.sha256` file.
In PowerShell, `Get-FileHash .\<bundle-name>.zip -Algorithm SHA256` must match the first value in its
sidecar. Extract the actual bundles into separate folders, for example `C:\SS14Voice\Client` and
`C:\SS14Voice\Server`. Keep the entire directory contents together.
Compare server `voice-build-info.json` with client `SkellySS14\build-info.json`: `content_commit`,
`engine_base` and `engine_patch_sha256` must match. The bundles include the .NET runtime.

## 2. Start the local server

Open PowerShell in the extracted server folder containing `Robust.Server.exe` and run:

```powershell
.\Robust.Server.exe --config-file voice-local.toml --data-dir voice-local-data
```

Leave this window open. Initial map loading can take several minutes. The preset binds gameplay and
status to `127.0.0.1:1212`, enables voice, disables hub advertising, and requires real SS14 authentication
including for localhost. It uses a separate data directory. Do not disable authentication to fix login.
No inbound firewall exception or port forwarding is needed for this localhost test.
Check `http://127.0.0.1:1212/info` in your browser: it should return JSON with authentication mode
`Required` (or `1`). If port 1212 is already occupied, stop the other local test server first.

## 3. Connect using the custom launcher

Start `SkellyLauncher.exe` inside the extracted client's `SkellySS14` folder. Sign in with your existing
SS14 username, password and two-factor code if enabled. Set the server address to:

```text
ss14://127.0.0.1:1212
```

Connect, create/select a character and ready up. If the round does not begin, enter `startround` in the
**server console**, then join the round and spawn as a living character. Leave the launcher open.
The stock SS14 launcher does not substitute for this bundled custom launcher/client.

## 4. Test your microphone by yourself

Use headphones. In **Options → Audio**:

1. Enable **Voice chat (experimental)** and select the microphone. Apply settings if prompted.
2. Click **Start microphone test (heard only by you)**.
3. Speak: the meter should move and you should hear your processed voice. Adjust microphone gain and
   voice volume. This checks capture, processing, Opus encoding/decoding and playback locally.
4. Click **Stop microphone test**. The microphone should close when the test stops and no PTT key is held.
5. In **Controls**, bind **Push-to-talk** and **Radio push-to-talk (also speaks locally)**; neither has a
   default binding. Close Options and hold local PTT while controlling your living character.
6. Release PTT, open a UI or switch focus: transmission should stop. Repeat after reconnecting and after
   changing microphone devices. If Windows blocks capture, check its microphone privacy permission for
   desktop apps and restart the client after enabling it.

The microphone test sends no voice packets. Hearing yourself there does **not** prove server routing,
distance attenuation, doors, radio or another player's reception. Normal PTT intentionally does not
play your own voice back to you, so silence with only one player is expected.

## 5. Optional: test full routing on the same machine

Full routing needs two distinct authenticated player sessions controlling two living characters. If
you have two legitimate SS14 accounts permitted by the account service, run two client copies from
separate extracted folders, sign each in separately and connect both to the same localhost server.
The same account cannot represent two players on one server. Copying the client folder does not create
another account; client audio preferences may still be shared through its user-data directory.

Mute the listener client's microphone, stop both local microphone tests, use headphones and keep the
speaking client focused while holding its PTT key. Disable **Mute audio when unfocused** on the listener
if needed, and apply settings before switching clients. Place the characters nearby; speech from the
focused client should be heard through the other client. Then move them apart or close a door between
them and repeat. Use [the acceptance checklist](ACCEPTANCE_TEST.md) for radio, disguises and moderation.
Without a second session, mark multiplayer scenarios **not tested**; do not weaken authentication or
create fake account identities. A second physical computer is unnecessary, but a later second-human
check is useful for feedback and microphone quality.

## 6. Stop and report results

Close the game and launcher. Enter `shutdown` in the server console and wait for it to exit.
Record the shared build commit, Windows version, microphone/headphones and which steps passed.
For failures, include relevant client/server logs with credentials/tokens removed. Admin mute
persistence, transcription and other unfinished items in `TODO.md` remain outside this test.

## Linux alternative

Download the matching `linux-x64` client and server artifacts and follow the same extraction/checksum
and metadata checks. In the server directory:

```sh
chmod +x Robust.Server
./Robust.Server --config-file voice-local.toml --data-dir voice-local-data
```

In the client bundle, mark `SkellySS14/SkellyLauncher` and `SkellySS14/Client/SkellySS14` executable,
then launch `SkellyLauncher`. Native graphical/audio dependencies are still required. Use the same
localhost address and microphone steps above.

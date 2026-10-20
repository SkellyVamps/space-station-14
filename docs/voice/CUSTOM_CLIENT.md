# Custom client and launcher

This is an experimental custom installation with integrated voice, superseding the original
stock-launcher-only requirement. Live microphone, graphical and multiplayer acceptance testing
remain required before a player release.

## Build and run

Use Python 3.11+ and .NET 10:

```sh
python3 Tools/voice/package_custom_client.py --runtime win-x64
# Or --runtime linux-x64
```

Extract the complete `SkellySS14` directory and run `SkellyLauncher.exe` (Windows) or `SkellyLauncher`
(Linux). Linux may need native OS dependencies and execute permission for `SkellyLauncher` and
`Client/SkellySS14`. The launcher and client dependencies are isolated because Robust uses a
customized Avalonia.Base assembly.

Enter existing SS14 credentials, a two-factor code if enabled, and the voice server address.
Passwords go directly to the official SS14 HTTPS auth endpoint; requests cannot redirect.
Authentication, renewal and explicit logout use the existing account UUID and protocol. Passwords
and tokens stay in memory; users sign in again after restarting. Closing the launcher drops its
session from memory; explicit Sign out revokes it. Closing while a game runs leaves the game running.
No official launcher credential database or engine cache is read or modified.

Server `/info` is bounded and supplies the authentication public key and advertised game address.
Authentication-disabled servers are refused. The game receives its token through the engine's normal
environment handshake, never command-line arguments; the engine clears the token variable after reading
it. Production servers should require normal SS14 authentication. No authentication bypass is needed.

`Voice.ClientHost` preserves sandboxing, mounts engine resources before game resources, and uses
separate settings. The executable under `Client/` accepts normal developer engine arguments.
The launcher currently runs its bundled content/engine for matching voice servers; it does not download
arbitrary server content like the official launcher. `--content-zip` reuses content without verifying
compatibility. ZIPs and SHA-256 sidecars appear under `release/voice-client/`.

## Updates

[Signed release setup](SIGNED_RELEASES.md) documents owner keys, signed manifests, verified downloads,
rollback checks and side-by-side portable installation. Updates default disabled until the owner embeds
its public key and fixed HTTPS channel. No private key is in the repository. Development ZIP checksums
alone do not authenticate a release, and development bundles are not OS-signed installers.

## Verification

`smoke_custom_client.py` verifies checksum and ZIP integrity, sandboxed headless menu initialization,
and graceful `+quit`. The engine patch fixes a quit request being ignored between loop creation and
startup. This does not test graphical operation, microphone hardware, live account sign-in or multiplayer.
`Voice.Launcher.Tests` uses synthetic credentials and HTTP responses only.

## Identity and moderation

Voice packets carry opaque listener-scoped IDs, rotated on entity/profile changes. Radio playback omits
the source entity. The heard-stream roster uses anonymous labels and stream volume/mutes. Account IDs
remain available server-side for admin mutes. This removes direct account IDs from playback; it does not
prevent inference from visible world/session data or recognizing a voice.

Speech-to-text moderation remains proposed, not implemented or enabled. It should be asynchronous,
moderator-only, tied to authenticated account identity and character/disguise context, with retention
and player disclosure. No audio recording or transcription service is enabled.

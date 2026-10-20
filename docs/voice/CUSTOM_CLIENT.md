# Custom client distribution decision

The server owner selected a custom client on 2026-10-19. The required player experience is now
one installation of our client/launcher, followed by integrated voice in-game. No browser voice
page, virtual microphone, Figaro installation or separate voice application is planned.
The original brief remains as historical requirements; this decision supersedes its stock-launcher-only requirement.

## Development executable

`Voice.ClientHost` embeds the patched Robust client using a small `ContentStart.StartEngineLibrary`
entry point added to the engine patch (engine-mode assembly loading). It keeps content sandboxing enabled, mounts engine resources before game resources,
and uses a separate `Skelly SS14 Voice` settings directory. It does not modify the official launcher
or its engine cache, disable signature verification in that launcher, or read saved login tokens.

Build a development bundle from the repository root with Python 3.11+ and the .NET 10 SDK:

```sh
python3 Tools/voice/package_custom_client.py --runtime win-x64
# Or on Linux:
python3 Tools/voice/package_custom_client.py --runtime linux-x64
```

The script applies the pinned engine patch, invokes the existing content packager, publishes a
self-contained executable with native dependencies, and bundles engine/game resources and codec
license notices. It creates a platform zip and SHA-256 sidecar in `release/voice-client/`. Native
OS dependencies may still be required on Linux. The Windows executable is `SkellySS14.exe` inside
the extracted `SkellySS14` folder; Linux uses `SkellySS14`. Do not run the executable directly out
of the zip. `--content-zip` accepts a previously packaged content build; it does not rebuild or
verify compatibility of that input. `--dotnet` selects a particular dotnet executable.

These are **unsigned development bundles**, not an installer, automatic updater, or completed
account-login flow. A checksum detects accidental corruption; it does not authenticate a release.
A developer may pass normal engine arguments, for example `--connect --connect-address localhost`.
Do not disable production server authentication to make an unfinished bundle connect.

## Player release path still to implement

1. A custom launcher/bootstrap with normal SS14 account authentication, explicit account selection,
   and short-lived game token handoff. Never scrape another launcher's credential database.
2. Our own update manifest and pinned signing public key, authenticated artifacts, atomic updates,
   rollback, and engine/content compatibility checks. Keep private signing keys outside the repo.
3. Installer/portable distribution, branding and OS signing where available. Start with Windows x64
   and Linux x64; macOS requires its own packaging/signing and testing.
4. Authenticated connection to our server, microphone permission/settings, and actual two-client
   proximity/radio/equipment testing before a player release.

This removes dependence on upstream distributing the microphone engine changes, while leaving
us responsible for maintaining and distributing the client. The small pinned engine patch remains
isolated to make upstream merges manageable.

## Moderation direction

The proposed moderation extension is asynchronous speech-to-text keyed to authenticated account
identity, with character/disguise context, timestamps and radio/local mode. This is not implemented
by the custom host. Live voice must not wait for transcription. Transcripts should have moderator
access controls, retention settings and player disclosure, and remain fallible review aids.
Audio recording remains off; no transcription service or new recording was enabled by this change.

## Automated smoke scope

`python3 Tools/voice/smoke_custom_client.py <bundle.zip>` verifies its SHA-256 and ZIP CRC,
extracts a fresh copy, runs sandboxed headless initialization, checks that the main menu is reached,
and exits using the engine's `hardquit` command. It disables eager texture preloading for this test.
It does not test graphics, microphone capture, account login, networking, or graceful shutdown.
The initial headless `quit` run reached the main menu but hung during shutdown; this remains an
open issue, and the startup smoke is deliberately not presented as a shutdown test.

The engine patch also adds an engine-mode embedding entry point (separate content assembly load
context) and fixes a release-build RSI-preload OpenGL query when running headless. The latter uses
the same non-GL atlas limit already used by the engine's development headless path.

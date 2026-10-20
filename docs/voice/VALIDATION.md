# Validation record

Development host: Linux x64, .NET SDK 10.0.100. Content/engine base revisions are in README.md.

## Completed checks

- `dotnet build Content.Shared/Content.Shared.csproj -m:1`: passed.
- Client and server compilation through focused test build: passed (upstream warnings remain).
- `dotnet test Content.Tests/Content.Tests.csproj -m:1 --filter FullyQualifiedName~VoiceFoundationTest`:
  seven tests passed in the final full run (461 ms), with no skipped tests. Covers ingress token/sequence
  guards, jitter reordering/wrap/PLC requests, walls/door penalty/vacuum/path range, real managed
  Opus round trip/decoder PLC, procedural DSP output, and a synthetic acoustic workload.
- Engine patch: applies cleanly to a separate worktree at the pinned revision; reverse check
  succeeds; `prepare_engine.py` detects an already-applied patch; Python syntax check passes.
- Diff whitespace checks: clean.

## Integration work

The first `PrototypesHaveKnownComponents` run successfully started headless client/server
instances, then found an indentation error introduced in `specific.yml`. This has been fixed.
The subsequent sandbox check found unverifiable stack allocation in the playback loop. It was
replaced with reusable arrays. The final rerun passed both `PrototypesHaveKnownComponents` and
`SandboxTest`: 2 passed, 0 failed, 0 skipped, in 16 seconds. The sandbox test checks both content
client and shared assemblies against the patched engine without weakening verification.

## Performance limits

The initial debug synthetic search benchmark ran 128 simulated speakers × 10 route epochs in
270.5 ms, allocating 27,792,192 bytes and sampling 1,025,280 tiles. This is a test of the pure
search, with constant atmosphere and no ECS, network, audio, or station. The final implementation
uses Robust's existing priority queue for sandbox compatibility and skips already-cheaper nodes
before sampling. Its rerun took 280.3 ms, allocated 29,208,600 bytes and sampled 282,880 tiles
(about 72% fewer atmosphere samples). Neither figure is an in-game player-count benchmark.

At 24 kbit/s a continuously transmitting speaker has about 3 kB/s codec payload before transport
headers/encryption. The server forwards only to authorized recipients, so output bandwidth grows
with the number of authorized listeners. This is an arithmetic estimate, not a network capture.

No physical microphone/speaker, multi-client listening, Windows/macOS device, PVS-boundary,
station acoustics, radio encryption abuse, sustained tick-load, packet fuzzing, or official-launcher
acceptance test has passed yet. No production performance or privacy guarantee is implied.

## Custom-client follow-up (2026-10-19)

- Added `Voice.ClientHost` and the engine-mode embedding entry point; host compilation passed.
- Host `--help` works, and missing resource archives produce a clear error with exit status 1.
- Release client content packaging passed and produced a roughly 203 MB content archive.
- Self-contained Linux x64 publication and bundle packaging passed (roughly 256 MB final zip).
- Bundle SHA-256, ZIP CRC, sandboxed content loading, RSI preload and main-menu initialization
  passed using `smoke_custom_client.py`. It uses `hardquit` after initialization and does not
  claim to verify graceful shutdown. The earlier headless `quit` test hung after reaching the menu.
- The updated complete engine patch applies cleanly to a fresh checkout at its pinned revision.
- Python syntax checks pass for the preparation, packaging and smoke tools.
- Existing GitHub `Voice development checks` run 39345769721 completed successfully before this follow-up.

Windows packaging and startup are configured in the new workflow but were not tested locally.
No graphical session, microphone hardware, authenticated server login, installer signing, updater,
or speech-to-text moderation service was verified or enabled by this follow-up.

## Launcher and matching server follow-up — 2026-10-20

The temporary workspace was reset before the unpublished follow-up could be saved. The changes were
reconstructed from the recorded work and published in checkpoints. Initial recovery CI caught an
incomplete engine patch and missing import; both were fixed. The patch now includes all new capture,
codec and license files, applies to a clean pinned engine checkout, and checks required files on reuse.

Validated checkpoint `f5c9a9b232484953b2b28d59a619066321ece25f`:

- Launcher authentication/update contract tests passed with synthetic credentials and HTTP responses.
  Coverage includes MFA, account UUID, renewal/logout, environment token handoff, server addresses,
  signature verification, tampering, rollback, installation and unsafe archive refusal.
- Nine focused voice tests passed; prototype validation and client/shared sandbox checks passed.
- Windows x64 and Linux x64 complete client bundles built, passed archive integrity, sandboxed
  headless menu initialization and graceful `+quit` shutdown checks, and were uploaded by CI.
- The matching self-contained Linux server built and passed archive integrity, fresh extracted
  startup, `/info` with authentication required, and graceful console shutdown in GitHub CI.
- The server smoke caught a clean-install failure in `/info`: the package needed Hybrid ACZ client
  content. Server packaging now builds and embeds `Content.Client.zip` instead of relying on paths
  that exist only in a development checkout.

Evidence:

- Voice checks: https://github.com/SkellyVamps/space-station-14/actions/runs/39404953542
- Client bundles: https://github.com/SkellyVamps/space-station-14/actions/runs/39404953510
- Server bundle/startup: https://github.com/SkellyVamps/space-station-14/actions/runs/39404953606

The Linux client was also built and smoke-tested locally. Local server startup is blocked by this
execution environment's network-interface enumeration policy; the successful CI server test is the
networked startup evidence. Local simultaneous Debug/Release restores interfered with integration
build assets, so clean CI runs are used for the reported integration results.

Subsequent launcher fixes add official `ss14s://` default port 443 handling, serialize update installs,
and dispose playback sources if initialization fails. Their exact revision is identified by the CI run.
No actual account credentials, microphone hardware, graphical two-client game, owner signing identity,
HTTPS update channel or production deployment has been verified. These remain live acceptance work;
see ACCEPTANCE_TEST.md and SIGNED_RELEASES.md. Speech-to-text is not enabled.

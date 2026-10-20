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

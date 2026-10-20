# Voice project readiness checklist

Legend: [x] implemented foundation; [ ] unfinished or not verified. A checked implementation is
not a claim of production readiness. The original goal remains full server-distributed voice.

## Engine and transport
- [x] Inspect pinned content, engine audio, sessions, network, atmosphere, radio and launcher code.
- [x] Native OpenAL capture and input enumeration API, with headless behavior.
- [x] Expose buffered PCM playback to content; managed Opus wrappers and license notice.
- [x] Dedicated unreliable bounded frame/playback messages; authenticated speaker identity.
- [x] Per-connection token bucket, size bounds, replay/old sequence rejection and idle reset.
- [x] Jitter reordering, bounded streams, fixed playback clock, Opus packet loss concealment.
- [x] Bounded mono/20 ms Opus framing validation on ingress and playback.
- [ ] Independent network fuzzing and sustained abuse tests.
- [ ] FEC, adaptive jitter, disconnect/reconnect hardware soak testing, cross-platform audio QA.
- [x] Use standard 16-bit PCM voice playback instead of requiring float-buffer support.
- [x] Owner selected a custom client; stock-launcher-only requirement superseded.
- [x] Add separate sandboxed executable host and self-contained development bundle recipe.
- [x] Linux development bundle integrity, sandbox loading and headless main-menu startup pass.
- [x] Implement honoring quit between loop creation and startup.
- [ ] Revalidate graceful bundled shutdown and normal graphical shutdown on both platforms.
- [x] Graphical launcher with official SS14 auth, MFA, renewal and logout; offline contract tests pass.
- [x] Signed-update code with runtime/rollback/hash validation and side-by-side installation.
- [ ] Owner key/channel setup, OS installer/signing and live player release tests.

## World and gameplay
- [x] Local positional playback; server range/falloff and map/grid checks.
- [x] Bounded path search and 100 ms acoustic cache; door invalidation.
- [x] Atmosphere pressure/vacuum checks and prototype-based barriers/containers.
- [x] Headset channels, keys, telecom and cancellation events; local/radio deduplication.
- [x] Pre-encode character/species/status/equipment procedural pitch/filter/drive/modulation.
- [x] Gas mask and voice changer prototype examples; existing character voice selector presets.
- [x] Voice entity mute/deaf denial components; account temporary admin mute and action logging.
- [ ] Station-based integration tests for walls, windows, doors, vacuum gaps, lockers and radios.
- [ ] Directional wall/window edges, docking/cross-grid acoustic portals, vehicles/disposals.
- [ ] Detailed closed-door material muffle filters, pressure-dependent gain, immediate atmos
      invalidation rather than bounded 100 ms refresh, and acoustically correct diagonals.
- [ ] Intercom/handheld/intrinsic-radio routing, radio output volume/channel controls in UI.
- [ ] Full hearing impairment, ghost/dead/spectator/admin policies and configuration.
- [x] Listener-scoped opaque playback IDs with entity/profile rotation and anonymous stream roster.
- [ ] Review identity inference from existing world/session data and local mute behavior after disguise changes.
- [ ] Equipment-specific active-state integration with existing VoiceMask toggles/implants.
- [ ] Character-specific independent pitch/formant controls, robust formant shifting, resonance,
      EQ/compressor/vocoder/static/crackle/echo framework and procedural impersonation mechanics.
- [ ] Dedicated character-setup voice preview and persisted procedural sliders beyond existing presets.
- [ ] Persistent moderation storage, admin panel, mute feedback, mute expiry cleanup.

## Client UX
- [x] Explicit opt-in, normal configurable local/radio PTT bindings, focus guards.
- [x] Device/gain/output settings, live mic meter/status and local encode/decode test.
- [x] Per-stream volume/mute preferences and speaking status; account IDs are not exposed by playback.
- [x] Dynamic anonymous heard-stream roster.
- [ ] HUD/world speaking indicators and broader privacy review.
- [ ] Voice activation threshold/attack/release; remains secondary to PTT.
- [ ] Physical microphone unplug/replug and permission-denied tests on Windows/Linux/macOS.
- [x] Device/source exception isolation, retry delay and capture codec reset.
- [ ] Physical device hot-switch and reconnect soak tests.

## Verification and release
- [x] Seven focused unit scenarios, real managed Opus round trip, and synthetic acoustic benchmark pass.
- [x] Existing prototype component validation and client/shared sandbox integration checks pass.
- [x] Reproducible revision-pinned engine patch and preparation script.
- [x] Feature-branch CI build/test workflow.
- [ ] All upstream build/packaging workflows adapted to apply pinned engine patch.
- [ ] Two-client audio integration, PVS boundary tests, late-join/profile replication tests.
- [ ] Realistic server/client CPU, network, allocation and tick measurements in a station.
- [ ] Privacy/security review, parser fuzz tests, sustained abuse and moderation tests.
- [ ] Signed custom-client distribution, hosted artifacts and custom-launcher acceptance test.
- [ ] Production release only after above blockers and verification are resolved.

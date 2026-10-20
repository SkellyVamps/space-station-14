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
- [ ] Independent network fuzzing; exact Opus packet-duration/framing validation on ingress.
- [ ] FEC, adaptive jitter, disconnect/reconnect hardware soak testing, cross-platform audio QA.
- [ ] Verify float-buffer support/fallback on older OpenAL implementations.
- [ ] Resolve official-launcher engine distribution/signing blocker.

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
- [ ] Stable anonymous disguise stream IDs and UI identity rules; raw account mute identity
      currently accompanies legitimate received audio and can reveal disguised speaker identity.
- [ ] Equipment-specific active-state integration with existing VoiceMask toggles/implants.
- [ ] Character-specific independent pitch/formant controls, robust formant shifting, resonance,
      EQ/compressor/vocoder/static/crackle/echo framework and procedural impersonation mechanics.
- [ ] Dedicated character-setup voice preview and persisted procedural sliders beyond existing presets.
- [ ] Persistent moderation storage, admin panel, mute feedback, mute expiry cleanup.

## Client UX
- [x] Explicit opt-in, normal configurable local/radio PTT bindings, focus guards.
- [x] Device/gain/output settings, live mic meter/status and local encode/decode test.
- [x] Per-account volume/mute preferences and speaking status in the voice controls list.
- [ ] HUD/world speaking indicators, privacy-reviewed displayed names, dynamic roster updates.
- [ ] Voice activation threshold/attack/release; remains secondary to PTT.
- [ ] Physical microphone unplug/replug and permission-denied tests on Windows/Linux/macOS.
- [ ] Full failure isolation for device/source errors, smooth hot-switch and codec-state reset.

## Verification and release
- [x] Seven focused unit scenarios, real managed Opus round trip, and synthetic acoustic benchmark pass.
- [x] Existing prototype component validation and client/shared sandbox integration checks pass.
- [x] Reproducible revision-pinned engine patch and preparation script.
- [x] Feature-branch CI build/test workflow.
- [ ] All upstream build/packaging workflows adapted to apply pinned engine patch.
- [ ] Two-client audio integration, PVS boundary tests, late-join/profile replication tests.
- [ ] Realistic server/client CPU, network, allocation and tick measurements in a station.
- [ ] Privacy/security review, parser fuzz tests, sustained abuse and moderation tests.
- [ ] Signed engine/module distribution, hosted artifacts and stock-launcher acceptance test.
- [ ] Production release only after above blockers and verification are resolved.

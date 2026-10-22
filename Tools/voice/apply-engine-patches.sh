#!/usr/bin/env bash
# Applies the voice chat engine patches on top of the RobustToolbox submodule.
#
# Voice chat needs a few engine primitives (microphone capture, streaming audio sources, Opus codec) that upstream
# RobustToolbox doesn't have yet. Until they are upstreamed or this fork points its submodule at a RobustToolbox fork
# that contains them, run this after `git submodule update`.
#
# Usage: Tools/voice/apply-engine-patches.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
patches="$root/Tools/voice/engine-patches"
engine="$root/RobustToolbox"

if [ -f "$engine/Robust.Client/Audio/Realtime/IRealtimeAudioManager.cs" ]; then
    echo "Voice engine patches already applied."
    exit 0
fi

cd "$engine"
git -c user.name="voice-patches" -c user.email="voice-patches@localhost" am --3way "$patches"/*.patch
echo "Applied voice engine patches to RobustToolbox."

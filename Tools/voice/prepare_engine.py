#!/usr/bin/env python3
"""Apply the reviewed, revision-pinned voice engine changes. Never resets local work."""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
ENGINE = ROOT / "RobustToolbox"
PATCH = pathlib.Path(__file__).with_name("robust-voice.patch")
EXPECTED = "de49cf724f8d8f3afa5c8bb3aaa5331a4cfac3b4"


def git(*args):
    return subprocess.run(["git", "-C", str(ENGINE), *args], capture_output=True, text=True)


def verify_files():
    required = ["Robust.Client/Audio/AudioManager.Capture.cs", "Robust.Client/Audio/IAudioCapture.cs",
                "Robust.Client/Audio/VoiceCodec.cs", "ThirdParty/Concentus/LICENSE.txt"]
    missing = [name for name in required if not (ENGINE / name).is_file()]
    if missing:
        sys.exit("Incomplete voice engine patch; required files missing: " + ", ".join(missing))


def main():
    revision = git("rev-parse", "HEAD")
    if revision.returncode or revision.stdout.strip() != EXPECTED:
        sys.exit("Voice engine patch requires RobustToolbox " + EXPECTED + "; inspect/rebase the patch before changing the pin.")
    if git("apply", "--reverse", "--check", str(PATCH)).returncode == 0:
        verify_files()
        print("Voice engine patch already applied.")
        return
    check = git("apply", "--check", str(PATCH))
    if check.returncode:
        sys.exit("Cannot apply voice patch without affecting existing work:\n" + check.stderr)
    applied = git("apply", str(PATCH))
    if applied.returncode:
        sys.exit(applied.stderr)
    verify_files()
    print("Applied voice engine patch. Build content and engine from this checkout together.")


if __name__ == "__main__":
    main()

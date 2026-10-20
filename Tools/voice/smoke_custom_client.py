#!/usr/bin/env python3
"""Verify a locally built development bundle, then check headless startup and graceful shutdown."""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=pathlib.Path)
    args = parser.parse_args()
    archive = args.archive.resolve()
    expected = archive.with_suffix(".zip.sha256").read_text().split()[0]
    with archive.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
            raise SystemExit("Bundle checksum mismatch")
    with tempfile.TemporaryDirectory(prefix="skelly-voice-smoke-") as scratch:
        with zipfile.ZipFile(archive) as packed:
            for entry in packed.infolist():
                path = pathlib.PurePosixPath(entry.filename)
                if path.is_absolute() or ".." in path.parts or path.parts[0] != "SkellySS14":
                    raise SystemExit("Unexpected archive layout")
            if packed.testzip() is not None:
                raise SystemExit("Bundle ZIP checksum failure")
            packed.extractall(scratch)
        root = pathlib.Path(scratch) / "SkellySS14"
        executable = root / "Client" / ("SkellySS14.exe" if os.name == "nt" else "SkellySS14")
        executable.chmod(executable.stat().st_mode | 0o100)
        try:
            result = subprocess.run([str(executable), "--headless", "--self-contained",
                                     "--cvar", "res.texturepreloadingenabled=false", "+quit"],
                                    cwd=scratch, capture_output=True, text=True, encoding="utf-8",
                                    errors="replace", timeout=60)
        except subprocess.TimeoutExpired as error:
            for output in (error.stdout, error.stderr):
                if output:
                    print(output.decode("utf-8", errors="replace") if isinstance(output, bytes) else output)
            raise SystemExit("Headless startup exceeded 60 seconds")
        log = result.stdout + result.stderr
        if (result.returncode or "[FATL]" in log or "ENABLING sandboxing" not in log or
                "Switching to state Content.Client.MainMenu.MainScreen" not in log):
            print(log)
            raise SystemExit(f"Client did not complete headless initialization (exit {result.returncode})")
        info = json.loads((root / "build-info.json").read_text())
        print(json.dumps({"runtime": info["runtime"], "checksum": "passed", "zip_crc": "passed",
                          "headless_startup": "passed", "sandbox_enabled": "ENABLING sandboxing" in log,
                          "signed": info["signed"], "graceful_shutdown_tested": True}))


if __name__ == "__main__":
    main()

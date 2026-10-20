#!/usr/bin/env python3
"""Build a complete development client bundle. Does not install, sign, or publish it."""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]


def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)


def package(args):
    run(sys.executable, str(ROOT / "Tools/voice/prepare_engine.py"))
    # Build serially and ask the packager to reuse these outputs; never wipe unrelated build artifacts.
    if not args.content_zip:
        run(args.dotnet, "build", "Content.Client/Content.Client.csproj", "-c", "Release",
            "-p:FullRelease=true", "-m:1")
        run(args.dotnet, "build", "Content.Packaging", "-c", "Release", "-m:1")
        run(args.dotnet, "run", "--project", "Content.Packaging", "-c", "Release", "--no-build",
            "--", "client", "--skip-build", "--configuration", "Release", "--no-wipe-release")
    content = pathlib.Path(args.content_zip or ROOT / "release/SS14.Client.zip").resolve()
    if not content.is_file():
        raise SystemExit("Client content zip does not exist: " + str(content))
    with zipfile.ZipFile(content) as archive:
        if "Assemblies/Content.Client.dll" not in archive.namelist():
            raise SystemExit("Expected a packaged SS14 client content zip, not an engine or server archive")
    output = pathlib.Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="skelly-voice-client-") as scratch:
        bundle = pathlib.Path(scratch) / "SkellySS14"
        run(args.dotnet, "publish", "Voice.ClientHost/Voice.ClientHost.csproj", "-c", "Release",
            "-r", args.runtime, "--self-contained", "true", "-p:FullRelease=true",
            "-p:RobustILLink=false", "-p:PublishTrimmed=false",
            "-p:TargetOS=" + ("Windows" if args.runtime.startswith("win-") else "Linux"),
            "-o", str(bundle), "-m:1")
        # Zip the actual engine resources; mount the separately packaged content after them.
        with zipfile.ZipFile(bundle / "engine-resources.zip", "w", zipfile.ZIP_DEFLATED, strict_timestamps=False) as archive:
            resource_root = ROOT / "RobustToolbox/Resources"
            for file in sorted(resource_root.rglob("*")):
                if file.is_file() and not any(part.startswith(".") for part in file.relative_to(resource_root).parts):
                    archive.write(file, file.relative_to(resource_root).as_posix())
        shutil.copyfile(content, bundle / "game-content.zip")
        notices = bundle / "Notices"
        notices.mkdir()
        shutil.copyfile(ROOT / "RobustToolbox/ThirdParty/Concentus/LICENSE.txt", notices / "Concentus.txt")
        for base, prefix in [(ROOT, "Content"), (ROOT / "RobustToolbox", "Robust")]:
            for source in base.glob("LICENSE*"):
                if source.is_file():
                    shutil.copyfile(source, notices / (prefix + "-" + source.name))
        shutil.copyfile(ROOT / "docs/voice/CUSTOM_CLIENT.md", bundle / "README.md")
        with content.open("rb") as stream:
            content_digest = hashlib.file_digest(stream, "sha256").hexdigest()
        metadata = {"content_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                    "engine_base": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT / "RobustToolbox", text=True).strip(),
                    "engine_patch_sha256": hashlib.sha256((ROOT / "Tools/voice/robust-voice.patch").read_bytes()).hexdigest(),
                    "runtime": args.runtime, "channel": "development", "signed": False,
                    "source_dirty": bool(subprocess.check_output(
                        ["git", "status", "--porcelain", "--ignore-submodules=dirty"], cwd=ROOT, text=True).strip()),
                    "content_sha256": content_digest}
        (bundle / "build-info.json").write_text(json.dumps(metadata, indent=2) + "\n")
        name = "SkellySS14-voice-dev-" + args.runtime
        archive = output / (name + ".zip")
        pending = archive.with_suffix(".zip.partial")
        # .NET runtime files may have epoch timestamps, which ZIP cannot represent.
        with zipfile.ZipFile(pending, "w", zipfile.ZIP_DEFLATED, strict_timestamps=False) as packed:
            for file in sorted(bundle.rglob("*")):
                if file.is_file():
                    packed.write(file, file.relative_to(scratch).as_posix())
        pending.replace(archive)
        with archive.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        archive.with_suffix(".zip.sha256").write_text(digest + "  " + archive.name + "\n")
        print("Created development client bundle: " + str(archive))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", choices=["win-x64", "linux-x64"], required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--content-zip", help="Use an already-built client content zip (does not rebuild content)")
    parser.add_argument("--output", default=str(ROOT / "release/voice-client"))
    args = parser.parse_args()
    executable = shutil.which(args.dotnet)
    if not executable:
        parser.error("dotnet executable was not found")
    args.dotnet = executable
    # Nested engine XAML builds also honor this; avoid uncontrolled compiler fan-out.
    os.environ.setdefault("DOTNET_PROCESSOR_COUNT", "1")
    os.environ["PATH"] = str(pathlib.Path(executable).parent) + os.pathsep + os.environ.get("PATH", "")
    package(args)


if __name__ == "__main__":
    main()

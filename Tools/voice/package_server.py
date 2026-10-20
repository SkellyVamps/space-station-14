#!/usr/bin/env python3
"""Build a matching self-contained Windows or Linux voice server; never installs or deploys it."""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--runtime', choices=['win-x64', 'linux-x64'], default='linux-x64')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        parser.error('dotnet not found')
    os.environ.setdefault('DOTNET_PROCESSOR_COUNT', '1')
    os.environ['PATH'] = str(pathlib.Path(dotnet).parent) + os.pathsep + os.environ.get('PATH', '')

    def run(*command):
        subprocess.run(command, cwd=ROOT, check=True)

    run(sys.executable, str(ROOT / 'Tools/voice/prepare_engine.py'))
    # /info prepares the client manifest even when players use our bundled custom client.
    # Embed Hybrid ACZ so a clean server never falls back to repository-only build paths.
    run(dotnet, 'build', 'Content.Client', '-c', 'Release', '-p:FullRelease=true', '-m:1')
    run(dotnet, 'build', 'Content.Server', '-c', 'Release', '-p:FullRelease=true', '-m:1')
    run(dotnet, 'publish', 'RobustToolbox/Robust.Server', '-c', 'Release', '-r', args.runtime,
        '--self-contained', 'true', '-p:FullRelease=true', '-p:RobustILLink=false', '-p:PublishTrimmed=false', '-m:1')
    run(dotnet, 'build', 'Content.Packaging', '-c', 'Release', '-m:1')
    run(dotnet, 'run', '--project', 'Content.Packaging', '-c', 'Release', '--no-build', '--',
        'server', '--skip-build', '--hybrid-acz', '--configuration', 'Release', '--platform', args.runtime, '--no-wipe-release')
    source = ROOT / f'release/SS14.Server_{args.runtime}.zip'
    output = ROOT / 'release/voice-server'
    output.mkdir(parents=True, exist_ok=True)
    archive = output / f'SkellySS14-voice-server-{args.runtime}.zip'
    shutil.copyfile(source, archive)
    metadata = {
        'content_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'engine_base': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT / 'RobustToolbox', text=True).strip(),
        'engine_patch_sha256': hashlib.sha256((ROOT / 'Tools/voice/robust-voice.patch').read_bytes()).hexdigest(),
        'runtime': args.runtime, 'channel': 'development', 'self_contained': True,
    }
    with zipfile.ZipFile(archive, 'a', zipfile.ZIP_DEFLATED) as bundle:
        bundle.writestr('voice-build-info.json', json.dumps(metadata, indent=2) + '\n')
        bundle.writestr('voice-test.toml', (ROOT / 'Tools/voice/server-test.toml').read_text())
        bundle.writestr('voice-local.toml', (ROOT / 'Tools/voice/server-local.toml').read_text())
        bundle.writestr('LOCAL-TESTING.md', (ROOT / 'docs/voice/LOCAL_TESTING.md').read_text())
        bundle.writestr('VOICE-TESTING.md', (ROOT / 'docs/voice/ACCEPTANCE_TEST.md').read_text())
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    archive.with_suffix('.zip.sha256').write_text(digest + '  ' + archive.name + '\n')
    print('Created matching development server: ' + str(archive))


if __name__ == '__main__':
    main()

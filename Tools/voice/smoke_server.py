#!/usr/bin/env python3
"""Verify a fresh server archive, authenticated /info startup and graceful console shutdown."""
import argparse
import hashlib
import json
import os
import pathlib
import socket
import subprocess
import tempfile
import time
import urllib.request
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=pathlib.Path)
    args = parser.parse_args()
    archive = args.archive.resolve()
    expected = archive.with_suffix('.zip.sha256').read_text().split()[0]
    with archive.open('rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != expected:
            raise SystemExit('Server archive checksum mismatch')
    with tempfile.TemporaryDirectory(prefix='skelly-server-smoke-') as scratch:
        root = pathlib.Path(scratch)
        with zipfile.ZipFile(archive) as bundle:
            for entry in bundle.infolist():
                path = pathlib.PurePosixPath(entry.filename)
                if path.is_absolute() or '..' in path.parts or '\\' in entry.filename:
                    raise SystemExit('Unsafe server archive layout')
            if 'Content.Client.zip' not in bundle.namelist():
                raise SystemExit('Server must include its prebuilt Hybrid ACZ client archive')
            if bundle.testzip() is not None:
                raise SystemExit('Server ZIP integrity failure')
            bundle.extractall(root)
        executable = root / ('Robust.Server.exe' if os.name == 'nt' else 'Robust.Server')
        executable.chmod(executable.stat().st_mode | 0o100)
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 0))
            port = probe.getsockname()[1]
        command = [str(executable), '--config-file', 'voice-test.toml', '--data-dir', str(root / 'test-data'),
                   '--cvar', 'net.bindto=127.0.0.1', '--cvar', 'net.port=' + str(port),
                   '--cvar', 'status.bind=127.0.0.1:' + str(port)]
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with (root / 'smoke.log').open('w+') as log:
            process = subprocess.Popen(command, cwd=root, stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT, text=True)
            error = None
            try:
                deadline = time.monotonic() + 240
                info = None
                while time.monotonic() < deadline and process.poll() is None:
                    try:
                        with opener.open('http://127.0.0.1:' + str(port) + '/info', timeout=5) as response:
                            info = json.load(response)
                        break
                    except (OSError, ValueError):
                        time.sleep(0.25)
                if info is None:
                    raise RuntimeError('Server did not expose /info within 240 seconds')
                if info.get('auth', {}).get('mode') not in ('Required', 1):
                    raise RuntimeError('Server did not require normal authentication')
                process.stdin.write('shutdown\n')
                process.stdin.flush()
                process.wait(timeout=45)
                if process.returncode:
                    raise RuntimeError('Server exited with code ' + str(process.returncode))
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as failure:
                error = str(failure)
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
            log.seek(0)
            output = log.read()
            if error or '[FATL]' in output:
                print(output)
                raise SystemExit(error or 'Fatal server startup error')
        print(json.dumps({'checksum': 'passed', 'zip_crc': 'passed', 'server_startup': 'passed',
                          'authentication_required': True, 'graceful_shutdown': 'passed'}))


if __name__ == '__main__':
    main()

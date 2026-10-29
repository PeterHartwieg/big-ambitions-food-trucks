#!/usr/bin/env python3
"""Build the rig-only AgentRigInput.dll against an explicit game Managed folder (read as references only).

Installs nothing: the agent test runtime's installer copies the result into the rig's Managed folder and records
its hash in the install receipt. The output may not be inside a game install, a ModsLocal folder or the references.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import zipfile

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
# The same pin as tools/build-mod.sh.
HARMONY_URL = 'https://api.nuget.org/v3-flatcontainer/lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg'
HARMONY_SHA256 = 'd64592e53090464559fce48612c9ca7c8dc73113841376b7aa3455f46fc5d579'
HARMONY_DLL_SHA256 = '7b9e756306fa3d7620e02a857c8927a6ab04973f9bd8a77d3866700a6deac55c'
REFERENCES = ('UnityEngine.CoreModule', 'UnityEngine.InputLegacyModule', 'UnityEngine.UIModule', 'UnityEngine.UI',
              'Unity.InputSystem', 'Newtonsoft.Json', 'BigAmbitions')
FORBIDDEN_PARTS = ('modslocal', 'steamapps', 'bigambitions-agenttests')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def check_output(output, managed):
    for part in output.resolve().parts:
        squashed = part.lower().replace(' ', '')
        if any(bad in squashed for bad in FORBIDDEN_PARTS):
            raise SystemExit(f'Refusing output inside a game install or mod folder: {output}')
    if output.resolve() == managed.resolve() or managed.resolve() in output.resolve().parents:
        raise SystemExit('Output must be outside the references folder')


def harmony(cache):
    dll = cache / '0Harmony.dll'
    if dll.is_file() and sha(dll) == HARMONY_DLL_SHA256:
        return dll
    cache.mkdir(parents=True, exist_ok=True)
    nuget = Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages')) / 'lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg'
    with tempfile.TemporaryDirectory() as tmp:
        package = Path(tmp) / 'harmony.nupkg'
        if nuget.is_file():
            shutil.copyfile(nuget, package)
        else:
            urllib.request.urlretrieve(HARMONY_URL, package)
        if sha(package) != HARMONY_SHA256:
            raise SystemExit('Harmony package does not match the pinned checksum')
        with zipfile.ZipFile(package) as archive:
            dll.write_bytes(archive.read('lib/net472/0Harmony.dll'))
    if sha(dll) != HARMONY_DLL_SHA256:
        dll.unlink()
        raise SystemExit('0Harmony.dll does not match the pinned checksum')
    return dll


def git_commit():
    try:
        return subprocess.run(['git', '-C', str(REPO), 'rev-parse', 'HEAD'], capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-managed', required=True, type=Path, help="The game's Managed folder; read only")
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--dotnet', default=os.environ.get('DOTNET', 'dotnet'))
    opts = parser.parse_args(argv)
    managed, output = opts.game_managed, opts.output
    for name in REFERENCES:
        if not (managed / (name + '.dll')).is_file():
            raise SystemExit(f'Missing reference {name}.dll in {managed}')
    check_output(output, managed)
    dll = harmony(REPO / 'build/rig-input/harmony')
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run([opts.dotnet, 'build', str(HERE / 'AgentRigInput.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet',
                        f'-p:GameManaged={managed}', f'-p:HarmonyDll={dll}', '-o', f'{tmp}/out'], check=True)
        output.mkdir(parents=True, exist_ok=True)
        built = output / 'AgentRigInput.dll'
        shutil.copyfile(Path(tmp) / 'out/AgentRigInput.dll', built)
    manifest = {
        'assembly': 'AgentRigInput.dll', 'sha256': sha(built), 'protocol': 1, 'source_commit': git_commit(),
        'built_utc': datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'harmony': {'version': '2.4.2', 'sha256': HARMONY_DLL_SHA256},
        'references': {name: sha(managed / (name + '.dll')) for name in REFERENCES},
        'install': 'Managed folder of the isolated agent test player only, through the rig runtime installer',
    }
    (output / 'rig-input-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(f"AgentRigInput.dll {manifest['sha256']} -> {built}")
    return 0


if __name__ == '__main__':
    sys.exit(main())

#!/usr/bin/env python3
"""Verify and install Food Trucks into local macOS ModsLocal, preserving other mods."""
import argparse
from datetime import datetime
import hashlib
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

REPO = Path(__file__).resolve().parents[1]


def verify(package, manifest):
    package = Path(package)
    if package.is_symlink() or not package.is_dir():
        raise ValueError("Package must be a real directory, not a symlink")
    entries = {}
    for line in Path(manifest).read_text().splitlines():
        match = re.fullmatch(r'([a-fA-F0-9]{64})\s+\*?(.+)', line)
        if not match:
            raise ValueError('Invalid SHA-256 manifest line')
        name = match[2].removeprefix('./')
        relative = Path(name)
        if relative.is_absolute() or '..' in relative.parts or name in entries:
            raise ValueError(f'Unsafe or duplicate manifest path: {name}')
        entries[name] = match[1].lower()
    required = {'FoodTrucks.dll', 'AssetBundles/Mac/foodtrucks.unity3d',
                'AssetBundles/Mac/foodtrucks.unity3d.manifest'}
    if not required <= entries.keys():
        raise ValueError('Manifest must list FoodTrucks.dll and the Mac bundle and manifest')
    actual = set()
    for path in Path(package).rglob('*'):
        if path.is_symlink():
            raise ValueError(f'Package contains a symlink: {path}')
        if path.is_file():
            name = path.relative_to(package).as_posix()
            actual.add(name)
            if name not in entries or hashlib.sha256(path.read_bytes()).hexdigest() != entries[name]:
                raise ValueError(f'Unlisted file or SHA-256 mismatch: {name}')
    if actual != entries.keys():
        raise ValueError('Package is missing files listed in the SHA-256 manifest')


def install(package, manifest, game_root):
    # pgrep exit 1 means no match; all other failures must fail closed.
    process = subprocess.run(['pgrep', '-f', r'/Big Ambitions\.app/Contents/MacOS/'],
                             capture_output=True, text=True)
    if process.returncode == 0:
        raise ValueError('Big Ambitions is running. Close the game before installing.')
    if process.returncode != 1:
        raise ValueError(f'Could not check whether Big Ambitions is running: {process.stderr.strip()}')
    game_root = Path(game_root)
    mods = game_root / 'ModsLocal'
    target = mods / 'FoodTrucks'
    backups = game_root / 'FoodTrucksPackageBackups'
    if any(path.is_symlink() for path in (game_root, mods, target, backups)):
        raise ValueError('Refusing to install through a symlinked game/mod/backup folder')
    if target.exists() and not target.is_dir():
        raise ValueError('ModsLocal/FoodTrucks exists and is not a directory')
    game_root.mkdir(parents=True, exist_ok=True)
    # Staging is outside ModsLocal: the loader must never see two copies of FoodTrucks.
    with tempfile.TemporaryDirectory(prefix='FoodTrucks-stage-', dir=game_root) as staging:
        incoming = Path(staging) / 'FoodTrucks'
        # Check source before copy, then the exact staged bytes before replacing anything.
        verify(package, manifest)
        shutil.copytree(package, incoming, symlinks=True)
        verify(incoming, manifest)
        mods.mkdir(exist_ok=True)
        backup = None
        if target.exists():
            backups.mkdir(exist_ok=True)
            backup = Path(tempfile.mkdtemp(prefix=f'FoodTrucks-{datetime.now():%Y%m%d-%H%M%S}-', dir=backups)) / 'FoodTrucks'
            target.rename(backup)
        try:
            incoming.rename(target)
        except BaseException:
            if backup is not None and not target.exists():
                backup.rename(target)
            raise
    return target


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=REPO / 'build/FoodTrucks')
    parser.add_argument('--manifest', type=Path, default=REPO / 'build/FoodTrucks.sha256')
    parser.add_argument('--game-root', type=Path, default=Path.home() / 'Library/Application Support/com.Hovgaard-Games.Big-Ambitions')
    args = parser.parse_args()
    if sys.platform != 'darwin':
        parser.error('This installer is for macOS')
    try:
        print(f'Installed and verified {install(args.source, args.manifest, args.game_root)}')
    except (ValueError, OSError) as exc:
        sys.exit(str(exc))


if __name__ == '__main__':
    main()

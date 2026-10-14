#!/usr/bin/env python3
"""Create a deterministic local release-candidate ZIP from a verified runtime package."""
import argparse
import hashlib
import os
from pathlib import Path
import re
import sys
import tempfile
import zipfile

from ftlib.package import verify, validate_bundles

REPO = Path(__file__).resolve().parents[1]
REQUIRED = {
    'FoodTrucks.dll', 'README.md', 'LICENSE.txt', 'Locales/en.json',
    'Dependencies/FoodTrucks.Core.dll', 'Dependencies/0Harmony.dll', 'Dependencies/Harmony-LICENSE.txt',
    *(f'data/{name}.json' for name in ('economy', 'demand', 'zones', 'rules', 'pitches', 'commissaries')),
    *(f'AssetBundles/{platform}/foodtrucks.unity3d{suffix}'
      for platform in ('Windows', 'Mac') for suffix in ('', '.manifest')),
}


def package(source, manifest, output):
    source, output = Path(source), Path(output)
    entries = verify(source, manifest)
    validate_bundles(source)
    if not REQUIRED <= entries.keys():
        raise ValueError('Runtime package is missing required files or license notices')
    for name in entries:
        if name not in REQUIRED and not re.fullmatch(r'Locales/[A-Za-z0-9_-]+\.json', name):
            raise ValueError(f'Unexpected runtime package file: {name}')
    if any(not (source / name).stat().st_size for name in REQUIRED):
        raise ValueError('Required runtime files and license notices must not be empty')
    if output.exists() or output.is_symlink():
        raise ValueError(f'Archive already exists; choose a new output path: {output}')
    if output.resolve().is_relative_to(source.resolve()):
        raise ValueError('Archive output must be outside the runtime package')
    output.parent.mkdir(parents=True, exist_ok=True)
    # Write outside the source and replace only after success. No full-package copy or large memory buffer.
    with tempfile.TemporaryDirectory(prefix='FoodTrucks-zip-', dir=output.parent) as temporary:
        archive = Path(temporary) / 'candidate.zip'
        with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
            for name in sorted(entries):
                info = zipfile.ZipInfo('FoodTrucks/' + name, date_time=(1980, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.external_attr = 0o100644 << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                info._compresslevel = 9
                with (source / name).open('rb') as incoming, zipped.open(info, 'w') as outgoing:
                    digest = hashlib.sha256()
                    for chunk in iter(lambda: incoming.read(1024 * 1024), b''):
                        digest.update(chunk)
                        outgoing.write(chunk)
                    if digest.hexdigest() != entries[name]:
                        raise ValueError(f'Package changed while archiving: {name}')
        # Detect changed bytes or a changed file set during packaging before keeping an archive.
        verify(source, manifest)
        # Both paths are on the same filesystem. Link atomically without overwriting an existing candidate;
        # temporary cleanup drops the staging name without a second archive copy or extra disk allocation.
        os.link(archive, output)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=REPO / 'build/FoodTrucks')
    parser.add_argument('--manifest', type=Path, default=REPO / 'build/FoodTrucks.sha256')
    parser.add_argument('--output', type=Path, default=REPO / 'build/FoodTrucks-0.9.10-rc.zip')
    args = parser.parse_args()
    try:
        print(f'Created local release candidate: {package(args.source, args.manifest, args.output)}')
    except (ValueError, OSError, zipfile.BadZipFile) as exc:
        sys.exit(str(exc))


if __name__ == '__main__':
    main()

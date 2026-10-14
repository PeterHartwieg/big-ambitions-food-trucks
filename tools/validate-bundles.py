#!/usr/bin/env python3
"""Check packaged vehicle asset names and required Windows/Mac bundle outputs."""
from collections import Counter
from pathlib import Path
import re
import sys


def validate(package):
    for platform in ('Windows', 'Mac'):
        bundle = Path(package) / 'AssetBundles' / platform / 'foodtrucks.unity3d'
        manifest = bundle.with_name(bundle.name + '.manifest')
        if not bundle.is_file() or not bundle.stat().st_size or not manifest.is_file():
            raise ValueError(f'Missing {platform} bundle or manifest: {bundle}')
        names = [Path(asset).stem.lower() for asset in re.findall(
            r'^- (Assets/.*\.(?:prefab|fbx|obj|dae|blend))\s*$',
            manifest.read_text(), re.MULTILINE | re.IGNORECASE)]
        if not names:
            raise ValueError(f'No prefab/model assets listed in {manifest}')
        clashes = sorted(name for name, count in Counter(names).items() if count > 1)
        if clashes:
            raise ValueError(f'{platform} bundle assets share a prefab name: {", ".join(clashes)}')


if __name__ == '__main__':
    try:
        validate(sys.argv[1])
    except (ValueError, OSError) as exc:
        sys.exit(str(exc))

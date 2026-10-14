"""Shared package integrity checks for local installation and release archives."""
from collections import Counter
import hashlib
from pathlib import Path
import re


def file_hash(path):
    with Path(path).open("rb") as stream:
        digest = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
        return digest.hexdigest()


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
        if (relative.is_absolute() or '..' in relative.parts or name in entries
                or '\\' in name or ':' in name or relative.as_posix() != name):
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
            if name not in entries or file_hash(path) != entries[name]:
                raise ValueError(f'Unlisted file or SHA-256 mismatch: {name}')
    if actual != entries.keys():
        raise ValueError('Package is missing files listed in the SHA-256 manifest')
    return entries



def validate_bundles(package):
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


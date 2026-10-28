"""Shared package integrity checks for local installation and release archives."""
from collections import Counter
import hashlib
import os
from pathlib import Path
import re
import stat


def is_link(path):
    return path.is_symlink() or bool(getattr(path.lstat(), 'st_file_attributes', 0)
                                    & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def safe_manifest_name(name):
    """Portable relative names, including Windows aliases and device names."""
    return all(part not in ('', '.', '..')
               and not re.search(r'[\x00-\x1f\\:<>"|?*]|[. ]$', part)
               and not re.match(r'^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)', part, re.I)
               for part in name.split('/'))


def file_hash(path):
    with Path(path).open("rb") as stream:
        digest = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
        return digest.hexdigest()


def verify(package, manifest):
    package = Path(package)
    if not package.is_dir() or is_link(package):
        raise ValueError("Package must be a real directory, not a symlink")
    entries = {}
    folded_names = set()
    for line in Path(manifest).read_text().splitlines():
        match = re.fullmatch(r'([a-fA-F0-9]{64})\s+\*?(.+)', line)
        if not match:
            raise ValueError('Invalid SHA-256 manifest line')
        name = match[2].removeprefix('./')
        if not safe_manifest_name(name) or name.casefold() in folded_names:
            raise ValueError(f'Unsafe or duplicate manifest path: {name}')
        folded_names.add(name.casefold())
        entries[name] = match[1].lower()
    required = {'FoodTrucks.dll', 'AssetBundles/Mac/foodtrucks.unity3d',
                'AssetBundles/Mac/foodtrucks.unity3d.manifest'}
    if not required <= entries.keys():
        raise ValueError('Manifest must list FoodTrucks.dll and the Mac bundle and manifest')
    actual = set()
    for directory, dirs, files in os.walk(package):
        # Inspect directories before os.walk can descend into Windows junctions.
        for name in dirs + files:
            path = Path(directory) / name
            if is_link(path):
                raise ValueError(f'Package contains a symlink/reparse point: {path}')
        for name in files:
            path = Path(directory) / name
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

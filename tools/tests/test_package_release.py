import hashlib
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
PACKAGE = runpy.run_path(str(TOOLS / 'package-release.py'))


class ReleasePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'runtime'
        self.manifest = self.root / 'FoodTrucks.sha256'
        for name in PACKAGE['REQUIRED']:
            file = self.source / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(b'runtime or license')
            if name.endswith('.manifest'):
                file.write_text('- Assets/Vehicles/FoodTruck.prefab\n- Assets/Vehicles/FoodTruck_Model.fbx\n')
        self.rehash()

    def rehash(self):
        self.manifest.write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  ./{p.relative_to(self.source).as_posix()}\n' for p in sorted(self.source.rglob('*')) if p.is_file()))

    def package(self, name='candidate.zip'):
        return PACKAGE['package'](self.source, self.manifest, self.root / name)

    def test_deterministic_zip_ignores_source_times_and_permissions(self):
        first = self.package('first.zip')
        for p in self.source.rglob('*'):
            if p.is_file():
                p.chmod(0o600)
                PACKAGE['os'].utime(p, (1000000000, 1000000000))
        second = self.package('second.zip')
        self.assertEqual(first.read_bytes(), second.read_bytes())
        with zipfile.ZipFile(first) as archive:
            self.assertEqual(archive.namelist(), ['FoodTrucks/' + name for name in sorted(PACKAGE['REQUIRED'])])
            for info in archive.infolist():
                self.assertEqual(info.date_time, (1980, 1, 1, 0, 0, 0))
                self.assertEqual(info.external_attr >> 16, 0o100644)
                self.assertEqual(archive.read(info), (self.source / info.filename.removeprefix('FoodTrucks/')).read_bytes())

    def test_tampering_and_unlisted_files_refused(self):
        (self.source / 'FoodTrucks.dll').write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'): self.package()
        self.rehash()
        (self.source / 'extra.txt').write_text('extra')
        with self.assertRaisesRegex(ValueError, 'Unlisted'): self.package()
        self.assertFalse((self.root / 'candidate.zip').exists())

    def test_coherent_source_and_manifest_change_at_final_verification_refused(self):
        real_verify = PACKAGE['verify']
        calls = 0

        def verify_with_rebuild(source, manifest):
            nonlocal calls
            calls += 1
            if calls == 2:
                (self.source / 'FoodTrucks.dll').write_bytes(b'rebuilt runtime')
                self.rehash()
            return real_verify(source, manifest)

        with patch.dict(PACKAGE['package'].__globals__, verify=verify_with_rebuild):
            with self.assertRaisesRegex(ValueError, 'Package changed'):
                self.package()
        self.assertEqual(calls, 2)
        self.assertFalse((self.root / 'candidate.zip').exists())

    def test_manifest_cannot_authorize_game_dlls_sources_or_saves(self):
        for name in ('BigAmbitions.dll', 'Dependencies/BigAmbitions.dll', 'save.hsg', 'save.hsg.bak', 'Source/Bridge.cs'):
            with self.subTest(name=name):
                p = self.source / name
                p.parent.mkdir(exist_ok=True)
                p.write_text('forbidden')
                self.rehash()
                with self.assertRaisesRegex(ValueError, 'Unexpected runtime'): self.package()
                p.unlink()
        self.assertFalse((self.root / 'candidate.zip').exists())

    def test_unsafe_paths_duplicates_and_symlinks_refused(self):
        original = self.manifest.read_text()
        for name in ('../outside', '/outside', r'C:\outside', r'..\outside', './data/../outside', 'data//outside'):
            self.manifest.write_text(original + '0' * 64 + '  ' + name + '\n')
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'Unsafe'): self.package()
        self.manifest.write_text(original + original.splitlines()[0] + '\n')
        with self.assertRaisesRegex(ValueError, 'duplicate'): self.package()
        self.manifest.write_text(original)
        (self.source / 'linked-directory').symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'symlink'): self.package()

    def test_missing_mac_bundle_or_license_refused_even_with_rehashed_manifest(self):
        for name in ('AssetBundles/Mac/foodtrucks.unity3d', 'Dependencies/Harmony-LICENSE.txt'):
            p = self.source / name
            data = p.read_bytes()
            p.unlink()
            self.rehash()
            with self.subTest(name=name), self.assertRaises(ValueError): self.package()
            p.write_bytes(data)

    def test_existing_archive_is_not_overwritten_and_output_stays_outside_source(self):
        archive = self.root / 'candidate.zip'
        archive.write_bytes(b'keep')
        with self.assertRaisesRegex(ValueError, 'already exists'): self.package()
        self.assertEqual(archive.read_bytes(), b'keep')
        with self.assertRaisesRegex(ValueError, 'outside'):
            PACKAGE['package'](self.source, self.manifest, self.source / 'candidate.zip')


if __name__ == '__main__': unittest.main()

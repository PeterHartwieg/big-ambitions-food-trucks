import hashlib
from pathlib import Path
import runpy
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
INSTALL = runpy.run_path(str(TOOLS / 'install-local-mac.py'))
BUNDLES = runpy.run_path(str(TOOLS / 'validate-bundles.py'))


class MacPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.package, self.game = root / 'package', root / 'game'
        self.package.mkdir()
        (self.package / 'FoodTrucks.dll').write_bytes(b'dll')
        for platform in ('Windows', 'Mac'):
            bundle = self.package / 'AssetBundles' / platform / 'foodtrucks.unity3d'
            bundle.parent.mkdir(parents=True)
            bundle.write_bytes(b'bundle')
            bundle.with_name(bundle.name + '.manifest').write_text('- Assets/Vehicles/FoodTruck.prefab\n- Assets/Vehicles/FoodTruck_Model.fbx\n')
        self.manifest = root / 'package.sha256'
        self.rehash()
        self.target = self.game / 'ModsLocal/FoodTrucks'
        self.target.mkdir(parents=True)
        (self.target / 'old.dll').write_bytes(b'previous')
        self.other = self.game / 'ModsLocal/BigAmbitionsMCP'
        self.other.mkdir()
        (self.other / 'other.dll').write_bytes(b'other')
        self.process = patch.object(INSTALL['subprocess'], 'run', return_value=SimpleNamespace(returncode=1, stderr=''))
        self.process.start()
        self.addCleanup(self.process.stop)

    def rehash(self):
        self.manifest.write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  ./{p.relative_to(self.package).as_posix()}\n' for p in sorted(self.package.rglob('*')) if p.is_file()))

    def install(self):
        return INSTALL['install'](self.package, self.manifest, self.game)

    def test_installs_mac_package_preserves_other_mods_and_backs_up_previous(self):
        self.assertEqual(self.install(), self.target)
        self.assertEqual((self.other / 'other.dll').read_bytes(), b'other')
        self.assertEqual((self.target / 'FoodTrucks.dll').read_bytes(), b'dll')
        backups = list((self.game / 'FoodTrucksPackageBackups').rglob('old.dll'))
        self.assertEqual(len(backups), 1)
        self.assertEqual(backups[0].read_bytes(), b'previous')

    def test_bad_hash_leaves_existing_install_intact(self):
        (self.package / 'FoodTrucks.dll').write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'): self.install()
        self.assertEqual((self.target / 'old.dll').read_bytes(), b'previous')
        self.assertFalse((self.game / 'FoodTrucksPackageBackups').exists())

    def test_running_game_or_failed_process_check_refuses_install(self):
        for code in (0, 2):
            with patch.object(INSTALL['subprocess'], 'run', return_value=SimpleNamespace(returncode=code, stderr='error')):
                with self.assertRaises(ValueError): self.install()
        self.assertTrue((self.target / 'old.dll').exists())

    def test_symlinked_package_file_and_manifest_traversal_refused(self):
        link = self.package / 'link'
        self.manifest.write_text(self.manifest.read_text() + '0' * 64 + '  ../outside\n')
        with self.assertRaisesRegex(ValueError, 'Unsafe'): self.install()
        self.rehash()
        try:
            link.symlink_to(self.target / 'old.dll')
        except OSError as exc:
            if getattr(exc, 'winerror', None) == 1314:
                self.skipTest('Windows file symlinks require Developer Mode or elevated privileges; junctions tested separately')
            raise
        with self.assertRaisesRegex(ValueError, 'symlink'): self.install()
        link.unlink()

    def test_failed_swap_restores_previous_install(self):
        original = Path.rename
        def rename(path, destination):
            if path.name == 'FoodTrucks' and path.parent.name.startswith('FoodTrucks-stage-'):
                raise OSError('swap failed')
            return original(path, destination)
        with patch.object(Path, 'rename', rename):
            with self.assertRaisesRegex(OSError, 'swap failed'): self.install()
        self.assertEqual((self.target / 'old.dll').read_bytes(), b'previous')
        self.assertEqual((self.other / 'other.dll').read_bytes(), b'other')

    def test_bundle_validation_checks_each_platform(self):
        BUNDLES['validate'](self.package)
        manifest = self.package / 'AssetBundles/Mac/foodtrucks.unity3d.manifest'
        manifest.write_text(manifest.read_text() + '- Assets/Other/FoodTruck.fbx\n')
        with self.assertRaisesRegex(ValueError, 'Mac bundle assets share'): BUNDLES['validate'](self.package)
        manifest.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing Mac'): BUNDLES['validate'](self.package)


if __name__ == '__main__': unittest.main()

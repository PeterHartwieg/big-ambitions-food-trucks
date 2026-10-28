"""Run the real installer with a disposable profile and stubbed bridge lease."""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
SHELL = shutil.which('powershell') if os.name == 'nt' else shutil.which('pwsh')


@unittest.skipUnless(SHELL, 'PowerShell is required')
class WindowsInstallTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        (self.source / 'FoodTrucks.dll').write_bytes(b'fixture, not a DLL')
        self.manifest = self.root / 'package.sha256'
        self.rehash()
        self.mods = self.root / 'game/ModsLocal'
        self.target = self.mods / 'FoodTrucks'
        self.target.mkdir(parents=True)
        (self.target / 'old.txt').write_text('previous')
        control = self.root / '.big-ambitions-bridge/Control.ps1'
        control.parent.mkdir()
        control.write_text('''Add-Type @'
namespace BridgeControl {
    public class LeaseStore {
        public static LeaseStore ForInstance(string instance) { return new LeaseStore(); }
        public object Begin(string token, string operation) { return null; }
        public void End(object permit) { }
    }
}
'@
''')
        self.wrapper = self.root / 'run.ps1'
        self.wrapper.write_text('''param($Installer, $Source, $Manifest, $ModsLocal)
$ErrorActionPreference = 'Stop'
function Get-Process { param($Name) }
& $Installer -Source $Source -Manifest $Manifest -ModsLocal $ModsLocal
''')

    def rehash(self):
        self.manifest.write_text(''.join(
            f'{hashlib.sha256(p.read_bytes()).hexdigest()}  ./{p.relative_to(self.source).as_posix()}\n'
            for p in sorted(self.source.rglob('*')) if p.is_file()))

    def install(self, source=None, manifest=None, mods=None):
        return subprocess.run([SHELL, '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
                               '-File', str(self.wrapper), str(TOOLS / 'Install-Windows.ps1'),
                               str(source or self.source), str(manifest or self.manifest), str(mods or self.mods)],
                              cwd=self.root,
                              env={**os.environ, 'USERPROFILE': str(self.root)},
                              capture_output=True, text=True)

    def change_location(self, path):
        self.wrapper.write_text(self.wrapper.read_text().replace('& $Installer',
            "Set-Location -LiteralPath '" + str(path).replace("'", "''") + "'\n& $Installer"))

    def junction(self, link, target):
        result = subprocess.run([SHELL, '-NoProfile', '-NonInteractive', '-Command',
                                 'New-Item -ItemType Junction -Path $env:TEST_LINK -Target $env:TEST_TARGET | Out-Null'],
                                env={**os.environ, 'TEST_LINK': str(link), 'TEST_TARGET': str(target)},
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.addCleanup(os.rmdir, link)

    def test_relative_inputs_follow_powershell_location(self):
        location = self.root / 'location[1]'
        location.mkdir()
        self.change_location(location)
        result = self.install('../source', '../package.sha256', './game/ModsLocal')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue((location / 'game/ModsLocal/FoodTrucks/FoodTrucks.dll').is_file())
        self.assertEqual((self.target / 'old.txt').read_text(), 'previous')

    @unittest.skipUnless(os.name == 'nt', 'Windows junction test')
    def test_relative_manifest_junction_is_rejected(self):
        location = self.root / 'location'
        location.mkdir()
        self.junction(location / 'manifests', self.root)
        self.change_location(location)
        result = self.install('../source', 'manifests/package.sha256')
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('link/reparse point', result.stderr)
        self.assertEqual((self.target / 'old.txt').read_text(), 'previous')
        self.assertTrue(self.source.exists())
        self.assertTrue(self.manifest.exists())

    def test_overlapping_source_is_rejected(self):
        destination = self.mods.parent / 'source'
        self.source.rename(destination)
        self.source = destination
        self.assert_rejected()

    @unittest.skipUnless(os.name == 'nt', 'Windows junction test')
    def test_destination_junction_is_rejected(self):
        outside = self.root / 'outside'
        self.target.rename(outside)
        self.junction(self.target, outside)
        self.assert_rejected()
        self.assertEqual((outside / 'old.txt').read_text(), 'previous')

    def assert_rejected(self):
        result = self.install()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual((self.target / 'old.txt').read_text(), 'previous')
        self.assertTrue(self.source.exists())
        self.assertTrue(self.manifest.exists())
        self.assertFalse((self.mods.parent / 'FoodTrucksPackageBackups').exists())

    def test_extra_file_is_rejected(self):
        (self.source / 'unexpected.dll').write_text('harmless extra fixture')
        self.assert_rejected()

    def test_valid_package(self):
        nested = self.source / 'data/tables[1].json'
        nested.parent.mkdir()
        nested.write_text('{}')
        self.rehash()
        result = self.install()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue((self.target / 'FoodTrucks.dll').is_file())
        self.assertEqual(len(list((self.mods.parent / 'FoodTrucksPackageBackups').rglob('old.txt'))), 1)
        backup = next((self.mods.parent / 'FoodTrucksPackageBackups').iterdir())
        self.assertRegex(backup.name, r'^FoodTrucks-\d{8}-\d{6}-[a-f0-9]{32}$')
        self.assertFalse(self.source.exists())
        self.assertFalse(self.manifest.exists())

    def test_missing_file_is_rejected(self):
        (self.source / 'FoodTrucks.dll').unlink()
        self.assert_rejected()

    def test_hash_mismatch_is_rejected(self):
        (self.source / 'FoodTrucks.dll').write_text('changed')
        self.assert_rejected()

    def test_unsafe_and_duplicate_manifest_paths(self):
        original = self.manifest.read_text()
        for name in ('../outside', '/outside', r'C:\outside', r'..\outside',
                     'data//outside', 'data/./outside', 'data/file:stream',
                     'data/trailing.', 'data/trailing ', 'data/NUL.json',
                     'data/file*', 'FoodTrucks.dll', 'foodtrucks.DLL'):
            with self.subTest(name=name):
                self.manifest.write_text(original + '0' * 64 + '  ' + name + '\n')
                self.assert_rejected()

    def test_malformed_manifest_is_rejected(self):
        self.manifest.write_text('not a digest  FoodTrucks.dll\n')
        self.assert_rejected()

    def test_staged_extra_file_is_rejected(self):
        self.wrapper.write_text(self.wrapper.read_text().replace('& $Installer', '''
function Copy-Item {
    param($LiteralPath, $Destination, [switch]$Recurse, [switch]$Force)
    Microsoft.PowerShell.Management\\Copy-Item @PSBoundParameters
    Set-Content -LiteralPath (Join-Path $Destination 'extra.dll') -Value 'extra'
}
& $Installer'''))
        self.assert_rejected()

    @unittest.skipUnless(os.name == 'nt', 'Windows hidden-file test')
    def test_hidden_extra_file_is_rejected(self):
        hidden = self.source / 'hidden.dll'
        hidden.write_text('extra')
        import ctypes
        self.assertTrue(ctypes.windll.kernel32.SetFileAttributesW(str(hidden), 2))
        try:
            self.assert_rejected()
        finally:
            ctypes.windll.kernel32.SetFileAttributesW(str(hidden), 128)

    def test_failed_swap_restores_backup(self):
        self.wrapper.write_text(self.wrapper.read_text().replace('& $Installer', '''
function Move-Item {
    param($LiteralPath, $Destination)
    if ((Split-Path $LiteralPath -Leaf) -like 'FoodTrucks.incoming-*') { throw 'injected swap failure' }
    Microsoft.PowerShell.Management\\Move-Item @PSBoundParameters
}
& $Installer'''))
        result = self.install()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('injected swap failure', result.stderr)
        self.assertEqual((self.target / 'old.txt').read_text(), 'previous')
        self.assertTrue(self.source.exists())
        self.assertFalse(list(self.mods.parent.glob('FoodTrucks.incoming-*')))

    @unittest.skipUnless(os.name == 'nt', 'Windows junction test')
    def test_directory_junction_is_rejected_without_traversal(self):
        outside = self.root / 'outside'
        outside.mkdir()
        sentinel = outside / 'keep.txt'
        sentinel.write_text('keep')
        link = self.source / 'linked'
        result = subprocess.run([SHELL, '-NoProfile', '-NonInteractive', '-Command',
                                 'New-Item -ItemType Junction -Path $env:TEST_LINK -Target $env:TEST_TARGET | Out-Null'],
                                env={**os.environ, 'TEST_LINK': str(link), 'TEST_TARGET': str(outside)},
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        try:
            self.assert_rejected()
            self.assertEqual(sentinel.read_text(), 'keep')
        finally:
            os.rmdir(link)


if __name__ == '__main__':
    unittest.main()

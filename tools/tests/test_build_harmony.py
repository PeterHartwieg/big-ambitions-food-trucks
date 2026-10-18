"""Offline execution of build-mod.sh with synthetic, checksum-pinned Harmony.

Only the copied script's hashes change to match the fixture. Real shasum, unzip
and cp exercise verification/extraction/copying; network and build tools are
replaced with local stubs. No SDK, Unity, game files or Harmony are needed.
"""

import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile


BUILD_SCRIPT = Path(__file__).resolve().parents[1] / 'build-mod.sh'
DLL = b'synthetic Harmony DLL\n'
NOTICE = b'synthetic Harmony license\n'
SDK_COMMIT = '1e03ddd5071b77a32cfd8bba89f0c130ba6c7d73'

STUB = r'''
import os
from pathlib import Path
import shutil
import sys

root = Path(os.environ['HARNESS_ROOT'])
command = Path(sys.argv[0]).name
args = sys.argv[1:]
with (root / 'events').open('a') as events:
    events.write(command + '\n')
if command == 'git':
    if 'rev-parse' in args:
        print(os.environ['HARNESS_SDK_COMMIT'])
    elif 'clone' in args:
        sys.exit('unexpected SDK clone')
elif command == 'curl':
    if os.environ.get('HARNESS_CURL_FAIL'):
        sys.exit(22)
    shutil.copyfile(root / 'fixture.nupkg', args[args.index('-o') + 1])
elif command == 'unity':
    Path(args[args.index('-logFile') + 1]).write_text('FoodTrucksBuild fixture\n')
    if os.environ.get('HARNESS_REMOVE_NOTICE'):
        (root / 'build/Harmony-LICENSE.txt').unlink(missing_ok=True)
elif command not in ('rsync', 'python3', 'dotnet'):
    sys.exit('unexpected command: ' + command)
'''


class BuildHarmonyTests(unittest.TestCase):
    def setUp(self):
        for command in ('bash', 'shasum', 'unzip'):
            if not shutil.which(command):
                self.skipTest(f'{command} is required for the shell harness')
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.deps = self.root / 'unity/FoodTrucks/Dependencies'
        self.notice = self.root / 'build/Harmony-LICENSE.txt'
        self.output = self.root / 'build/FoodTrucks'
        for directory in (
            'tools', 'stubs', 'build/sdk/.git', 'build/sdk/Assets/Mods',
            'build/sdk/Assets/Editor', 'build/sdk/Output/FoodTrucks/Dependencies',
            'build/game-dlls', 'unity/FoodTrucks/Dependencies', 'unity/BuildEditor',
            'Core/bin/Release/netstandard2.1', 'data', 'tools/bridge-locks/bin/Release/netstandard2.0',
        ):
            (self.root / directory).mkdir(parents=True, exist_ok=True)
        # All content is synthetic; the build and SDK output are supplied by the harness.
        for filename in (
            'build/game-dlls/BigAmbitions.dll',
            'Core/bin/Release/netstandard2.1/FoodTrucks.Core.dll',
            'build/sdk/Output/FoodTrucks/Dependencies/0Harmony.dll',
            'README.md', 'LICENSE.txt', 'data/fixture.json',
            # The bridge lock controller build-mod.sh builds with dotnet (stubbed) and copies in.
            'tools/bridge-locks/bin/Release/netstandard2.0/BridgeControl.dll',
        ):
            (self.root / filename).write_bytes(b'fixture\n')
        (self.deps / '0Harmony.dll').write_bytes(DLL)
        self.write_package()
        self.script = self.root / 'tools/build-mod.sh'
        script = BUILD_SCRIPT.read_text()
        for variable, content in (
            ('HARMONY_SHA256', (self.root / 'fixture.nupkg').read_bytes()),
            ('HARMONY_DLL_SHA256', DLL),
        ):
            script, count = re.subn(
                rf'^{variable}="[0-9a-f]+"$',
                f'{variable}="{hashlib.sha256(content).hexdigest()}"',
                script, flags=re.MULTILINE,
            )
            self.assertEqual(count, 1, f'fixture must replace exactly one {variable}')
        self.script.write_text(script)
        stub = self.root / 'stubs/dispatch'
        stub.write_text(f'#!{sys.executable}\n' + STUB)
        stub.chmod(0o755)
        for command in ('git', 'curl', 'rsync', 'python3', 'dotnet', 'unity'):
            (stub.parent / command).symlink_to(stub.name)
        self.env = os.environ.copy()
        self.env.update({
            'PATH': str(stub.parent) + os.pathsep + self.env.get('PATH', ''),
            'HARNESS_ROOT': str(self.root),
            'HARNESS_SDK_COMMIT': SDK_COMMIT,
            'BA_SDK': str(self.root / 'build/sdk'),
            'BA_GAME_DLLS': str(self.root / 'build/game-dlls'),
            'UNITY': str(stub.parent / 'unity'),
            'TMPDIR': str(self.root),
        })
        for key in ('HARNESS_CURL_FAIL', 'HARNESS_REMOVE_NOTICE'):
            self.env.pop(key, None)

    def write_package(self, dll=DLL, notice=NOTICE):
        with zipfile.ZipFile(self.root / 'fixture.nupkg', 'w') as package:
            package.writestr('lib/net472/0Harmony.dll', dll)
            if notice is not None:
                package.writestr('LICENSE', notice)

    def run_build(self, **environment):
        return subprocess.run(
            ['bash', str(self.script)], cwd=self.root,
            env={**self.env, **environment}, capture_output=True, text=True, timeout=20,
        )

    def events(self):
        return (self.root / 'events').read_text().splitlines()

    def assert_stopped_before_build(self, result):
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn('dotnet', self.events())
        self.assertNotIn('unity', self.events())
        self.assertFalse(self.output.exists())
        self.assertFalse((self.root / 'build/FoodTrucks.sha256').exists())

    def test_cached_dll_missing_notice_recovers_and_ships_notice(self):
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.events().count('curl'), 1)
        self.assertEqual((self.deps / '0Harmony.dll').read_bytes(), DLL)
        self.assertEqual(self.notice.read_bytes(), NOTICE)
        self.assertEqual((self.output / 'Dependencies/Harmony-LICENSE.txt').read_bytes(), NOTICE)
        manifest = (self.root / 'build/FoodTrucks.sha256').read_text()
        self.assertIn(hashlib.sha256(NOTICE).hexdigest() + '  ./Dependencies/Harmony-LICENSE.txt', manifest)

    def test_complete_cache_needs_no_download(self):
        self.notice.write_bytes(NOTICE)
        result = self.run_build(HARNESS_CURL_FAIL='1')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertNotIn('curl', self.events())
        self.assertEqual((self.output / 'Dependencies/Harmony-LICENSE.txt').read_bytes(), NOTICE)

    def test_missing_notice_download_failure_stops_build(self):
        result = self.run_build(HARNESS_CURL_FAIL='1')
        self.assert_stopped_before_build(result)
        self.assertIn('curl', self.events())
        self.assertFalse(self.notice.exists())
        self.assertEqual((self.deps / '0Harmony.dll').read_bytes(), DLL)

    def test_unverified_package_is_not_extracted_or_copied(self):
        self.write_package(dll=b'unverified DLL', notice=b'unverified notice')
        result = self.run_build()
        self.assert_stopped_before_build(result)
        self.assertIn('curl', self.events())
        self.assertFalse(self.notice.exists())
        self.assertEqual((self.deps / '0Harmony.dll').read_bytes(), DLL)

    def test_cached_dll_checksum_still_required(self):
        self.notice.write_bytes(NOTICE)
        (self.deps / '0Harmony.dll').write_bytes(b'tampered DLL')
        result = self.run_build()
        self.assert_stopped_before_build(result)
        self.assertIn('Cached 0Harmony.dll does not match the pinned hash', result.stderr)
        self.assertNotIn('curl', self.events())

    def test_final_notice_copy_failure_is_fatal(self):
        self.notice.write_bytes(NOTICE)
        result = self.run_build(HARNESS_REMOVE_NOTICE='1')
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('unity', self.events())
        self.assertFalse((self.output / 'Dependencies/Harmony-LICENSE.txt').exists())
        self.assertFalse((self.root / 'build/FoodTrucks.sha256').exists())
        self.assertNotIn('Built ', result.stdout)


if __name__ == '__main__':
    unittest.main()

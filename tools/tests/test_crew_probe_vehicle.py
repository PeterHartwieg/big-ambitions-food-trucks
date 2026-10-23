"""Compile the actual native ownership adapter against instrumented boundaries.

The fake engine deliberately retains its running cache after StopEngine. These
checks establish adapter behavior, not native physics or successful driving.
"""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CrewProbeVehicleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        directory = tempfile.TemporaryDirectory(prefix='ft-crew-vehicle-')
        cls.addClassCleanup(directory.cleanup)
        folder = Path(directory.name)
        major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
        files = [ROOT / 'unity/FoodTrucks/Scripts/CrewProbeVehicle.cs']
        files += [ROOT / 'tools/tests/crew_probe_vehicle' / name
                  for name in ('Stubs.cs', 'Scenarios.cs')]
        includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{includes}</ItemGroup></Project>''')
        built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
                                '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                               capture_output=True, text=True, timeout=120)
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        cls.dll = folder / f'bin/Debug/net{major}.0/Harness.dll'

    def run_case(self, *args):
        ran = subprocess.run(['dotnet', str(self.dll), *args], capture_output=True, text=True, timeout=30)
        self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
        self.assertIn('PASS:', ran.stdout)

    def test_stale_running_cache_admits_without_writes(self):
        self.run_case('admission')

    def test_begin_restore_and_reacquisition_preserve_commanded_off_state(self):
        self.run_case('lifecycle')

    def test_fresh_parked_ignition_true_is_admitted_and_restored_without_extra_starts(self):
        self.run_case('fresh-default')

    def test_unsafe_native_states_are_refused_before_any_write(self):
        for state in ('ignition-controller', 'starter', 'controller', 'player', 'dynamic', 'linear', 'angular'):
            with self.subTest(state=state):
                self.run_case('refusal', state)

    def test_partially_failed_native_start_restores_without_restarting(self):
        self.run_case('partial')

"""Compile the actual bounded body certificate, shape helper and Core geometry.

Stub verification is not evidence of native physics or successful powered driving.
"""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CrewBodyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        directory = tempfile.TemporaryDirectory(prefix='ft-crew-body-')
        cls.addClassCleanup(directory.cleanup)
        folder = Path(directory.name)
        major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
        files = [ROOT / 'unity/FoodTrucks/Scripts' / name
                 for name in ('CrewProbeBody.cs', 'CrewProbeShape.cs', 'CrewProbeTrigger.cs')]
        files += [ROOT / 'tools/tests/crew_probe_body' / name
                  for name in ('Stubs.cs', 'Scenarios.cs')]
        files += sorted((ROOT / 'Core/CrewDriving').glob('*.cs'))
        includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<DefineConstants>FOODTRUCKS_CREW_DRIVING_DEV;CREW_PROBE_BODY_TESTS</DefineConstants><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{includes}</ItemGroup></Project>''')
        built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
                                '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                               capture_output=True, text=True, timeout=120)
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        cls.dll = folder / f'bin/Debug/net{major}.0/Harness.dll'

    def run_case(self, name):
        ran = subprocess.run(['dotnet', str(self.dll), name], capture_output=True, text=True, timeout=60)
        self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
        self.assertIn('PASS:', ran.stdout)

    def test_native_truck_and_disabled_bottom_activation(self):
        self.run_case('normal')

    def test_exact_identity_inventory_and_rigidbody_guards(self):
        self.run_case('identities')

    def test_body_hull_sweep_and_geometry_guards(self):
        self.run_case('geometry')

    def test_actual_cooked_prisms_and_tapered_body(self):
        self.run_case('cooked')

    def test_trigger_height_and_world_root_tilt_contract(self):
        self.run_case('triggers')

    def test_default_masks_required_and_fail_closed_reads(self):
        self.run_case('filtering')

    def test_native_step_configuration_changes(self):
        self.run_case('step')

    def test_static_level_native_ground_hits(self):
        self.run_case('ground')

    def test_bounded_admission_and_no_writes_on_failure(self):
        self.run_case('limits')

    def test_zero_steady_state_allocations(self):
        self.run_case('allocations')

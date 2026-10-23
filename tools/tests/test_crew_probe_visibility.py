"""Compile the actual read-only inspector and command against instrumented native stubs.

This verifies reporting contracts, not Unity physics or installed rig state.
"""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CrewVisibilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        directory = tempfile.TemporaryDirectory(prefix='ft-crew-visibility-')
        cls.addClassCleanup(directory.cleanup)
        folder = Path(directory.name)
        major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
        files = [ROOT / 'unity/FoodTrucks/Scripts' / name
                 for name in ('CrewProbeVisibility.cs', 'CrewProbeShape.cs', 'TestBridgeCrewDrive.cs')]
        files += [ROOT / 'tools/tests/crew_probe_visibility' / name
                  for name in ('Stubs.cs', 'Scenarios.cs')]
        includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><DefineConstants>FOODTRUCKS_CREW_DRIVING_DEV</DefineConstants><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{includes}<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
        built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
                                '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                               capture_output=True, text=True, timeout=120)
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        cls.dll = folder / f'bin/Debug/net{major}.0/Harness.dll'

    def run_case(self, name):
        ran = subprocess.run(['dotnet', str(self.dll), name], capture_output=True, text=True, timeout=30)
        self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
        self.assertIn('PASS:', ran.stdout)

    def test_every_collider_and_exact_native_roles_without_writes(self):
        self.run_case('inventory')

    def test_native_precedence_matrix_and_grouping(self):
        self.run_case('rules')

    def test_shape_local_bounds_and_unknown_shapes(self):
        self.run_case('bounds')

    def test_inventory_and_model_limits_are_explicit(self):
        self.run_case('limits')

    def test_reporting_failures_leave_collider_inventory_readable(self):
        self.run_case('failures')

    def test_command_requires_actual_owned_spawned_food_truck(self):
        self.run_case('command')

    def test_collider_and_rigidbody_overrides_and_world_positions(self):
        self.run_case('overrides')

    def test_unavailable_diagnostic_values_are_explicit(self):
        self.run_case('unknowns')

"""Compile the actual shape helper against instrumented Unity stubs.

This verifies reporting contracts, not Unity physics or installed rig state.
"""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CrewShapeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        directory = tempfile.TemporaryDirectory(prefix='ft-crew-shape-')
        cls.addClassCleanup(directory.cleanup)
        folder = Path(directory.name)
        major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
        files = [ROOT / 'unity/FoodTrucks/Scripts' / name
                 for name in ('CrewProbeShape.cs',)]
        files += [ROOT / 'tools/tests/crew_probe_visibility/Stubs.cs',
                  ROOT / 'tools/tests/crew_probe_shape/Scenarios.cs']
        includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
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

    def test_rotated_and_scaled_roots_and_colliders(self):
        self.run_case('rotation')

    def test_supported_shapes_and_capsule_axes(self):
        self.run_case('shapes')

    def test_disabled_and_inactive_colliders_without_world_bounds(self):
        self.run_case('disabled')

    def test_invalid_dimensions_centers_and_unsupported_shapes(self):
        self.run_case('invalid')

    def test_nonfinite_and_collapsed_transforms_are_refused(self):
        self.run_case('transforms')

    def test_repeated_calls_do_not_allocate(self):
        self.run_case('allocations')

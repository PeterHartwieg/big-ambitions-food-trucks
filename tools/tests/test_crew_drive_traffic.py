"""Compile the actual traffic adapter against deterministic native/physics boundaries.

Exercises registry lifetime and observation contracts, not real Unity collision or timing.
"""
from pathlib import Path
import subprocess
import shutil
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CrewTrafficTests(unittest.TestCase):
    def test_native_observer_contracts(self):
        with tempfile.TemporaryDirectory(prefix='ft-crew-traffic-') as temporary:
            folder = Path(temporary)
            shutil.copytree(ROOT / 'Core', folder / 'Core', ignore=shutil.ignore_patterns('bin', 'obj'))
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            files = [ROOT / 'unity/FoodTrucks/Scripts/CrewProbeTraffic.cs',
                     ROOT / 'unity/FoodTrucks/Scripts/CrewProbeTrigger.cs',
                     ROOT / 'tools/tests/crew_probe_traffic/Stubs.cs',
                     ROOT / 'tools/tests/crew_probe_traffic/Scenarios.cs']
            includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><DefineConstants>FOODTRUCKS_CREW_DRIVING_DEV</DefineConstants><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{includes}<ProjectReference Include="Core/FoodTrucks.Core.csproj" /></ItemGroup></Project>''')
            built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
                                    '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                                   capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(folder / f'bin/Debug/net{major}.0/Harness.dll')],
                                 capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('adapter harness assertions passed', ran.stdout)

    def test_actual_body_common_trigger_band_and_steady_observation(self):
        with tempfile.TemporaryDirectory(prefix='ft-trigger-integrated-') as temporary:
            folder = Path(temporary)
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            files = [ROOT / 'unity/FoodTrucks/Scripts' / name for name in
                     ('CrewProbeTraffic.cs', 'CrewProbeBody.cs', 'CrewProbeShape.cs', 'CrewProbeTrigger.cs')]
            files += [ROOT / 'tools/tests/crew_probe_body' / name for name in ('Stubs.cs', 'Scenarios.cs')]
            files += [ROOT / 'tools/tests/crew_probe_traffic' / name for name in ('IntegratedStubs.cs', 'Integrated.cs')]
            files += sorted((ROOT / 'Core/CrewDriving').glob('*.cs'))
            includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<StartupObject>TrafficIntegrated</StartupObject><DefineConstants>FOODTRUCKS_CREW_DRIVING_DEV;TRAFFIC_INTEGRATION;CREW_PROBE_BODY_TESTS</DefineConstants>
<Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{includes}</ItemGroup></Project>''')
            built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
                                    '-m:1', '-nr:false', '-p:UseSharedCompilation=false'], capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(folder / f'bin/Debug/net{major}.0/Harness.dll')], capture_output=True, text=True, timeout=60)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('integrated trigger assertions passed', ran.stdout)

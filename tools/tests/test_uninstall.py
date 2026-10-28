"""Run the production uninstall step against controlled game lists, native deletion, payments and notices (issue #13)."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class UninstallAdapterTests(unittest.TestCase):
    def test_uninstall_covers_private_driver_storage(self):
        with tempfile.TemporaryDirectory(prefix='ft-uninstall-') as directory:
            folder = Path(directory)
            includes = [ROOT / 'unity/FoodTrucks/Scripts/Uninstall.cs',
                        ROOT / 'tools/tests/uninstall/Stubs.cs', ROOT / 'tools/tests/uninstall/Scenarios.cs']
            files = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in includes)
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>{files}
<ProjectReference Include="{ROOT / 'Core/FoodTrucks.Core.csproj'}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: uninstall', result.stdout)


if __name__ == '__main__':
    unittest.main()

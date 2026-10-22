"""Run the production sale adapter against controlled native payment/deletion/UI boundaries."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CartSaleAdapterTests(unittest.TestCase):
    def test_sale_lifecycle(self):
        with tempfile.TemporaryDirectory(prefix='ft-cart-sale-') as directory:
            folder = Path(directory)
            source = (ROOT / 'unity/FoodTrucks/Scripts/OverlayButtons.cs').read_text()
            start = source.index('internal static void CommissaryCartCtaPostfix(')
            end = source.index('{', start) + 1
            depth = 1
            while depth:
                depth += (source[end] == '{') - (source[end] == '}')
                end += 1
            (folder / 'Overlay.cs').write_text('using System; namespace FoodTrucks { static class OverlayButtons {' + source[start:end] + '} }')
            includes = [ROOT / 'unity/FoodTrucks/Scripts/CartSale.cs', ROOT / 'tools/tests/cart_sale/Scenarios.cs']
            files = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in includes)
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>{files}
<ProjectReference Include="{ROOT / 'Core/FoodTrucks.Core.csproj'}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: cart sale', result.stdout)

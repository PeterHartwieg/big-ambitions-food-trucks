"""Execute the actual logo patch prefixes against unrelated shop editor instances.

Native rendering/Unity lifetime still require the manual checks in the release evidence.
"""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def method(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


class LogoIsolationTests(unittest.TestCase):
    def test_shop_prefixes_and_generator_names_remain_isolated_across_editor_lifecycles(self):
        editor = (ROOT / 'unity/FoodTrucks/Scripts/TruckLogoEditor.cs').read_text()
        logos = (ROOT / 'unity/FoodTrucks/Scripts/TruckLogos.cs').read_text()
        prefixes = '\n'.join(method(editor, 'internal static bool ' + name) for name in
                             ('RefreshPrefix(', 'UpdateSavePrefix(', 'SavePrefix('))
        ours = next(line.strip() for line in editor.splitlines() if 'private static bool Ours(' in line)
        generator = method(logos, 'internal static void GeneratorPrefix(')
        with tempfile.TemporaryDirectory(prefix='ft-logo-isolation-') as tmp:
            folder = Path(tmp)
            (folder / 'Check.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit>
</PropertyGroup><ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
            (folder / 'Program.cs').write_text(STUBS + '\nclass TruckLogoEditor {\n' + MEMBERS + ours + '\n' + prefixes + '\n' + CHECK + '\n}\nclass TruckLogos {\n' + GENERATOR + generator + '\n}\n')
            run = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Check.csproj'), '-c', 'Release'], capture_output=True, text=True, timeout=120)
            self.assertEqual(0, run.returncode, run.stdout + run.stderr)
            self.assertIn('PASS ordinary shops and generator isolation', run.stdout)


STUBS = '''
using Newtonsoft.Json.Linq;
class LogoCustomizer { public readonly string Artwork = "original shop artwork"; }
class Object { public string name = ""; }
class Component { public object transform = new(); }
class Button { public bool interactable; }
class Color { }
enum FontFace { Rubik }
class LogoSettings { public string logoShape = ""; public FontFace font; public Color backgroundColor = new(), logoColor = new(), fontColor = new(); }
class LogoShapes { public void SetSelectedShape(string s) => throw new Exception("shop shape touched"); public void RefreshLogoShapes() => throw new Exception("shop shapes touched"); }
class GlobalReferences { }
class InstanceBehavior<T> { public static object Instance => throw new Exception("native global accessed"); }
class BizManParts { public static void SetText(object o, string s) => throw new Exception("shop text touched"); }
class Log { public static void Error(string s) => throw new Exception(s); public static void Info(string s) => throw new Exception(s); }
class VehicleLogo { public JObject Save() => throw new Exception("shop save touched"); }
class Record { public JToken? Logo; }
class StreetSales { public static Record GetOrAdd(object s, string id) => throw new Exception("vehicle save touched for shop"); }
namespace UI.Elements { class Dropdown { public void ResetSelectedOption(int i) => throw new Exception("shop font touched"); } }
'''
MEMBERS = '''
static LogoCustomizer? editor;
static object session = new(); static string vehicleId = "truck", title = "title", savedDesign = "";
static LogoSettings initial = new();
static bool Writable => throw new Exception("shop reached vehicle ownership");
static T Field<T>(object target, string name) => throw new Exception("shop field touched");
static void Set(object target, string field, object value) => throw new Exception("shop field changed");
static void Invoke(object target, string method, Type[] args, params object[] values) => throw new Exception("shop invocation");
static VehicleLogo Draft() => throw new Exception("shop draft read");
static LogoSettings Settings(VehicleLogo logo) => throw new Exception("shop settings changed");
'''
CHECK = '''
static void Main() {
 var shop = new LogoCustomizer();
 for (var i = 0; i < 50; i++) {
  editor = i % 2 == 0 ? null : new LogoCustomizer();
  if (!RefreshPrefix(shop) || !UpdateSavePrefix(shop) || !SavePrefix(shop) || shop.Artwork != "original shop artwork")
   throw new Exception("ordinary shop was intercepted");
  var shopName = "Ordinary Shop"; TruckLogos.GeneratorPrefix(ref shopName);
  if (shopName != "Ordinary Shop") throw new Exception("shop generator renamed");
  var a = "private-a"; var b = "private-b";
  TruckLogos.GeneratorPrefix(ref a); TruckLogos.GeneratorPrefix(ref b);
  if (a != "Truck Alpha" || b != "Truck Beta") throw new Exception("cross-truck generator names");
 }
 Console.WriteLine("PASS ordinary shops and generator isolation");
}
'''
GENERATOR = '''
static readonly Dictionary<string, string> generationNames = new() { ["private-a"] = "Truck Alpha", ["private-b"] = "Truck Beta" };
'''

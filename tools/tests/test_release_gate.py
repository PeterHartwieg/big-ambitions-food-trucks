"""Compile the actual release seam; development probe tests opt in explicitly."""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]

class ReleaseGateTests(unittest.TestCase):
    def test_release_cannot_acquire_or_start_physical_driving(self):
        with tempfile.TemporaryDirectory(prefix='ft-release-gate-') as temp:
            folder = Path(temp)
            files = list((ROOT / 'unity/FoodTrucks/Scripts').glob('CrewProbe*.cs'))
            files += [ROOT / 'unity/FoodTrucks/Scripts' / name for name in ('CrewDriveProbe.cs', 'TestBridgeCrewDrive.cs')]
            includes = ''.join(f'<Compile Include="{p}" />' for p in files)
            (folder / 'Gate.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>''')
            (folder / 'Program.cs').write_text('''using System;
using System.Reflection;
class VehicleController { }
class Program {
 static void Main() {
  Environment.SetEnvironmentVariable("FOODTRUCKS_CREW_DRIVING_DEV", "1");
  var type = typeof(FoodTrucks.CrewDriveProbe);
  if (!type.IsAbstract || !type.IsSealed || type.BaseType != typeof(object)) throw new Exception("Not inert");
  foreach (var name in new[] { "StartProbe", "FixedUpdate", "Update", "HooksReady", "StationaryReady" })
   if (type.GetMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Length != 0)
    throw new Exception("Release exposes " + name);
  for (int i = 0; i < 50; i++) {
   FoodTrucks.CrewDriveProbe.ResetSession();
   if (FoodTrucks.CrewDriveProbe.Active || FoodTrucks.CrewDriveProbe.Owns("truck") ||
       FoodTrucks.CrewDriveProbe.Owns(new VehicleController()) || !FoodTrucks.CrewDriveProbe.StopAll("load"))
    throw new Exception("Release acquired ownership");
  }
  foreach (var t in type.Assembly.GetTypes())
   if (t.Name.StartsWith("CrewProbe") || t.Name == "TestBridgeCommands") throw new Exception("Developer type survived");
  Console.WriteLine("PASS release cannot enable physical driving");
 }
}''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Gate.csproj'), '-c', 'Release'], text=True, capture_output=True, timeout=120)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn('PASS release', result.stdout)

    def test_shipped_player_text_and_tuning_expose_no_physical_driving(self):
        import re
        files = list((ROOT / 'unity/FoodTrucks/Locales').glob('*.json')) + list((ROOT / 'data').glob('*.json'))
        for path in files:
            self.assertIsNone(re.search(r'crew.?driv|physical.{0,20}crew|crew.{0,20}physical|crew.?probe', path.read_text(), re.I), path)

    def test_release_copy_has_no_driving_claims_and_required_support_footer(self):
        import re
        release = ROOT / 'docs/release/0.10.6'
        for name in ('release-notes.txt', 'release-notes.bbcode.txt'):
            self.assertIsNone(re.search(r'crew|driv|probe|candidate', (release / name).read_text(), re.I))
        description = (release / 'workshop-description.bbcode.txt').read_text().rstrip()
        self.assertLess(len(description), 8000)
        self.assertIn('19 food and drink options', description)
        self.assertTrue(description.endswith("[h2]Support[/h2]\nThe mod is free and stays free. If you'd like to say thanks, you can [url=https://www.paypal.com/donate/?hosted_button_id=Q8KVURCRBFLQN]support my Big Ambitions projects via PayPal[/url]."))

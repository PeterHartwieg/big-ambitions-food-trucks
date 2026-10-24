"""Exercise the production JSON error helper with Newtonsoft, without Unity stubs."""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ObstacleEvidenceTests(unittest.TestCase):
    def test_incomplete_preserves_identity_and_emits_exception(self):
        source = (ROOT / 'unity/FoodTrucks/Scripts/CrewProbeTrafficEvidence.cs').read_text()
        # Compile the actual helper and its Text dependency, unchanged. The rest of
        # the adapter needs Unity; this JSON-only regression needs no native fakes.
        helper = source[source.index('        private static void Incomplete('):
                        source.index('        private static JArray Vec(')]
        with tempfile.TemporaryDirectory(prefix='ft-obstacle-evidence-') as temporary:
            folder = Path(temporary)
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><DefineConstants>FOODTRUCKS_CREW_DRIVING_DEV</DefineConstants><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup>
</Project>''')
            (folder / 'Program.cs').write_text('''using System;
using Newtonsoft.Json.Linq;
static class Program {
''' + helper + '''
    static void Main() {
        var result = new JObject { ["complete"] = true };
        var live = new JObject(); result["live_read"] = live;
        var collider = new JObject { ["cached_instance_id"] = 4868606, ["status"] = "available" };
        live["collider"] = collider;
        var exception = new InvalidOperationException("collider bounds unavailable");
        Incomplete(result, live, "collider", exception);
        Incomplete(result, live, "truck", exception);
        var emitted = JObject.Parse(result.ToString());
        var section = emitted["live_read"]!["collider"]!;
        if ((bool)emitted["complete"]! || (int)section["cached_instance_id"]! != 4868606 ||
            (string?)section["status"] != "error" ||
            (string?)section["error"] != "InvalidOperationException: collider bounds unavailable" ||
            (string?)emitted["live_read"]!["truck"]!["error"] != (string?)section["error"] ||
            !ReferenceEquals(collider, live["collider"]))
            throw new Exception("Incomplete must preserve the section and serialize its identity and exception.");
        Console.WriteLine("obstacle error regression passed");
    }
}
''')
            built = subprocess.run(
                ['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
                 '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(
                ['dotnet', str(folder / f'bin/Debug/net{major}.0/Harness.dll')],
                capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('obstacle error regression passed', ran.stdout)

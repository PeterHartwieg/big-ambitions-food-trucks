"""Execute the production simulated-drive coroutine at interrupted clock boundaries."""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]

class SimulatedDriveTests(unittest.TestCase):
    def test_arrival_requires_the_original_clock_to_reach_its_target(self):
        source = (ROOT / 'unity/FoodTrucks/Scripts/TestBridgeDrive.cs').read_text()
        start = source.index('        private static IEnumerator DriveSteps(')
        end = source.index('        private static IEnumerator Ride(', start)
        method = source[start:end]
        with tempfile.TemporaryDirectory(prefix='ft-simulated-drive-') as directory:
            folder = Path(directory)
            (folder / 'Harness.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
            (folder / 'Program.cs').write_text(STUB + method + CASES)
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: simulated drive boundaries', result.stdout)

STUB = '''using System.Collections;
using Newtonsoft.Json.Linq;
class Game { public int Day=1, Hour=23; public double Minute=59.8; }
static class SaveGameManager { public static Game Current=new(); }
static class Log { public static void Info(string text) {} }
class BridgeError : Exception { public BridgeError(string text):base(text){} }
static class TestBridgeCommands {
    class DriveRun { public string Phase="riding", Id="truck", Place="bay:test";
        public bool Simulated=true, Done; public double FromPosition=0, Goal=100; public JObject Ride; }
    const float SimDetour=1.35f, SimMetresPerMinute=8f;
    static bool RunningUntil; static double target; static int parks; static string failure;
    static double Flat(double value)=>Math.Abs(value);
    static double NowMinutes(Game game)=>game.Day*1440.0+game.Hour*60+game.Minute;
    static void RunClockUntil(int day,int minuteOfDay,bool forDrive=false) { target=day*1440.0+minuteOfDay; RunningUntil=true; }
    static void Fail(DriveRun run,string reason) { failure=reason; run.Done=true; }
    static IEnumerator Rides(DriveRun run) { yield break; }
    static void Park(DriveRun run) { parks++; }
    static void Finish(DriveRun run,object error) { run.Done=true; }
'''
CASES = '''
    static void Main() {
        foreach(var scenario in new[]{"arrive","cancel","reload","already-done"}) {
            SaveGameManager.Current=new(); parks=0; failure=null;
            var original=SaveGameManager.Current; var run=new DriveRun(); var steps=DriveSteps(run);
            if(!steps.MoveNext() || parks!=0) throw new Exception("parked before waiting");
            if(target<=NowMinutes(original)+16) throw new Exception("rounded travel time down");
            RunningUntil=false;
            if(scenario=="arrive" || scenario=="reload") {
                original.Day=(int)(target/1440); original.Hour=(int)(target%1440)/60; original.Minute=target%60;
            }
            if(scenario=="reload") SaveGameManager.Current=new();
            if(scenario=="already-done") run.Done=true;
            while(steps.MoveNext()) {}
            if(parks!=(scenario=="arrive"?1:0)) throw new Exception("unexpected parking: "+scenario);
            if((scenario=="cancel" || scenario=="reload") && failure==null) throw new Exception("missing failure");
        }
        Console.WriteLine("PASS: simulated drive boundaries");
    }
}
'''

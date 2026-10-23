"""Execute the native bridge's notice cache across save-state replacements."""
from pathlib import Path
import subprocess
import tempfile
import unittest
ROOT = Path(__file__).resolve().parents[2]

class NoticeSessionsTests(unittest.TestCase):
    def test_notices_are_bounded_and_never_cross_loaded_states(self):
        source=(ROOT/'unity/FoodTrucks/Scripts/TestBridgeHttp.cs').read_text()
        source=source[source.index('    internal static class TestBridgeEvents'):source.rfind('\n}')]
        with tempfile.TemporaryDirectory(prefix='ft-notice-sessions-') as directory:
            p=Path(directory)
            (p/'Harness.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
            (p/'Program.cs').write_text(STUB+source+TEST)
            r=subprocess.run(['dotnet','run','--project',str(p/'Harness.csproj'),'-v:q'],capture_output=True,text=True,timeout=120)
            self.assertEqual(r.returncode,0,r.stdout+r.stderr)
            self.assertIn('PASS: notice save isolation',r.stdout)
STUB='''using Newtonsoft.Json.Linq;
using UI.Notification;
namespace UnityEngine {
 public enum LogType { Error, Exception, Assert, Log }
 public static class Application { public static event Action<string,string,LogType>? logMessageReceivedThreaded; }
}
namespace UI.Notification {
 public enum NotificationType { Message }
 public static class Notifications {
  public static event Action<NotificationType,string,Dictionary<string,string>,float,string,Action,bool,bool>? onShow;
  public static void Show(string text)=>onShow?.Invoke(NotificationType.Message,text,new(),1,"",()=>{},false,false);
 }
}
static class Log { public static Action<string,string>? Listener; public static bool Writing; }
static class Notices { public static bool Showing; public static string Text(string key,Dictionary<string,string> data)=>key; }
static class TestBridge { public static (int?,string?) ClockNow()=>(1,"12:00"); }
static class SaveGameManager { public static object Current=new(); }
static class FoodTrucksSession { public static object CurrentState=new(); }
'''
TEST='''
static class Program {
 static void Assert(bool value) { if(!value)throw new Exception("notice isolation failed"); }
 static void Main() {
  TestBridgeEvents.Hook(); Notifications.Show("first save");
  Assert(TestBridgeEvents.GameNoticesAt(1,"12:00").Single()=="first save");
  FoodTrucksSession.CurrentState=new();
  Assert(TestBridgeEvents.GameNoticesAt(1,"12:00").Count==0);
  Notifications.Show("same game, new state"); SaveGameManager.Current=new();
  Assert(TestBridgeEvents.GameNoticesAt(1,"12:00").Count==0);
  Notifications.Show("second save");
  Assert(TestBridgeEvents.GameNoticesAt(1,"12:00").Single()=="second save");
  TestBridgeEvents.Clear(); Assert(TestBridgeEvents.GameNoticesAt(1,"12:00").Count==0);
  for(int i=0;i<205;i++)Notifications.Show(i.ToString());
  var rows=TestBridgeEvents.GameNoticesAt(1,"12:00"); Assert(rows.Count==200 && rows[0]=="5" && rows[^1]=="204");
  Notices.Showing=true; Notifications.Show("duplicate mod notice"); Notices.Showing=false;
  Assert(TestBridgeEvents.GameNoticesAt(1,"12:00").Count==200);
  TestBridgeEvents.Unhook(); Notifications.Show("unhooked");
  Assert(TestBridgeEvents.GameNoticesAt(1,"12:00")[^1]=="204");
  Console.WriteLine("PASS: notice save isolation");
 }
}
'''

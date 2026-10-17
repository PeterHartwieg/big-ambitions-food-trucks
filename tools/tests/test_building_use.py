"""Run the bridge's actual building-click binding with small native API doubles, without Unity or a game PC."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class BuildingUseBindingTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which('dotnet'), 'native binding harness needs dotnet')
    def test_id_click_ignores_desktop_pointer_but_keeps_native_walk_and_entry_guards(self):
        source = (Path(__file__).resolve().parents[2] / 'unity/FoodTrucks/Scripts/TestBridgeLife.cs').read_text()
        start = source.index('        private static bool UseBuildingClick(')
        method = source[start:source.index('        private static object UseClick(', start)]
        start = source.index('        private sealed class UseNoticeCapture')
        notice_capture = source[start:source.index('        // An id click', start)]
        major = subprocess.check_output(['dotnet', '--version'], text=True).strip().split('.')[0]
        with tempfile.TemporaryDirectory(prefix='ft-building-use-') as folder:
            root = Path(folder)
            (root / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit>
<UseSharedCompilation>false</UseSharedCompilation></PropertyGroup></Project>''')
            (root / 'Program.cs').write_text('''
record struct Vector3(int X) { public static Vector3 zero => new(0); }
enum NotificationType { Error, Info }
static class Notifications {
    public static Action<NotificationType, string, Dictionary<string, string>, float, string, Action, bool, bool>? onShow;
    public static void Show(string key, Dictionary<string, string> data, NotificationType type = NotificationType.Error) => onShow?.Invoke(type, key, data, 4, "", () => {}, true, true);
}
static class Notices {
    public static string Text(string key, Dictionary<string, string> data) => key + ": " + data["reason"];
}
static class FullMenu { public static bool IsOpen; }
static class CityMap { public static bool IsOpen; }
static class GameManager { public static bool MiniGame; public static bool IsAnyMiniGameActive() => MiniGame; }
static class PlayerHelper {
    public static PlayerController Player = new();
    public static bool IsUsingVehicle;
    public static string? Hands;
    public static Vector3 GetPosition() => new(2);
    public static bool IsWithinPlayerDistance(CityBuildingController b) => b.Near;
}
class PlayerController {
    public bool NavigationDisabled, OnMesh = true, Route = true;
    public string Blocker = "Map";
    public int RouteNotices;
    public Action? Goal;
    public bool hasOnGoalReachedAction => Goal != null;
    public bool IsOnNavmesh() => OnMesh;
    public bool ExistsRoute(Vector3 target, bool showErrorNotification, CityBuildingController entityController) {
        bool found = target != Vector3.zero && Route;
        if (!found && showErrorNotification) RouteNotices++;
        return found;
    }
}
class CityBuildingController {
    public bool Near, HasTarget = true, Entered, StartWalk = true;
    public int Interactions, Refreshes, Walks;
    public string? Notice;
    // Deliberately reject physical mouse clicks, as when its pointer is parked over the bag HUD.
    public bool OnIoLeftClick() => throw new Exception("Desktop pointer swallowed the click");
    public void UpdateNavMeshTargets() => Refreshes++;
    public Vector3 GetClosestNavMeshTargetPosition(Vector3 from) => HasTarget ? new(1) : Vector3.zero;
    public virtual bool Interact() { Interactions++; Entered = true; return true; }
    public void MoveTowardsEntity(Action action) { Walks++; if (StartWalk) PlayerHelper.Player.Goal = action; }
}
class RefusingBuilding : CityBuildingController {
    public override bool Interact() { Interactions++; Notice = "Private property"; return true; }
}
class Harness {
    static string Blockers(PlayerController p) => "; navigation blocked by " + p.Blocker;
''' + notice_capture + method + '''
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reset() {
        FullMenu.IsOpen = CityMap.IsOpen = GameManager.MiniGame = PlayerHelper.IsUsingVehicle = false;
        PlayerHelper.Player = new();
    }
    static void Main() {
        foreach (var held in new[] { "paid paper bag: 5 banana bags", "shopping bag", "cardboard box", "unpaid bag" }) {
            foreach (bool near in new[] { false, true }) {
                Reset(); PlayerHelper.Hands = held;
                var player = PlayerHelper.Player; var building = new CityBuildingController { Near = near };
                Check(UseBuildingClick(building, player, out var why) && why == null, "id click accepted with " + held);
                Check(building.Refreshes == 1, "native targets refreshed");
                Check(building.Interactions == (near ? 1 : 0), "never enter before reaching the door");
                if (!near) { Check(player.Goal != null && building.Walks == 1, "native walk scheduled"); player.Goal!(); }
                Check(building.Entered && building.Interactions == 1, "one native interaction");
                Check(PlayerHelper.Hands == held, "carried cargo stays untouched");
            }
        }
        foreach (string guard in new[] { "phone", "map", "blocker", "minigame", "off mesh", "no target", "no route", "walk refused" }) {
            Reset(); var player = PlayerHelper.Player; var building = new CityBuildingController();
            switch (guard) {
                case "phone": FullMenu.IsOpen = true; break;
                case "map": CityMap.IsOpen = true; break;
                case "blocker": player.NavigationDisabled = true; break;
                case "minigame": GameManager.MiniGame = true; break;
                case "off mesh": player.OnMesh = false; break;
                case "no target": building.HasTarget = false; break;
                case "no route": player.Route = false; break;
                case "walk refused": building.StartWalk = false; break;
            }
            Check(!UseBuildingClick(building, player, out var why) && !string.IsNullOrEmpty(why), guard + " has a reason");
            Check(!building.Entered && building.Interactions == 0 && player.Goal == null, guard + " never enters");
            if (guard == "blocker") Check(why!.Contains("Map"), "navigation blocker named");
            if (guard is "no target" or "no route") Check(player.RouteNotices == 1, "native cannot-reach notice requested");
        }
        Reset(); var locked = new RefusingBuilding { Near = true };
        Check(UseBuildingClick(locked, PlayerHelper.Player, out _) && !locked.Entered, "handled is not entry success");
        Check(locked.Notice == "Private property" && locked.Interactions == 1, "virtual game refusal remains authoritative");
        Reset(); PlayerHelper.IsUsingVehicle = true; PlayerHelper.Player.NavigationDisabled = true;
        Check(UseBuildingClick(new(), PlayerHelper.Player, out _), "native active-vehicle navigation exception preserved");
        int otherListener = 0;
        Notifications.onShow += (_, _, _, _, _, _, _, _) => otherListener++;
        var capture = new UseNoticeCapture();
        Notifications.Show("vanilla", new() { ["reason"] = "Private property" });
        Notifications.Show("mod", new() { ["reason"] = "one notice" }, NotificationType.Info);
        Check(string.Join("|", capture.Messages) == "vanilla: Private property|mod: one notice", "native key/data resolved once per event");
        Check(capture.LastError == "vanilla: Private property", "an unrelated info notice cannot replace the game's refusal");
        capture.Dispose(); capture.Dispose();
        Notifications.Show("later", new() { ["reason"] = "not part of the click" });
        Check(capture.Messages.Count == 2 && otherListener == 3, "capture cleanup preserves the game's other listeners");
        var cancelled = new UseNoticeCapture();
        try { using (cancelled) { throw new Exception("click failed"); } } catch { }
        Notifications.Show("later", new() { ["reason"] = "not part of failed click" });
        Check(cancelled.Messages.Count == 0, "exception cleanup unsubscribes capture");
        Console.WriteLine("PASS: building use preserves cargo, native guards, virtual interaction and arrival ordering");
    }
}
''')
            result = subprocess.run(['dotnet', 'run', '--project', str(root / 'Harness.csproj'), '-m:1', '--verbosity', 'quiet'],
                                    env={**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1'},
                                    capture_output=True, text=True, timeout=90)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: building use', result.stdout)


if __name__ == '__main__':
    unittest.main()

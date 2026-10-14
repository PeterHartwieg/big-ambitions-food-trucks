#!/usr/bin/env python3
"""Run the bridge's actual text helpers against small Unity component stubs (no Unity/game)."""
from pathlib import Path
import subprocess
import tempfile

SOURCE = Path(__file__).resolve().parents[2] / 'unity/FoodTrucks/Scripts/TestBridgeUi.cs'


def main():
    source = SOURCE.read_text()
    start = source.index('        private static IEnumerable<(Component Component, string Text)> TextBlocks(')
    end = source.index('        private static string PathOf(', start)
    methods = source[start:end]
    version = subprocess.check_output(['dotnet', '--version'], text=True).strip()
    major = int(version.split('.', 1)[0])
    with tempfile.TemporaryDirectory(prefix='ft-ui-text-') as folder:
        root = Path(folder)
        (root / 'Harness.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
        (root / 'Program.cs').write_text('''
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine {
    public class Component { }
    public class Behaviour : Component { public bool isActiveAndEnabled = true; }
    public class Transform {
        public string name = "Button";
        public List<Behaviour> Children = new();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Children.OfType<T>().ToArray();
    }
    public struct Color { public float a; }
}
namespace UnityEngine.UI {
    public class Text : Behaviour { public string text = ""; public Color color = new() { a = 1 }; }
}
class TmpFixture : Behaviour { public string text { get; set; } = ""; public float alpha { get; set; } = 1; }
class Harness {
    private static Type? TmpText = typeof(TmpFixture);
    private static PropertyInfo? TmpTextProperty = TmpText.GetProperty("text");
    private static PropertyInfo? TmpAlphaProperty = TmpText.GetProperty("alpha");
''' + methods + '''
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main() {
        var root = new Transform();
        root.Children.Add(new Text { text = "<b>Street food</b>  fleet\\ncontrols" });
        Assert(Label(root) == "Street food fleet controls", "legacy caption must replace Button fallback and clean markup");
        Assert(TextBlocks(root).Count() == 1, "legacy label enumerated once");
        root.Children.Insert(0, new TmpFixture { text = "Native first" });
        Assert(string.Join("|", TextBlocks(root).Select(x => x.Text)) == "Native first|Street food fleet controls", "mixed TMP/legacy hierarchy order");
        Assert(Label(root) == "Native first", "TMP caption still works");
        root.Children.Insert(0, new Text { text = "disabled", isActiveAndEnabled = false });
        root.Children.Insert(0, new Text { text = "transparent", color = new() { a = 0.01f } });
        root.Children.Insert(0, new TmpFixture { text = "transparent TMP", alpha = 0.01f });
        Assert(TextBlocks(root).Count() == 2, "disabled and transparent labels must be skipped");
        TmpText = null; TmpTextProperty = null; TmpAlphaProperty = null;
        Assert(Label(root) == "Street food fleet controls", "legacy captions must work without TMP assembly");
        root.Children.Clear();
        root.Children.Add(new Text { text = "   <b></b> " });
        Assert(Label(root) == "Button", "empty captions preserve object-name fallback");
        Console.WriteLine("PASS: five bridge UI text cases (actual TextBlocks, Label and Clean methods)");
    }
}
''')
        subprocess.run(['dotnet', 'run', '--project', str(root / 'Harness.csproj'), '--verbosity', 'quiet'], check=True)


if __name__ == '__main__':
    main()

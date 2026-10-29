using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using AgentRigInput;
using HarmonyLib;
using Mono.Cecil;
using Xunit;

// Stand-ins for UnityEngine.Input and the shim: same shape (static, identical signatures), no Unity needed.
public static class FakeInput
{
    public static bool Held;
    public static float X;
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool GetMouseButton(int button) => Held;
    public static float mouseX { [MethodImpl(MethodImplOptions.NoInlining)] get => X; }
}

public static class FakeShim
{
    public static bool Active;
    public static int Reads;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool GetMouseButton(int button) { if (!Active) return FakeInput.GetMouseButton(button); Reads++; return button == 0; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float get_mouseX() => Active ? 42f : FakeInput.mouseX;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Wrong(string button) => false;
}

// Game-like consumers: a polling coroutine with a try/finally (labels and exception blocks to keep) and a loop.
public static class FakeGame
{
    public static readonly List<string> Log = new List<string>();

    public static IEnumerator DraggingCoroutine()
    {
        var origin = FakeInput.mouseX;
        try
        {
            var n = 0;
            while (FakeInput.GetMouseButton(0) && n++ < 3)
            {
                Log.Add("x=" + (FakeInput.mouseX - origin));
                yield return null;
            }
        }
        finally { Log.Add("end"); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CountHeld()
    {
        var count = 0;
        for (var i = 0; i < 3; i++)
            switch (i)
            {
                case 0: if (FakeInput.GetMouseButton(0)) count++; break;
                case 1: if (FakeInput.GetMouseButton(1)) count++; break;
                default: count += FakeInput.GetMouseButton(2) ? 1 : 0; break;
            }
        return count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long NoInput(long a, double b) => a + (long)b;
}

[Collection("harmony")]
public sealed class CallSiteTests : IDisposable
{
    readonly Harmony harmony = new Harmony("rig-input-tests-" + Guid.NewGuid());
    static readonly MethodInfo Button = typeof(FakeInput).GetMethod(nameof(FakeInput.GetMouseButton));
    static readonly MethodInfo X = typeof(FakeInput).GetProperty(nameof(FakeInput.mouseX)).GetGetMethod();

    public CallSiteTests()
    {
        CallSiteTranspiler.SetMap(new Dictionary<MethodBase, MethodInfo>
        {
            [Button] = typeof(FakeShim).GetMethod(nameof(FakeShim.GetMouseButton)),
            [X] = typeof(FakeShim).GetMethod(nameof(FakeShim.get_mouseX)),
        });
        FakeShim.Active = false; FakeShim.Reads = 0; FakeInput.Held = false; FakeGame.Log.Clear();
    }

    public void Dispose() => harmony.UnpatchAll(harmony.Id);

    static MethodBase MoveNext() => typeof(FakeGame).GetNestedTypes(BindingFlags.NonPublic)
        .Single(t => t.Name.Contains(nameof(FakeGame.DraggingCoroutine))).GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    [Fact]
    public void ScannerFindsCallSitesInIteratorsAndSwitchesWithCorrectCounts()
    {
        var problems = new List<string>();
        var found = CallSiteTranspiler.Scan(new[] { typeof(FakeGame).Assembly }, typeof(string).Assembly, problems)
            .Where(f => f.Method.DeclaringType == typeof(FakeGame) || f.Method.DeclaringType.DeclaringType == typeof(FakeGame))
            .ToDictionary(f => f.Method.Name, f => f.Calls);
        Assert.DoesNotContain(problems, p => p.Contains("FakeGame"));
        Assert.Equal(3, found["CountHeld"]);
        // The origin read, the loop's button read and the loop's position read.
        Assert.Equal(3, found["MoveNext"]);
        Assert.False(found.ContainsKey("NoInput"));
        Assert.False(found.ContainsKey(nameof(FakeGame.DraggingCoroutine)));
    }

    [Fact]
    public void TheShimsOwnAssemblyIsNeverACandidate()
    {
        var found = CallSiteTranspiler.Scan(new[] { typeof(FakeShim).Assembly }, typeof(FakeShim).Assembly, new List<string>());
        Assert.Empty(found);
    }

    [Fact]
    public void MalformedIlIsReportedNotGuessed()
    {
        Assert.Throws<FormatException>(() => IlScanner.Calls(new byte[] { 0x28, 0x01 }));
        Assert.Throws<FormatException>(() => IlScanner.Calls(new byte[] { 0xFE, 0xFF }));
        Assert.Empty(IlScanner.Calls(new byte[] { 0x00, 0x2A }));
    }

    [Fact]
    public void TranspiledCoroutineReadsTheShimAndKeepsItsFinallyBlock()
    {
        var target = MoveNext();
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(CallSiteTranspiler).GetMethod(nameof(CallSiteTranspiler.Transpiler))));
        Assert.Equal(3, CallSiteTranspiler.Replaced[target]);

        // Inactive: real input passes through unchanged (button up, so the loop never runs).
        var e = FakeGame.DraggingCoroutine();
        while (e.MoveNext()) { }
        Assert.Equal(new[] { "end" }, FakeGame.Log);
        Assert.Equal(0, FakeShim.Reads);

        // Active: the virtual button is held, the virtual x is read; the finally still runs.
        FakeGame.Log.Clear();
        FakeShim.Active = true;
        e = FakeGame.DraggingCoroutine();
        while (e.MoveNext()) { }
        Assert.Equal(new[] { "x=0", "x=0", "x=0", "end" }, FakeGame.Log);
        Assert.Equal(4, FakeShim.Reads);
    }

    [Fact]
    public void TranspiledSwitchKeepsBranchTargets()
    {
        var target = typeof(FakeGame).GetMethod(nameof(FakeGame.CountHeld));
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(CallSiteTranspiler).GetMethod(nameof(CallSiteTranspiler.Transpiler))));
        Assert.Equal(3, CallSiteTranspiler.Replaced[target]);
        FakeInput.Held = true;
        Assert.Equal(3, FakeGame.CountHeld());
        FakeShim.Active = true;
        Assert.Equal(1, FakeGame.CountHeld());
    }

    [Fact]
    public void MismatchedShimSignaturesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => CallSiteTranspiler.SetMap(new Dictionary<MethodBase, MethodInfo>
        {
            [Button] = typeof(FakeShim).GetMethod(nameof(FakeShim.Wrong)),
        }));
        Assert.Throws<ArgumentException>(() => CallSiteTranspiler.SetMap(new Dictionary<MethodBase, MethodInfo>
        {
            [Button] = typeof(FakeShim).GetMethod(nameof(FakeShim.get_mouseX)),
        }));
    }
}

public sealed class ReleaseGuardTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "rig-guard-" + Guid.NewGuid());
    public ReleaseGuardTests() => Directory.CreateDirectory(Path.Combine(root, "Dependencies"));
    public void Dispose() => Directory.Delete(root, true);

    void Assembly(string file, Action<ModuleDefinition> change)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll);
        asm.MainModule.Types.Add(new TypeDefinition("FoodTrucks", "Plain", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, asm.MainModule.TypeSystem.Object));
        change(asm.MainModule);
        asm.Write(Path.Combine(root, file));
    }

    [Fact]
    public void CleanPackagePasses()
    {
        Assembly("FoodTrucks.dll", _ => { });
        File.WriteAllText(Path.Combine(root, "README.md"), "readme");
        RigInputGuard.Check(root);
    }

    [Fact]
    public void RigFileIsRejectedWhateverItsHashOrFolder()
    {
        File.WriteAllBytes(Path.Combine(root, "Dependencies", "AgentRigInput.dll"), new byte[] { 1, 2, 3 });
        Assert.Contains("Rig-only input file", Assert.Throws<Exception>(() => RigInputGuard.Check(root)).Message);
    }

    [Fact]
    public void RenamedRigAssemblyIsRejected()
    {
        Assembly("Dependencies/Helper.dll", m => { m.Assembly.Name.Name = "AgentRigInput"; });
        Assert.Contains("assembly", Assert.Throws<Exception>(() => RigInputGuard.Check(root)).Message);
    }

    [Fact]
    public void ReferenceToTheRigAssemblyIsRejected()
    {
        Assembly("FoodTrucks.dll", m => m.AssemblyReferences.Add(new AssemblyNameReference("AgentRigInput", new Version(1, 0))));
        Assert.Contains("references", Assert.Throws<Exception>(() => RigInputGuard.Check(root)).Message);
    }

    [Fact]
    public void EmbeddedShimTypesAreRejectedIncludingNestedOnes()
    {
        Assembly("FoodTrucks.dll", m =>
        {
            var outer = m.Types.Single(t => t.Name == "Plain");
            outer.NestedTypes.Add(new TypeDefinition("", "LegacyInputShim", Mono.Cecil.TypeAttributes.NestedPrivate | Mono.Cecil.TypeAttributes.Class, m.TypeSystem.Object));
        });
        Assert.Contains("LegacyInputShim", Assert.Throws<Exception>(() => RigInputGuard.Check(root)).Message);
    }

    [Fact]
    public void EmbeddedResourceIsRejected()
    {
        Assembly("FoodTrucks.dll", m => m.Resources.Add(new EmbeddedResource("AgentRigInput.dll", Mono.Cecil.ManifestResourceAttributes.Private, new byte[] { 1 })));
        Assert.Contains("resource", Assert.Throws<Exception>(() => RigInputGuard.Check(root)).Message);
    }
}

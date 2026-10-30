using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

// RigInputGuard (tools/release) refuses the rig-only virtual mouse (big-ambitions-agent-rig rig-input/) in packages.
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

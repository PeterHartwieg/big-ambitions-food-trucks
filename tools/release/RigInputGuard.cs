using Mono.Cecil;

// The rig-only virtual mouse (tools/rig-input) must never ship: no file, assembly reference, embedded type or
// resource of it may appear anywhere in a runtime package, whatever the file allowlist or regenerated hashes say.
public static class RigInputGuard
{
    private const string Name = "AgentRigInput";
    private static readonly HashSet<string> RigTypes = new(StringComparer.Ordinal)
        { "LegacyInputShim", "RigPointerInputModule", "RigBaseInput", "CallSiteTranspiler", "MouseEngine", "MouseTimeline" };

    public static void Check(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Contains(Name, StringComparison.OrdinalIgnoreCase)) throw new Exception("Rig-only input file in release: " + relative);
            if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            AssemblyDefinition assembly;
            try { assembly = AssemblyDefinition.ReadAssembly(file); }
            catch (BadImageFormatException) { continue; }
            using (assembly)
            {
                if (assembly.Name.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)) throw new Exception("Rig-only input assembly in release: " + relative);
                foreach (var module in assembly.Modules)
                {
                    if (module.AssemblyReferences.Any(r => r.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)))
                        throw new Exception($"{relative} references the rig-only input assembly");
                    if (module.Resources.Any(r => r.Name.Contains(Name, StringComparison.OrdinalIgnoreCase)))
                        throw new Exception($"{relative} embeds a rig-only input resource");
                    var type = All(module.Types).FirstOrDefault(t => RigTypes.Contains(t.Name) || t.FullName.Contains(Name + ".", StringComparison.Ordinal) || t.Namespace == Name);
                    if (type != null) throw new Exception($"{relative} contains rig-only input type {type.FullName}");
                }
            }
        }
    }

    private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types)
    {
        foreach (var t in types)
        {
            yield return t;
            foreach (var nested in All(t.NestedTypes)) yield return nested;
        }
    }
}

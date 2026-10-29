using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AgentRigInput
{
    // Finds call instructions in a method body without decompiling it: only opcodes and metadata tokens are read.
    public static class IlScanner
    {
        private static readonly OpCode[] One = new OpCode[0x100], Two = new OpCode[0x100];
        private static readonly bool[] HasOne = new bool[0x100], HasTwo = new bool[0x100];

        static IlScanner()
        {
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(f.GetValue(null) is OpCode op)) continue;
                var v = (ushort)op.Value;
                if (op.Size == 1) { One[v] = op; HasOne[v] = true; }
                else { Two[v & 0xff] = op; HasTwo[v & 0xff] = true; }
            }
        }

        public struct Call
        {
            public int Offset;
            public int Token;
        }

        // Every call/callvirt with its method token. Throws FormatException for malformed IL.
        public static List<Call> Calls(byte[] il)
        {
            var calls = new List<Call>();
            var i = 0;
            while (i < il.Length)
            {
                var start = i;
                OpCode op;
                var b = il[i++];
                if (b == 0xFE)
                {
                    if (i >= il.Length || !HasTwo[il[i]]) throw new FormatException($"Unknown two-byte opcode at {start}");
                    op = Two[il[i++]];
                }
                else
                {
                    if (!HasOne[b]) throw new FormatException($"Unknown opcode 0x{b:x2} at {start}");
                    op = One[b];
                }
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch:
                        if (i + 4 > il.Length) throw new FormatException("Truncated switch");
                        var n = BitConverter.ToInt32(il, i);
                        if (n < 0) throw new FormatException("Negative switch");
                        i += 4 + 4 * n;
                        break;
                    case OperandType.InlineMethod:
                        if (i + 4 > il.Length) throw new FormatException("Truncated method token");
                        if (op == OpCodes.Call || op == OpCodes.Callvirt)
                            calls.Add(new Call { Offset = start, Token = BitConverter.ToInt32(il, i) });
                        i += 4;
                        break;
                    default: i += 4; break;
                }
                if (i > il.Length) throw new FormatException($"Truncated operand at {start}");
            }
            return calls;
        }
    }

    // Compares methods by module and metadata token, so two reflection objects for one method are the same key.
    public sealed class MethodKey : IEqualityComparer<MethodBase>
    {
        public static readonly MethodKey Instance = new MethodKey();
        public bool Equals(MethodBase a, MethodBase b) =>
            ReferenceEquals(a, b) || (a != null && b != null && a.MetadataToken == b.MetadataToken && a.Module == b.Module);
        public int GetHashCode(MethodBase m) => m.MetadataToken ^ m.Module.GetHashCode();
    }

    // Replaces the operands of calls to mapped methods (Unity's legacy mouse reads) with identical-signature shims.
    // Labels, exception blocks and every other instruction are left as they are.
    public static class CallSiteTranspiler
    {
        public static readonly Dictionary<MethodBase, MethodInfo> Map = new Dictionary<MethodBase, MethodInfo>(MethodKey.Instance);
        public static readonly ConcurrentDictionary<MethodBase, int> Replaced = new ConcurrentDictionary<MethodBase, int>(MethodKey.Instance);

        public static void SetMap(IEnumerable<KeyValuePair<MethodBase, MethodInfo>> pairs)
        {
            Map.Clear();
            foreach (var p in pairs)
            {
                var original = p.Key; var shim = p.Value;
                if (!shim.IsStatic || shim.ReturnType != ((original as MethodInfo)?.ReturnType ?? typeof(void)) ||
                    !shim.GetParameters().Select(x => x.ParameterType).SequenceEqual(original.GetParameters().Select(x => x.ParameterType)) ||
                    !original.IsStatic)
                    throw new ArgumentException($"Shim {shim.Name} does not match {original.DeclaringType?.Name}.{original.Name}");
                Map[original] = shim;
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var count = 0;
            var list = instructions.ToList();
            foreach (var ci in list)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodBase m && Map.TryGetValue(m, out var shim))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = shim;
                    count++;
                }
            }
            if (__originalMethod != null) Replaced[__originalMethod] = count;
            return list;
        }

        // The methods of these assemblies whose bodies call a mapped method, with the number of call sites.
        // Methods of the shim's own assembly are never candidates, so its fallback calls stay on Unity's API.
        public static List<(MethodBase Method, int Calls)> Scan(IEnumerable<Assembly> assemblies, Assembly shimAssembly, List<string> problems)
        {
            var found = new List<(MethodBase, int)>();
            foreach (var asm in assemblies)
            {
                if (asm == shimAssembly) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); problems.Add($"{asm.GetName().Name}: some types did not load"); }
                var cache = new Dictionary<int, bool>();
                foreach (var type in types)
                {
                    const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                    foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
                    {
                        byte[] il;
                        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                        catch (Exception) { continue; }
                        if (il == null) continue;
                        List<IlScanner.Call> calls;
                        try { calls = IlScanner.Calls(il); }
                        catch (FormatException ex) { problems.Add($"{type.FullName}.{method.Name}: {ex.Message}"); continue; }
                        var hits = 0;
                        foreach (var call in calls)
                        {
                            if (!cache.TryGetValue(call.Token, out var mapped))
                            {
                                try { mapped = Map.ContainsKey(method.Module.ResolveMethod(call.Token)); }
                                catch (Exception) { mapped = false; }
                                cache[call.Token] = mapped;
                            }
                            if (mapped) hits++;
                        }
                        if (hits > 0) found.Add((method, hits));
                    }
                }
            }
            return found;
        }
    }
}

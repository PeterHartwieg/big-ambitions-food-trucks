using Mono.Cecil;
using Mono.Cecil.Cil;
var root = Path.GetFullPath(args[0]);
var manifest = File.ReadAllText(args[1]);
var version = System.Text.RegularExpressions.Regex.Match(manifest, @"(?m)^  Version: (\S+)$").Groups[1].Value;
if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new Exception("Manifest must have a final release version");
using var dll = AssemblyDefinition.ReadAssembly(Path.Combine(root, "FoodTrucks.dll"));
var types = dll.MainModule.Types.ToArray();
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var seam = types.Single(t => t.FullName == "FoodTrucks.CrewDriveProbe");
Check(seam.IsAbstract && seam.IsSealed && seam.BaseType.FullName == "System.Object", "Probe must be an inert static seam");
Check(!types.Any(t => t.Name.StartsWith("CrewProbe")), "Native probe type in release");
Check(seam.Methods.Select(m => m.Name).Order().SequenceEqual(new[] { "get_Active", "Owns", "Owns", "ResetSession", "StopAll" }.Order()), "Unexpected probe entry point");
foreach (var m in seam.Methods) {
 var code = m.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
 var expected = m.Name == "ResetSession" ? new[] { Code.Ret } : new[] { m.Name == "StopAll" ? Code.Ldc_I4_1 : Code.Ldc_I4_0, Code.Ret };
 Check(code.Select(i => i.OpCode.Code).SequenceEqual(expected), "Nonconstant release seam: " + m.Name);
}
var instructions = types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).ToArray();
Check(!instructions.Any(i => i.Operand is MethodReference m && (m.Name == "StartProbe" || m.DeclaringType.Name.StartsWith("CrewProbe"))), "Probe invocation survives");
Check(!instructions.Any(i => i.Operand is string s && (s.Contains("crew_drive_probe") || s.Contains("crew driving probe", StringComparison.OrdinalIgnoreCase))), "Probe command/help survives");
Check(types.Any(t => t.Name == "HireRoutes") && types.Any(t => t.Name == "TruckLogoEditor") && types.Any(t => t.Name == "TruckLogos"), "Missing production features");
Check(types.Single(t => t.Name == "FoodTrucksMod").Fields.Any(f => f.Name == "Version" && f.HasConstant && Equals(f.Constant, version)), "Wrong version constant");
void CheckAssemblyVersion(AssemblyDefinition assembly) {
 Check(assembly.Name.Version == Version.Parse(version + ".0"), "Wrong assembly version: " + assembly.Name.Name);
 var fileVersion = assembly.CustomAttributes.Single(a => a.AttributeType.FullName == "System.Reflection.AssemblyFileVersionAttribute");
 Check(Equals(fileVersion.ConstructorArguments[0].Value, version + ".0"), "Wrong file version: " + assembly.Name.Name);
 var infoVersion = assembly.CustomAttributes.Single(a => a.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute");
 var informational = (string)infoVersion.ConstructorArguments[0].Value;
 Check(informational == version, "Wrong informational version: " + assembly.Name.Name);
 Console.WriteLine($"PASS: {assembly.Name.Name} assembly/file {version}.0; informational {informational}.");
}
CheckAssemblyVersion(dll);
Check(instructions.Any(i => Equals(i.Operand, $"Version {version} loading.")), "Wrong startup version log line");
using var core = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Dependencies/FoodTrucks.Core.dll"));
CheckAssemblyVersion(core);
Check(Equals(core.MainModule.Types.Single(t => t.Name == "SaveState").Fields.Single(f => f.Name == "CurrentSchemaVersion").Constant, 2), "Save schema changed");
Console.WriteLine($"PASS: {version} DLL; no physical component/hooks/start/command; inert seam; timed routes and branding retained.");

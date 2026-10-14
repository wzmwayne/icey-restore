using System; using System.Linq; using Mono.Cecil;
class Members { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  foreach (var t in asm.MainModule.Types.Where(x => x.FullName == a[1])) {
    Console.WriteLine("== " + t.FullName);
    foreach (var f in t.Fields) Console.WriteLine($"   F {(f.IsStatic?"static ":"")}{f.FieldType.Name,-10} {f.Name}");
    foreach (var p in t.Properties) Console.WriteLine($"   P {p.PropertyType.Name,-10} {p.Name}");
    foreach (var m in t.Methods.Where(m => m.IsPublic || m.Name.Contains("Atk") || m.Name.Contains("Damage") || m.Name.Contains("Attack")))
      Console.WriteLine($"   M {m.ReturnType.Name,-10} {m.Name}({string.Join(", ", m.Parameters.Select(x=>x.ParameterType.Name).ToArray())})");
  }
} }

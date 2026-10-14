using System; using System.Linq; using Mono.Cecil;
class Owners2 { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  string target = a[1];
  Console.WriteLine("=== 类型匹配 " + target + " ===");
  foreach (var t in asm.MainModule.Types.Where(x => x.Name == target || x.FullName == target))
    Console.WriteLine($"   {t.FullName}  base={t.BaseType?.FullName}");
  Console.WriteLine("=== 谁持有该类型的成员 ===");
  foreach (var t in asm.MainModule.Types) {
    foreach (var f in t.Fields) if (f.FieldType.Name == target) Console.WriteLine($"   {(f.IsStatic?"static ":"")}{t.FullName}::{f.Name}");
    foreach (var p in t.Properties) if (p.PropertyType.Name == target) Console.WriteLine($"   prop {t.FullName}::{p.Name}");
  }
} }

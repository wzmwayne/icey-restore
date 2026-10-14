using System; using System.Linq; using Mono.Cecil;
class Owners { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  string target = a[1];
  Console.WriteLine("=== 持有 " + target + " 的成员 ===");
  foreach (var t in asm.MainModule.Types) {
    foreach (var f in t.Fields) if (f.FieldType.Name == target) Console.WriteLine($"   {(f.IsStatic?"static ":"")}{t.FullName}.{f.Name}");
    foreach (var p in t.Properties) if (p.PropertyType.Name == target) Console.WriteLine($"   prop {t.FullName}.{p.Name}");
  }
  Console.WriteLine("\n=== 自身是否有静态实例 ===");
  foreach (var t in asm.MainModule.Types.Where(x => x.Name == target))
    foreach (var f in t.Fields.Where(f => f.IsStatic && f.FieldType.Name == target))
      Console.WriteLine("   static " + t.FullName + "." + f.Name);
} }

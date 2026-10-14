using System; using System.Linq; using Mono.Cecil;
class Base { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  foreach (var t in asm.MainModule.Types.Where(x => x.FullName == a[1]))
    Console.WriteLine(t.FullName + "  base=" + (t.BaseType != null ? t.BaseType.FullName : "-"));
} }

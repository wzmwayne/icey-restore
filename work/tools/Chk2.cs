using System; using System.Linq; using Mono.Cecil;
class Chk2 { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/work/host"); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  foreach (var t in asm.MainModule.Types) {
    if (a.Length > 1 && !t.Name.Contains(a[1])) continue;
    Console.WriteLine(t.FullName);
    foreach (var m in t.Methods.Where(x => x.HasBody))
      Console.WriteLine($"   {m.Name,-18} {m.Body.Instructions.Count,4} 条指令  异常处理 {m.Body.ExceptionHandlers.Count}");
  }
} }

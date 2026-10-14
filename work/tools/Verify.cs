using System; using System.Linq; using Mono.Cecil;
class Verify { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver();
  r.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(a[0])));
  r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  foreach (var t in asm.MainModule.Types) {
    if (t.FullName == "IceyMod.Bootstrap") {
      Console.WriteLine("注入类型存在: " + t.FullName + "  方法: " + string.Join(", ", t.Methods.Select(m => m.Name).ToArray()));
      foreach (var m in t.Methods) {
        if (!m.HasBody) continue;
        Console.WriteLine("  " + m.Name + ": " + m.Body.Instructions.Count + " 条指令, 异常处理 " + m.Body.ExceptionHandlers.Count);
      }
    }
    if (t.FullName == "SRDebugger.AutoInitialize")
      foreach (var m in t.Methods)
        if (m.HasBody && m.Body.Instructions.Count > 0)
          Console.WriteLine("钩子 " + t.FullName + "::" + m.Name + " 首条指令: " + m.Body.Instructions[0]);
  }
} }

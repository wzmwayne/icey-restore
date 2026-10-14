using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class PatchDebugFlag {
  static int Main(string[] a) {
    var r = new DefaultAssemblyResolver();
    r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    var flag = asm.MainModule.Types.SelectMany(t => t.Methods).FirstOrDefault(m => m.Name == "DebugFlag");
    if (flag == null) { Console.Error.WriteLine("找不到 DebugFlag（需先注入新桩）"); return 1; }
    int n = 0;
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      if (!m.HasBody) continue;
      foreach (var ins in m.Body.Instructions) {
        var mr = ins.Operand as MethodReference;
        if (mr == null) continue;
        if (mr.Name == "get_isDebugBuild" && mr.DeclaringType != null && mr.DeclaringType.Name == "Debug" && mr.DeclaringType.Namespace == "UnityEngine") {
          ins.Operand = flag; n++;
        }
      }
    }
    asm.Write(a[1]);
    Console.WriteLine("已把 " + n + " 处 UnityEngine.Debug::get_isDebugBuild 改写为 " + flag.DeclaringType.Name + "::DebugFlag");
    return 0;
  }
}

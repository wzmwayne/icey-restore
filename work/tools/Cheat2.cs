using System; using System.Collections.Generic; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class Cheat2 {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    Console.WriteLine("=== 1) 仅 Debug 构建才有的功能（Debug.isDebugBuild）===");
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      if (!m.HasBody) continue;
      bool hit = m.Body.Instructions.Any(i => (i.Operand as MethodReference)?.Name == "get_isDebugBuild");
      if (hit) Console.WriteLine($"   {t.FullName}.{m.Name}");
    }
    Console.WriteLine("\n=== 2) 读取 GameLogicMgr.debug 的地方 ===");
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      if (!m.HasBody) continue;
      if (m.Body.Instructions.Any(i => (i.Operand as FieldReference)?.Name == "debug" && (i.Operand as FieldReference)?.DeclaringType?.Name == "GameLogicMgr"))
        Console.WriteLine($"   {t.FullName}.{m.Name}");
    }
    Console.WriteLine("\n=== 3) GameSettingDebugGui：自带调试菜单 ===");
    var g = asm.MainModule.Types.FirstOrDefault(x => x.Name == "GameSettingDebugGui");
    if (g != null) {
      foreach (var m in g.Methods) {
        Console.WriteLine($"   -- {m.Name}");
        if (!m.HasBody) continue;
        foreach (var i in m.Body.Instructions) {
          if (i.OpCode == OpCodes.Ldstr) Console.WriteLine($"        \"{i.Operand}\"");
          var mr = i.Operand as MethodReference;
          if (mr != null && (mr.Name.StartsWith("Set") || mr.Name.Contains("Unlock") || mr.Name.Contains("Add") || mr.Name.Contains("Kill") || mr.Name.Contains("Save") || mr.Name.Contains("Load")))
            Console.WriteLine($"        → {mr.DeclaringType.Name}.{mr.Name}");
        }
      }
    }
    Console.WriteLine("\n=== 4) 谁引用/创建 GameSettingDebugGui ===");
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      if (!m.HasBody) continue;
      bool hit = m.Body.Instructions.Any(i => {
        var mr = i.Operand as MemberReference; var tr = i.Operand as TypeReference;
        return (mr != null && mr.DeclaringType?.Name == "GameSettingDebugGui") || (tr != null && tr.Name == "GameSettingDebugGui");
      });
      if (hit) Console.WriteLine($"   {t.FullName}.{m.Name}");
    }
  }
}

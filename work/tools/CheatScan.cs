using System; using System.Collections.Generic; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class CheatScan {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    string[] keys = { "CheatMode", "cheat", "Cheat" };
    Console.WriteLine("=== 引用 CheatMode / cheat 的方法 ===");
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      if (!m.HasBody) continue;
      var ins = m.Body.Instructions;
      for (int i = 0; i < ins.Count; i++) {
        bool hit = false; string what = null;
        var mr = ins[i].Operand as MemberReference;
        if (mr != null && keys.Any(k => (mr.Name ?? "").Contains(k))) { hit = true; what = mr.DeclaringType.Name + "::" + mr.Name; }
        if (ins[i].OpCode == OpCodes.Ldstr && keys.Any(k => ((string)ins[i].Operand).Contains(k))) { hit = true; what = "字符串 \"" + ins[i].Operand + "\""; }
        if (!hit) continue;
        Console.WriteLine($"\n--- {t.FullName}.{m.Name}   [{what}]  (IL_{ins[i].Offset:X4})");
        for (int j = Math.Max(0, i - 6); j < Math.Min(ins.Count, i + 12); j++)
          Console.WriteLine("      " + (j == i ? ">> " : "   ") + ins[j]);
      }
    }
  }
}

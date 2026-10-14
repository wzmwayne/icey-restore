using System; using System.Collections.Generic; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class UnlockScan {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    var keys = new string[] { "unlock", "purchase", "iap", "billing", "buy", "paid", "full", "license", "chapter", "dlc", "restore", "成就", "章节" };
    Console.WriteLine("=== A. 名称含关键词的方法（全类型扫方法名，参数 ≤2）===");
    int n = 0;
    foreach (var t in asm.MainModule.Types) {
      foreach (var m in t.Methods) {
        if (m.Parameters.Count > 2) continue;
        string nm = m.Name.ToLowerInvariant();
        if (nm.StartsWith("get_") || nm.StartsWith("set_") || nm.StartsWith("add_") || nm.StartsWith("remove_")) continue;
        if (keys.Any(k => nm.Contains(k))) { Console.WriteLine($"   {(m.IsStatic?"static ":"")}{t.FullName}.{m.Name}({string.Join(", ", m.Parameters.Select(p=>p.ParameterType.Name).ToArray())})"); if (++n > 45) break; }
      }
      if (n > 45) break;
    }
    Console.WriteLine("\n=== B. 字段/属性含关键词（疑似解锁旗标）===");
    foreach (var t in asm.MainModule.Types) {
      foreach (var f in t.Fields) { string nm = f.Name.ToLowerInvariant(); if (keys.Any(k => nm.Contains(k))) Console.WriteLine($"   F {(f.IsStatic?"static ":"")}{f.FieldType.Name,-8} {t.FullName}.{f.Name}"); }
      foreach (var p in t.Properties) { string nm = p.Name.ToLowerInvariant(); if (keys.Any(k => nm.Contains(k))) Console.WriteLine($"   P {p.PropertyType.Name,-8} {t.FullName}.{p.Name}"); }
    }
    Console.WriteLine("\n=== C. PlayerPrefs 键（从 IL 里 ldstr + PlayerPrefs 调用提取）===");
    var keysFound = new Dictionary<string, string>();
    foreach (var t in asm.MainModule.Types) {
      foreach (var m in t.Methods) {
        if (!m.HasBody) continue;
        var ins = m.Body.Instructions;
        for (int i = 0; i < ins.Count - 1; i++) {
          if (ins[i].OpCode != OpCodes.Ldstr) continue;
          var nx = ins[i + 1];
          if (nx.OpCode != OpCodes.Call && nx.OpCode != OpCodes.Callvirt) continue;
          var mr = nx.Operand as MethodReference;
          if (mr == null || mr.DeclaringType == null || mr.DeclaringType.Name != "PlayerPrefs") continue;
          string key = (string)ins[i].Operand;
          if (!keysFound.ContainsKey(key)) keysFound[key] = t.Name + "." + m.Name + " [" + mr.Name + "]";
        }
      }
    }
    foreach (var kv in keysFound) Console.WriteLine($"   {kv.Key,-40}  ← {kv.Value}");
    Console.WriteLine($"   共 {keysFound.Count} 个键");
  }
}

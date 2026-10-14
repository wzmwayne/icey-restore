using System; using System.Collections.Generic; using System.Linq; using System.Text; using Mono.Cecil;
class UnlockList {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    string[] keys = { "Unlock", "Achievement", "Chip", "AddMoney", "Recover", "Invincible", "SetHp", "AllAttribute" };
    var types = asm.MainModule.Types.ToList();
    var seen = new List<string>(); var outList = new List<string>();
    foreach (var k in keys) {
      var found = types.Where(t => (t.FullName ?? "").ToLowerInvariant().Contains(k.ToLowerInvariant())).Take(60).ToList();
      Console.WriteLine($"# 关键词 {k,-12} 命中类型 {found.Count} 个: " + string.Join(", ", found.Take(6).Select(t => t.Name).ToArray()));
      foreach (var t in found) {
        if (outList.Count >= 45) break;
        if (seen.Contains(t.FullName)) continue; seen.Add(t.FullName);
        MethodDefinition[] ms; try { ms = t.Methods.ToArray(); } catch { continue; }
        foreach (var m in ms) {
          if (outList.Count >= 45) break;
          if (m.Parameters.Count > 2) continue;
          string n = m.Name;
          if (n.StartsWith("get_")||n.StartsWith("set_")||n.StartsWith("add_")||n.StartsWith("remove_")) continue;
          bool hit = keys.Any(q => n.ToLowerInvariant().Contains(q.ToLowerInvariant()));
          if (!hit) continue;
          outList.Add((m.IsStatic?"static ":"") + t.Name + "." + n + "(" + string.Join(", ", m.Parameters.Select(p=>p.ParameterType.Name).ToArray()) + ")");
        }
      }
    }
    Console.WriteLine("\n===== 解锁页实际会列出的按钮（最多 45 条）=====");
    for (int i = 0; i < outList.Count; i++) Console.WriteLine($"  {i+1,2}. {outList[i]}");
    Console.WriteLine($"\n共 {outList.Count} 条");
  }
}

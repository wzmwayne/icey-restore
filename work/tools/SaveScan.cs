using System; using System.Collections.Generic; using System.Linq; using Mono.Cecil;
class SaveScan {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    string[] keys = { "save", "check", "point", "gate", "section", "respawn", "revive", "start", "portal", "teleport", "storage", "battlezone", "terminal", "warp", "record" };
    Console.WriteLine("=== 类型名含关键词 ===");
    foreach (var t in asm.MainModule.Types) {
      string n = (t.Name ?? "").ToLowerInvariant();
      if (keys.Any(k => n.Contains(k))) {
        var ms = t.Methods.Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")).Select(m => m.Name).Take(8);
        var fs = t.Fields.Select(f => f.Name).Take(8);
        Console.WriteLine($"  {t.FullName}  [方法: {string.Join(", ", ms.ToArray())}]  [字段: {string.Join(", ", fs.ToArray())}]");
      }
    }
    Console.WriteLine("\n=== 方法名含 savepoint/checkpoint/gate/teleport/portal ===");
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      string n = m.Name.ToLowerInvariant();
      if (n.Contains("savepoint") || n.Contains("checkpoint") || n.Contains("teleport") || n.Contains("portal") || n.Contains("warp") || n.Contains("revive") || n.Contains("respawn"))
        Console.WriteLine($"  {t.FullName}.{m.Name}({string.Join(", ", m.Parameters.Select(p=>p.ParameterType.Name).ToArray())})");
    }
  }
}

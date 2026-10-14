using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class Deep {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    string[] want = a.Skip(1).ToArray();
    foreach (var w in want) {
      foreach (var t in asm.MainModule.Types.Where(x => x.Name == w || x.FullName == w)) {
        Console.WriteLine("==== " + t.FullName + "  (base=" + t.BaseType?.Name + ")");
        foreach (var f in t.Fields) Console.WriteLine($"   F {(f.IsStatic?"static ":"")}{Pub(f)} {f.FieldType.Name,-22} {f.Name}");
        foreach (var p in t.Properties) Console.WriteLine($"   P {p.PropertyType.Name,-22} {p.Name}");
        int mc = 0;
        foreach (var m in t.Methods.Where(x => !x.Name.StartsWith("get_") && !x.Name.StartsWith("set_"))) {
          if (mc++ > 25) break;
          Console.WriteLine($"   M {(m.IsStatic?"static ":"")}{m.ReturnType.Name,-10} {m.Name}({string.Join(", ", m.Parameters.Select(x=>x.ParameterType.Name).ToArray())})");
        }
      }
    }
    Console.WriteLine("\n==== 谁在调用 TapIabHelper / TapTapAPI（内购路径）====");
    foreach (var t in asm.MainModule.Types) foreach (var m in t.Methods) {
      if (!m.HasBody) continue;
      foreach (var i in m.Body.Instructions) {
        var mr = i.Operand as MethodReference;
        if (mr == null || mr.DeclaringType == null) continue;
        string dt = mr.DeclaringType.FullName;
        if (dt.Contains("TapIab") || dt.Contains("TapTap")) {
          Console.WriteLine($"   {t.FullName}.{m.Name} 调用 {dt}.{mr.Name}");
          break;
        }
      }
    }
  }
  static string Pub(FieldDefinition f) { return f.IsPublic ? "public" : (f.IsPrivate ? "priv" : "prot"); }
}

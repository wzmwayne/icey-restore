using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class IL {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    foreach (var t in asm.MainModule.Types.Where(x => a.Skip(1).Any(w => x.Name == w || x.FullName == w))) {
      Console.WriteLine("#### " + t.FullName);
      foreach (var m in t.Methods.Where(x => x.HasBody)) {
        Console.WriteLine("  -- " + m.Name + "(" + string.Join(", ", m.Parameters.Select(p=>p.ParameterType.Name).ToArray()) + ")");
        foreach (var i in m.Body.Instructions) {
          string op = i.OpCode.ToString();
          if (op == "nop" || op == "ldarg.0" || op == "ldarg.1" || op == "stloc.0") continue;
          Console.WriteLine("       " + i);
        }
      }
    }
  }
}

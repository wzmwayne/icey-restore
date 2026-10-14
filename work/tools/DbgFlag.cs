using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class DbgFlag {
  static void Main(string[] a) {
    var asm = AssemblyDefinition.ReadAssembly(a[0]);
    foreach (var t in asm.MainModule.Types.Where(x => x.Name == "Debug" || x.Name == "Application")) {
      foreach (var m in t.Methods.Where(x => x.Name.Contains("isDebugBuild") || x.Name.Contains("isEditor") || x.Name == "get_isDebugBuild")) {
        Console.WriteLine($"#### {t.FullName}.{m.Name}  attrs={m.Attributes} impl={m.ImplAttributes} 有方法体={m.HasBody}");
        if (m.HasBody) foreach (var i in m.Body.Instructions) Console.WriteLine("     " + i);
      }
    }
  }
}

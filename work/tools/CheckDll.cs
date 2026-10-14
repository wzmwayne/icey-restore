using System; using System.Linq; using Mono.Cecil;
class CheckDll { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(a[0])));
  r.AddSearchDirectory("/home/wayne/date/icey/work/host");
  r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  Console.WriteLine(asm.Name.Name + " 类型数=" + asm.MainModule.Types.Count);
  foreach (var t in asm.MainModule.Types.Where(x => x.Name.Contains("WebBridge") || x.Name.Contains("Bootstrap") || x.Name.Contains("DebugConsole") || x.Name.Contains("ModContext")))
    Console.WriteLine("  " + t.FullName + " 方法: " + string.Join(", ", t.Methods.Select(m => m.Name).ToArray()));
  foreach (var ar in asm.MainModule.AssemblyReferences) Console.WriteLine("  引用: " + ar.Name);
} }

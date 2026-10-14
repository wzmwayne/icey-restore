using System; using System.Linq; using Mono.Cecil;
class Hooks { static void Main(string[] a) {
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
  var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
  string[] names = { "Entry", "GameSceneInit", "GameLauncher", "Launcher", "Boot", "GameBoot", "GameInit", "GameManager", "Global", "App", "Main", "Initialize", "Startup", "SRDebuggerInit", "AutoInitialize" };
  Console.WriteLine("=== 候选挂钩类型（含可挂方法）===");
  foreach (var t in asm.MainModule.Types) {
    if (Array.IndexOf(names, t.Name) < 0) continue;
    var ms = t.Methods.Where(m => m.HasBody && (m.Name == ".cctor" || m.Name == "Awake" || m.Name == "Start" || m.Name == "OnEnable" || m.Name == "OnLoad" || m.Name == "Update")).Select(m => m.Name).ToArray();
    Console.WriteLine($"  {t.FullName,-45} base={t.BaseType?.Name,-18} 方法=[{string.Join(", ", ms)}]");
  }
  Console.WriteLine("\n=== 全部 [RuntimeInitializeOnLoadMethod] ===");
  foreach (var t in asm.MainModule.Types)
    foreach (var m in t.Methods)
      if (m.CustomAttributes.Any(c => c.AttributeType.Name.Contains("RuntimeInitializeOnLoadMethod")))
        Console.WriteLine("  " + t.FullName + "::" + m.Name);
} }

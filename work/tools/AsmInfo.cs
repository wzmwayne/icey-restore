using System; using System.Linq; using Mono.Cecil;
class AsmInfo { static void Main(string[] a) {
  foreach (var p in a) {
    var asm = AssemblyDefinition.ReadAssembly(p);
    Console.WriteLine($"{System.IO.Path.GetFileName(p),-32} name={asm.Name.Name,-28} ver={asm.Name.Version} runtime={asm.MainModule.Runtime}");
    var refs = asm.MainModule.AssemblyReferences.Select(r => r.Name + " " + r.Version).Take(6);
    Console.WriteLine("     refs: " + string.Join(", ", refs.ToArray()));
  } } }

using System;
using System.Linq;
using Mono.Cecil;

class Inspect {
    static void Main(string[] args) {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        var mod = asm.MainModule;
        Console.WriteLine("程序集: " + mod.Assembly.Name.Name + "  类型数: " + mod.Types.Count);
        Console.WriteLine("\n=== [RuntimeInitializeOnLoadMethod] 方法（启动即执行，最佳挂钩）===");
        foreach (var t in mod.Types)
            foreach (var m in t.Methods)
                if (m.CustomAttributes.Any(c => c.AttributeType.Name.Contains("RuntimeInitializeOnLoadMethod")))
                    Console.WriteLine("  " + t.FullName + " :: " + m.Name);
        Console.WriteLine("\n=== 静态构造 (.cctor) 数量: " + mod.Types.SelectMany(t => t.Methods).Count(m => m.Name == ".cctor"));
        Console.WriteLine("\n=== 名称含 Entry/Boot/Init/Launch 的类型及其方法 ===");
        foreach (var t in mod.Types.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.Name, "Entry|Boot|Launch|AutoInit|GameSceneInit"))) {
            Console.WriteLine("  " + t.FullName + "  (base=" + (t.BaseType == null ? "-" : t.BaseType.FullName) + ")");
            foreach (var m in t.Methods.Take(12)) Console.WriteLine("      " + m.Name + " " + m.ReturnType.Name + " (" + string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name).ToArray()) + ")");
        }
        Console.WriteLine("\n=== 最早的 MonoBehaviour Awake/Start（前 25 个类型）===");
        int n = 0;
        foreach (var t in mod.Types) {
            if (t.BaseType == null || t.BaseType.FullName != "UnityEngine.MonoBehaviour") continue;
            var aw = t.Methods.FirstOrDefault(m => m.Name == "Awake");
            if (aw == null) continue;
            if (n++ >= 25) break;
            Console.WriteLine("  " + t.FullName + " :: Awake");
        }
    }
}

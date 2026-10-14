using System; using System.Reflection;
class StubTest2 {
  static void Main() {
    Console.WriteLine("-- 加载注入后的 Assembly-CSharp（模块 cctor 会自动执行）--");
    var asm = Assembly.LoadFrom("/home/wayne/date/icey/work/test/Assembly-CSharp.dll");
    var t = asm.GetType("IceyMod.Bootstrap");
    Console.WriteLine("桩类型: " + t);
    var m = t.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
    m.Invoke(null, null);
    Console.WriteLine("-- 显式再调一次 --");
    m.Invoke(null, null);
    Console.WriteLine("-- 无异常 --");
  }
}

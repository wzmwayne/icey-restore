using System; using System.Reflection;
class StubTest {
  static void Main() {
    var asm = Assembly.LoadFrom("/home/wayne/date/icey/work/test/Assembly-CSharp.dll");
    var t = asm.GetType("IceyMod.Bootstrap");
    Console.WriteLine("类型: " + t);
    var m = t.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
    Console.WriteLine("方法: " + m);
    m.Invoke(null, null);   // 第一次
    m.Invoke(null, null);   // 第二次（验证幂等）
    Console.WriteLine("桩执行结束，无异常抛出");
  }
}

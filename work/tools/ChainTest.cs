using System; using System.IO; using System.Reflection;
class ChainTest {
  static void Main() {
    string p = "/sdcard/ICEYMods/IceyModHost.dll";
    Console.WriteLine("宿主 DLL 存在: " + File.Exists(p));
    var asm = Assembly.Load(File.ReadAllBytes(p));
    var t = asm.GetType("IceyModHost.Bootstrap");
    Console.WriteLine("Bootstrap 类型: " + t);
    var m = t.GetMethod("Init");
    Console.WriteLine("Init 方法: " + m);
    try { m.Invoke(null, null); Console.WriteLine("宿主 Init 调用成功(非 Unity 环境下会由内部 try/catch 兜住)"); }
    catch (Exception e) { Console.WriteLine("宿主 Init 异常(链路上层已捕获): " + (e.InnerException ?? e).Message); }
    var consoleType = asm.GetType("IceyModHost.ModContext");
    Console.WriteLine("ModContext 类型: " + consoleType);
  }
}

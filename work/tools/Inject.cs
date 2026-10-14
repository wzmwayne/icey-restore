using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

/** 向 Assembly-CSharp.dll 注入 IceyMod.Bootstrap 桩，并挂到 [RuntimeInitializeOnLoadMethod] 方法开头。 */
class Inject {
    static TypeReference _exT;

    static MethodDefinition NewMethod(TypeDefinition type, TypeReference ret, string name, params TypeReference[] ps) {
        var m = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, ret);
        if (ps != null) foreach (var p in ps) m.Parameters.Add(new ParameterDefinition(p));
        type.Methods.Add(m);
        return m;
    }

    /** 追加 try { emit() } catch (Exception) { } */
    static void TryCatch(MethodDefinition m, Action<ILProcessor> emit) {
        var il = m.Body.GetILProcessor();
        var start = il.Create(OpCodes.Nop);
        il.Append(start);
        emit(il);
        var end = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Leave, end));
        var hStart = il.Create(OpCodes.Pop);
        il.Append(hStart);
        il.Append(il.Create(OpCodes.Leave, end));
        il.Append(end);
        m.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) {
            CatchType = _exT, TryStart = start, TryEnd = hStart, HandlerStart = hStart, HandlerEnd = end
        });
    }

    static int Main(string[] args) {
        if (args.Length < 2) { Console.WriteLine("用法: inject <in.dll> <out.dll>"); return 1; }
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[0])));
        string managed = "/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed";
        if (System.IO.Directory.Exists(managed)) resolver.AddSearchDirectory(managed);
        var rp = new ReaderParameters { AssemblyResolver = resolver };
        var asm = AssemblyDefinition.ReadAssembly(args[0], rp);
        var mod = asm.MainModule;
        var ts = mod.TypeSystem;
        var voidT = ts.Void; var boolT = ts.Boolean; var stringT = ts.String; var byteArr = new ArrayType(ts.Byte);
        _exT = mod.ImportReference(typeof(Exception));
        var readAllBytes = mod.ImportReference(typeof(System.IO.File).GetMethod("ReadAllBytes", new[] { typeof(string) }));
        var asmLoadBytes = mod.ImportReference(typeof(System.Reflection.Assembly).GetMethod("Load", new[] { typeof(byte[]) }));
        var asmLoadName = mod.ImportReference(typeof(System.Reflection.Assembly).GetMethod("Load", new[] { typeof(string) }));
        var getType = mod.ImportReference(typeof(Type).GetMethod("GetType", new[] { typeof(string) }));
        var getMethod = mod.ImportReference(typeof(Type).GetMethod("GetMethod", new[] { typeof(string) }));
        var invoke = mod.ImportReference(typeof(System.Reflection.MethodInfo).GetMethod("Invoke", new[] { typeof(object), typeof(object[]) }));

        var type = new TypeDefinition("IceyMod", "Bootstrap",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, ts.Object);
        mod.Types.Add(type);
        var fDone = new FieldDefinition("_done", FieldAttributes.Private | FieldAttributes.Static, boolT);
        type.Fields.Add(fDone);

        var mBytes = NewMethod(type, voidT, "LoadBytes", byteArr);
        { var il = mBytes.Body.GetILProcessor();
          il.Emit(OpCodes.Ldarg_0);
          il.Emit(OpCodes.Call, asmLoadBytes);
          EmitCallHost(il, getType, getMethod, invoke); }

        var mName = NewMethod(type, voidT, "LoadName", stringT);
        { var il = mName.Body.GetILProcessor();
          il.Emit(OpCodes.Ldarg_0);
          il.Emit(OpCodes.Call, asmLoadName);
          EmitCallHost(il, getType, getMethod, invoke); }

        var mFile = NewMethod(type, voidT, "FileTry");
        string[] paths = {
            "/sdcard/ICEYMods/IceyModHost.dll",
            "/storage/emulated/0/ICEYMods/IceyModHost.dll",
            "/sdcard/Android/data/com.fantablade.icey/files/mods/IceyModHost.dll"
        };
        foreach (var p in paths) {
            string path = p;
            TryCatch(mFile, delegate(ILProcessor il) {
                il.Emit(OpCodes.Ldstr, path);
                il.Emit(OpCodes.Call, readAllBytes);
                il.Emit(OpCodes.Call, mBytes);
            });
        }
        mFile.Body.GetILProcessor().Emit(OpCodes.Ret);

        var mAsm = NewMethod(type, voidT, "AssemblyTry");
        TryCatch(mAsm, delegate(ILProcessor il) {
            il.Emit(OpCodes.Ldstr, "IceyModHost");
            il.Emit(OpCodes.Call, mName);
        });
        mAsm.Body.GetILProcessor().Emit(OpCodes.Ret);

        var mInit = NewMethod(type, voidT, "Init");
        { var il = mInit.Body.GetILProcessor();
          var ret = il.Create(OpCodes.Ret);
          il.Emit(OpCodes.Ldsfld, fDone);
          il.Emit(OpCodes.Brtrue, ret);
          il.Emit(OpCodes.Ldc_I4_1);
          il.Emit(OpCodes.Stsfld, fDone);
          il.Emit(OpCodes.Call, mFile);
          il.Emit(OpCodes.Call, mAsm);
          il.Append(ret); }

        int hooked = 0;
        foreach (var t in mod.Types.ToArray()) {
            foreach (var m in t.Methods.ToArray()) {
                if (!m.HasBody) continue;
                bool ria = m.CustomAttributes.Any(c => c.AttributeType.Name.Contains("RuntimeInitializeOnLoadMethod"));
                if (!ria) continue;
                var ilp = m.Body.GetILProcessor();
                ilp.InsertBefore(m.Body.Instructions[0], ilp.Create(OpCodes.Call, mInit));
                hooked++;
                Console.WriteLine("  挂钩 [RuntimeInitializeOnLoadMethod] " + t.FullName + "::" + m.Name);
            }
        }
        string[] extra = { "SRDebugger.SRDebuggerInit", "SRDebugger.Internal.SRDebuggerUtil", "Entry" };
        foreach (var t in mod.Types.ToArray()) {
            if (Array.IndexOf(extra, t.FullName) < 0) continue;
            var cc = t.Methods.FirstOrDefault(mm => mm.Name == ".cctor" && mm.HasBody);
            if (cc == null) continue;
            var ilp = cc.Body.GetILProcessor();
            ilp.InsertBefore(cc.Body.Instructions[0], ilp.Create(OpCodes.Call, mInit));
            hooked++;
            Console.WriteLine("  挂钩静态构造 " + t.FullName + "::.cctor");
        }
        asm.Write(args[1], new WriterParameters());
        Console.WriteLine("完成：共挂钩 " + hooked + " 处 → " + args[1]);
        return hooked > 0 ? 0 : 2;
    }

    static void EmitCallHost(ILProcessor il, MethodReference getType, MethodReference getMethod, MethodReference invoke) {
        il.Emit(OpCodes.Ldstr, "IceyModHost.Bootstrap");
        il.Emit(OpCodes.Callvirt, getType);
        il.Emit(OpCodes.Ldstr, "Init");
        il.Emit(OpCodes.Callvirt, getMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, invoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }
}

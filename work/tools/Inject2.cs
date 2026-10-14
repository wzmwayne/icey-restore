using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

/** 把 IceyMod.Bootstrap 桩整型拷贝进目标程序集，并挂到多个启动点。 */
class Inject2 {
    static string Managed = "/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed";

    static DefaultAssemblyResolver Resolver() {
        var r = new DefaultAssemblyResolver();
        r.AddSearchDirectory(Managed);
        r.AddSearchDirectory("/home/wayne/date/icey/work/stub");
        r.AddSearchDirectory("/home/wayne/date/icey/work/host");
        return r;
    }

    static int Main(string[] args) {
        if (args.Length < 3) { Console.WriteLine("用法: inject2 <target.dll> <stub.dll> <out.dll>"); return 1; }
        var res = Resolver();
        var target = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = res });
        var stub = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = res });
        var stubType = stub.MainModule.Types.FirstOrDefault(t => t.FullName == "IceyMod.Bootstrap");
        if (stubType == null) { Console.WriteLine("桩里没有 IceyMod.Bootstrap"); return 2; }
        var tmod = target.MainModule;
        if (tmod.Types.Any(t => t.FullName == "IceyMod.Bootstrap")) { Console.WriteLine("目标里已存在桩，请用原始 DLL 重来"); return 3; }
        var newType = CopyType(tmod, stubType);
        var initRef = newType.Methods.First(m => m.Name == "Init");
        int hooks = 0;

        // 1) <Module>::.cctor —— 程序集加载即执行（最可靠）
        var moduleType = tmod.Types.First(t => t.Name == "<Module>");
        var mcctor = moduleType.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (mcctor == null) {
            mcctor = new MethodDefinition(".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                tmod.TypeSystem.Void);
            moduleType.Methods.Add(mcctor);
            var ip = mcctor.Body.GetILProcessor();
            ip.Emit(OpCodes.Call, initRef);
            ip.Emit(OpCodes.Ret);
            hooks++;
            Console.WriteLine("  挂钩 <Module>::.cctor（已新建）");
        } else {
            Insert(mcctor, initRef); hooks++;
            Console.WriteLine("  挂钩 <Module>::.cctor（已存在）");
        }

        // 2) [RuntimeInitializeOnLoadMethod]
        foreach (var t in tmod.Types.ToArray())
            foreach (var m in t.Methods.ToArray()) {
                if (!m.HasBody) continue;
                if (!m.CustomAttributes.Any(c => c.AttributeType.Name.Contains("RuntimeInitializeOnLoadMethod"))) continue;
                Insert(m, initRef); hooks++;
                Console.WriteLine("  挂钩 RIA " + t.FullName + "::" + m.Name);
            }

        // 3) 场景级与插件级入口
        string[][] wanted = new string[][] {
            new string[] { "GameSceneInit", ".cctor" }, new string[] { "GameSceneInit", "Awake" }, new string[] { "GameSceneInit", "Start" },
            new string[] { "SRDebugger.SRDebuggerInit", "Awake" }, new string[] { "SRDebugger.SRDebuggerInit", "Start" },
            new string[] { "SRDebugger.AutoInitialize", "OnLoad" }
        };
        foreach (var w in wanted) {
            var t = tmod.Types.FirstOrDefault(x => x.FullName == w[0]);
            if (t == null) continue;
            var m = t.Methods.FirstOrDefault(x => x.Name == w[1] && x.HasBody);
            if (m == null) continue;
            Insert(m, initRef); hooks++;
            Console.WriteLine("  挂钩 " + w[0] + "::" + w[1]);
        }
        target.Write(args[2], new WriterParameters());
        Console.WriteLine("完成：共 " + hooks + " 处挂钩 → " + args[2]);
        return hooks > 0 ? 0 : 4;
    }

    static void Insert(MethodDefinition m, MethodReference init) {
        var il = m.Body.GetILProcessor();
        il.InsertBefore(m.Body.Instructions[0], il.Create(OpCodes.Call, init));
    }

    static TypeDefinition CopyType(ModuleDefinition target, TypeDefinition src) {
        var nt = new TypeDefinition(src.Namespace, src.Name, src.Attributes, target.ImportReference(src.BaseType));
        target.Types.Add(nt);
        var fieldMap = new Dictionary<FieldDefinition, FieldDefinition>();
        var methodMap = new Dictionary<MethodDefinition, MethodDefinition>();
        foreach (var f in src.Fields) {
            var nf = new FieldDefinition(f.Name, f.Attributes, target.ImportReference(f.FieldType));
            nt.Fields.Add(nf); fieldMap[f] = nf;
        }
        foreach (var m in src.Methods) {
            var nm = new MethodDefinition(m.Name, m.Attributes, target.ImportReference(m.ReturnType));
            foreach (var p in m.Parameters)
                nm.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, target.ImportReference(p.ParameterType)));
            nt.Methods.Add(nm); methodMap[m] = nm;
        }
        foreach (var m in src.Methods)
            if (m.HasBody) CopyBody(target, m, methodMap[m], src, nt, methodMap, fieldMap);
        return nt;
    }

    static void CopyBody(ModuleDefinition target, MethodDefinition sm, MethodDefinition dm, TypeDefinition srcType, TypeDefinition dstType, Dictionary<MethodDefinition, MethodDefinition> mmap, Dictionary<FieldDefinition, FieldDefinition> fmap) {
        MethodBody src = sm.Body, dst = dm.Body;
        foreach (var v in src.Variables) dst.Variables.Add(new VariableDefinition(target.ImportReference(v.VariableType)));
        var map = new Dictionary<Instruction, Instruction>();
        var placeholder = Instruction.Create(OpCodes.Nop);
        foreach (var i in src.Instructions) {
            Instruction ni;
            switch (i.OpCode.OperandType) {
                case OperandType.InlineNone:
                    ni = Instruction.Create(i.OpCode); break;
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineBrTarget:
                    ni = Instruction.Create(i.OpCode, placeholder); break;
                case OperandType.InlineSwitch:
                    ni = Instruction.Create(i.OpCode, new Instruction[0]); break;
                case OperandType.InlineString:
                    ni = Instruction.Create(i.OpCode, (string)i.Operand); break;
                case OperandType.InlineVar:
                case OperandType.ShortInlineVar:
                    ni = Instruction.Create(i.OpCode, dst.Variables[src.Variables.IndexOf((VariableDefinition)i.Operand)]); break;
                case OperandType.InlineArg:
                case OperandType.ShortInlineArg:
                    ni = Instruction.Create(i.OpCode, dm.Parameters[sm.Parameters.IndexOf((ParameterDefinition)i.Operand)]); break;
                case OperandType.InlineType:
                    ni = Instruction.Create(i.OpCode, MapType(target, srcType, dstType, (TypeReference)i.Operand)); break;
                case OperandType.InlineMethod:
                    ni = Instruction.Create(i.OpCode, MapMethod(target, mmap, (MethodReference)i.Operand)); break;
                case OperandType.InlineField:
                    ni = Instruction.Create(i.OpCode, MapField(target, fmap, (FieldReference)i.Operand)); break;
                case OperandType.InlineTok:
                    if (i.Operand is TypeReference) ni = Instruction.Create(i.OpCode, MapType(target, srcType, dstType, (TypeReference)i.Operand));
                    else if (i.Operand is MethodReference) ni = Instruction.Create(i.OpCode, MapMethod(target, mmap, (MethodReference)i.Operand));
                    else if (i.Operand is FieldReference) ni = Instruction.Create(i.OpCode, MapField(target, fmap, (FieldReference)i.Operand));
                    else ni = Instruction.Create(i.OpCode); break;
                default: {
                    object o = i.Operand;
                    if (o is int) ni = Instruction.Create(i.OpCode, (int)o);
                    else if (o is long) ni = Instruction.Create(i.OpCode, (long)o);
                    else if (o is float) ni = Instruction.Create(i.OpCode, (float)o);
                    else if (o is double) ni = Instruction.Create(i.OpCode, (double)o);
                    else if (o is byte) ni = Instruction.Create(i.OpCode, (byte)o);
                    else if (o is sbyte) ni = Instruction.Create(i.OpCode, (sbyte)o);
                    else if (o is string) ni = Instruction.Create(i.OpCode, (string)o);
                    else ni = Instruction.Create(i.OpCode, (CallSite)o);
                    break;
                }
            }
            map[i] = ni;
            dst.Instructions.Add(ni);
        }
        for (int k = 0; k < src.Instructions.Count; k++) {
            var i = src.Instructions[k];
            if (i.Operand == null) continue;
            switch (i.OpCode.OperandType) {
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineBrTarget:
                    dst.Instructions[k].Operand = map[(Instruction)i.Operand]; break;
                case OperandType.InlineSwitch:
                    var arr = (Instruction[])i.Operand;
                    var narr = new Instruction[arr.Length];
                    for (int j = 0; j < arr.Length; j++) narr[j] = map[arr[j]];
                    dst.Instructions[k].Operand = narr; break;
            }
        }
        foreach (var h in src.ExceptionHandlers) {
            var nh = new ExceptionHandler(h.HandlerType);
            nh.CatchType = h.CatchType == null ? null : MapType(target, srcType, dstType, h.CatchType);
            nh.TryStart = map[h.TryStart];
            nh.TryEnd = h.TryEnd == null ? null : map[h.TryEnd];
            nh.HandlerStart = map[h.HandlerStart];
            nh.HandlerEnd = h.HandlerEnd == null ? null : map[h.HandlerEnd];
            nh.FilterStart = h.FilterStart == null ? null : map[h.FilterStart];
            dst.ExceptionHandlers.Add(nh);
        }
        dst.MaxStackSize = src.MaxStackSize;
        dst.InitLocals = src.InitLocals;
    }

    static TypeReference MapType(ModuleDefinition target, TypeDefinition srcType, TypeDefinition dstType, TypeReference tr) {
        try { var rd = tr.Resolve(); if (rd != null && rd == srcType) return dstType; } catch (Exception) { }
        return target.ImportReference(tr);
    }

    static MethodReference MapMethod(ModuleDefinition target, Dictionary<MethodDefinition, MethodDefinition> mmap, MethodReference mr) {
        try { var rd = mr.Resolve(); if (rd != null && mmap.ContainsKey(rd)) return mmap[rd]; } catch (Exception) { }
        return target.ImportReference(mr);
    }

    static FieldReference MapField(ModuleDefinition target, Dictionary<FieldDefinition, FieldDefinition> fmap, FieldReference fr) {
        try { var rd = fr.Resolve(); if (rd != null && fmap.ContainsKey(rd)) return fmap[rd]; } catch (Exception) { }
        return target.ImportReference(fr);
    }
}

#!/usr/bin/env python3
"""装配 v3 自包含包：Java WebUI 外壳 + 注入桩的 Assembly-CSharp.dll + 打包 IceyModHost.dll + 原始数据。"""
import zipfile, os, sys
A="/home/wayne/.dsh/attachments/v1/files/a6/a62e4e029a4392b69c591d174d22d5c935177711ba4f079bb50bff9905a70076/com.fantablade.icey.apk"
OBB="out/main.19.com.fantablade.icey.obb"
BASE=sys.argv[4] if len(sys.argv)>4 else "work/shell2_base.apk"          # 自建外壳清单+资源（启动项 com.icey.shell.LoaderActivity）
DEX="work/shell3_lean.dex"           # 精简 Java 集 + 外壳3 类
PATCHED=sys.argv[2] if len(sys.argv)>2 else "work/patched/Assembly-CSharp.v2.dll"
HOST="work/host/IceyModHost.dll"
OUT=sys.argv[1] if len(sys.argv)>1 else "work/v3_unaligned.apk"
za=zipfile.ZipFile(A); zb=zipfile.ZipFile(BASE); zo=zipfile.ZipFile(OBB)
w=zipfile.ZipFile(OUT,'w',zipfile.ZIP_DEFLATED)
def put(n,data,dt,comp=zipfile.ZIP_DEFLATED):
    zi=zipfile.ZipInfo(n,date_time=dt); zi.compress_type=comp; zi.external_attr=0o644<<16; w.writestr(zi,data)
for n in zb.namelist():
    if n.endswith('/'): continue
    put(n, zb.read(n), zb.getinfo(n).date_time, zipfile.ZIP_STORED if n=='resources.arsc' else zipfile.ZIP_DEFLATED)
put("classes.dex", open(DEX,'rb').read(), (2018,7,24,0,0,0))
core=0; replaced=0
for n in za.namelist():
    if n.startswith('lib/'):
        put(n, za.read(n), za.getinfo(n).date_time); core+=1
    elif n.startswith('assets/bin/Data/'):
        if n.endswith('Managed/Assembly-CSharp.dll'):
            put(n, open(PATCHED,'rb').read(), za.getinfo(n).date_time); replaced+=1
        else:
            put(n, za.read(n), za.getinfo(n).date_time); core+=1
# 宿主 DLL 作为兜底一起打包进 Managed/
put("assets/bin/Data/Managed/IceyModHost.dll", open(HOST,'rb').read(), (2018,7,24,0,0,0))
# 内置调试模块（首次启动由宿主释放到可写目录）
EMBED = len(sys.argv) > 3 and sys.argv[3] == 'embed'
if EMBED:
    put("assets/bin/Data/Managed/IceyDebugConsole.dll", open("work/console/IceyDebugConsole.dll",'rb').read(), (2018,7,24,0,0,0))
    put("assets/bin/Data/Managed/mods.txt", b"IceyDebugConsole.dll IceyDebugConsole.DebugConsole\n", (2018,7,24,0,0,0))
    html = open("work/webui/debug/index.html",'rb').read()
    put("assets/bin/Data/Managed/webui/debug/index.html", html, (2018,7,24,0,0,0))
    print("已内置: IceyDebugConsole.dll + mods.txt + webui/debug/index.html")
obb=0
for n in zo.namelist():
    if n.endswith('/'): continue
    put(n, zo.read(n), zo.getinfo(n).date_time); obb+=1
w.close()
print(f"{OUT}: {os.path.getsize(OUT)} 字节, {len(zipfile.ZipFile(OUT).namelist())} 条目 (核心 {core} / 替换 {replaced} / OBB {obb})")

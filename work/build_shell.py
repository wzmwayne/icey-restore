#!/usr/bin/env python3
"""装配自建加载器外壳 APK。用法: build_shell.py merged|core <output.apk>"""
import zipfile, os, sys
mode=sys.argv[1]; OUT=sys.argv[2]
A="/home/wayne/.dsh/attachments/v1/files/a6/a62e4e029a4392b69c591d174d22d5c935177711ba4f079bb50bff9905a70076/com.fantablade.icey.apk"
OBB="out/main.19.com.fantablade.icey.obb"; BASE="work/shell_base.apk"; DEX="work/classes_shell.dex"
za=zipfile.ZipFile(A); zb=zipfile.ZipFile(BASE)
w=zipfile.ZipFile(OUT,'w',zipfile.ZIP_DEFLATED)
def put(n, data, dt, comp=zipfile.ZIP_DEFLATED):
    zi=zipfile.ZipInfo(n, date_time=dt); zi.compress_type=comp; zi.external_attr=0o644<<16
    w.writestr(zi, data)
for n in zb.namelist():
    if n.endswith('/'): continue
    put(n, zb.read(n), zb.getinfo(n).date_time, zipfile.ZIP_STORED if n=='resources.arsc' else zipfile.ZIP_DEFLATED)
put("classes.dex", open(DEX,'rb').read(), (2018,7,24,0,0,0))
cnt=0
for n in za.namelist():
    if n.startswith('lib/') or n.startswith('assets/bin/Data/'):
        i=za.getinfo(n); put(n, za.read(n), i.date_time); cnt+=1
if mode=='merged':
    zo=zipfile.ZipFile(OBB); o=0
    for n in zo.namelist():
        if n.endswith('/'): continue
        put(n, zo.read(n), zo.getinfo(n).date_time); o+=1
    print(f"核心 {cnt} 条 + OBB {o} 条")
else:
    print(f"核心 {cnt} 条（依赖外部 OBB）")
w.close()
print(OUT, os.path.getsize(OUT), "bytes,", len(zipfile.ZipFile(OUT).namelist()), "entries")

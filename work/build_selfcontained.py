#!/usr/bin/env python3
"""装配自包含 v1.1.1：自建外壳(含 mod 接口, 精简 Java 集) + 原始 APK 核心 + 原始 OBB 内容。"""
import zipfile, os, sys
A="/home/wayne/.dsh/attachments/v1/files/a6/a62e4e029a4392b69c591d174d22d5c935177711ba4f079bb50bff9905a70076/com.fantablade.icey.apk"
OBB="out/main.19.com.fantablade.icey.obb"
BASE="work/shell2_base.apk"; DEX="work/classes_lean.dex"
OUT=sys.argv[1] if len(sys.argv)>1 else "work/selfcontained_unaligned.apk"
za=zipfile.ZipFile(A); zb=zipfile.ZipFile(BASE); zo=zipfile.ZipFile(OBB)
w=zipfile.ZipFile(OUT,'w',zipfile.ZIP_DEFLATED)
def put(n,data,dt,comp=zipfile.ZIP_DEFLATED):
    zi=zipfile.ZipInfo(n,date_time=dt); zi.compress_type=comp; zi.external_attr=0o644<<16; w.writestr(zi,data)
for n in zb.namelist():
    if n.endswith('/'): continue
    put(n, zb.read(n), zb.getinfo(n).date_time, zipfile.ZIP_STORED if n=='resources.arsc' else zipfile.ZIP_DEFLATED)
put("classes.dex", open(DEX,'rb').read(), (2018,7,24,0,0,0))
c=0
for n in za.namelist():
    if n.startswith('lib/') or n.startswith('assets/bin/Data/'): put(n, za.read(n), za.getinfo(n).date_time); c+=1
o=0
for n in zo.namelist():
    if n.endswith('/'): continue
    put(n, zo.read(n), zo.getinfo(n).date_time); o+=1
w.close()
print(f"{OUT}: {os.path.getsize(OUT)} bytes, {len(zipfile.ZipFile(OUT).namelist())} entries (核心 {c} + OBB {o})")

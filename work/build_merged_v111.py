#!/usr/bin/env python3
"""生成 v1.1.1 整合单文件包：APK 核心 + 官方 OBB 内容全部并入 assets/（形态同葫芦侠整合包，已验证可运行）。"""
import zipfile, os
A="/home/wayne/.dsh/attachments/v1/files/a6/a62e4e029a4392b69c591d174d22d5c935177711ba4f079bb50bff9905a70076/com.fantablade.icey.apk"
OBB="out/main.19.com.fantablade.icey.obb"; BASE="work/A_base3.apk"; DEX="work/classes.dex"
OUT="work/merged_unaligned.apk"
za=zipfile.ZipFile(A); zb=zipfile.ZipFile(BASE); zo=zipfile.ZipFile(OBB)
w=zipfile.ZipFile(OUT,'w',zipfile.ZIP_DEFLATED)
def put(name, data, dt):
    zi=zipfile.ZipInfo(name, date_time=dt); zi.compress_type=zipfile.ZIP_DEFLATED; zi.external_attr=0o644<<16
    w.writestr(zi, data)
for n in zb.namelist():
    if n.endswith('/'): continue
    zi=zipfile.ZipInfo(n, date_time=zb.getinfo(n).date_time)
    zi.compress_type=zipfile.ZIP_STORED if n=='resources.arsc' else zipfile.ZIP_DEFLATED
    zi.external_attr=0o644<<16
    w.writestr(zi, zb.read(n))
put("classes.dex", open(DEX,'rb').read(), (2018,7,24,0,0,0))
cnt=0
for n in za.namelist():
    if n.startswith('lib/') or n.startswith('assets/bin/Data/'):
        i=za.getinfo(n); put(n, za.read(n), i.date_time); cnt+=1
print(f"APK 核心负载 {cnt} 条")
cnt=0
for n in zo.namelist():
    if n.endswith('/'): continue
    put(n, zo.read(n), zo.getinfo(n).date_time); cnt+=1
print(f"OBB 负载 {cnt} 条")
w.close()
print(OUT, os.path.getsize(OUT), "bytes,", len(zipfile.ZipFile(OUT).namelist()), "entries")

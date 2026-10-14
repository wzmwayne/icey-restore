#!/usr/bin/env python3
"""从 PlayMods 重打包版重建 v1.1.1 原貌 APK（移除注入 SDK / 内嵌 OBB 副本，还原启动 Activity）。"""
import zipfile, os, sys
A="/home/wayne/.dsh/attachments/v1/files/a6/a62e4e029a4392b69c591d174d22d5c935177711ba4f079bb50bff9905a70076/com.fantablade.icey.apk"
BASE="work/A_base3.apk"      # 由 aapt2(shim) link 生成的 清单+资源
DEX="work/classes.dex"       # 由修补后的 smali 汇编
OUT=sys.argv[1] if len(sys.argv)>1 else "work/final_unaligned.apk"
za=zipfile.ZipFile(A); zb=zipfile.ZipFile(BASE)
carry=[n for n in za.namelist() if n.startswith('lib/') or n.startswith('assets/bin/Data/')]
zo=zipfile.ZipFile(OUT,'w',zipfile.ZIP_DEFLATED)
for n in zb.namelist():
    if n.endswith('/'): continue
    zi=zipfile.ZipInfo(n, date_time=zb.getinfo(n).date_time)
    zi.compress_type=zipfile.ZIP_STORED if n=='resources.arsc' else zipfile.ZIP_DEFLATED
    zi.external_attr=0o644<<16
    zo.writestr(zi, zb.read(n))
zi=zipfile.ZipInfo("classes.dex", date_time=(2018,7,24,0,0,0)); zi.compress_type=zipfile.ZIP_DEFLATED; zi.external_attr=0o644<<16
zo.writestr(zi, open(DEX,'rb').read())
for n in carry:
    i=za.getinfo(n)
    zi=zipfile.ZipInfo(n, date_time=i.date_time); zi.compress_type=zipfile.ZIP_DEFLATED
    zi.external_attr=(i.external_attr & 0xFFFF0000) | 0o644
    zo.writestr(zi, za.read(n))
zo.close()
print(f"{OUT}: {os.path.getsize(OUT)} bytes, {len(zipfile.ZipFile(OUT).namelist())} entries")

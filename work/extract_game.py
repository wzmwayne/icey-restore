#!/usr/bin/env python3
"""提取两个版本的原始游戏负载；把官方 .splitN 分块还原为逻辑文件，并校验 Unity 序列化头。"""
import zipfile, os, re, struct, collections, shutil, io

A="/home/wayne/.dsh/attachments/v1/files/a6/a62e4e029a4392b69c591d174d22d5c935177711ba4f079bb50bff9905a70076/com.fantablade.icey.apk"
B="/home/wayne/.dsh/attachments/v1/files/79/79ea816a89058d85ea0eb696c17d816ae5eba1234af45a8d722743ef98592058/icey.apk"
OBB="out/main.19.com.fantablade.icey.obb"
EX="out/extracted"

SPLIT=re.compile(r'^(?P<base>.+)\.split(?P<idx>\d+)$')

def logical_map(names):
    """把条目名集合折叠成 逻辑名 -> [分段条目(按序)] 或 [单一]"""
    groups=collections.defaultdict(list)
    for n in names:
        m=SPLIT.match(n)
        if m: groups[m.group('base')].append((int(m.group('idx')), n))
        else: groups[n].append((0, n))
    return {k: [n for _, n in sorted(v)] for k, v in groups.items()}

def unity_check(path):
    """校验 Unity 序列化文件头：前 4 字节=元数据大小(大端)，8..12=文件总大小(大端)"""
    try:
        with open(path,'rb') as f: h=f.read(20)
        if len(h)<20: return None
        meta=struct.unpack('>I',h[0:4])[0]; size=struct.unpack('>I',h[4:8])[0]
        ver=h[16:20].decode('ascii','replace')
        actual=os.path.getsize(path)
        return (meta,size,actual,ver)
    except Exception as e:
        return None

def emit(zips, out_root, label):
    """zips: [(zipfile, prefix)] 按顺序取条目；写出原样文件 + 合并/还原后的逻辑文件"""
    names=[]; seen=set()
    for z,pref in zips:
        for n in z.namelist():
            if n.endswith('/') or not n.startswith(pref): continue
            if n in seen: continue
            seen.add(n); names.append(n)
    raw_dir=os.path.join(out_root,"shipped"); log_dir=os.path.join(out_root,"unity_data")
    # 1) 原样（保留分块）
    for n in names:
        for z,pref in zips:
            try: data=z.read(n); break
            except KeyError: continue
        p=os.path.join(raw_dir,n); os.makedirs(os.path.dirname(p),exist_ok=True)
        open(p,'wb').write(data)
    # 2) 逻辑视图（分块串接）
    lm=logical_map(names)
    ok=bad=na=0; details=[]
    for base,parts in lm.items():
        buf=io.BytesIO()
        for p in parts:
            for z,pref in zips:
                try: buf.write(z.read(p)); break
                except KeyError: continue
        p=os.path.join(log_dir,base); os.makedirs(os.path.dirname(p),exist_ok=True)
        open(p,'wb').write(buf.getvalue())
        if base.startswith('assets/bin/Data/') and not base.endswith('.resource') and not base.endswith('.dex'):
            r=unity_check(p)
            if r:
                if r[1]==r[2] or r[1]==0: ok+=1
                else: bad+=1; details.append((base,r[1],r[2],r[3]))
            else: na+=1
    return dict(label=label, files=len(names), logical=len(lm), joined=sum(1 for b,p in lm.items() if len(p)>1),
                unity_ok=ok, unity_size_mismatch=bad, not_serialized=na, mismatch_sample=details[:5])

if __name__=='__main__':
    os.makedirs(EX,exist_ok=True)
    za=zipfile.ZipFile(A); zo=zipfile.ZipFile(OBB); zb=zipfile.ZipFile(B)
    print("[v1.1.1] 合并 APK 核心 + OBB …")
    r1=emit([(za,'assets/bin/Data/'),(zo,'assets/bin/Data/')], f"{EX}/v1.1.1", "v1.1.1")
    # APK 侧额外负载
    for n in za.namelist():
        if n.startswith('lib/') or n=='classes.dex':
            p=os.path.join(EX,"v1.1.1","apk",n); os.makedirs(os.path.dirname(p),exist_ok=True); open(p,'wb').write(za.read(n))
    print("  ",r1)
    print("[v1.0.5] 从整合包拆出数据 …")
    r2=emit([(zb,'assets/')], f"{EX}/v1.0.5", "v1.0.5")
    for n in zb.namelist():
        if n.startswith('lib/') or n=='classes.dex':
            p=os.path.join(EX,"v1.0.5","apk",n); os.makedirs(os.path.dirname(p),exist_ok=True); open(p,'wb').write(zb.read(n))
    print("  ",r2)

#!/usr/bin/env python3
"""按名称模式从 Data 目录抽取 Spine 资源（.skel 二进制 / .atlas 文本 / 图集 PNG）"""
import UnityPy, os, re, sys, collections

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "../../out/extracted/v1.1.1/unity_data/assets/bin/Data")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../web/assets/spine")

def raw_text(o, d):
    s = getattr(d, 'm_Script', None)
    if isinstance(s, bytes): return s, False
    if isinstance(s, str): return s.encode('utf-8', 'surrogateescape'), False
    return b'', False

def main(pats, outdir=OUT):
    os.makedirs(outdir, exist_ok=True)
    want = re.compile('|'.join(pats), re.I)
    got = collections.Counter()
    for f in sorted(os.listdir(DATA)):
        p = os.path.join(DATA, f)
        if os.path.isdir(p) or os.path.getsize(p) < 256: continue
        try: head = open(p, 'rb').read(400000)
        except Exception: continue
        if not want.search(head.decode('utf8', 'ignore')): continue
        try: env = UnityPy.load(p)
        except Exception: continue
        for o in env.objects:
            if o.type.name not in ('TextAsset', 'Texture2D'): continue
            try:
                d = o.read(); nm = getattr(d, 'm_Name', '') or ''
            except Exception: continue
            if not want.search(nm): continue
            if o.type.name == 'TextAsset':
                b, _ = raw_text(o, d)
                if not b: continue
                fn = os.path.join(outdir, nm)
                open(fn, 'wb').write(b); got['text'] += 1
                print(f"   TextAsset {nm}  {len(b)} B → {fn}  头: {b[:16]!r}")
            else:
                try:
                    img = d.image
                    if img is None: continue
                    fn = os.path.join(outdir, nm.replace('/', '_') + '.png'); img.save(fn)
                    got['tex'] += 1
                    print(f"   Texture2D {nm}  {img.size} → {fn}")
                except Exception as e: pass
    print("   合计:", dict(got))

if __name__ == '__main__':
    main(sys.argv[1:] or ['NewPlayer'])

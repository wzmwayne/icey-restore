#!/usr/bin/env python3
"""从 NGUI UIAtlas 的序列化数据里按名字裁切精灵（UISpriteData: name + 12 int）。
用法: python3 export_ngui_atlas.py Joystick_circle Joystick_bg Button_01 ..."""
import UnityPy, os, re, sys, struct, json

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../out/extracted/v1.1.1/unity_data/assets/bin/Data")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../web/assets/ui")

def main(want):
    os.makedirs(OUT, exist_ok=True)
    want = set(want)
    got = {}
    files = [f for f in sorted(os.listdir(DATA)) if not f.endswith('.dll')]
    for f in files:
        p = os.path.join(DATA, f)
        if os.path.isdir(p) or os.path.getsize(p) < 256: continue
        try: raw = open(p, 'rb').read()
        except Exception: continue
        hits = [w for w in want if w.encode() in raw]
        if not hits: continue
        try: env = UnityPy.load(p)
        except Exception: continue
        # 同文件的贴图（取最大者当图集）
        tex = None
        for o in env.objects:
            if o.type.name != 'Texture2D': continue
            try:
                d = o.read(); img = d.image
                if img is not None and (tex is None or img.size[0]*img.size[1] > tex[1].size[0]*tex[1].size[1]):
                    tex = (getattr(d, 'm_Name', '') or f, img)
            except Exception: pass
        if tex is None: continue
        img = tex[1]; W, H = img.size
        for w in hits:
            if w in got: continue
            b = w.encode()
            i = raw.find(b); ok = False
            while i >= 0 and not ok:
                n = struct.unpack_from('<i', raw, i - 4)[0] if i >= 4 else -1
                if n == len(b):                                   # 前面是长度=名字长度 → 命中真正名字字段
                    off = i + len(b)
                    off += (-off) % 4                             # 4 字节对齐
                    try:
                        x, y, ww, hh = struct.unpack_from('<4i', raw, off)
                        if 0 <= x and 0 <= y and 0 < ww <= W and 0 < hh <= H and x + ww <= W and y + hh <= H:
                            crop = img.crop((x, H - (y + hh), x + ww, H - y))   # NGUI y 自下而上
                            fn = re.sub(r'[^A-Za-z0-9_.-]', '_', w) + '.png'
                            crop.save(os.path.join(OUT, fn))
                            got[w] = {'file': fn, 'size': list(crop.size), 'rect': [x, y, ww, hh], 'atlas': tex[0]}
                            ok = True
                    except Exception: pass
                i = raw.find(b, i + 1)
        if len(got) == len(want): break
    for k, v in got.items(): print(f"   ✓ {k:22s} {v['size']} ← 图集 {v['atlas']} rect={v['rect']}")
    print(f"   得到 {len(got)}/{len(want)}")
    if got:
        old = {}
        jf = os.path.join(OUT, 'ui.json')
        if os.path.exists(jf):
            try: old = json.load(open(jf, encoding='utf-8'))
            except Exception: old = {}
        old.update(got); json.dump(old, open(jf, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

if __name__ == '__main__':
    main(sys.argv[1:])

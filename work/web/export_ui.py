#!/usr/bin/env python3
"""按名称抽取 Sprite（裁切后的 PNG）与元数据，用于还原原作 UI/操作组件。"""
import UnityPy, os, re, sys, json, collections

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../out/extracted/v1.1.1/unity_data/assets/bin/Data")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../web/assets/ui")

def main(pats, outdir=OUT):
    os.makedirs(outdir, exist_ok=True)
    rx = re.compile('|'.join(pats))
    meta, got = {}, collections.Counter()
    for f in sorted(os.listdir(DATA)):
        p = os.path.join(DATA, f)
        if os.path.isdir(p) or os.path.getsize(p) < 256 or f.endswith('.dll'): continue
        try: head = open(p, 'rb').read(400000).decode('utf8', 'ignore')
        except Exception: continue
        if not rx.search(head): continue
        try: env = UnityPy.load(p)
        except Exception: continue
        for o in env.objects:
            if o.type.name != 'Sprite': continue
            try:
                d = o.read(); nm = getattr(d, 'm_Name', '') or ''
            except Exception: continue
            if not rx.search(nm) or nm in meta: continue
            try:
                img = d.image
                if img is None: continue
                fn = re.sub(r'[^A-Za-z0-9_.-]', '_', nm) + '.png'
                img.save(os.path.join(outdir, fn))
                r = getattr(d, 'm_Rect', None)
                meta[nm] = {'file': fn, 'size': list(img.size),
                            'rect': [getattr(r, 'x', 0), getattr(r, 'y', 0), getattr(r, 'width', 0), getattr(r, 'height', 0)]}
                got['sprites'] += 1
                print(f"   {nm:24s} {img.size} → {fn}")
            except Exception: pass
        if got['sprites'] > 200: break
    if meta:
        fn = os.path.join(outdir, 'ui.json')
        json.dump(meta, open(fn, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        print(f"   共 {got['sprites']} 个精灵 → {fn}")

if __name__ == '__main__':
    main(sys.argv[1:] or ['Joystick_bg', 'Joystick_circle', 'Button_0', 'Button_bg', 'CrossBtn'])

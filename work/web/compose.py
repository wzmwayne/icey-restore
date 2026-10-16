#!/usr/bin/env python3
"""把 shot.js 记录的绘制调用用 PIL 合成为 PNG（无头截图）"""
import json, os, sys
from PIL import Image
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../web/")
d = json.load(open('/tmp/drawlist.json'))
W, H = d['W'], d['H']
img = Image.new('RGBA', (W, H), (11, 11, 14, 255))
cache = {}
def tex(name):
    if name in cache: return cache[name]
    p = os.path.join(ROOT, name.replace('scenes/', 'scenes/')) if name else None
    if not p or not os.path.exists(p): cache[name] = None; return None
    cache[name] = Image.open(p).convert('RGBA'); return cache[name]
def apply_m(m, x, y):
    return (m['a']*x + m['c']*y + m['e'], m['b']*x + m['d']*y + m['f'])
for dr in d['draws']:
    m = dr['m']
    if dr['op'] == 'fill':
        x, y = apply_m(m, dr['x'], dr['y'])
        x2, y2 = apply_m(m, dr['x']+dr['w'], dr['y']+dr['h'])
        col = dr['color'] if isinstance(dr['color'], str) and dr['color'].startswith('#') else '#000000'
        rgb = tuple(int(col[i:i+2], 16) for i in (1, 3, 5))
        a = int(255 * (dr['alpha'] if dr.get('alpha') is not None else 1))
        layer = Image.new('RGBA', (max(1,int(abs(x2-x))), max(1,int(abs(y2-y)))), rgb + (a,))
        img.alpha_composite(layer, (int(min(x, x2)), int(min(y, y2))))
        continue
    t = tex(dr['img'])
    if t is None: continue
    sx, sy, sw, sh = int(dr['sx']), int(dr['sy']), int(dr['sw']), int(dr['sh'])
    sw = max(1, min(sw, t.width - sx)); sh = max(1, min(sh, t.height - sy))
    crop = t.crop((sx, sy, sx+sw, sy+sh))
    dw, dh = max(1, int(dr['dw'])), max(1, int(dr['dh']))
    flip = dw < 0 or dh < 0
    crop = crop.resize((abs(dw), abs(dh)), Image.NEAREST)
    if flip: crop = crop.transpose(Image.FLIP_LEFT_RIGHT)
    if abs(sw) == 0: continue
    # 目标位置：drawImage 的 dx,dy 已是左上角
    x, y = apply_m(m, dr['dx'], dr['dy'])
    # 旋转变换（仅当矩阵含旋转）
    rot = 0.0
    import math
    rot = math.degrees(math.atan2(m['b'], m['a']))
    if abs(rot) > 0.01: crop = crop.rotate(-rot, expand=True, resample=Image.BICUBIC)
    if dr.get('alpha') is not None and dr['alpha'] < 1:
        a = crop.split()[3].point(lambda v: int(v * dr['alpha'])); crop.putalpha(a)
    img.alpha_composite(crop, (int(x), int(y)))
out = sys.argv[1] if len(sys.argv) > 1 else '/tmp/shot.png'
img.convert('RGB').save(out, quality=92)
print(f"  截图已生成: {out}  ({os.path.getsize(out)//1024} KB)  绘制 {len(d['draws'])} 次，对象 {d['objects']}，相机 {d['camera']}")

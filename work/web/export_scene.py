#!/usr/bin/env python3
# 导出某个关卡场景为网页可用格式：精灵 PNG + 布局 JSON（供 web/viewer.js 直接渲染）
# 用法: python3 export_scene.py <输出目录> <关卡索引> [更多索引...]
import UnityPy, os, json, sys, collections

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "../../out/extracted/v1.1.1/unity_data/assets/bin/Data")

def export(outdir, idx, name=None):
    name = name or ("level%d" % idx)
    lvdir = os.path.join(outdir, name)
    texdir = os.path.join(outdir, "tex")
    os.makedirs(lvdir, exist_ok=True); os.makedirs(texdir, exist_ok=True)
    p = os.path.join(DATA, "level%d" % idx)
    if not os.path.exists(p):
        print("  缺文件", p); return
    env = UnityPy.load(p)
    objs = list(env.objects)
    # 1) 收集容器：GameObject / Transform 层级
    go = {}          # pathId -> (name, active)
    tr = {}          # pathId -> {go, father, pos, scale, rot}
    for o in objs:
        if o.type.name == 'GameObject':
            d = o.read()
            go[o.path_id] = (getattr(d, 'm_Name', '') or '', bool(getattr(d, 'm_IsActive', True)))
        elif o.type.name in ('Transform', 'RectTransform'):
            d = o.read()
            lp = getattr(d, 'm_LocalPosition', None)
            ls = getattr(d, 'm_LocalScale', None)
            lr = getattr(d, 'm_LocalRotation', None)
            tr[o.path_id] = {
                'go': getattr(getattr(d, 'm_GameObject', None), 'path_id', 0),
                'father': getattr(getattr(d, 'm_Father', None), 'path_id', 0),
                'pos': [round(getattr(lp, 'x', 0.0), 4), round(getattr(lp, 'y', 0.0), 4), round(getattr(lp, 'z', 0.0), 4)],
                'scale': [round(getattr(ls, 'x', 1.0), 4), round(getattr(ls, 'y', 1.0), 4), round(getattr(ls, 'z', 1.0), 4)],
                'rot': [round(getattr(lr, 'x', 0.0), 5), round(getattr(lr, 'y', 0.0), 5),
                        round(getattr(lr, 'z', 0.0), 5), round(getattr(lr, 'w', 1.0), 5)],
            }
    # 2) 精灵渲染器
    sprites, atlas = [], {}
    for o in objs:
        if o.type.name != 'SpriteRenderer':
            continue
        try:
            d = o.read()
        except Exception:
            continue
        spr_pp = getattr(d, 'm_Sprite', None)
        tex_name, pw, ph, rect, pivot, off = None, 0, 0, None, None, None
        if spr_pp is not None:
            try:
                spr = spr_pp.read()
                r = getattr(spr, 'm_Rect', None)
                rect = [round(getattr(r, 'x', 0.0), 2), round(getattr(r, 'y', 0.0), 2),
                        round(getattr(r, 'width', 0.0), 2), round(getattr(r, 'height', 0.0), 2)] if r is not None else None
                pw = round(float(getattr(spr, 'm_PixelsToUnits', 100.0) or 100.0), 3)
                pv = getattr(spr, 'm_Pivot', None)
                if pv is not None:
                    pivot = [round(float(getattr(pv, 'x', 0.5)), 4), round(float(getattr(pv, 'y', 0.5)), 4)]
                of = getattr(spr, 'm_Offset', None)
                if of is not None:
                    off = [round(float(getattr(of, 'x', 0.0)), 4), round(float(getattr(of, 'y', 0.0)), 4)]
                tpp = getattr(getattr(spr, 'm_RD', None), 'texture', None)
                if tpp is not None:
                    tex = tpp.read()
                    tex_name = getattr(tex, 'm_Name', None)
                    if tex_name and tex_name not in atlas:
                        fn = "tex_%s.png" % tex_name.replace('/', '_')
                        dst = os.path.join(texdir, fn)
                        if not os.path.exists(dst):
                            img = tex.image
                            if img is not None:
                                img.save(dst)
                        if os.path.exists(dst):
                            atlas[tex_name] = "../tex/" + fn
            except Exception:
                pass
        gid = getattr(getattr(d, 'm_GameObject', None), 'path_id', 0)
        sr = {
            'go': gid,
            'name': go.get(gid, ('', True))[0],
            'active': go.get(gid, ('', True))[1],
            'tex': atlas.get(tex_name),
            'rect': rect, 'ppu': pw, 'pivot': pivot, 'offset': off,
            'order': int(getattr(d, 'm_SortingOrder', 0) or 0),
            'layer': int(getattr(d, 'm_SortingLayerID', 0) or 0),
            'color': [round(float(getattr(getattr(d, 'm_Color', None), k, 1.0)), 3) for k in ('r', 'g', 'b', 'a')],
            'flipX': bool(getattr(d, 'm_FlipX', False)), 'flipY': bool(getattr(d, 'm_FlipY', False)),
        }
        sprites.append(sr)
    # 3) 碰撞盒
    colliders = []
    for o in objs:
        if o.type.name != 'BoxCollider2D':
            continue
        try:
            d = o.read()
            sz = getattr(d, 'm_Size', None); off = getattr(d, 'm_Offset', None)
            colliders.append({'go': getattr(getattr(d, 'm_GameObject', None), 'path_id', 0),
                              'size': [round(getattr(sz, 'x', 0.0), 3), round(getattr(sz, 'y', 0.0), 3)],
                              'offset': [round(getattr(off, 'x', 0.0), 3), round(getattr(off, 'y', 0.0), 3)],
                              'isTrigger': bool(getattr(d, 'm_IsTrigger', False)),
                              'enabled': bool(getattr(d, 'm_Enabled', True))})
        except Exception:
            pass
    # 4) 相机
    cams = []
    for o in objs:
        if o.type.name != 'Camera':
            continue
        try:
            d = o.read()
            cams.append({'go': getattr(getattr(d, 'm_GameObject', None), 'path_id', 0),
                         'ortho': bool(getattr(d, 'orthographic', True)),
                         'size': round(float(getattr(d, 'orthographic size', 5.0) or 5.0), 3),
                         'bg': [round(float(getattr(getattr(d, 'm_BackGroundColor', None), k, 0.0)), 3) for k in ('r', 'g', 'b', 'a')],
                         'depth': round(float(getattr(d, 'm_Depth', 0.0) or 0.0), 2)})
        except Exception:
            pass
    trs = [{'id': k, **v} for k, v in tr.items()]
    out = {'level': idx, 'sprites': sprites, 'transforms': trs, 'colliders': colliders,
           'cameras': cams, 'textures': atlas}
    out['name'] = name
    fn = os.path.join(lvdir, "scene.json")
    with open(fn, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False)
    print("  level%d → %s：精灵 %d、贴图 %d、碰撞盒 %d、相机 %d、%.0f KB"
          % (idx, fn, len(sprites), len(atlas), len(colliders), len(cams), os.path.getsize(fn) / 1024))

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else "web/scenes"
    names = {"13": "C1L1S1", "18": "C1L1S2", "19": "C1L1S3", "20": "C1L1S4", "21": "C1L1S5",
             "2": "TEST_C0L1S1_t", "6": "TEST_C1L1S1_t"}
    for a in sys.argv[2:]:
        export(out, int(a), names.get(a))

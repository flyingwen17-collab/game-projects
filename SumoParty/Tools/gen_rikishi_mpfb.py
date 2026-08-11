# MPFB 真人體力士生成（真五官、真人體拓樸）
# 手臂刻意砍掉：遊戲的手臂是程式 IK 即時驅動（推/拉/抓廻し），模型自帶的
# T-pose 手臂會跟 IK 手臂重疊。臉和身體用真人體網格，手臂維持程式生成。
#
#   blender.exe -b --python gen_rikishi_mpfb.py
# 成功才覆蓋 E:/Tools/rikishi_base.fbx（三角面數在合理範圍才算成功）

import bpy, importlib

bpy.ops.wm.read_factory_settings(use_empty=True)

hs = importlib.import_module("bl_ext.blender_org.mpfb.services.humanservice")
ts = importlib.import_module("bl_ext.blender_org.mpfb.services.targetservice")
HumanService = hs.HumanService
TargetService = ts.TargetService

# ---------- 體型：幕內力士 ----------
macro = TargetService.get_default_macro_info_dict()
print("[mpfb] 預設 macro keys:", list(macro.keys()))
def set_if(k, v):
    if k in macro: macro[k] = v
set_if("gender", 0.95)
set_if("age", 0.45)
set_if("muscle", 0.62)
set_if("weight", 1.0)        # 體重拉滿
set_if("height", 0.55)
set_if("proportions", 0.5)
if "race" in macro and isinstance(macro["race"], dict):
    for k in macro["race"]: macro["race"][k] = 0.0
    if "asian" in macro["race"]: macro["race"]["asian"] = 1.0

body = HumanService.create_human(macro_detail_dict=macro)
body.name = "Body"
print("[mpfb] 人體生成:", body.name, "verts:", len(body.data.vertices))

# ---------- 清理：刪 helper 幾何與手臂 ----------
groups = [g.name for g in body.vertex_groups]
print("[mpfb] vertex groups:", ", ".join(groups))

DROP = ("helper", "joint", "fingernail")
drop_idx = [g.index for g in body.vertex_groups
            if any(k in g.name.lower() for k in DROP)]
print("[mpfb] 要刪的群組數:", len(drop_idx))

bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='DESELECT')
bpy.ops.object.mode_set(mode='OBJECT')
for v in body.data.vertices:
    v.select = any(g.group in drop_idx for g in v.groups)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.delete(type='VERT')
bpy.ops.object.mode_set(mode='OBJECT')
print("[mpfb] 刪 helper 後 verts:", len(body.data.vertices))

# ---------- 砍手臂（幾何切除；基網格沒有手臂頂點群組，只能用座標判斷） ----------
# 遊戲的手臂由程式 IK 即時驅動，模型的 A-pose 手臂會重疊成四隻手。
zsA = [v.co.z for v in body.data.vertices]
minZA, maxZA = min(zsA), max(zsA)
HA = maxZA - minZA
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='DESELECT')
bpy.ops.object.mode_set(mode='OBJECT')
armCut = 0
for v in body.data.vertices:
    zr = (v.co.z - minZA) / HA
    ax = abs(v.co.x)
    # 肩帶以上、軀幹寬度以外＝上臂；更外側往下延伸＝前臂與手（A-pose 垂到臀高）
    if (ax > 0.33 and zr > 0.50) or (ax > 0.40 and zr > 0.28):
        v.select = True
        armCut += 1
print("[mpfb] 砍手臂頂點:", armCut)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.delete(type='VERT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.fill_holes(sides=0)          # 切口補面
bpy.ops.object.mode_set(mode='OBJECT')
print("[mpfb] 砍手臂後 verts:", len(body.data.vertices))

# shape keys（morph targets）擋住所有修改器；把當前體型烘死進網格
if body.data.shape_keys:
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    print("[mpfb] shape keys 已烘入網格")

# 面數壓到單力士預算（≤15k 三角面）
tri0 = sum(max(0, len(p.vertices) - 2) for p in body.data.polygons)
if tri0 > 15000:
    dec = body.modifiers.new("dec", "DECIMATE")
    dec.ratio = 14000.0 / tri0
    bpy.ops.object.modifier_apply(modifier="dec")
bpy.ops.object.shade_smooth()

# 移除 mask modifier（helper 已實際刪除）
for m in list(body.modifiers):
    body.modifiers.remove(m)

# ---------- 量測（廻し/髮髻/眼睛的定位基準；永不假設模型比例） ----------
xs = [v.co.x for v in body.data.vertices]
ys = [v.co.y for v in body.data.vertices]
zs = [v.co.z for v in body.data.vertices]
minZ, maxZ = min(zs), max(zs)
H = maxZ - minZ
print(f"[mpfb] 高度 {H:.3f}  z範圍 {minZ:.3f}~{maxZ:.3f}")

hipZ = minZ + H * 0.47
hipXs = [abs(v.co.x) for v in body.data.vertices if abs(v.co.z - hipZ) < H * 0.03]
hipR = (max(hipXs) if hipXs else 0.3) + 0.055
headZ = minZ + H * 0.94

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.75
    return m

# ---------- 廻し ----------
bpy.ops.mesh.primitive_torus_add(major_radius=hipR, minor_radius=0.075,
                                 location=(0, 0.01, hipZ), major_segments=36, minor_segments=12)
maw = bpy.context.active_object
maw.name = "Mawashi"
maw.scale = (1, 0.95, 0.75)
bpy.ops.object.transform_apply(scale=True)
bpy.ops.mesh.primitive_cube_add(location=(0, -hipR + 0.02, hipZ - 0.18))
apron = bpy.context.active_object
apron.scale = (0.10, 0.025, 0.15)
bpy.ops.object.transform_apply(scale=True)
apron.select_set(True); maw.select_set(True)
bpy.context.view_layer.objects.active = maw
bpy.ops.object.join()

# ---------- 大銀杏髮髻 ----------
bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=(0, 0.01, headZ + 0.02),
                                     segments=16, ring_count=8)
hair = bpy.context.active_object
hair.name = "Hair"
hair.scale = (0.095, 0.11, 0.045)
bpy.ops.object.transform_apply(scale=True)

# ---------- 眼睛（helper 眼球已刪，眼窩是空的；放深色眼珠做對比） ----------
eyeZ = minZ + H * 0.883
front = [v.co.y for v in body.data.vertices if abs(v.co.z - eyeZ) < 0.03 and abs(v.co.x) < 0.09]
frontY = (min(front) if front else -0.12) + 0.006
eyes = []
for sx in (1, -1):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=(0.052 * sx * H / 1.67, frontY, eyeZ),
                                         segments=12, ring_count=8)
    e = bpy.context.active_object
    e.scale = (0.016, 0.010, 0.013)
    bpy.ops.object.transform_apply(scale=True)
    eyes.append(e)
for o in bpy.data.objects: o.select_set(False)
for o in eyes: o.select_set(True)
bpy.context.view_layer.objects.active = eyes[0]
bpy.ops.object.join()
eyeObj = bpy.context.active_object
eyeObj.name = "Eyes"

# ---------- 材質 ----------
skin = mat("M_Skin", (0.72, 0.53, 0.40))
if body.data.materials: body.data.materials[0] = skin
else: body.data.materials.append(skin)
maw.data.materials.append(mat("M_Mawashi", (0.18, 0.22, 0.45)))
hair_m = mat("M_Hair", (0.05, 0.04, 0.04))
hair.data.materials.append(hair_m)
eyeObj.data.materials.append(hair_m)

# ---------- 驗證後才匯出（防止壞模型蓋掉可用版本） ----------
tri = sum(max(0, len(p.vertices) - 2) for p in body.data.polygons)
print(f"[mpfb] Body 三角面 {tri}")
if 4000 <= tri <= 40000 and H > 1.2:
    out = "E:/Tools/rikishi_base.fbx"
    bpy.ops.export_scene.fbx(filepath=out, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_selection=False)
    print(f"[mpfb] OK → {out}")
else:
    print("[mpfb] FAIL 三角面或高度不合理，不覆蓋既有 FBX")

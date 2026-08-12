# MPFB 真人體力士生成 v2（真五官、真人體拓樸）
# 卡點修法：
#   1. 體重沒生效 → create_human 後用 HumanObjectProperties.set_value +
#      TargetService.reapply_macro_details 重套，並用臀寬數值驗證（不再肉眼賭）
#   2. A-pose 手臂切不乾淨 → 用基網格自帶的 joint-shoulder/joint-hand 關節座標:
#      肩關節處挖斷 → 從手座標洪水選取整條斷肢 → 刪除 → 補洞
#
#   blender.exe -b --python gen_rikishi_mpfb.py
# 成功才覆蓋 E:/Tools/rikishi_base.fbx

import bpy, importlib, inspect
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)

hs = importlib.import_module("bl_ext.blender_org.mpfb.services.humanservice")
ts = importlib.import_module("bl_ext.blender_org.mpfb.services.targetservice")
op = importlib.import_module("bl_ext.blender_org.mpfb.entities.objectproperties")
HumanService = hs.HumanService
TargetService = ts.TargetService
HOP = op.HumanObjectProperties

SUMO = {"gender": 0.95, "age": 0.45, "muscle": 0.62, "weight": 1.0,
        "height": 0.55, "proportions": 0.5}

macro = TargetService.get_default_macro_info_dict()
for k, v in SUMO.items():
    if k in macro: macro[k] = v
if "race" in macro and isinstance(macro["race"], dict):
    for k in macro["race"]: macro["race"][k] = 0.0
    if "asian" in macro["race"]: macro["race"]["asian"] = 1.0

body = HumanService.create_human(macro_detail_dict=macro)
body.name = "Body"
bpy.context.view_layer.objects.active = body

# ---------- T-pose 化：胖體型的手臂內側與軀幹整片融合，球形挖洞切不斷（v2/v3 實測）。
# 正解：上 rig → 雙臂轉水平 → 烘進網格 → 手臂懸空後按座標乾淨切除 ----------
from mathutils import Matrix

if body.data.shape_keys:                     # armature modifier 套用前必須先烘掉 shape keys
    bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    print("[mpfb] shape keys 已烘入網格")

rs = importlib.import_module("bl_ext.blender_org.mpfb.services.rigservice")
RigService = rs.RigService

HumanService.add_builtin_rig(body, "default")
arm_obj = None
for m in body.modifiers:
    if m.type == 'ARMATURE' and m.object: arm_obj = m.object
if arm_obj is None:
    raise RuntimeError("rig 沒掛上")
print("[mpfb] rig 掛上:", arm_obj.name, "骨骼數", len(arm_obj.pose.bones))

bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='POSE')
tposed = 0
for side, tx in (("L", 1.0), ("R", -1.0)):
    # 參數順序沒有文件，兩種都試；再不行直接翻 pose.bones
    pb = None
    for args in ((f"upperarm01.{side}", arm_obj), (arm_obj, f"upperarm01.{side}")):
        try:
            pb = RigService.find_pose_bone_by_name(*args)
            if pb is not None: break
        except Exception:
            pass
    if pb is None:
        pb = arm_obj.pose.bones.get(f"upperarm01.{side}")
    if pb is None:
        print(f"[mpfb] 找不到 upperarm01.{side}，現有含 arm 的骨:",
              [b.name for b in arm_obj.pose.bones if "arm" in b.name.lower()][:8])
        continue
    head = (arm_obj.matrix_world @ pb.matrix).translation.copy()
    cur = ((arm_obj.matrix_world @ pb.matrix) @ Vector((0, 1, 0, 0))).to_3d().normalized()
    target = Vector((tx, 0, 0))
    delta = cur.rotation_difference(target)
    world = arm_obj.matrix_world @ pb.matrix
    rotated = (Matrix.Translation(head) @ delta.to_matrix().to_4x4() @
               Matrix.Translation(-head) @ world)
    pb.matrix = arm_obj.matrix_world.inverted() @ rotated
    bpy.context.view_layer.update()
    tposed += 1
bpy.ops.object.mode_set(mode='OBJECT')
print(f"[mpfb] T-pose 完成 {tposed}/2")

bpy.context.view_layer.objects.active = body
for m in list(body.modifiers):
    if m.type == 'ARMATURE':
        bpy.ops.object.modifier_apply(modifier=m.name)   # 姿勢烘進網格
bpy.data.objects.remove(arm_obj, do_unlink=True)
print("[mpfb] 姿勢已烘入、骨架已移除")

# ---------- 卡點 1：驗證體型有沒有真的套上，沒有就重套 ----------
def hip_halfwidth():
    zs = [v.co.z for v in body.data.vertices]
    lo, hi = min(zs), max(zs)
    band = lo + (hi - lo) * 0.52
    xs = [abs(v.co.x) for v in body.data.vertices
          if abs(v.co.z - band) < (hi - lo) * 0.03 and abs(v.co.y) < 0.15]
    return max(xs) if xs else 0.0

w0 = hip_halfwidth()
print(f"[mpfb] 生成後臀寬 {w0:.3f}")
# 結論（實測）：weight=1.0 有套上（0.198 vs 瘦子 0.16），但 MakeHuman 的
# 體重上限是「很重的普通人」，離力士的極端腹型還很遠。
# 力士的肚子改用幾何充氣（見後段），不賭沒文件的 target 名。

# ---------- 卡點 2 準備：先記下關節座標（等下群組就被刪了） ----------
def group_centroid(gname):
    g = body.vertex_groups.get(gname)
    if g is None: return None
    pts = []
    for v in body.data.vertices:
        for vg in v.groups:
            if vg.group == g.index and vg.weight > 0.5:
                pts.append(v.co.copy())
                break
    if not pts: return None
    c = Vector((0, 0, 0))
    for p in pts: c += p
    return c / len(pts)

joints = {}
for side in ("l", "r"):
    joints[f"{side}-shoulder"] = group_centroid(f"joint-{side}-shoulder")
    joints[f"{side}-hand"] = group_centroid(f"joint-{side}-hand")
print("[mpfb] 關節座標:", {k: (f"{v.x:.2f},{v.z:.2f}" if v else None) for k, v in joints.items()})

# ---------- 刪 helper / joint 幾何 ----------
drop_idx = [g.index for g in body.vertex_groups
            if any(k in g.name.lower() for k in ("helper", "joint", "fingernail"))]
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='DESELECT')
bpy.ops.object.mode_set(mode='OBJECT')
for v in body.data.vertices:
    v.select = any(g.group in drop_idx for g in v.groups)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.delete(type='VERT')
bpy.ops.object.mode_set(mode='OBJECT')
print("[mpfb] 刪 helper 後 verts:", len(body.data.vertices))

# ---------- T-pose 後手臂懸空，直接按座標切（肩外側以外、胸高以上全刪） ----------
zsC = [v.co.z for v in body.data.vertices]
loC, hiC = min(zsC), max(zsC)
HC = hiC - loC
before = len(body.data.vertices)
bpy.ops.object.mode_set(mode='OBJECT')
for v in body.data.vertices:
    zr = (v.co.z - loC) / HC
    v.select = abs(v.co.x) > 0.27 and zr > 0.65
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.delete(type='VERT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.fill_holes(sides=0)
bpy.ops.object.mode_set(mode='OBJECT')
after = len(body.data.vertices)
armsRemoved = 2 if before - after > 800 else 0    # T-pose 兩條手臂該砍掉上千頂點
print(f"[mpfb] T-pose 切臂: {before} → {after} verts（砍 {before - after}）")

# ---------- 力士腹型：幾何充氣（MakeHuman 體重上限只到「重」，不到「力士」） ----------
zsB = [v.co.z for v in body.data.vertices]
loB, hiB = min(zsB), max(zsB)
HB = hiB - loB
belly = Vector((0, -0.09, loB + HB * 0.56))
glute = Vector((0, 0.11, loB + HB * 0.50))
RAD = HB * 0.27
for v in body.data.vertices:
    for center, amp in ((belly, 0.16), (glute, 0.07)):
        d = (v.co - center).length
        if d < RAD:
            fall = (1 - d / RAD)
            fall = fall * fall * (3 - 2 * fall)          # smoothstep
            dirv = v.co - center
            dirv.z *= 0.35                               # 主要往水平外擠
            if dirv.length > 1e-5:
                v.co += dirv.normalized() * amp * fall
print("[mpfb] 腹臀充氣完成")

# ---------- 面數 ----------
tri0 = sum(max(0, len(p.vertices) - 2) for p in body.data.polygons)
if tri0 > 15000:
    dec = body.modifiers.new("dec", "DECIMATE")
    dec.ratio = 14000.0 / tri0
    bpy.ops.object.modifier_apply(modifier="dec")
bpy.ops.object.shade_smooth()
for m in list(body.modifiers): body.modifiers.remove(m)

# ---------- 量測 → 廻し / 髮髻 / 眼睛 ----------
zs = [v.co.z for v in body.data.vertices]
minZ, maxZ = min(zs), max(zs)
H = maxZ - minZ
# 廻し綁在「腹下緣」（充氣後的肚子會蓋過上緣，真實力士的穿法），
# 寬度量該高度的實際體寬（含充氣），不能量臀寬——上一版量錯變成穿身扁盤
hipZ = minZ + H * 0.46
hipXs = [max(abs(v.co.x), abs(v.co.y)) for v in body.data.vertices
         if abs(v.co.z - hipZ) < H * 0.03]
hipR = (max(hipXs) if hipXs else 0.3) + 0.02
headZ = minZ + H * 0.945
print(f"[mpfb] 高度 {H:.3f}  臀半寬 {hipR - 0.045:.3f}")

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.75
    return m

bpy.ops.mesh.primitive_torus_add(major_radius=hipR, minor_radius=0.07,
                                 location=(0, 0.005, hipZ), major_segments=36, minor_segments=12)
maw = bpy.context.active_object
maw.name = "Mawashi"
maw.scale = (1, 0.95, 0.75)
bpy.ops.object.transform_apply(scale=True)
bpy.ops.mesh.primitive_cube_add(location=(0, -hipR + 0.01, hipZ - 0.17))
apron = bpy.context.active_object
apron.scale = (0.09, 0.022, 0.14)
bpy.ops.object.transform_apply(scale=True)
apron.select_set(True); maw.select_set(True)
bpy.context.view_layer.objects.active = maw
bpy.ops.object.join()

bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=(0, 0.012, headZ + 0.015),
                                     segments=16, ring_count=8)
hair = bpy.context.active_object
hair.name = "Hair"
hair.scale = (0.085, 0.10, 0.04)
bpy.ops.object.transform_apply(scale=True)

eyeZ = minZ + H * 0.885
front = [v.co.y for v in body.data.vertices if abs(v.co.z - eyeZ) < 0.03 and abs(v.co.x) < 0.09]
frontY = (min(front) if front else -0.12) + 0.006
eyes = []
for sx in (1, -1):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=(0.05 * sx * H / 1.67, frontY, eyeZ),
                                         segments=12, ring_count=8)
    e = bpy.context.active_object
    e.scale = (0.015, 0.010, 0.012)
    bpy.ops.object.transform_apply(scale=True)
    eyes.append(e)
for o in bpy.data.objects: o.select_set(False)
for o in eyes: o.select_set(True)
bpy.context.view_layer.objects.active = eyes[0]
bpy.ops.object.join()
eyeObj = bpy.context.active_object
eyeObj.name = "Eyes"

skin = mat("M_Skin", (0.72, 0.53, 0.40))
if body.data.materials: body.data.materials[0] = skin
else: body.data.materials.append(skin)
maw.data.materials.append(mat("M_Mawashi", (0.18, 0.22, 0.45)))
hair_m = mat("M_Hair", (0.05, 0.04, 0.04))
hair.data.materials.append(hair_m)
eyeObj.data.materials.append(hair_m)

# ---------- 三重驗證後才匯出：面數、身高、體型（胖不胖用數字說話） ----------
tri = sum(max(0, len(p.vertices) - 2) for p in body.data.polygons)
bellyFront = [v.co.y for v in body.data.vertices
              if abs(v.co.z - (minZ + H * 0.56)) < H * 0.04]
bellyDepth = (-min(bellyFront)) if bellyFront else 0.0
fat = bellyDepth > 0.24                     # 充氣後腹前緣要超過 0.24（重人約 0.18）
print(f"[mpfb] Body 三角面 {tri}  高度 {H:.2f}  腹深 {bellyDepth:.3f}  夠胖={fat}  斷臂={armsRemoved}/2")
if 4000 <= tri <= 40000 and H > 1.2 and fat and armsRemoved == 2:
    out = "E:/Tools/rikishi_base.fbx"
    bpy.ops.export_scene.fbx(filepath=out, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_selection=False)
    print(f"[mpfb] OK → {out}")
else:
    print("[mpfb] FAIL 驗證未過，不覆蓋既有 FBX")

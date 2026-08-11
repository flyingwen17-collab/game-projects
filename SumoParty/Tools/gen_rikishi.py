# 力士模型生成（流程 MD §4.7 Blender headless）
# 寫實「體型」：幕內力士比例——低重心、大腹、粗腿、溜肩。
# 手臂不在這裡做：Unity 端用三節式 IK 手臂（RikishiArms），才能即時抓廻し/推掌。
#
#   blender.exe -b --python gen_rikishi.py
# 輸出 E:/Tools/rikishi_base.fbx（Body / Mawashi / Hair 三個物件，Unity 端依名稱換材質）

import bpy, math

bpy.ops.wm.read_factory_settings(use_empty=True)

def sphere(name, loc, scale, seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1, segments=seg, ring_count=seg//2, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(scale=True)
    return o

def cyl(name, loc, r, depth):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=depth, location=loc, vertices=20)
    o = bpy.context.active_object
    o.name = name
    return o

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.75
    return m

# ---------- 身體：球堆 + remesh + 拉普拉斯平滑（Z-up、面朝 -Y、腳底 z=0） ----------
# 註：metaball 實測翻車（等值面參數把身體縮到 212 面），回到已驗證的路線。
# 疊球感用 SMOOTH modifier 熔掉——remesh 只抹表面，平滑才會把球界融成連續的脂肪體。
# 目標剪影：大關級大噸位——腹部垂墜蓋過廻し、胸垂、腰側肉、臀、雙下巴、分開的粗腿
parts = []
parts.append(sphere("belly",    (0, -0.02, 0.95), (0.50, 0.50, 0.44)))
parts.append(sphere("bellyLow", (0, -0.20, 0.86), (0.35, 0.31, 0.30)))
parts.append(sphere("pelvis",   (0, 0.02, 0.72),  (0.44, 0.40, 0.30)))
parts.append(sphere("gluteL",   (0.17, 0.30, 0.80),  (0.20, 0.19, 0.21)))
parts.append(sphere("gluteR",   (-0.17, 0.30, 0.80), (0.20, 0.19, 0.21)))
parts.append(sphere("chest",    (0, -0.03, 1.30), (0.44, 0.38, 0.28)))
parts.append(sphere("pecL",     (0.17, -0.25, 1.21),  (0.145, 0.11, 0.12)))
parts.append(sphere("pecR",     (-0.17, -0.25, 1.21), (0.145, 0.11, 0.12)))
parts.append(sphere("flankL",   (0.44, 0.02, 0.96),  (0.14, 0.17, 0.19)))
parts.append(sphere("flankR",   (-0.44, 0.02, 0.96), (0.14, 0.17, 0.19)))
parts.append(sphere("shoL",     (0.37, 0, 1.44),  (0.17, 0.16, 0.14)))
parts.append(sphere("shoR",     (-0.37, 0, 1.44), (0.17, 0.16, 0.14)))
parts.append(cyl("neck",        (0, -0.02, 1.55), 0.15, 0.14))
parts.append(sphere("chin",     (0, -0.13, 1.58), (0.12, 0.10, 0.08)))
parts.append(sphere("head",     (0, -0.02, 1.72), (0.18, 0.19, 0.20)))   # 加大：要撐得過平滑
for sx in (1, -1):
    # 腿加粗、站距加寬：平滑會收縮細長特徵，腿太近會被熔成一支圓錐
    parts.append(sphere(f"thigh{sx}", (0.24*sx, 0, 0.50), (0.22, 0.23, 0.30)))
    parts.append(cyl(f"calf{sx}",     (0.24*sx, 0, 0.22), 0.155, 0.42))
    parts.append(sphere(f"foot{sx}",  (0.24*sx, -0.09, 0.05), (0.16, 0.26, 0.08)))

for o in bpy.data.objects: o.select_set(False)
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
body = bpy.context.active_object
body.name = "Body"

body.data.remesh_voxel_size = 0.030
bpy.ops.object.voxel_remesh()

sm = body.modifiers.new("smooth", "SMOOTH")   # 熔掉球界，變成連續的肉
sm.iterations = 12                            # 35 次會把腿熔成圓錐、頭縮進肩膀
sm.factor = 1.0
bpy.ops.object.modifier_apply(modifier="smooth")
bpy.ops.object.shade_smooth()

# 面數壓到行動裝置預算（目標約 7000 面）
faces = len(body.data.polygons)
dec = body.modifiers.new("dec", "DECIMATE")
dec.ratio = min(1.0, 7000.0 / max(1, faces))
bpy.ops.object.modifier_apply(modifier="dec")

# ---------- 廻し（不 remesh，保持布帶的俐落邊緣） ----------
bpy.ops.mesh.primitive_torus_add(major_radius=0.445, minor_radius=0.105, location=(0, 0.0, 0.80),
                                 major_segments=36, minor_segments=12)
maw = bpy.context.active_object
maw.name = "Mawashi"
maw.scale = (1, 0.95, 0.62)          # 綁得低，讓腹部垂墜蓋過上緣（真實力士的剪影）
bpy.ops.object.transform_apply(scale=True)

bpy.ops.mesh.primitive_cube_add(location=(0, -0.48, 0.58))               # 前垂
apron = bpy.context.active_object
apron.scale = (0.13, 0.03, 0.18)
bpy.ops.object.transform_apply(scale=True)
apron.select_set(True); maw.select_set(True)
bpy.context.view_layer.objects.active = maw
bpy.ops.object.join()
bpy.ops.object.shade_smooth()

# ---------- 大銀杏髮髻 ----------
bpy.ops.mesh.primitive_uv_sphere_add(radius=1, location=(0, -0.02, 1.815), segments=16, ring_count=8)
hair = bpy.context.active_object
hair.name = "Hair"
hair.scale = (0.125, 0.14, 0.055)
bpy.ops.object.transform_apply(scale=True)
bpy.ops.mesh.primitive_cube_add(location=(0, -0.10, 1.845))              # 前折的髻
knot = bpy.context.active_object
knot.scale = (0.035, 0.075, 0.022)
bpy.ops.object.transform_apply(scale=True)
knot.select_set(True); hair.select_set(True)
bpy.context.view_layer.objects.active = hair
bpy.ops.object.join()
bpy.ops.object.shade_smooth()

# 材質（Unity 端會依物件名稱換成 URP 材質，這裡只是佔位）
body.data.materials.append(mat("M_Skin", (0.72, 0.53, 0.40)))
maw.data.materials.append(mat("M_Mawashi", (0.18, 0.22, 0.45)))
hair.data.materials.append(mat("M_Hair", (0.05, 0.04, 0.04)))

# ---------- 匯出 ----------
out = "E:/Tools/rikishi_base.fbx"
bpy.ops.export_scene.fbx(filepath=out, axis_forward='-Z', axis_up='Y',
                         bake_space_transform=True, use_selection=False)

tri = sum(len(p.vertices) - 2 for p in body.data.polygons)
print(f"[gen_rikishi] OK  Body 三角面約 {tri}  → {out}")

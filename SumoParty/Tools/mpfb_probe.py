# MPFB API 探勘（先看清楚再寫生成腳本，不猜方法名）
import bpy, inspect, importlib

# 擴充平台安裝的模組路徑是 bl_ext.blender_org.mpfb，不是 mpfb
try:
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mpfb")
    bpy.ops.wm.save_userpref()
    print("[probe] addon enabled + prefs saved")
except Exception as e:
    print("[probe] enable:", e)

hs = importlib.import_module("bl_ext.blender_org.mpfb.services.humanservice")
HS = hs.HumanService
print("[probe] create_human:", inspect.signature(HS.create_human))
for n in dir(HS):
    if not n.startswith("_") and callable(getattr(HS, n)):
        try:
            print("  HS.%s%s" % (n, inspect.signature(getattr(HS, n))))
        except Exception:
            print("  HS.%s(?)" % n)

try:
    ts = importlib.import_module("bl_ext.blender_org.mpfb.services.targetservice")
    names = [n for n in dir(ts.TargetService) if not n.startswith("_")]
    print("[probe] TargetService:", ", ".join(names[:40]))
except Exception as e:
    print("[probe] targetservice:", e)

try:
    op = importlib.import_module("bl_ext.blender_org.mpfb.entities.objectproperties")
    print("[probe] objectproperties 內容:", [n for n in dir(op) if not n.startswith("_")][:20])
except Exception as e:
    print("[probe] objectproperties:", e)

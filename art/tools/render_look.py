"""Render models side by side under a street-like day or night light, to judge the surface look.

Usage:
  blender -b --factory-startup --python art/tools/render_look.py -- <scene.json> <out.png>

scene.json:
  {"light": "day" | "night", "view": "front" | "high", "width": 1600, "height": 900,
   "items": [{"mesh": "x.fbx" | "x.obj", "objects": ["Name_Body", ...] (optional filter),
              "bc": "T_BC.png", "m": "T_M.png" (optional), "n": "T_N.png" (optional),
              "smoothness": 0.5, "metallic": 0.0 (used without a mask map, as HDRP/Lit does),
              "label": "ours", "rotate_z": 0, "gap": 0.3}]}
Items stand on a pavement plane in a row along X at real scale. Without a mask map an item renders like
an HDRP/Lit material with only a base colour map: one smoothness and metallic for the whole surface.
The mask's AO is applied at half strength to the base colour, standing in for HDRP's indirect occlusion; the
mask is decoded as sRGB, as Unity samples it (art/README.md, "Surface look").
"""
import bpy, json, math, os, sys
from mathutils import Vector

cfg_path, out_path = sys.argv[sys.argv.index("--") + 1:][:2]
CFG = json.load(open(cfg_path))
BASE = os.path.dirname(os.path.abspath(cfg_path))


def p(path):
    return path if os.path.isabs(path) else os.path.join(BASE, path)


def material(item, i):
    ma = bpy.data.materials.new(f"Look_{i}")
    ma.use_nodes = True
    nt = ma.node_tree
    bsdf = nt.nodes["Principled BSDF"]

    def tex(path, colour):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(p(path), check_existing=False)
        n.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        n.image.alpha_mode = "CHANNEL_PACKED"
        return n
    bc = tex(item["bc"], True)
    if item.get("m"):
        m = tex(item["m"], True)   # decoded as sRGB, as Unity samples the game's masks and ours (only G changes)
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(m.outputs["Color"], sep.inputs[0])
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 0.5
        nt.links.new(bc.outputs["Color"], mix.inputs["A"])
        nt.links.new(sep.outputs["Green"], mix.inputs["B"])
        nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
        rem = nt.nodes.new("ShaderNodeMapRange")   # HDRP smoothness remap
        rem.inputs["To Min"].default_value = item.get("smooth_min", 0.0)
        rem.inputs["To Max"].default_value = item.get("smooth_max", 1.0)
        nt.links.new(m.outputs["Alpha"], rem.inputs["Value"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(rem.outputs["Result"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    else:
        nt.links.new(bc.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 1 - item.get("smoothness", 0.5)
        bsdf.inputs["Metallic"].default_value = item.get("metallic", 0.0)
    if item.get("n"):
        n = tex(item["n"], False)
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(n.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return ma


def load(item, i):
    before = set(bpy.data.objects)
    path = p(item["mesh"])
    if path.endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    keep = []
    for o in new:
        name = o.name.lower()
        bad = o.type != "MESH" or "collision" in name or name.startswith("colmesh")
        if item.get("objects") and o.name.split(".")[0] not in item["objects"]:
            bad = True
        if bad:
            bpy.data.objects.remove(o, do_unlink=True)
        else:
            keep.append(o)
    ma = material(item, i)
    for o in keep:
        o.data.materials.clear()
        o.data.materials.append(ma)
        if item.get("smooth_all"):   # meshes from the game: their normals are part of the look
            pass
    return keep


def bounds(obs):
    pts = [o.matrix_world @ Vector(c) for o in obs for c in o.bound_box]
    lo = Vector((min(v.x for v in pts), min(v.y for v in pts), min(v.z for v in pts)))
    hi = Vector((max(v.x for v in pts), max(v.y for v in pts), max(v.z for v in pts)))
    return lo, hi


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
x = 0.0
labels = []
all_obs = []
for i, item in enumerate(CFG["items"]):
    obs = load(item, i)
    if not obs:
        raise SystemExit(f"no meshes in {item['mesh']}")
    pivot = bpy.data.objects.new(f"Pivot_{i}", None)
    sc.collection.objects.link(pivot)
    for o in obs:
        o.parent = pivot
    pivot.rotation_euler.z = math.radians(item.get("rotate_z", 0))
    pivot.scale = [item.get("scale", 1.0)] * 3
    bpy.context.view_layer.update()
    lo, hi = bounds(obs)
    pivot.location = Vector((x - lo.x, -(lo.y + hi.y) / 2, -lo.z))
    bpy.context.view_layer.update()
    lo, hi = bounds(obs)
    x = hi.x + item.get("gap", 0.25)
    all_obs += obs

lo, hi = bounds(all_obs)
centre = (lo + hi) / 2
span = max(hi.x - lo.x, (hi.z - lo.z) * CFG.get("width", 1600) / CFG.get("height", 900))

# Pavement
bpy.ops.mesh.primitive_plane_add(size=200, location=(centre.x, 0, 0))
ground = bpy.context.object
gm = bpy.data.materials.new("Pavement")
gm.use_nodes = True
gb = gm.node_tree.nodes["Principled BSDF"]
gb.inputs["Base Color"].default_value = (0.12, 0.12, 0.115, 1)
gb.inputs["Roughness"].default_value = 0.85
ground.data.materials.append(gm)

# Camera: three-quarter view from the front (-Y), slightly above, as a player on foot sees it.
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.lens = 50
elev = math.radians(CFG.get("elevation", 32 if CFG.get("view", "front") == "high" else 18))
yaw = math.radians(CFG.get("yaw", -22))
dist = span * 1.12 / (2 * math.tan(cam.data.angle / 2)) + (hi.y - lo.y)
direction = Vector((math.sin(yaw) * math.cos(elev), -math.cos(yaw) * math.cos(elev), math.sin(elev)))
target = Vector((centre.x, centre.y, (hi.z - lo.z) * 0.45))
cam.location = target + direction * dist
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()

world = bpy.data.worlds.new("World")
sc.world = world
world.use_nodes = True
bg = world.node_tree.nodes["Background"]
if CFG.get("light", "day") == "day":
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    try:
        sky.sky_type = "NISHITA"
    except TypeError:
        pass
    sky.sun_elevation = math.radians(38)
    sky.sun_rotation = math.radians(210)
    world.node_tree.links.new(sky.outputs[0], bg.inputs[0])
    bg.inputs[1].default_value = 0.35
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(52), 0, math.radians(210))
    sc.collection.objects.link(sun)
    sc.view_settings.exposure = -1.6
else:
    bg.inputs[0].default_value = (0.010, 0.014, 0.030, 1)
    bg.inputs[1].default_value = 1.0
    for k, (lx, ly) in enumerate([(lo.x - 0.5, -2.0), (hi.x + 0.5, -1.5)]):
        lamp = bpy.data.objects.new(f"Street{k}", bpy.data.lights.new(f"Street{k}", "POINT"))
        lamp.data.energy = 120 * max(1.0, span / 2) ** 2
        lamp.data.color = (1.0, 0.78, 0.52)
        lamp.data.shadow_soft_size = 0.3
        lamp.location = (lx, ly, max(2.5, (hi.z - lo.z) * 1.5))
        sc.collection.objects.link(lamp)
    sc.view_settings.exposure = 0.0

sc.render.engine = "CYCLES"
sc.cycles.samples = CFG.get("samples", 96)
sc.cycles.use_denoising = True
sc.view_settings.view_transform = "AgX"
sc.render.resolution_x = CFG.get("width", 1600)
sc.render.resolution_y = CFG.get("height", 900)
sc.render.filepath = os.path.abspath(out_path)
bpy.ops.render.render(write_still=True)
print("RENDERED", out_path)

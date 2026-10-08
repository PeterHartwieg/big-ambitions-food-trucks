"""Render a model from four fixed angles under a fixed light, for review.

Usage:
  blender -b <model>.blend --python art/tools/render_views.py -- <out_dir> [open]

Front is -Y, left is +X (art/README.md). With "open", the lid or hatch is
rotated open first, by the sign recorded in score.json. Doesn't save the .blend.
"""
import bpy, json, math, os, sys
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
out_dir = args[0]
want_open = len(args) > 1 and args[1] == "open"
os.makedirs(out_dir, exist_ok=True)
scene = bpy.context.scene
folder = os.path.dirname(bpy.data.filepath)

for ob in list(bpy.data.objects):
    if ob.type in {"CAMERA", "LIGHT"}:
        bpy.data.objects.remove(ob, do_unlink=True)
    elif ob.name.startswith("HumanRef") or ob.name.endswith("_Collision"):
        ob.hide_render = True

if want_open:
    spec = json.load(open(os.path.join(folder, "spec.json")))
    score = json.load(open(os.path.join(folder, "score.json")))
    for name, cfg in spec["objects"].items():
        if "hinge" in cfg and name in bpy.data.objects:
            sign = score["blend"]["per_object"][name]["opens_with_rotation_sign"]
            ob = bpy.data.objects[name]
            pivot = ob.matrix_world.translation.copy()
            rot = Matrix.Rotation(math.radians(cfg["hinge"]["open_deg"]) * sign, 4, cfg["hinge"]["axis"])
            ob.matrix_world = Matrix.Translation(pivot) @ rot @ Matrix.Translation(-pivot) @ ob.matrix_world

visible = [o for o in bpy.data.objects if o.type == "MESH" and not o.hide_render]
pts = [o.matrix_world @ Vector(c) for o in visible for c in o.bound_box]
mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
size = (mx - mn).length
target = (mn + mx) / 2

world = bpy.data.worlds.new("ReviewWorld")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.8, 0.82, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.4
scene.world = world


def light(name, energy, direction):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    ob = bpy.data.objects.new(name, data)
    ob.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(ob)


light("Key", 3.0, (0.5, 0.6, -0.8))
light("Fill", 0.8, (-0.6, 0.3, -0.5))
light("Rim", 1.2, (0.0, -0.8, -0.4))

bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
floor = bpy.context.object
mat = bpy.data.materials.new("ReviewFloor")
mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.5, 0.5, 0.52, 1)
floor.data.materials.append(mat)

cam = bpy.data.objects.new("ReviewCam", bpy.data.cameras.new("ReviewCam"))
cam.data.lens = 50
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1200, 900
scene.view_settings.view_transform = "AgX"

# Azimuth measured from the front (-Y) towards the model's right (-X).
views = {"a-front-right": (40, 18), "b-right-side": (90, 6), "c-rear-left": (215, 22), "d-front-left-high": (-35, 45)}
for name, (az, el) in views.items():
    a, e = math.radians(az), math.radians(el)
    direction = Vector((-math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = target + direction * size * 1.55
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out_dir, f"{name}{'-open' if want_open else ''}.png")
    bpy.ops.render.render(write_still=True)
print("RENDERED", out_dir)

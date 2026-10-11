"""Render a 512 x 512 item icon with a transparent background, in the style of the game's item icons.

Usage:
  blender -b --factory-startup --python art/tools/render_icon.py -- <model>.fbx <T_..._BC>.png <out>.png

Renders the FBX with its surface maps (T_<Name>_BC, and the _M mask and _N normal map next to it when present),
so the icon shows what the game shows: the <Name>_Body mesh only, seen from the front and a little to its left,
from above, filling the frame as the game's icons do (512 px, about seven eighths of the width; measured on the
game's 1.0 item icons, e.g. HotdogGrill, IndustrialGrill). Front is -Y, the model's left is +X (art/README.md).
Writes nothing else.
"""
import os
import bpy, math, sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path, texture_path, out_path = args[0], args[1], args[2]
SIZE = 512
FILL = 0.86
AZIMUTH = 26   # degrees from the front towards the model's left (+X): the game's icons show the front and the left side
ELEVATION = 24

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.ops.import_scene.fbx(filepath=fbx_path)
for ob in list(bpy.data.objects):
    if ob.type != "MESH" or not ob.name.endswith("_Body"):
        bpy.data.objects.remove(ob, do_unlink=True)
bodies = [o for o in bpy.data.objects if o.type == "MESH"]
if not bodies:
    raise SystemExit(f"No *_Body mesh in {fbx_path}")

mat = bpy.data.materials.new("Icon")
mat.use_nodes = True
nodes = mat.node_tree.nodes
bsdf = nodes["Principled BSDF"]
tex = nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(texture_path)
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.55
mask_path = texture_path.replace("_BC.png", "_M.png")
normal_path = texture_path.replace("_BC.png", "_N.png")
if mask_path != texture_path and os.path.exists(mask_path):
    m = nodes.new("ShaderNodeTexImage")
    m.image = bpy.data.images.load(mask_path)
    m.image.colorspace_settings.name = "Non-Color"
    m.image.alpha_mode = "CHANNEL_PACKED"
    sep = nodes.new("ShaderNodeSeparateColor")
    mat.node_tree.links.new(m.outputs["Color"], sep.inputs[0])
    mat.node_tree.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    inv = nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    mat.node_tree.links.new(m.outputs["Alpha"], inv.inputs[1])
    mat.node_tree.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
if normal_path != texture_path and os.path.exists(normal_path):
    n = nodes.new("ShaderNodeTexImage")
    n.image = bpy.data.images.load(normal_path)
    n.image.colorspace_settings.name = "Non-Color"
    nm = nodes.new("ShaderNodeNormalMap")
    mat.node_tree.links.new(n.outputs["Color"], nm.inputs["Color"])
    mat.node_tree.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
for ob in bodies:
    ob.data.materials.clear()
    ob.data.materials.append(mat)

world = bpy.data.worlds.new("IconWorld")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
scene.world = world


def sun(name, energy, direction):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = math.radians(10)
    ob = bpy.data.objects.new(name, data)
    ob.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(ob)


sun("Key", 2.6, (-0.45, 0.55, -0.75))
sun("Fill", 0.5, (0.6, 0.2, -0.4))
sun("Rim", 0.8, (-0.1, -0.9, -0.3))

a, e = math.radians(AZIMUTH), math.radians(ELEVATION)
direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
pts = [o.matrix_world @ Vector(c) for o in bodies for c in o.bound_box]
centre = sum(pts, Vector()) / len(pts)
cam = bpy.data.objects.new("IconCam", bpy.data.cameras.new("IconCam"))
cam.data.type = "ORTHO"
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = centre + direction * 5
cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
bpy.context.view_layer.update()
# Fit the projected box: the camera's right and up axes.
inv = cam.matrix_world.inverted()
local = [inv @ p for p in pts]
xs, ys = [p.x for p in local], [p.y for p in local]
cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
cam.location = cam.matrix_world @ Vector((cx, cy, 0))
cam.data.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) / FILL

scene.render.engine = "BLENDER_EEVEE"
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = SIZE
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "AgX"
scene.view_settings.look = "AgX - Punchy"
scene.render.filepath = out_path
bpy.ops.render.render(write_still=True)
print("ICON", out_path)

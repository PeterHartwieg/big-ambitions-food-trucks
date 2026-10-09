"""Render a 256 x 256 item icon with a transparent background, in the style of the game's item icons.

Usage:
  blender -b --factory-startup --python art/tools/render_icon.py -- <model>.fbx <T_..._BaseColor>.png <out>.png

Renders the FBX with its base colour texture, so the icon shows what the game shows: the <Name>_Body mesh
only, seen from the front and a little to its right, from above, filling about four fifths of the frame.
Front is -Y, the model's right is -X (art/README.md). Writes nothing else.
"""
import bpy, math, sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path, texture_path, out_path = args[0], args[1], args[2]
SIZE = 256
FILL = 0.80
AZIMUTH = 28   # degrees from the front towards the model's right (-X), as the game's icons
ELEVATION = 30

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
for ob in bodies:
    ob.data.materials.clear()
    ob.data.materials.append(mat)

world = bpy.data.worlds.new("IconWorld")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.35
scene.world = world


def sun(name, energy, direction):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = math.radians(10)
    ob = bpy.data.objects.new(name, data)
    ob.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(ob)


sun("Key", 2.2, (0.45, 0.55, -0.75))
sun("Fill", 0.5, (-0.6, 0.2, -0.4))
sun("Rim", 0.8, (-0.1, -0.9, -0.3))

a, e = math.radians(AZIMUTH), math.radians(ELEVATION)
direction = Vector((-math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
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

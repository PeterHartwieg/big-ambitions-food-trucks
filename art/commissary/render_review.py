"""Review renders of the commissary bay: road paint, lettering, sign and props laid out as BayMarkers and BayProps place
them in game, on a stand-in street (procedural asphalt, a 15 cm kerb, a pavement and a garage front).

    blender -b --factory-startup --python art/commissary/render_review.py

Run build_commissary.py first. Writes art/commissary/renders/review-*.png and review-sheet.png.
Layout (Blender, metres): the street runs along X; the parking lane's middle is y = 0, the kerb at y = 1.25, the
pavement from 1.25 to 5.25, the facade at y = 5.25 with the door at x = 0. The bay is the paint's 13 m stretch.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
R = os.path.join(OUT, 'renders')
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, 'BayCommissary.blend'))
scene = bpy.context.scene
for ob in list(scene.objects):
    if not ob.name.endswith('_Body') and not ob.name.startswith('BaySign_'):
        bpy.data.objects.remove(ob, do_unlink=True)

LANE_W = 2.4          # the parking lane
PAINT_W = 2.2         # BayZone width: the legal run less the probe boxes
LINE = 0.15
KERB_Y, KERB_H = 1.25, 0.15
FACADE_Y = 5.25
SPAN = (-6.5, 6.5)    # one stretch, as in Hell's Kitchen's log


def mat(name, rgb, rough=0.9, noise=0.0, scale=4.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes['Principled BSDF']
    lin = [((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    b.inputs['Base Color'].default_value = (*lin, 1)
    b.inputs['Roughness'].default_value = rough
    if noise:
        tex = nt.nodes.new('ShaderNodeTexNoise')
        tex.inputs['Scale'].default_value = scale
        tex.inputs['Detail'].default_value = 8
        ramp = nt.nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color = (*[x * (1 - noise) for x in lin], 1)
        ramp.color_ramp.elements[1].color = (*[min(1, x * (1 + noise)) for x in lin], 1)
        nt.links.new(tex.outputs['Fac'], ramp.inputs['Fac'])
        nt.links.new(ramp.outputs['Color'], b.inputs['Base Color'])
    return m


def slab(name, x0, x1, y0, y1, z0, z1, material):
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    o = bpy.context.active_object
    o.name = name
    o.scale = (x1 - x0, y1 - y0, z1 - z0)
    o.data.materials.append(material)
    return o


asphalt = mat('asphalt', (0.20, 0.20, 0.21), 0.95, 0.35, 60)
concrete = mat('concrete', (0.55, 0.54, 0.51), 0.9, 0.15, 20)
brick = mat('brick', (0.42, 0.24, 0.18), 0.9, 0.2, 8)
door = mat('door', (0.30, 0.32, 0.33), 0.5)
lane_paint = mat('lane', (0.86, 0.86, 0.82), 0.8)
paint = bpy.data.materials['M_BayPaint']

slab('Road', -30, 30, -9, KERB_Y, -0.1, 0.0, asphalt)
slab('Kerb', -30, 30, KERB_Y, KERB_Y + 0.25, -0.1, KERB_H, concrete)
slab('Pavement', -30, 30, KERB_Y + 0.25, FACADE_Y, -0.1, KERB_H, concrete)
slab('Facade', -30, 30, FACADE_Y, FACADE_Y + 0.4, 0, 9, brick)
slab('RollerDoor', -1.6, 1.6, FACADE_Y - 0.05, FACADE_Y, KERB_H, 3.4, door)
# The driving lane's centre line beyond the parking lane.
for x in np.arange(-30, 30, 6):
    slab('Dash', x, x + 3, -4.6, -4.48, 0, 0.004, lane_paint)

# The bay outline exactly as BayMarkers.BuildMesh draws it: long edges and end bars, 15 cm lines, 2.5 cm up.
bm = bmesh.new()
uv = bm.loops.layers.uv.verify()


def quad(x0, y0, x1, y1, z=0.012):
    vs = [bm.verts.new((x, y, z)) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
    f = bm.faces.new(vs)
    for l in f.loops:
        l[uv].uv = (l.vert.co.x / 2, l.vert.co.y / 2)


h = PAINT_W / 2
a, b = SPAN
quad(a, -h, b, -h + LINE); quad(a, h - LINE, b, h)
quad(a, -h + LINE, a + LINE, h - LINE); quad(b - LINE, -h + LINE, b, h - LINE)
me = bpy.data.meshes.new('BayOutline')
bm.to_mesh(me); bm.free()
outline = bpy.data.objects.new('BayOutline', me)
scene.collection.objects.link(outline)
me.materials.append(paint)

# Lettering: 0.70 m cap height, centred on the stretch, reading from the pavement (letters' tops to the road, -Y).
letters = bpy.data.objects['BayLettering_Body']
scale = 0.70
letters.scale = (scale, scale, 1)
letters.location = (0, 0, 0.013)
letters.rotation_euler = (0, 0, 0)  # built with the tops to -Y (the road), reading left to right from the pavement

# The sign at the kerb by the bay's middle, plate facing the road; props by the door, against the facade.
for n in ('BaySign_Pole', 'BaySign_Plate'):
    o = bpy.data.objects[n]
    o.location = (0.0, KERB_Y + 0.45, KERB_H)
props = {'BayPalletStack': (-3.2, FACADE_Y - 0.6, 0), 'BayRollCage': (-4.6, FACADE_Y - 0.5, 0),
         'BayHandTruck': (2.6, FACADE_Y - 0.4, 0.3), 'BayDumpster': (5.0, FACADE_Y - 0.75, 0)}
for n, (x, y, rot) in props.items():
    o = bpy.data.objects[n + '_Body']
    o.location = (x, y, KERB_H)
    o.rotation_euler = (0, 0, rot)

# A box truck stand-in parked in the bay, to judge the scale (5.8 x 2.2 x 3.1 m).
truck = mat('truck', (0.85, 0.82, 0.70), 0.6)
slab('Truck', -4.9, 0.9, -1.05, 1.05, 0.35, 3.1, truck)
for x in (-4.0, 0.0):
    for y in (-1.0, 1.0):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.38, depth=0.25, location=(x, y, 0.38), rotation=(math.pi / 2, 0, 0))
        tyre = bpy.context.active_object
        tyre.name = 'Tyre'
        tyre.data.materials.append(mat('tyre', (0.05, 0.05, 0.05), 0.9))

world = bpy.data.worlds.new('w')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.55, 0.65, 0.80, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.8
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN'))
sun.data.energy = 3.5
sun.data.angle = math.radians(2)
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
scene.collection.objects.link(sun)

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
scene.view_settings.view_transform = 'AgX' if 'AgX' in [i.identifier for i in scene.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
scene.collection.objects.link(cam)
scene.camera = cam
views = {
    'pavement': ((9.0, 4.2, 1.7), (-1.0, -0.2, 0.6), 40),
    'driver': ((-22.0, -3.4, 1.6), (0.0, 0.2, 0.5), 35),
    'top': ((0.0, 1.0, 22.0), (0.0, 1.0, 0.0), 40),
    'sign': ((1.8, -2.4, 1.7), (0.0, KERB_Y + 0.45, 2.3), 45),
    'props': ((0.5, 1.6, 2.2), (-1.5, FACADE_Y - 0.6, 0.5), 28),
}
# Without the truck too, for the paint and lettering.
paths = []
for name, (loc, target, lens) in views.items():
    for o in scene.objects:
        if o.name.startswith(('Truck', 'Tyre')):
            o.hide_render = name != 'driver'
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    p = os.path.join(R, f'review-{name}.png')
    scene.render.filepath = p
    bpy.ops.render.render(write_still=True)
    paths.append(p)

# One sheet of the five views, 2560 x 1440 (the top view in the corner left over).
sheet = np.zeros((1440 + 720, 2560, 4), dtype=np.float32)
for i, p in enumerate(paths):
    img = bpy.data.images.load(p)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(720, 1280, 4)[::-1]
    r, c = divmod(i, 2)
    sheet[r * 720:(r + 1) * 720, c * 1280:(c + 1) * 1280] = px
sheet[..., 3] = 1
out = bpy.data.images.new('sheet', 2560, 2160)
out.pixels.foreach_set(sheet[::-1].ravel())
out.filepath_raw = os.path.join(R, 'review-sheet.png')
out.file_format = 'PNG'
out.save()
print('[commissary] review renders:', paths)

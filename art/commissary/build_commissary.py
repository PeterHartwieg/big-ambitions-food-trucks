"""The commissary loading bay's street furniture: road paint, lettering, the kerb sign and props by the door.

Run headless from the repo root:
    blender -b --factory-startup --python art/commissary/build_commissary.py

Writes into art/commissary/ (textures, FBX files, BayCommissary.blend) and renders a review scene into
art/commissary/renders/. Nothing here is a game asset: shapes are modelled from scratch, textures are procedural or
rendered from Blender's own Inter font (SIL Open Font License), and the names on the signs come from data/ and the
mod's en.json.

Blender is Z up; models face -Y with their left on +X, so in Unity (-x, z, -y) the front is +Z (art/README.md "Axes").
"""
import bpy, bmesh, math, os, json, sys
import numpy as np
from mathutils import Vector, Matrix

OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(OUT))
RENDERS = os.path.join(OUT, 'renders')
os.makedirs(RENDERS, exist_ok=True)
FONT_FILE = os.path.join(os.path.dirname(bpy.app.binary_path), '..', 'Resources',
                         '%d.%d' % bpy.app.version[:2], 'datafiles', 'fonts', 'Inter.woff2')
RNG = np.random.default_rng(20261011)

# Road paint: the game's own decal paint is a flat 188 sRGB (T_Line_*, T_Crosswalk_*), so the same white (sRGB 0..1).
PAINT = (0.74, 0.74, 0.72)
ASPHALT = (0.20, 0.20, 0.20)
SIGN_RED = (0.72, 0.08, 0.08)
SIGN_GREEN = (0.05, 0.36, 0.20)
SIGN_BLACK = (0.06, 0.06, 0.06)

# ---------------------------------------------------------------------------------------------- helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0


def periodic_noise(size, scale, rng):
    """Tileable smooth noise in 0..1: white noise low-passed in the frequency domain."""
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    k = np.exp(-((fx ** 2 + fy ** 2) * (scale ** 2)))
    n = np.real(np.fft.ifft2(np.fft.fft2(white) * k))
    n -= n.min()
    return n / max(n.max(), 1e-9)


def save_png(name, rgb, alpha=None):
    """rgb: H x W x 3 in sRGB 0..1, row 0 at the top. Saved as art/commissary/<name>."""
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=alpha is not None)
    a = np.ones((h, w)) if alpha is None else alpha
    px = np.dstack([rgb, a])[::-1].astype(np.float32)  # Blender rows start at the bottom
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = os.path.join(OUT, name)
    img.file_format = 'PNG'
    img.save()
    return img


def material(name, color=(1, 1, 1), image=None, roughness=0.8, metallic=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*srgb_to_linear(color), 1)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    if image is not None:
        tex = nt.nodes.new('ShaderNodeTexImage')
        tex.image = image
        nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    mat.diffuse_color = (*srgb_to_linear(color), 1)
    return mat


def srgb_to_linear(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def box(name, size, at=(0, 0, 0), mat=None, uv_scale=1.0):
    """An axis-aligned box with its bottom centre at `at`; UVs planar per face in metres * uv_scale."""
    sx, sy, sz = size
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz + sz / 2)) + Vector(at)
    planar_uv(bm, uv_scale)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new(name, me))
    if mat:
        me.materials.append(mat)
    return ob


def planar_uv(bm, scale):
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            c = loop.vert.co
            u, v = [(c.y, c.z), (c.x, c.z), (c.x, c.y)][ax]
            loop[uv].uv = (u * scale, v * scale)


def join(name, obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    if len(obs) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def triangulate(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(ob.data)
    bm.free()
    return len(ob.data.polygons)


def export_fbx(path, obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)


def text_mesh(name, body, size=1.0, bold=0.0, align='CENTER'):
    font = bpy.data.fonts.load(FONT_FILE, check_existing=True)
    cu = bpy.data.curves.new(name, 'FONT')
    cu.body = body
    cu.font = font
    cu.size = size
    cu.offset = bold
    cu.align_x = align
    cu.align_y = 'BOTTOM_BASELINE'
    cu.fill_mode = 'BOTH'
    ob = link(bpy.data.objects.new(name, cu))
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.ops.object.convert(target='MESH')
    return bpy.context.view_layer.objects.active

# ---------------------------------------------------------------------------------------------- road paint

def paint_texture():
    """Worn road paint, tileable over 2 m: the white with asphalt showing through scuffs and fine grit."""
    n = 512
    blotch = periodic_noise(n, 40, RNG)
    grit = RNG.random((n, n))
    streak = periodic_noise(n, 12, RNG)
    wear = np.clip((blotch - 0.62) * 4.0, 0, 1) * 0.55 + (grit > 0.985) * 0.6 + np.clip((streak - 0.8) * 3, 0, 0.25)
    wear = np.clip(wear, 0, 0.85)
    tone = 0.96 + 0.06 * (periodic_noise(n, 6, RNG) - 0.5)
    rgb = np.zeros((n, n, 3))
    for i in range(3):
        rgb[..., i] = (PAINT[i] * tone) * (1 - wear) + ASPHALT[i] * wear
    return save_png('T_BayPaint_BaseColor.png', np.clip(rgb, 0, 1))


def lettering(paint_mat):
    """LOADING ZONE as flat paint: cap height 1 m, centred, reading along Unity +x with the letters' tops to Unity +z."""
    ob = text_mesh('BayLettering_Body', 'LOADING ZONE', size=1.0, bold=0.0)
    me = ob.data
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]
    cap = max(ys) - min(ys)
    cx = (max(xs) + min(xs)) / 2; cy = (max(ys) + min(ys)) / 2
    # Condensed, like street lettering, then turned 180 degrees about Z: Blender -X is Unity +x and -Y Unity +z.
    m = Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Diagonal((0.80 / cap, 1.0 / cap, 1, 1)) @ Matrix.Translation((-cx, -cy, 0))
    me.transform(m)
    for v in me.vertices:
        v.co.z = 0.0
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co.x / 2.0, loop.vert.co.y / 2.0)
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(paint_mat)
    tris = triangulate(ob)
    xs = [v.co.x for v in me.vertices]
    print(f'[commissary] BayLettering: {tris} tris, {max(xs) - min(xs):.2f} m long at 1 m cap height')
    return ob

# ---------------------------------------------------------------------------------------------- the sign

PLATE_W, PLATE_H, PLATE_T = 0.40, 0.80, 0.006
PLATE_BOTTOM = 1.90
POLE_H, POLE_R = 3.00, 0.032


def sign_texture(file_name, place):
    """Renders the plate's face (512 x 1024) with Workbench, flat colours, then adds a little grime."""
    scene = bpy.data.scenes.new('SignFace')
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'FLAT'
    scene.display.shading.color_type = 'MATERIAL'
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x, scene.render.resolution_y = 512, 1024
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new('SignWorld')
    scene.world.color = (1, 1, 1)
    cam = bpy.data.objects.new('SignCam', bpy.data.cameras.new('SignCam'))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 1.0  # 0.5 x 1.0 units: the plate is 0.40 x 0.80 m, drawn here at 1.25 x
    cam.location = (0, 0, 5)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cols = {k: bpy.data.materials.new('sign_' + k) for k in ('white', 'red', 'green', 'black')}
    for k, c in (('white', (1, 1, 1)), ('red', SIGN_RED), ('green', SIGN_GREEN), ('black', SIGN_BLACK)):
        cols[k].diffuse_color = (*srgb_to_linear(c), 1)

    def put(ob, mat, z=0.0):
        for c in list(ob.users_collection):
            c.objects.unlink(ob)
        scene.collection.objects.link(ob)
        ob.location.z = z
        ob.data.materials.clear()
        ob.data.materials.append(cols[mat])
        return ob

    def text(body, y, height, mat, max_w=0.42, bold=0.0):
        ob = text_mesh('t', body, size=1.0, bold=bold)
        xs = [v.co.x for v in ob.data.vertices]; ys = [v.co.y for v in ob.data.vertices]
        w = max(xs) - min(xs); h = max(ys) - min(ys) if body.isupper() else 0.72
        s = min(height / max(h, 1e-6), max_w / max(w, 1e-6))
        ob.scale = (s, s, 1)
        ob.location = (-(max(xs) + min(xs)) / 2 * s, y, 0)
        return put(ob, mat, 0.01)

    def rect(x0, y0, x1, y1, mat, z=0.005):
        me = bpy.data.meshes.new('r')
        me.from_pydata([(x0, y0, 0), (x1, y0, 0), (x1, y1, 0), (x0, y1, 0)], [], [(0, 1, 2, 3)])
        return put(bpy.data.objects.new('r', me), mat, z)

    rect(-0.25, -0.5, 0.25, 0.5, 'white', 0)
    # A red border, as on NYC parking signs.
    for (a, b, c, d) in ((-0.235, 0.475, 0.235, 0.487), (-0.235, -0.487, 0.235, -0.475),
                         (-0.235, -0.487, -0.223, 0.487), (0.223, -0.487, 0.235, 0.487)):
        rect(a, b, c, d, 'red')
    text('COMMERCIAL', 0.36, 0.065, 'red')
    text('VEHICLES ONLY', 0.27, 0.065, 'red')
    text('LOADING', 0.135, 0.095, 'red')
    text('ZONE', 0.015, 0.095, 'red')
    # A double-headed arrow: the zone runs both ways along the kerb from the sign.
    rect(-0.15, -0.055, 0.15, -0.035, 'red')
    for sx in (-1, 1):
        me = bpy.data.meshes.new('a')
        me.from_pydata([(sx * 0.20, -0.045, 0), (sx * 0.13, -0.000, 0), (sx * 0.13, -0.090, 0)], [], [(0, 1, 2)])
        put(bpy.data.objects.new('a', me), 'red', 0.006)
    rect(-0.20, -0.135, 0.20, -0.128, 'black')
    text('FOOD VENDORS', -0.215, 0.050, 'black')
    if place:
        text(place, -0.320, 0.058, 'green')
        text('Commissary', -0.410, 0.050, 'green')
    else:
        text('COMMISSARY', -0.350, 0.055, 'green')
    path = os.path.join(RENDERS, '_' + file_name)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=scene.name)
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:]).reshape(1024, 512, 4)[::-1, :, :3]
    bpy.data.images.remove(img)
    os.remove(path)
    grime = periodic_noise(1024, 30, RNG)[:, :512]
    rgb = px * (0.97 + 0.03 * grime[..., None]) - 0.015 * (grime[..., None] > 0.85)
    for ob in list(scene.collection.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.scenes.remove(scene)
    return save_png(file_name, np.clip(rgb, 0, 1))


def pole_texture():
    """Galvanised steel: mid grey with a faint spangle, 64 x 256."""
    n = periodic_noise(256, 3, RNG)[:, :64]
    g = 0.58 + 0.10 * (n - 0.5)
    rgb = np.dstack([g * 0.98, g, g * 1.02])
    return save_png('T_BaySignPole_BaseColor.png', np.clip(rgb, 0, 1))


def sign_model(plate_mat, pole_mat):
    """BaySign: a round galvanised pole with a cap, and a plate on its road side (-Y, Unity +z), readable from both
    sides (the back's UVs are mirrored). Origin at the pole's foot."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=POLE_R, depth=POLE_H, location=(0, 0, POLE_H / 2))
    pole = bpy.context.active_object
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=POLE_R + 0.006, depth=0.03, location=(0, 0, POLE_H + 0.015))
    cap = bpy.context.active_object
    clamps = []
    for z in (PLATE_BOTTOM + 0.12, PLATE_BOTTOM + PLATE_H - 0.12):
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=POLE_R + 0.008, depth=0.04, location=(0, 0, z))
        clamps.append(bpy.context.active_object)
    pole = join('BaySign_Pole', [pole, cap] + clamps)
    bm = bmesh.new(); bm.from_mesh(pole.data)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for loop in f.loops:
            c = loop.vert.co
            loop[uv].uv = ((math.atan2(c.y, c.x) / (2 * math.pi)) % 1.0, c.z / POLE_H)
    bm.to_mesh(pole.data); bm.free()
    pole.data.materials.clear(); pole.data.materials.append(pole_mat)

    y_front = -(POLE_R + 0.012)
    x0, x1 = -PLATE_W / 2, PLATE_W / 2
    z0, z1 = PLATE_BOTTOM, PLATE_BOTTOM + PLATE_H
    yb = y_front + PLATE_T
    verts = [(x0, y_front, z0), (x1, y_front, z0), (x1, y_front, z1), (x0, y_front, z1),
             (x0, yb, z0), (x1, yb, z0), (x1, yb, z1), (x0, yb, z1)]
    faces = [(0, 1, 2, 3), (5, 4, 7, 6), (1, 5, 6, 2), (4, 0, 3, 7), (3, 2, 6, 7), (4, 5, 1, 0)]
    me = bpy.data.meshes.new('BaySign_Plate')
    me.from_pydata(verts, [], faces)
    me.update()
    plate = link(bpy.data.objects.new('BaySign_Plate', me))
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for i, f in enumerate(bm.faces):
        for loop in f.loops:
            c = loop.vert.co
            u = (c.x - x0) / PLATE_W; v = (c.z - z0) / PLATE_H
            if i == 0:          # front, seen from -Y (the road): +X on the viewer's right
                loop[uv].uv = (u, v)
            elif i == 1:        # back, seen from +Y (the pavement): +X on the viewer's left
                loop[uv].uv = (1 - u, v)
            else:               # the plate's edges: the white margin
                loop[uv].uv = (0.02, 0.5)
    bm.to_mesh(me); bm.free()
    me.materials.append(plate_mat)
    for ob in (pole, plate):
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    print(f'[commissary] BaySign: pole {triangulate(pole)} tris, plate {triangulate(plate)} tris')
    return pole, plate

# ---------------------------------------------------------------------------------------------- props

def wood_texture():
    """Pallet and crate timber: pale weathered pine with grain along U, 256 x 256."""
    n = 256
    grain = periodic_noise(n, 2, RNG)
    rows = np.repeat(periodic_noise(n, 50, RNG)[:, :1], n, axis=1)
    t = 0.75 * grain[:, :] ** 0 * 0 + 0.6 * rows + 0.4 * periodic_noise(n, 20, RNG)
    base = np.array([0.70, 0.58, 0.42])
    dark = np.array([0.45, 0.36, 0.25])
    rgb = base * (1 - t[..., None] * 0.6) + dark * (t[..., None] * 0.6)
    return save_png('T_BayWood_BaseColor.png', np.clip(rgb, 0, 1))


def flat_texture(name, color, noise=0.06, size=128, scale=8):
    n = periodic_noise(size, scale, RNG)
    rgb = np.array(color)[None, None, :] * (1 - noise / 2 + noise * n[..., None])
    return save_png(name, np.clip(rgb, 0, 1))


def pallet(name, mat, at=(0, 0, 0)):
    """A 1.2 x 1.0 m EUR-style pallet, 0.144 m high: three bottom boards, three blocks rows, five top boards."""
    parts = []
    x0, y0, z = at
    for y in (-0.45, 0.0, 0.45):
        parts.append(box(name + '_b', (1.2, 0.10, 0.022), (x0, y0 + y, z), mat))
    for x in (-0.55, 0.0, 0.55):
        for y in (-0.45, 0.0, 0.45):
            parts.append(box(name + '_k', (0.10, 0.10, 0.078), (x0 + x, y0 + y, z + 0.022), mat))
    for x in (-0.55, 0.0, 0.55):
        parts.append(box(name + '_s', (0.10, 1.0, 0.022), (x0 + x, y0, z + 0.100), mat))
    for y in (-0.45, -0.225, 0.0, 0.225, 0.45):
        parts.append(box(name + '_t', (1.2, 0.10, 0.022), (x0, y0 + y, z + 0.122), mat))
    return parts


def crate(name, mat, size, at, produce_mat=None):
    """A plastic produce crate: a box with an open top and a darker rim; one material, coloured by texture."""
    sx, sy, sz = size
    x, y, z = at
    walls = [box(name, (sx, 0.012, sz), (x, y - sy / 2 + 0.006, z), mat), box(name, (sx, 0.012, sz), (x, y + sy / 2 - 0.006, z), mat),
             box(name, (0.012, sy, sz), (x - sx / 2 + 0.006, y, z), mat), box(name, (0.012, sy, sz), (x + sx / 2 - 0.006, y, z), mat),
             box(name, (sx, sy, 0.012), (x, y, z), mat)]
    # Filled to just under the rim with produce.
    walls.append(box(name + '_fill', (sx - 0.03, sy - 0.03, 0.02), (x, y, z + sz - 0.05), produce_mat))
    return walls


def carton(name, mat, size, at):
    return [box(name, size, at, mat)]


def hand_truck(name, mat):
    """A two-wheel hand truck standing upright: frame 1.2 m, nose plate, two wheels, handle loop. Origin at its foot."""
    parts = []
    for x in (-0.19, 0.19):
        parts.append(box(name, (0.025, 0.025, 1.20), (x, 0.04, 0.02), mat))
    for z in (0.35, 0.70, 1.05):
        parts.append(box(name, (0.40, 0.02, 0.02), (0, 0.04, z), mat))
    parts.append(box(name, (0.40, 0.20, 0.008), (0, -0.07, 0.0), mat))
    parts.append(box(name, (0.30, 0.025, 0.025), (0, 0.04, 1.20), mat))
    for x in (-0.23, 0.23):
        bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.12, depth=0.06, location=(x, 0.13, 0.12), rotation=(0, math.pi / 2, 0))
        w = bpy.context.active_object
        w.data.materials.append(mat)
        parts.append(w)
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.012, depth=0.52, location=(0, 0.13, 0.12), rotation=(0, math.pi / 2, 0))
    a = bpy.context.active_object; a.data.materials.append(mat); parts.append(a)
    return parts


def roll_cage(name, mat):
    """A roll cage (0.72 x 0.80 x 1.70 m): wire sides on a wheeled base. Bars as thin boxes."""
    parts = [box(name, (0.72, 0.80, 0.04), (0, 0, 0.12), mat)]
    for x in (-0.33, 0.33):
        for y in (-0.37, 0.37):
            bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.05, depth=0.03, location=(x, y, 0.05), rotation=(0, math.pi / 2, 0))
            w = bpy.context.active_object; w.data.materials.append(mat); parts.append(w)
            parts.append(box(name, (0.03, 0.03, 0.06), (x, y, 0.07), mat))
    for side in (-1, 1):
        y = side * 0.39
        for x in np.linspace(-0.35, 0.35, 7):
            parts.append(box(name, (0.012, 0.012, 1.54), (float(x), y, 0.16), mat))
        for z in np.linspace(0.16, 1.70, 6):
            parts.append(box(name, (0.72, 0.015, 0.015), (0, y, float(z)), mat))
    x = -0.35
    for y in np.linspace(-0.38, 0.38, 6):
        parts.append(box(name, (0.012, 0.012, 1.54), (x, float(y), 0.16), mat))
    for z in np.linspace(0.16, 1.70, 6):
        parts.append(box(name, (0.015, 0.78, 0.015), (x, 0, float(z)), mat))
    return parts


def dumpster(name, mat, lid_mat):
    """A 2-yard commercial dumpster, 1.85 x 1.10 m, 1.25 m high, slanted front, two lids. Origin at its foot."""
    w, d, h_back, h_front = 1.85, 1.10, 1.25, 1.05
    verts = [(-w / 2, -d / 2, 0.12), (w / 2, -d / 2, 0.12), (w / 2, d / 2, 0.12), (-w / 2, d / 2, 0.12),
             (-w / 2, -d / 2 - 0.12, h_front), (w / 2, -d / 2 - 0.12, h_front), (w / 2, d / 2, h_back), (-w / 2, d / 2, h_back)]
    faces = [(0, 3, 2, 1), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    body = link(bpy.data.objects.new(name, me))
    bm = bmesh.new(); bm.from_mesh(me); planar_uv(bm, 0.5); bm.to_mesh(me); bm.free()
    me.materials.append(mat)
    parts = [body]
    # Lids: two slightly raised dark panels over the top, a lip and the corner casters.
    for x in (-w / 4, w / 4):
        lid = box(name + '_lid', (w / 2 - 0.03, d + 0.16, 0.04), (x, -0.06, 0), lid_mat)
        lid.rotation_euler.x = math.atan2(h_back - h_front, d + 0.12)
        lid.location.z = (h_back + h_front) / 2 + 0.005
        bpy.context.view_layer.objects.active = lid
        parts.append(lid)
    parts.append(box(name, (w + 0.06, 0.06, 0.08), (0, -d / 2 - 0.14, h_front - 0.10), mat))
    for x in (-w / 2 + 0.12, w / 2 - 0.12):
        for y in (-d / 2 + 0.12, d / 2 - 0.12):
            bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.06, depth=0.04, location=(x, y, 0.06), rotation=(math.pi / 2, 0, 0))
            c = bpy.context.active_object; c.data.materials.append(lid_mat); parts.append(c)
    return parts


def apply_all(obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def prop(name, parts, mats):
    """Joins the parts into <name>_Body, origin at the bottom centre, and makes a box collider <name>_Collision."""
    apply_all(parts)
    body = join(name + '_Body', parts)
    xs = [v.co.x for v in body.data.vertices]; ys = [v.co.y for v in body.data.vertices]; zs = [v.co.z for v in body.data.vertices]
    c = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, min(zs)))
    body.data.transform(Matrix.Translation(-c))
    size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    tris = triangulate(body)
    print(f'[commissary] {name}: {tris} tris, {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m, {len(body.data.materials)} materials')
    return body, size


def props(mats):
    """The props by the commissary's door, each its own FBX: a pallet stack with crates and cartons, a hand truck with
    cartons, a roll cage with cartons, and a dumpster."""
    out = {}
    # PalletStack: two pallets, three crates of produce and two cartons on top.
    parts = pallet('p1', mats['wood']) + pallet('p2', mats['wood'], (0, 0, 0.144))
    for i, (x, y) in enumerate(((-0.33, -0.22), (0.07, -0.22), (0.39, 0.20))):
        parts += crate('c', mats['crate'], (0.40, 0.30, 0.26), (x, y, 0.288), mats[('produce', 'greens', 'produce')[i]])
    parts += crate('c', mats['crate'], (0.40, 0.30, 0.26), (-0.13, -0.22, 0.548), mats['greens'])
    parts += carton('k', mats['carton'], (0.50, 0.38, 0.32), (-0.30, 0.22, 0.288))
    parts += carton('k', mats['carton'], (0.38, 0.30, 0.26), (-0.30, 0.22, 0.608))
    out['BayPalletStack'] = prop('BayPalletStack', parts, mats)
    # HandTruck with two cartons on its nose plate.
    parts = hand_truck('h', mats['steel'])
    parts += carton('k', mats['carton'], (0.38, 0.30, 0.34), (0, -0.10, 0.008))
    parts += carton('k', mats['carton'], (0.34, 0.28, 0.28), (0, -0.10, 0.348))
    out['BayHandTruck'] = prop('BayHandTruck', parts, mats)
    # RollCage, part loaded.
    parts = roll_cage('r', mats['steel'])
    parts += carton('k', mats['carton'], (0.60, 0.70, 0.40), (0.02, 0, 0.14))
    parts += carton('k', mats['carton'], (0.45, 0.40, 0.30), (0.05, 0.10, 0.54))
    out['BayRollCage'] = prop('BayRollCage', parts, mats)
    # Dumpster.
    out['BayDumpster'] = prop('BayDumpster', dumpster('d', mats['dumpster'], mats['dumpster_lid']), mats)
    return out

# ---------------------------------------------------------------------------------------------- build

def build():
    reset()
    img_paint = paint_texture()
    paint_mat = material('M_BayPaint', image=img_paint, roughness=0.75)
    letters = lettering(paint_mat)
    export_fbx(os.path.join(OUT, 'BayLettering.fbx'), [letters])

    names = {}
    try:
        with open(os.path.join(REPO, 'data', 'commissaries.json')) as f:
            sites = json.load(f)['commissaries']
        with open(os.path.join(REPO, 'unity', 'FoodTrucks', 'Locales', 'en.json')) as f:
            locale = json.load(f)
        for s in sites:
            full = locale.get(s['name'], '')
            names[s['id']] = full[:-len(' Commissary')] if full.endswith(' Commissary') else full
    except Exception as ex:
        print('[commissary] names not read:', ex)
    sign_imgs = {'': sign_texture('T_BaySign_BaseColor.png', '')}
    for sid, place in names.items():
        sign_imgs[sid] = sign_texture(f'T_BaySign_{sid}_BaseColor.png', place)
    pole_img = pole_texture()
    pole_mat = material('M_BaySignPole', image=pole_img, roughness=0.5, metallic=0.6)
    plate_mat = material('M_BaySign', image=sign_imgs.get('hells-kitchen', sign_imgs['']), roughness=0.45)
    pole, plate = sign_model(plate_mat, pole_mat)
    export_fbx(os.path.join(OUT, 'BaySign.fbx'), [pole, plate])

    tex = {
        'wood': wood_texture(),
        'crate': flat_texture('T_BayCrate_BaseColor.png', (0.12, 0.32, 0.18), 0.10),
        'carton': flat_texture('T_BayCarton_BaseColor.png', (0.62, 0.47, 0.30), 0.12),
        'steel': flat_texture('T_BaySteel_BaseColor.png', (0.55, 0.57, 0.58), 0.10),
        'dumpster': flat_texture('T_BayDumpster_BaseColor.png', (0.16, 0.30, 0.22), 0.25, scale=14),
        'produce': flat_texture('T_BayProduce_BaseColor.png', (0.78, 0.22, 0.10), 0.35, scale=40),
        'greens': flat_texture('T_BayGreens_BaseColor.png', (0.30, 0.52, 0.16), 0.35, scale=40),
        'dumpster_lid': flat_texture('T_BayDumpsterLid_BaseColor.png', (0.06, 0.06, 0.06), 0.15),
    }
    mats = {k: material('M_Bay' + k.title().replace('_', ''), image=v, roughness=0.8) for k, v in tex.items()}
    built = props(mats)
    for name, (body, size) in built.items():
        export_fbx(os.path.join(OUT, f'{name}.fbx'), [body])
    with open(os.path.join(OUT, 'sizes.json'), 'w') as f:
        json.dump({n: [round(x, 3) for x in s] for n, (b, s) in built.items()}, f, indent=1)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'BayCommissary.blend'))
    return letters, pole, plate, built, paint_mat, sign_imgs


if __name__ == '__main__':
    build()
    print('[commissary] built')

"""Build the cash register shown on the cart's counter and the truck's serving counter. Writes only beside this file.

Run headless from the repo root, then give it the game's surface look:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python art/props/CashRegister/build_cash_register.py
  art/tools/apply_look.sh art/props/CashRegister

A compact point-of-sale register in the style of the game's own (ConcessionsStandRegister, CheckoutCounter):
a charcoal cash drawer with a lighter deck, a screen on a short post facing the customer (-Y) and tilted back,
a card terminal at the front right and a receipt slot. 0.15 x 0.13 m, 0.18 m tall. Front is -Y, origin at the
bottom centre, Z up. Every face gets its own cell in a 256 px atlas of flat colours (art/README.md).
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Matrix, Vector

OUT = os.path.dirname(os.path.abspath(__file__))
NAME = 'CashRegister'
SIZE = 256
PALETTE = {
    'body': (0.17, 0.18, 0.19, 1),
    'deck': (0.33, 0.34, 0.35, 1),
    'gap': (0.04, 0.04, 0.045, 1),
    'bezel': (0.07, 0.075, 0.08, 1),
    'screen': (0.20, 0.42, 0.55, 1),
    'keys': (0.86, 0.85, 0.80, 1),
    'paper': (0.96, 0.95, 0.91, 1),
    'lock': (0.72, 0.73, 0.74, 1),
    'accent': (0.80, 0.16, 0.12, 1),
}


class Geometry:
    def __init__(self):
        self.v, self.f, self.colors = [], [], []

    def add(self, verts, faces, color):
        start = len(self.v)
        self.v.extend([tuple(v) for v in verts])
        self.f.extend([tuple(start + i for i in face) for face in faces])
        self.colors.extend([color] * len(faces))

    def box(self, lo, hi, color, bevel=0.0, matrix=None):
        """Axis box from lo to hi, optional one-segment chamfer, optionally transformed."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1)
        c = (Vector(lo) + Vector(hi)) / 2
        d = Vector(hi) - Vector(lo)
        for v in bm.verts:
            v.co = Vector((v.co.x * d.x, v.co.y * d.y, v.co.z * d.z))
        if bevel:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(d) * 0.3), segments=1, affect='EDGES')
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        m = matrix if matrix is not None else Matrix.Identity(4)
        for v in bm.verts:
            v.co = m @ (v.co + c)
        bm.verts.ensure_lookup_table()
        bm.verts.index_update()
        self.add([v.co for v in bm.verts], [[v.index for v in f.verts] for f in bm.faces], color)
        bm.free()


def build():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    g = Geometry()
    # Cash drawer and deck.
    g.box((-.075, -.065, 0), (.075, .065, .050), 'body', .004)
    g.box((-.072, -.062, .050), (.072, .062, .058), 'deck', .003)
    g.box((-.060, -.0665, .018), (.060, -.064, .021), 'gap')
    g.box((-.006, -.068, .030), (.006, -.064, .038), 'lock', .001)
    # Receipt slot with a curl of paper at the back left.
    g.box((-.065, .030, .058), (-.025, .055, .064), 'bezel', .002)
    g.box((-.060, .040, .064), (-.030, .044, .082), 'paper')
    # Screen post and the screen, tilted back by 18 degrees, facing the customer (-Y).
    g.box((-.014, .010, .058), (.014, .030, .090), 'body', .003)
    tilt = Matrix.Translation((0, .020, .132)) @ Matrix.Rotation(math.radians(-18), 4, 'X')
    g.box((-.066, -.009, -.046), (.066, .009, .046), 'bezel', .004, tilt)
    g.box((-.058, -.0105, -.038), (.058, -.008, .038), 'screen', 0, tilt)
    g.box((.044, -.0115, -.044), (.054, -.0095, -.040), 'accent', 0, tilt)
    # Card terminal at the front right, keypad tilted towards the customer.
    g.box((.022, -.064, .058), (.068, -.026, .068), 'bezel', .003)
    pad = Matrix.Translation((.045, -.045, .074)) @ Matrix.Rotation(math.radians(20), 4, 'X')
    g.box((-.020, -.017, -.006), (.020, .017, .006), 'bezel', .003, pad)
    g.box((-.015, -.012, .006), (.015, .004, .0075), 'keys', 0, pad)
    g.box((-.015, .007, .006), (.015, .014, .0075), 'screen', 0, pad)

    me = bpy.data.meshes.new(NAME + '_Mesh')
    me.from_pydata(g.v, [], g.f)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    body = bpy.data.objects.new(NAME + '_Body', me)
    sc.collection.objects.link(body)
    texture(body, g.colors)

    hull = bmesh.new()
    for x in (-.075, .075):
        for y in (-.068, .065):
            hull.verts.new((x, y, 0))
            hull.verts.new((x, y, .060))
    for x in (-.066, .066):
        for y, z in ((.020 - .046 * math.sin(math.radians(18)) - .01, .132 - .046), (.020 + .046 * math.sin(math.radians(18)) + .01, .132 + .046)):
            hull.verts.new((x, y, z))
    bmesh.ops.convex_hull(hull, input=list(hull.verts), use_existing_faces=False)
    loose = [v for v in hull.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(hull, geom=loose, context='VERTS')
    bmesh.ops.triangulate(hull, faces=list(hull.faces))
    bmesh.ops.recalc_face_normals(hull, faces=list(hull.faces))
    cme = bpy.data.meshes.new(NAME + '_Collision_Mesh')
    hull.to_mesh(cme)
    hull.free()
    col = bpy.data.objects.new(NAME + '_Collision', cme)
    sc.collection.objects.link(col)
    col.hide_render = True
    col.display_type = 'WIRE'

    bpy.ops.object.select_all(action='DESELECT')
    for ob in (body, col):
        ob.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, NAME + '.fbx'), use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
    col.hide_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, NAME + '.blend'))
    return {'triangles': sum(len(p.vertices) - 2 for p in me.polygons), 'dimensions': [round(d, 3) for d in body.dimensions]}


def texture(ob, colors):
    me = ob.data
    count = len(me.polygons)
    cols = math.ceil(math.sqrt(count))
    rows = math.ceil(count / cols)
    uv = me.uv_layers.new(name='UVMap')
    px = np.ones((SIZE, SIZE, 4), dtype=np.float32)
    px[:, :, :3] = PALETTE['body'][:3]
    for p in me.polygons:
        x, y = p.index % cols, p.index // cols
        x0, x1 = round(x * SIZE / cols), round((x + 1) * SIZE / cols)
        y0, y1 = round(y * SIZE / rows), round((y + 1) * SIZE / rows)
        px[y0:y1, x0:x1] = PALETTE[colors[p.index]]
        lo = ((x0 + 1.0) / SIZE, (y0 + 1.0) / SIZE)
        hi = ((x1 - 1.0) / SIZE, (y1 - 1.0) / SIZE)
        if len(p.loop_indices) == 4:
            coords = [lo, (hi[0], lo[1]), hi, (lo[0], hi[1])]
        else:
            coords = [lo, (hi[0], lo[1]), (lo[0], hi[1])]
        for li, c in zip(p.loop_indices, coords):
            uv.data[li].uv = c
    img = bpy.data.images.new('T_%s_BaseColor' % NAME, width=SIZE, height=SIZE, alpha=True)
    img.colorspace_settings.name = 'sRGB'
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = os.path.join(OUT, 'T_%s_BaseColor.png' % NAME)
    img.file_format = 'PNG'
    img.save()
    mat = bpy.data.materials.new('M_' + NAME)
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = img
    mat.node_tree.links.new(tex.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    me.materials.append(mat)


if __name__ == '__main__':
    print('BUILD', build())

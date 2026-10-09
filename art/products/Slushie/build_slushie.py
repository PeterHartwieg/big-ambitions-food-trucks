"""Regenerate in the supplied metric start scene with Blender 5.2.

Run this file in Blender's Python console, or via the Blender MCP server.
Only the two named asset meshes are exported. HumanRef_1.8m is untouched.
Front is -Y; the straw bends toward the customer. All writes stay in OUT.
PNG alpha identifies the clear plastic; configure HDRP transparency on import.
"""
import bpy
import bmesh
import math
import os
import json
import subprocess
import numpy as np
from mathutils import Vector

REPO = '/Users/peterhartwieg/Coding_Projects/big-ambitions-food-trucks/.claude/worktrees/milestone-7'
OUT = REPO + '/art/products/Slushie'
PALETTE = {
    'plastic': (0.80, 0.94, 0.98, 0.13),
    'edge': (0.77, 0.91, 0.96, 0.56),
    'glint': (0.94, 0.99, 1.00, 0.34),
    'blue': (0.025, 0.57, 0.94, 1.0),
    'blue_light': (0.06, 0.70, 1.0, 1.0),
    'red': (0.96, 0.045, 0.16, 1.0),
    'red_light': (1.00, 0.18, 0.29, 1.0),
    'ice': (1.00, 0.46, 0.53, 1.0),
    'straw': (0.96, 0.985, 1.00, 1.0),
    'straw_blue': (0.09, 0.65, 0.86, 1.0),
    'hole': (0.035, 0.13, 0.19, 1.0),
}


def active(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


class Shape:
    def __init__(self):
        self.v, self.f, self.colors = [], [], []

    def ring(self, n, radius, z, centre=(0, 0), wobble=0):
        ids = []
        for i in range(n):
            a = 2 * math.pi * i / n
            ids.append(len(self.v))
            self.v.append((centre[0] + radius * math.cos(a),
                           centre[1] + radius * math.sin(a),
                           z + wobble * math.sin(3 * a + 0.6)))
        return ids

    def face(self, ids, color):
        self.f.append(tuple(ids))
        self.colors.append(color)

    def bridge(self, a, b, color):
        for i in range(len(a)):
            j = (i + 1) % len(a)
            c = color(i) if callable(color) else color
            self.face((a[i], a[j], b[j], b[i]), c)

    def cap(self, a, co, color, reverse=False):
        c = len(self.v)
        self.v.append(co)
        for i in range(len(a)):
            ids = (a[i], a[(i+1) % len(a)], c)
            self.face(ids[::-1] if reverse else ids, color)

    def object(self, name):
        me = bpy.data.meshes.new(name + '_Mesh')
        me.from_pydata(self.v, [], self.f)
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        # Recalculate rather than rely on profile traversal direction.
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(me)
        bm.free()
        # Store per-face semantic colours for a reproducible single atlas.
        attr = me.attributes.new('part_color', 'INT', 'FACE')
        keys = list(PALETTE)
        for p, color in zip(attr.data, self.colors):
            p.value = keys.index(color)
        return ob


def blockout(detail=False):
    for name in ('Slushie_Body', 'Slushie_Collision'):
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    s = Shape()
    # Closed thin-wall cup: 64 ring vertices and two radial bottom fans.
    a = s.ring(16, .024, 0)
    b = s.ring(16, .032, .091)
    c = s.ring(16, .0308, .091)
    d = s.ring(16, .023, .003)
    s.bridge(a, b, lambda i: 'glint' if i in (9, 14) else 'plastic')
    s.bridge(b, c, 'edge')
    s.bridge(c, d, 'plastic')
    s.cap(a, (0, 0, 0), 'edge', True)
    s.cap(d, (0, 0, .003), 'plastic')
    # The irregular boundary is geometric, visible through the clear shell.
    a = s.ring(16, .0234, .0033)
    b = s.ring(16, .0267, .043, wobble=.0018)
    c = s.ring(16, .0300, .0868, wobble=.0010)
    d = s.ring(16, .0170, .1015, wobble=.0011)
    s.bridge(a, b, lambda i: 'blue_light' if i in (9, 10, 14) else 'blue')
    s.bridge(b, c, 'red')
    s.bridge(c, d, lambda i: 'red_light' if i % 3 == 0 else 'red')
    s.cap(a, (0, 0, .0033), 'blue', True)
    s.cap(d, (-.001, .001, .108), 'red_light')
    # Twelve-sided double-wall dome, with a real opening for the straw.
    profile = [(.033, .090), (.034, .093), (.029, .104),
               (.018, .1135), (.0045, .117), (.0045, .1156),
               (.0175, .1121), (.028, .103), (.0325, .0926),
               (.0315, .090)]
    rings = [s.ring(12, r, z) for r, z in profile]
    for k in range(len(rings)):
        color = 'edge' if k in (0, 4, 8, 9) else 'plastic'
        s.bridge(rings[k], rings[(k+1) % len(rings)], color)
    # Straw starts in the mound, passes through the central lid aperture,
    # then bends toward -Y. Coordinate construction leaves transforms applied.
    centres = [Vector((0, 0, .079)), Vector((0, 0, .127)),
               Vector((.020, -.018, .1465))]
    ring_ids = []
    for k, pt in enumerate(centres):
        direction = (centres[min(k+1, 2)] - centres[max(k-1, 0)]).normalized()
        u = direction.cross(Vector((0, 1, 0))).normalized()
        v = direction.cross(u).normalized()
        rr = []
        for i in range(6):
            angle = 2 * math.pi * i / 6
            rr.append(len(s.v))
            s.v.append(tuple(pt + .003 * (math.cos(angle)*u + math.sin(angle)*v)))
        ring_ids.append(rr)
    for k in range(2):
        s.bridge(ring_ids[k], ring_ids[k+1],
                 lambda i: 'straw_blue' if i in (1, 4) else 'straw')
    s.cap(ring_ids[0], tuple(centres[0]), 'straw', True)
    direction = (centres[2]-centres[1]).normalized()
    u = direction.cross(Vector((0, 1, 0))).normalized()
    v = direction.cross(u).normalized()
    inner = []
    for depth in (0, .005):
        rr = []
        for i in range(6):
            angle = 2*math.pi*i/6
            rr.append(len(s.v))
            s.v.append(tuple(centres[2] - depth*direction +
                             .0018*(math.cos(angle)*u + math.sin(angle)*v)))
        inner.append(rr)
    s.bridge(ring_ids[2], inner[0], 'straw')
    s.bridge(inner[0], inner[1], 'hole')
    s.cap(inner[1], tuple(centres[2]-.005*direction), 'hole')
    if detail:
        # Five broad ice facets, deliberately few and large enough for icons.
        for x, y, z, size in [(-.012, -.013, .101, .004),
                               (.012, -.009, .102, .0035),
                               (-.014, .007, .101, .0035),
                               (.010, .011, .103, .003),
                               (-.005, -.001, .106, .003)]:
            t = len(s.v)
            s.v.extend([(x-size,y-size*.5,z-size*.7),
                        (x+size,y-size*.5,z-size*.7),
                        (x,y+size,z-size*.7), (x,y,z+size)])
            for face in ((0,2,1),(0,1,3),(1,2,3),(2,0,3)):
                s.face(tuple(t+i for i in face), 'ice')
    ob = s.object('Slushie_Body')
    setup_temp_materials(ob)
    active(ob)
    bpy.context.view_layer.update()
    return ob


def setup_temp_materials(ob):
    for key, rgba in PALETTE.items():
        mat = bpy.data.materials.get('Preview_'+key) or bpy.data.materials.new('Preview_'+key)
        mat.use_nodes = True
        mat.diffuse_color = rgba
        mat.surface_render_method = 'DITHERED'
        bs = mat.node_tree.nodes.get('Principled BSDF')
        linear = tuple(c/12.92 if c <= .04045 else ((c+.055)/1.055)**2.4 for c in rgba[:3])
        bs.inputs['Base Color'].default_value = (*linear, rgba[3])
        bs.inputs['Alpha'].default_value = rgba[3]
        bs.inputs['Roughness'].default_value = .18 if rgba[3] < 1 else .44
        ob.data.materials.append(mat)
    for p, a in zip(ob.data.polygons, ob.data.attributes['part_color'].data):
        p.material_index = a.value


def setup_scene():
    os.makedirs(OUT+'/renders', exist_ok=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 96
    sc.cycles.use_denoising = True
    sc.cycles.transparent_max_bounces = 16
    sc.render.resolution_x = 768
    sc.render.resolution_y = 768
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (.055, .073, .09, 1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value = .45
    def object_with_data(name, kind):
        ob = bpy.data.objects.get(name)
        if not ob:
            data = bpy.data.cameras.new(name) if kind == 'CAMERA' else bpy.data.lights.new(name, 'AREA')
            ob = bpy.data.objects.new(name, data)
            sc.collection.objects.link(ob)
        return ob
    cam = object_with_data('Slushie_PreviewCamera', 'CAMERA')
    cam.location = (.19, -.34, .22)
    target = Vector((0, 0, .074))
    cam.rotation_euler = (target-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = .19
    cam.data.clip_start = .001
    sc.camera = cam
    for name, pos, power, size in [('Key',(.13,-.18,.27),.90,.15),
                                  ('Fill',(-.18,-.08,.16),.45,.12),
                                  ('Rim',(.05,.17,.22),1.20,.1)]:
        ob = object_with_data('Slushie_Preview'+name, 'LIGHT')
        ob.location = pos
        ob.rotation_euler = (target-ob.location).to_track_quat('-Z','Y').to_euler()
        ob.data.energy, ob.data.shape, ob.data.size = power, 'DISK', size
    # Preview helpers also have unit scale. Their rotations are baked into data
    # at finalization, so every scene object has rotation zero and scale one.
    for a in bpy.context.screen.areas:
        if a.type == 'VIEW_3D':
            a.spaces.active.region_3d.view_rotation = cam.rotation_euler.to_quaternion()
            a.spaces.active.region_3d.view_distance = .26
            a.spaces.active.region_3d.view_location = target
            a.spaces.active.clip_start = .001
            a.spaces.active.shading.type = 'MATERIAL'


def render_stage(filename):
    setup_scene()
    img = bpy.data.images.get('T_Slushie_BaseColor.png')
    if img and img.source == 'FILE':
        img.reload()
        img.update()
    ob = bpy.data.objects.get('Slushie_Body')
    if ob:
        ob.data.update()
        for mat in ob.data.materials:
            mat.update_tag()
    bpy.context.view_layer.update()
    bpy.context.scene.render.filepath = OUT + '/renders/' + filename
    bpy.ops.render.render(write_still=True)


def texture_atlas():
    ob = bpy.data.objects['Slushie_Body']
    me = ob.data
    # Per-face tiles preserve deliberately flat colour blocks, with unique UVs.
    count = len(me.polygons)
    cols = math.ceil(math.sqrt(count))
    rows = math.ceil(count/cols)
    pixels = np.ones((256,256,4), dtype=np.float32)
    pixels[:,:,:3] = (.09, .13, .18)
    for layer in list(me.uv_layers):
        me.uv_layers.remove(layer)
    uv = me.uv_layers.new(name='UVMap')
    keys = list(PALETTE)
    for i, p in enumerate(me.polygons):
        x0, x1 = round((i % cols)*256/cols), round((i % cols+1)*256/cols)
        y0, y1 = round((i // cols)*256/rows), round((i // cols+1)*256/rows)
        rgba = PALETTE[keys[me.attributes['part_color'].data[i].value]]
        pixels[y0:y1,x0:x1] = rgba
        # 1.25-pixel inset, including the image boundaries.
        lo = ((x0+1.25)/256, (y0+1.25)/256)
        hi = ((x1-1.25)/256, (y1-1.25)/256)
        coords = [lo, (hi[0],lo[1]), hi, (lo[0],hi[1])]
        if len(p.loop_indices) == 3:
            coords = [lo, (hi[0],lo[1]), (lo[0],hi[1])]
        for idx, co in zip(p.loop_indices, coords):
            uv.data[idx].uv = co
    img = bpy.data.images.get('T_Slushie_BaseColor.png')
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new('T_Slushie_BaseColor.png', width=256, height=256, alpha=True)
    img.colorspace_settings.name = 'sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.file_format = 'PNG'
    img.filepath_raw = OUT + '/T_Slushie_BaseColor.png'
    img.save()
    # Keep absolute while the open scene still resides at art/start.blend.
    img.filepath = OUT + '/T_Slushie_BaseColor.png'
    # Invalidate Cycles' image cache when regenerating an existing atlas.
    img.reload()
    img.update()
    mat = bpy.data.materials.get('M_Slushie') or bpy.data.materials.new('M_Slushie')
    mat.use_nodes = True
    mat.surface_render_method = 'DITHERED'
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = 'Closest'
    tex.location = (-500,100)
    bs = nodes.new('ShaderNodeBsdfPrincipled')
    bs.location = (0,100)
    bs.inputs['Roughness'].default_value = .32
    links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    links.new(tex.outputs['Alpha'], bs.inputs['Alpha'])
    rough = nodes.new('ShaderNodeMath')
    rough.operation = 'MULTIPLY_ADD'
    rough.inputs[1].default_value = .27
    rough.inputs[2].default_value = .17
    rough.location = (-230,-140)
    links.new(tex.outputs['Alpha'], rough.inputs[0])
    links.new(rough.outputs[0], bs.inputs['Roughness'])
    output = nodes.new('ShaderNodeOutputMaterial')
    output.location = (330,100)
    links.new(bs.outputs[0],output.inputs['Surface'])
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
        p.use_smooth = False
    for m in list(bpy.data.materials):
        if m.name.startswith('Preview_') and m.users == 0:
            bpy.data.materials.remove(m)
    me.update()
    mat.update_tag()
    bpy.context.view_layer.update()
    return {'faces':count,'atlas_columns':cols,'atlas_rows':rows}


def collision():
    bm = bmesh.new()
    # 20 support points: eight floor, eight shoulder, four around the straw tip.
    for z, radius in ((0, .027), (.110, .041)):
        for i in range(8):
            a = 2*math.pi*i/8
            bm.verts.new((radius*math.cos(a),radius*math.sin(a),z))
    for x, y in ((.015,-.024),(.025,-.024),(.025,-.012),(.015,-.012)):
        bm.verts.new((x,y,.15))
    hull = bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
    unused = list(hull.get('geom_interior', [])) + list(hull.get('geom_unused', []))
    if unused:
        bmesh.ops.delete(bm, geom=unused, context='VERTS')
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new('Slushie_Collision_Mesh')
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new('Slushie_Collision', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.hide_render = True
    ob.display_type = 'WIRE'
    ob.hide_set(True)
    return ob


def export_asset():
    body = bpy.data.objects['Slushie_Body']
    col = bpy.data.objects['Slushie_Collision']
    bpy.ops.object.select_all(action='DESELECT')
    col.hide_set(False)
    body.select_set(True)
    col.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(filepath=OUT + '/Slushie.fbx', use_selection=True,
        object_types={'MESH'}, global_scale=1.0, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_space_transform=True, mesh_smooth_type='OFF',
        use_triangles=True, use_tspace=True, bake_anim=False, add_leaf_bones=False,
        path_mode='RELATIVE', embed_textures=False)
    col.hide_set(True)
    active(body)
    # Preview lighting is retained in the build code and saved stage renders.
    # Keep only applied asset meshes and the untouched reference in the blend.
    for helper in list(bpy.data.objects):
        if helper.name.startswith('Slushie_Preview'):
            bpy.data.objects.remove(helper, do_unlink=True)
    bpy.data.images['T_Slushie_BaseColor.png'].filepath = '//T_Slushie_BaseColor.png'
    bpy.ops.wm.save_as_mainfile(filepath=OUT + '/Slushie.blend', check_existing=False,
                              relative_remap=False)
    return {o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in (body,col)}


def score():
    p = subprocess.run(['/Applications/Blender.app/Contents/MacOS/Blender', '-b',
        OUT+'/Slushie.blend', '--python', REPO+'/art/tools/score.py', '--',
        OUT+'/spec.json'], stdin=subprocess.DEVNULL, capture_output=True, text=True,
        timeout=300)
    with open(OUT+'/scorer.log','w') as f:
        f.write(p.stdout+'\n'+p.stderr)
    return {'exit':p.returncode,'log':p.stdout[-4000:]}


if __name__ == '__main__':
    setup_scene()
    blockout(False)
    render_stage('01-blockout.png')
    blockout(True)
    render_stage('02-detail.png')
    texture_atlas()
    render_stage('03-texture.png')
    collision()
    render_stage('04-final.png')
    print(export_asset())
    print(score())

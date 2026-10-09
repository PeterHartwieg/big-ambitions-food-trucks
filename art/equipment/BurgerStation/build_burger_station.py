"""Regenerate in Blender with: exec(compile(open(__file__).read(), __file__, 'exec')).

Run this file with Blender --python to build, render the four stages and export.
Uses the current metric scene, preserving HumanRef_1.8m. Front is -Y; left +X.
All output is confined to this file's folder. No external images are required.
"""
import bpy
import bmesh
import math
import os
import numpy as np
from mathutils import Vector, Matrix

OUT = os.path.dirname(os.path.abspath(__file__))
PALETTE = {
    'steel': (0.52, 0.57, 0.62, 1),
    'edge': (0.72, 0.76, 0.79, 1),
    'side': (0.39, 0.44, 0.49, 1),
    'charcoal': (0.075, 0.081, 0.087, 1),
    'grate': (0.15, 0.16, 0.17, 1),
    'red': (0.63, 0.028, 0.045, 1),
    'black': (0.035, 0.042, 0.05, 1),
    'wire': (0.61, 0.65, 0.69, 1),
    'oil': (0.24, 0.17, 0.065, 1),
    'green': (0.36, 0.63, 0.12, 1),
}
KEYS = list(PALETTE)
verts, faces, colors = [], [], []


def select_only(ob):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    ob.hide_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def append_bm(bm, color):
    bm.normal_update()
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    offset = len(verts)
    verts.extend(tuple(v.co) for v in bm.verts)
    for f in bm.faces:
        faces.append(tuple(offset + v.index for v in f.verts))
        colors.append(KEYS.index(color))
    bm.free()


def box(center, dims, color='steel', bevel=0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    for v in bm.verts:
        v.co = Vector(tuple(center[i] + v.co[i] * dims[i] for i in range(3)))
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel,
                        segments=1, affect='EDGES', profile=0.5, clamp_overlap=True)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    # Split only caps that exceed four sides; the final export triangulates all.
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    append_bm(bm, color)


def cylinder(center, radius, depth, color, axis='Y', sides=10):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=True, segments=sides,
                          radius1=radius, radius2=radius, depth=depth)
    rot = Matrix.Rotation(math.pi / 2, 3, 'X') if axis == 'Y' else Matrix.Identity(3)
    for v in bm.verts:
        v.co = rot @ v.co + Vector(center)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    append_bm(bm, color)


def beam(a, b, width, color='wire'):
    a, b = Vector(a), Vector(b)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    rot = (b-a).to_track_quat('Z', 'Y').to_matrix()
    for v in bm.verts:
        v.co = rot @ Vector((v.co.x*width, v.co.y*width, v.co.z*(b-a).length)) + (a+b)/2
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    append_bm(bm, color)


def sync_body():
    ob = bpy.data.objects.get('BurgerStation_Body')
    mesh = bpy.data.meshes.new('BurgerStation_Mesh')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    if ob is None:
        ob = bpy.data.objects.new('BurgerStation_Body', mesh)
        bpy.context.scene.collection.objects.link(ob)
    else:
        old = ob.data
        ob.data = mesh
        if old.users == 0:
            bpy.data.meshes.remove(old)
    for key in KEYS:
        mat = bpy.data.materials.get('Preview_' + key)
        if not mat:
            mat = bpy.data.materials.new('Preview_' + key)
            mat.diffuse_color = PALETTE[key]
            mat.use_nodes = True
            shader = mat.node_tree.nodes.get('Principled BSDF')
            shader.inputs['Base Color'].default_value = PALETTE[key]
            shader.inputs['Roughness'].default_value = 0.48
            shader.inputs['Metallic'].default_value = 0.25 if key in ('steel','edge','side','wire') else 0.0
        mesh.materials.append(mat)
    attr = mesh.attributes.new('PartColor', 'INT', 'FACE')
    for i, p in enumerate(mesh.polygons):
        p.material_index = colors[i]
        attr.data[i].value = colors[i]
        p.use_smooth = False
    ob.location = (0, 0, 0)
    ob.rotation_euler = (0, 0, 0)
    ob.scale = (1, 1, 1)
    select_only(ob)
    return ob


def setup_presentation():
    scene = bpy.context.scene
    os.makedirs(OUT + '/renders', exist_ok=True)
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 40
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 850
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX'
    scene.world.use_nodes = True
    scene.world.node_tree.nodes.get('Background').inputs[0].default_value = (0.19, 0.22, 0.26, 1)
    scene.world.node_tree.nodes.get('Background').inputs[1].default_value = 0.65
    # Presentation contains no extra mesh; the reference remains untouched.
    cam_data = bpy.data.cameras.new('BurgerStation_ReviewCamera')
    cam = bpy.data.objects.new('BurgerStation_ReviewCamera', cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.61, -0.92, 0.68)
    cam.rotation_euler = (Vector((0, 0, 0.118))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 0.58
    scene.camera = cam
    for name, loc, power, size in [
        ('Key', (0.3,-0.5,0.8), 28, 0.65),
        ('Fill', (-0.6,-0.1,0.45), 13, 0.5),
        ('Rim', (0.15,0.5,0.65), 22, 0.5),
    ]:
        ld = bpy.data.lights.new('BurgerStation_' + name, 'AREA')
        lo = bpy.data.objects.new(ld.name, ld)
        scene.collection.objects.link(lo)
        lo.location = loc
        lo.rotation_euler = (Vector((0,0,0.11))-lo.location).to_track_quat('-Z','Y').to_euler()
        ld.energy = power
        ld.shape = 'DISK'
        ld.size = size
    # A framed customer view in the interactive scene too.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                region = area.spaces.active.region_3d
                region.view_distance = 0.8
                region.view_location = (0, 0, 0.12)
                region.view_rotation = cam.rotation_euler.to_quaternion()
                region.view_perspective = 'ORTHO'
                area.spaces.active.shading.color_type = 'MATERIAL'


def render_stage(filename):
    bpy.context.scene.render.filepath = OUT + '/renders/' + filename
    bpy.ops.render.render(write_still=True)


def blockout():
    global verts, faces, colors
    verts, faces, colors = [], [], []
    # Only remove previously generated content when regenerating.
    for ob in list(bpy.data.objects):
        if ob.name.startswith('BurgerStation_'):
            bpy.data.objects.remove(ob, do_unlink=True)
    # Flat full-width underside includes the placement origin.
    box((0, 0, 0.009), (0.386, 0.330, 0.018), 'black')
    box((0, 0.006, 0.065), (0.396, 0.332, 0.110), 'steel', 0.004)
    # Grill zone: x -0.057 .. +0.19 (customer's left two thirds).
    box((0.066,-0.006,0.122), (0.246,0.270,0.013), 'charcoal')
    box((0.067,-0.150,0.129), (0.257,0.028,0.025), 'edge', 0.003)
    box((0.187,0.0,0.158), (0.018,0.309,0.078), 'side')
    box((-0.062,0.0,0.147), (0.016,0.309,0.056), 'edge')
    box((0.065,0.156,0.173), (0.255,0.024,0.108), 'side', 0.003)
    # Fryer is an open steel well with an amber oil surface below the lip.
    box((-0.134,-0.037,0.119), (0.109,0.223,0.018), 'oil')
    box((-0.189,-0.024,0.139), (0.014,0.270,0.039), 'edge')
    box((-0.133,-0.152,0.138), (0.122,0.020,0.038), 'edge', 0.002)
    box((-0.132,0.106,0.14), (0.120,0.020,0.043), 'edge')
    # Fryer control head at the back, analogous to the larger game fryer.
    box((-0.132,0.143,0.189), (0.122,0.052,0.138), 'steel', 0.003)
    sync_body()
    setup_presentation()


def detail():
    # Strong ribbed silhouette: 19 cast-iron grill bars, each closed.
    for x in np.linspace(-0.045,0.173,19):
        box((float(x),-0.007,0.134), (0.0062,0.264,0.011), 'grate')
    # Rolled steel top edges and back lip.
    box((0.188,-0.003,0.199), (0.019,0.301,0.006), 'edge')
    box((0.065,0.151,0.227), (0.255,0.025,0.006), 'edge')
    # Three red rings with black octagonal knobs and pale indicator blades.
    for x in (0.153,0.067,-0.019):
        cylinder((x,-0.164,0.068), 0.016, 0.004, 'red')
        cylinder((x,-0.169,0.068), 0.012, 0.011, 'black')
        box((x,-0.1755,0.069), (0.0025,0.002,0.018), 'edge')
        box((x+0.026,-0.161,0.069), (0.007,0.002,0.004), 'red')
    # Front seam and the dark fryer controller with a tiny green display.
    box((0,-0.1605,0.024), (0.378,0.0015,0.0025), 'side')
    box((-0.132,-0.162,0.077), (0.076,0.004,0.026), 'black')
    box((-0.125,-0.1645,0.080), (0.030,0.002,0.008), 'green')
    box((-0.157,-0.1645,0.079), (0.005,0.002,0.005), 'red')
    # Small panel on the rear head faces the customer, with no text/logos.
    box((-0.132,0.1145,0.224), (0.092,0.002,0.035), 'black')
    cylinder((-0.151,0.109,0.223), 0.010,0.010,'black',sides=8)
    box((-0.151,0.1035,0.224), (0.002,0.002,0.012),'edge')
    box((-0.113,0.1125,0.225), (0.027,0.002,0.009),'green')
    # One raised wire basket suspended over the well. Real open mesh sides.
    x0,x1 = -0.173,-0.091
    y0,y1 = -0.104,0.043
    z0,z1 = 0.150,0.198
    w = 0.0024
    for z in (z0,z1):
        beam((x0,y0,z),(x1,y0,z),w)
        beam((x0,y1,z),(x1,y1,z),w)
        beam((x0,y0,z),(x0,y1,z),w)
        beam((x1,y0,z),(x1,y1,z),w)
    for x in np.linspace(x0+0.014,x1-0.014,4):
        for y in (y0,y1):
            beam((x,y,z0),(x,y,z1),w)
        beam((x,y0,z0),(x,y1,z0),w)
    for y in np.linspace(y0+0.025,y1-0.025,4):
        for x in (x0,x1):
            beam((x,y,z0),(x,y,z1),w)
        beam((x0,y,z0),(x1,y,z0),w)
    # Two metal arms hook the basket onto the head; grip stays in footprint.
    for x in (-0.151,-0.113):
        beam((x,0.043,0.198),(x,0.093,0.272),0.004)
        beam((x,0.093,0.272),(x,0.151,0.272),0.004)
    for x in (-0.151,-0.113):
        box((x,0.132,0.263), (0.009,0.016,0.018), 'wire')
    box((-0.132,0.15,0.272), (0.062,0.014,0.014), 'black', 0.002)
    sync_body()


def uv_texture():
    ob = bpy.data.objects['BurgerStation_Body']
    select_only(ob)
    me = ob.data
    # Flat colour needs no surface detail. Give narrow wires the same useful
    # texel footprint as other faces, avoiding subpixel islands and colour bleed.
    # Pair triangles of the same colour along a shared UV diagonal. No overlaps.
    groups = []
    for key_index in range(len(KEYS)):
        tris = []
        for poly in me.polygons:
            if me.attributes['PartColor'].data[poly.index].value != key_index:
                continue
            if len(poly.vertices) == 4:
                groups.append(([poly], key_index))
            else:
                tris.append(poly)
        for i in range(0,len(tris),2):
            groups.append((tris[i:i+2],key_index))
    res = 512
    columns = math.ceil(math.sqrt(len(groups)))
    layer = me.uv_layers.active or me.uv_layers.new(name='UVMap')
    uv = layer.data
    pixels = np.zeros((res,res,4),dtype=np.float32)
    pixels[:,:,:] = (0.40,0.44,0.48,1)
    for index,(polys,key_index) in enumerate(groups):
        tx,ty = index % columns,index // columns
        x0,y0 = round(tx*res/columns),round(ty*res/columns)
        x1,y1 = round((tx+1)*res/columns),round((ty+1)*res/columns)
        pixels[y0:y1,x0:x1] = PALETTE[KEYS[key_index]]
        corners = [((x0+2)/res,(y0+2)/res), ((x1-2)/res,(y0+2)/res),
                   ((x1-2)/res,(y1-2)/res), ((x0+2)/res,(y1-2)/res)]
        for j,poly in enumerate(polys):
            coords = corners if len(poly.vertices) == 4 else ([corners[0],corners[1],corners[2]] if j == 0 else [corners[0],corners[2],corners[3]])
            for loop_index,coord in zip(poly.loop_indices,coords):
                uv[loop_index].uv = coord
    me.calc_loop_triangles()
    img = bpy.data.images.get('T_BurgerStation_BaseColor')
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new('T_BurgerStation_BaseColor',width=res,height=res,alpha=True)
    img.colorspace_settings.name = 'sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw = OUT + '/T_BurgerStation_BaseColor.png'
    img.file_format = 'PNG'
    img.save()
    img.filepath = OUT + '/T_BurgerStation_BaseColor.png'
    mat = bpy.data.materials.get('M_BurgerStation') or bpy.data.materials.new('M_BurgerStation')
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = 'Closest'
    tex.location = (-430,0)
    shader = nt.nodes.new('ShaderNodeBsdfPrincipled')
    shader.location = (-100,0)
    shader.inputs['Roughness'].default_value = 0.48
    shader.inputs['Metallic'].default_value = 0.20
    output = nt.nodes.new('ShaderNodeOutputMaterial')
    output.location = (240,0)
    nt.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    nt.links.new(shader.outputs[0],output.inputs['Surface'])
    me.materials.clear()
    me.materials.append(mat)
    for poly in me.polygons:
        poly.material_index = 0
    for preview in list(bpy.data.materials):
        if preview.name.startswith('Preview_') and preview.users == 0:
            bpy.data.materials.remove(preview)
    return {'triangles':len(me.loop_triangles), 'uv_layers':len(me.uv_layers)}


def finalize():
    ob = bpy.data.objects['BurgerStation_Body']
    select_only(ob)
    # Freeze triangulation so .blend and FBX have identical topology/counts.
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(ob.data)
    bm.free()
    # Conservative stepped envelope: hull encloses all visible vertices.
    # Simplified extremal points avoid excessive collider facets on wires.
    bm = bmesh.new()
    for z,x0,x1,y0,y1 in [(0,-.198,.198,-.178,.172),
                          (.233,-.198,.198,-.178,.172),
                          (.280,-.196,-.068,.086,.172)]:
        for x in (x0,x1):
            for y in (y0,y1):
                bm.verts.new((x,y,z))
    hull = bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    interior = [g for g in hull['geom_interior'] if isinstance(g,bmesh.types.BMVert)]
    if interior:
        bmesh.ops.delete(bm,geom=interior,context='VERTS')
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    mesh = bpy.data.meshes.new('BurgerStation_Collision_Mesh')
    bm.to_mesh(mesh)
    bm.free()
    col = bpy.data.objects.new('BurgerStation_Collision',mesh)
    bpy.context.scene.collection.objects.link(col)
    col.hide_render = True
    col.display_type = 'WIRE'
    col.location = (0,0,0)
    col.rotation_euler = (0,0,0)
    col.scale = (1,1,1)
    select_only(ob)
    col.select_set(True)
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=OUT + '/BurgerStation.blend')
    bpy.data.images['T_BurgerStation_BaseColor'].filepath = '//T_BurgerStation_BaseColor.png'
    bpy.ops.export_scene.fbx(filepath=OUT + '/BurgerStation.fbx', use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
    col.hide_set(True)
    # Save with the collider hidden in the interactive viewport as well.
    bpy.ops.wm.save_as_mainfile(filepath=OUT + '/BurgerStation.blend')
    return {o.name:len(o.data.polygons) for o in (ob,col)}


def finish_scene():
    # Saved asset contains only the two meshes and untouched scale reference.
    for item in list(bpy.data.objects):
        if item.name.startswith('BurgerStation_') and item.type != 'MESH':
            bpy.data.objects.remove(item, do_unlink=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=OUT + '/BurgerStation.blend')


def build_all():
    blockout()
    render_stage('01-blockout.png')
    detail()
    render_stage('02-detail.png')
    uv_texture()
    render_stage('03-texture.png')
    finalize()
    render_stage('04-final.png')
    finish_scene()


if __name__ == '__main__':
    build_all()

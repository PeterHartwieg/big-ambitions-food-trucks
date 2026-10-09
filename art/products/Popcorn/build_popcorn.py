"""Regenerate in the existing metric start scene: build('final').

All generated files stay beside this script. HumanRef_1.8m is preserved.
The front is -Y. Geometry is authored in metres at the floor origin.
"""
import bpy
import bmesh
import math
import random
from pathlib import Path
from mathutils import Vector, Euler
import numpy as np

OUT = Path(__file__).resolve().parent
PALETTE = [(0.79, 0.045, 0.065, 1), (0.97, 0.945, 0.85, 1),
           (1.0, 0.95, 0.72, 1), (1.0, 0.985, 0.88, 1),
           (0.93, 0.80, 0.46, 1)]


def select(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.hide_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def make_body(detail):
    verts, faces, colors = [], [], []
    def face(vs, color):
        faces.append(vs)
        colors.append(color)
    n = 16
    # A sealed vessel; its pale top is hidden beneath the popcorn.
    for r, z in [(0.037, 0), (0.0505, 0.103)]:
        for i in range(n):
            a = 2 * math.pi * i / n - math.pi / 2 - math.pi / n
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for i in range(n):
        j = (i + 1) % n
        face((i, j, n+j, n+i), i % 2)
    for i in range(1, n-1):
        face((0, i+1, i), 1)
        face((n, n+i, n+i+1), 2)
    if detail:
        # Closed four-sided rolled paper lip, just 3 mm thick.
        start = len(verts)
        for r, z in [(0.0505, 0.1015), (0.053, 0.104),
                     (0.0505, 0.1065), (0.049, 0.104)]:
            for i in range(n):
                a = 2 * math.pi * i / n - math.pi / 2 - math.pi / n
                verts.append((r*math.cos(a), r*math.sin(a), z))
        for k in range(4):
            for i in range(n):
                j = (i+1) % n
                face((start+k*n+i, start+k*n+j,
                      start+((k+1)%4)*n+j, start+((k+1)%4)*n+i), 1)
    # The blockout uses a simple dome. Detail replaces it with 20 puffs.
    bm = bmesh.new()
    if detail:
        # A closed twenty-triangle puff. Broad lobes stay legible in a tiny
        # stock-box icon; two recessed vertices suggest the split kernel.
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1)
        bm.verts.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        template_v = [v.co.copy() for v in bm.verts]
        template_f = [tuple(v.index for v in f.verts) for f in bm.faces]
        rng = random.Random(28)
        placements = []
        # Eleven shoulder puffs, seven on the next tier, two at the crown.
        for count, radius, z, size, phase in [(11, .040, .112, .0143, .08),
                                             (7, .025, .129, .0148, .31),
                                             (2, .010, .137, .0122, .6)]:
            for i in range(count):
                a = 2*math.pi*i/count + phase
                placements.append((radius*math.cos(a), radius*math.sin(a),
                                   z+rng.uniform(-.0015,.0015), size))
        for ki, (x,y,z,s) in enumerate(placements):
            start = len(verts)
            rot = Euler((rng.random()*3, rng.random()*3, rng.random()*6)).to_matrix()
            # Alternating swollen vertices make asymmetric butterfly kernels.
            for vi, v in enumerate(template_v):
                bulge = (.58 if vi in (1,7) else 1.14 if vi%3==0 else .98)
                q = rot @ Vector((v.x*s*bulge, v.y*s*bulge*.93, v.z*s*bulge*.83))
                verts.append((x+q.x, y+q.y, min(z+q.z, .149)))
            for fi, f in enumerate(template_f):
                # Cream lobes with a few warm buttery recesses, all flat blocks.
                color = 4 if fi == (ki*3)%20 else (2 if (fi+ki)%5 == 0 else 3)
                face(tuple(start+i for i in f), color)
    else:
        bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=6, radius=1)
        bm.verts.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        start=len(verts)
        for v in bm.verts:
            verts.append((v.co.x*.050, v.co.y*.050, .109+v.co.z*.039))
        for f in bm.faces:
            face(tuple(start+v.index for v in f.verts), 3)
    bm.free()
    me=bpy.data.meshes.new('Popcorn_Mesh')
    me.from_pydata(verts, [], faces)
    me.update()
    attr=me.attributes.new('PartColor', 'INT', 'FACE')
    for p, c in zip(me.polygons, colors):
        attr.data[p.index].value=c
    bm=bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    ob=bpy.data.objects.new('Popcorn_Body', me)
    bpy.context.scene.collection.objects.link(ob)
    ca=me.color_attributes.new(name='PreviewColor', type='FLOAT_COLOR', domain='CORNER')
    for p in me.polygons:
        c=PALETTE[me.attributes['PartColor'].data[p.index].value]
        # Color attributes store linear values; palette itself is sRGB.
        c=tuple(((v+.055)/1.055)**2.4 if v>.04045 else v/12.92 for v in c[:3])+(1,)
        for li in p.loop_indices:
            ca.data[li].color=c
    return ob


def material(ob):
    mat=bpy.data.materials.get('M_Popcorn') or bpy.data.materials.new('M_Popcorn')
    mat.use_nodes=True
    mat.node_tree.nodes.clear()
    out=mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    shader=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Roughness'].default_value=.84
    shader.inputs['Specular IOR Level'].default_value=.18
    attr=mat.node_tree.nodes.new('ShaderNodeVertexColor')
    attr.layer_name='PreviewColor'
    mat.node_tree.links.new(attr.outputs['Color'], shader.inputs['Base Color'])
    mat.node_tree.links.new(shader.outputs['BSDF'],out.inputs['Surface'])
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    return mat, shader


def texture(ob, mat, shader):
    # Every triangle has a unique, generously padded atlas address. Flat
    # colours need no directional texture detail or shared/stacked UVs.
    # 16 x 20 cells, two separate triangular islands per cell.
    me=ob.data
    uv=me.uv_layers.new(name='UVMap')
    pixels=np.zeros((256,256,4), dtype=np.float32)
    pixels[:]=PALETTE[1]
    slot=0
    for ci,color in enumerate(PALETTE):
        # Pair only equally coloured triangles. The entire cell is painted
        # their colour, including padding and the common diagonal.
        group=[p for p in me.polygons if me.attributes['PartColor'].data[p.index].value==ci]
        for p in group:
            cell=slot//2
            col=cell%16
            row=cell//16
            x0,x1=col*16,(col+1)*16
            y0,y1=row*12,(row+1)*12
            pad=1.5
            if slot%2==0:
                coords=[(x0+pad,y0+pad),(x1-pad,y0+pad),(x0+pad,y1-pad)]
            else:
                coords=[(x1-pad,y1-pad),(x0+pad,y1-pad),(x1-pad,y0+pad)]
            for li, xy in zip(p.loop_indices,coords):
                uv.data[li].uv=(xy[0]/256,xy[1]/256)
            pixels[y0:y1,x0:x1]=color
            slot+=1
        if slot%2:
            slot+=1
    img=bpy.data.images.get('T_Popcorn_BaseColor')
    if img:
        bpy.data.images.remove(img)
    img=bpy.data.images.new('T_Popcorn_BaseColor', width=256, height=256, alpha=False)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=str(OUT/'T_Popcorn_BaseColor.png')
    img.file_format='PNG'
    img.save()
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image=img
    tex.interpolation='Closest'
    mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    for n in list(mat.node_tree.nodes):
        if n.type=='VERTEX_COLOR':
            mat.node_tree.nodes.remove(n)
    me.color_attributes.remove(me.color_attributes['PreviewColor'])


def collision():
    bm=bmesh.new()
    # Ten-vertex rings yield 36 triangles. A conservative tapered envelope
    # includes the popped dome without spending faces on kernel concavities.
    for radius,z in [(.041,0),(.064,.150)]:
        for i in range(10):
            a=2*math.pi*i/10
            bm.verts.new((radius*math.cos(a),radius*math.sin(a),z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('Popcorn_Hull')
    bm.to_mesh(me)
    bm.free()
    ob=bpy.data.objects.new('Popcorn_Collision',me)
    bpy.context.scene.collection.objects.link(ob)
    ob.hide_render=True
    ob.display_type='WIRE'
    ob.hide_set(True)
    return ob


def render(filename):
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.device='CPU'
    scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.render.resolution_x=900
    scene.render.resolution_y=900
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.13,.16,.20,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
    rigs=[]
    data=bpy.data.cameras.new('PreviewCamera')
    cam=bpy.data.objects.new('PreviewCamera',data)
    scene.collection.objects.link(cam)
    cam.location=(.225,-.39,.285)
    cam.rotation_euler=(Vector((0,0,.075))-cam.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO'
    data.ortho_scale=.205
    scene.camera=cam
    rigs.append(cam)
    for name,loc,power,size in [('PreviewKey',(.12,-.20,.34),.8,.22),
                               ('PreviewFill',(-.2,-.05,.2),.3,.18),
                               ('PreviewRim',(.05,.2,.28),.6,.18)]:
        ld=bpy.data.lights.new(name,'AREA')
        ld.energy=power
        ld.shape='DISK'
        ld.size=size
        lo=bpy.data.objects.new(name,ld)
        scene.collection.objects.link(lo)
        lo.location=loc
        lo.rotation_euler=(Vector((0,0,.075))-lo.location).to_track_quat('-Z','Y').to_euler()
        rigs.append(lo)
    scene.render.filepath=str(OUT/'renders'/filename)
    bpy.ops.render.render(write_still=True)
    scene.camera=None
    for ob in rigs:
        data=ob.data
        bpy.data.objects.remove(ob,do_unlink=True)
        if isinstance(data,bpy.types.Camera):
            bpy.data.cameras.remove(data)
        else:
            bpy.data.lights.remove(data)


def export(body,hull):
    bpy.ops.object.select_all(action='DESELECT')
    hull.hide_set(False)
    for ob in (body,hull):
        ob.select_set(True)
    bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Popcorn.fbx'),use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    hull.hide_set(True)
    select(body)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Popcorn.blend'))


def build(stage='final'):
    global name_out
    (OUT/'renders').mkdir(exist_ok=True)
    if bpy.context.object and bpy.context.object.mode!='OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for n in ('Popcorn_Body','Popcorn_Collision'):
        ob=bpy.data.objects.get(n)
        if ob:
            bpy.data.objects.remove(ob,do_unlink=True)
    body=make_body(stage!='blockout')
    mat,shader=material(body)
    hull=collision()
    if stage in ('texture','final'):
        texture(body,mat,shader)
    select(body)
    # Frame the asset while preserving the reference object itself.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_location=(0,0,.075)
                area.spaces.active.region_3d.view_distance=.32
                area.spaces.active.region_3d.view_rotation=Euler((math.radians(65),0,math.radians(25))).to_quaternion()
                area.spaces.active.clip_start=.001
    name_out={'blockout':'01-blockout.png','detail':'02-detail.png',
              'texture':'03-uv-texture.png','final':'04-final.png'}[stage]
    render(name_out)
    if stage=='final':
        export(body,hull)
    else:
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Popcorn.blend'))
    return {'stage':stage,'body_triangles':len(body.data.polygons),
            'collision_triangles':len(hull.data.polygons), 'render':str(OUT/'renders'/name_out)}


if __name__=='__main__':
    print(build('final'))

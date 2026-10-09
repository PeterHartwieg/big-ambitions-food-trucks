"""Regenerate in the supplied start scene. Run with Blender's Python API.

STOP_AFTER (1..4) optionally selects a review stage; default builds/exports all.
The HumanRef object is preserved. All file writes stay in this asset folder.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector, Matrix

OUT = os.path.dirname(os.path.abspath(__file__))
STAGE = globals().get('STOP_AFTER', 4)
os.makedirs(OUT + '/renders', exist_ok=True)
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for ob in list(bpy.data.objects):
    if ob.name.startswith(('Griddle_', '_G_')):
        bpy.data.objects.remove(ob, do_unlink=True)

# Deliberately flat, neutral part colours. No lettering or decorative marks.
PALETTE = {
    'steel': (0.57, 0.64, 0.66, 1),
    'edge': (0.72, 0.77, 0.78, 1),
    'plate': (0.22, 0.27, 0.28, 1),
    'trough': (0.12, 0.16, 0.17, 1),
    'black': (0.035, 0.043, 0.045, 1),
    'meat': (0.31, 0.12, 0.055, 1),
    'meat_light': (0.43, 0.20, 0.085, 1),
    'cheese': (1.0, 0.62, 0.075, 1),
    'tortilla': (0.93, 0.69, 0.32, 1),
    'tortilla_edge': (0.74, 0.45, 0.16, 1),
}
temp = {}
for key, col in PALETTE.items():
    mat = bpy.data.materials.get('_G_' + key) or bpy.data.materials.new('_G_' + key)
    mat.diffuse_color = col
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = col
    bsdf.inputs['Roughness'].default_value = 0.72
    temp[key] = mat
parts = []

def mesh_part(label, verts, faces, color, bevel=0):
    me = bpy.data.meshes.new('_G_' + label)
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new('_G_' + label, me)
    bpy.context.collection.objects.link(ob)
    me.materials.append(temp[color])
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES')
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    parts.append(ob)
    return ob

def box(label, center, dims, color, bevel=0, rotation=0):
    c = Vector(center)
    rot = Matrix.Rotation(rotation, 3, 'Z')
    vs = [tuple(c + rot @ Vector((x*dims[0]/2,y*dims[1]/2,z*dims[2]/2)))
          for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                        (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    fs = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    return mesh_part(label, vs, fs, color, bevel)

def extrusion(label, outline, offset, color):
    n=len(outline)
    vs=list(outline) + [tuple(Vector(p)+Vector(offset)) for p in outline]
    fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    fs += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh_part(label,vs,fs,color)

def cylinder(label, center, radius, depth, color, axis='Z', n=12, top_radius=None):
    r2 = radius if top_radius is None else top_radius
    vs=[]
    for d,r in [(-depth/2,radius),(depth/2,r2)]:
        for i in range(n):
            a=2*math.pi*i/n
            p=(r*math.cos(a),r*math.sin(a),d)
            if axis=='Y': p=(p[0],p[2],p[1])
            vs.append(tuple(Vector(center)+Vector(p)))
    fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    fs += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh_part(label,vs,fs,color)

# 39 x 34 cm: a compact commercial countertop unit, about a person's torso width.
# The solid underside includes the placement pivot and lies exactly on Z=0.
box('chassis',(0,0,0.039),(0.386,0.324,0.078),'steel',0.002)
box('cooking_plate',(0,0.009,0.088),(0.390,0.306,0.020),'plate',0.0015)
box('back_guard',(0,0.165,0.138),(0.390,0.010,0.106),'steel',0.001)
# Side guards taper down at the front for a clear view of the cooking surface.
for s in (-1,1):
    x=s*0.190
    extrusion('side_guard',[(x,-0.135,0.098),(x,0.170,0.098),
                            (x,0.170,0.187),(x,0.096,0.187),
                            (x,-0.135,0.122)],(s*0.005,0,0),'steel')

if STAGE>=2:
    # Open, full-width grease gutter in front of the plate.
    box('gutter_bottom',(0,-0.154,0.076),(0.380,0.029,0.005),'trough')
    box('gutter_lip',(0,-0.171,0.084),(0.390,0.008,0.018),'edge',0.001)
    for x in (-0.190,0.190):
        box('gutter_end',(x,-0.154,0.084),(0.008,0.030,0.018),'edge')
    # Knobs face the customer at -Y. Mount rings and white raised pointer bars.
    for x in (-0.105,0.105):
        cylinder('knob_ring',(x,-0.164,0.041),0.026,0.006,'trough','Y',16)
        cylinder('knob',(x,-0.171,0.041),0.021,0.012,'black','Y',12,0.022)
        box('knob_pointer',(x,-0.178,0.049),(0.0035,0.002,0.014),'edge')
    # A generous heap of chopped beef. Polygonal chunks, not surface noise.
    chunks=[(0.047,-0.021,0.015,0.009),(0.067,-0.040,0.019,0.011),
            (0.092,-0.044,0.017,0.013),(0.117,-0.030,0.018,0.010),
            (0.138,-0.010,0.016,0.009),(0.143,0.011,0.015,0.012),
            (0.121,0.029,0.018,0.010),(0.096,0.032,0.016,0.013),
            (0.072,0.024,0.019,0.011),(0.047,0.007,0.015,0.010),
            (0.067,-0.013,0.021,0.017),(0.092,-0.023,0.019,0.020),
            (0.117,-0.009,0.022,0.015),(0.097,0.010,0.020,0.019),
            (0.073,0.007,0.015,0.020),(0.114,0.012,0.013,0.023)]
    for i,(x,y,r,h) in enumerate(chunks):
        cylinder('chopped_beef',(x,y,0.098+h/2),r,h,
                 'meat_light' if i%3==0 else 'meat',n=7,top_radius=r*0.72)
    for i,(x,y,a) in enumerate([(0.068,-0.019,.35),(0.105,-0.024,-.45),
                               (0.109,.012,.7),(0.075,.017,-.2)]):
        box('cheese',(x,y,0.118),(0.034,0.014,0.003),'cheese',rotation=a)
    # Two folded tortillas: solid semicircular panels with their straight folds
    # down on the plate and a shallow opening between the warm golden sides.
    for taco,cx,cy,a in [(0,-0.078,-0.055,-0.15),(1,-0.079,0.040,0.18)]:
        rot=Matrix.Rotation(a,3,'Z')
        for side in (-1,1):
            outline=[]
            for i in range(11):
                t=math.pi*i/10
                x=.042*math.cos(t)
                z=.042*math.sin(t)
                y=side*(.0025+.24*z)
                outline.append(tuple(Vector((cx,cy,.099))+rot@Vector((x,y,z))))
            extrusion('folded_tortilla',outline,tuple(rot@Vector((0,side*.003,0))),'tortilla')
        box('tortilla_fold',(cx,cy,.101),(.080,.008,.005),'tortilla_edge',rotation=a)
    # Resting offset spatula at the back, polished trapezoidal blade, dark grip.
    a=-.24
    rot=Matrix.Rotation(a,3,'Z')
    origin=Vector((.034,.078,.103))
    outline=[(-.023,-.029,0),(.023,-.029,0),(.026,.017,0),(-.026,.017,0)]
    extrusion('spatula_blade',[tuple(origin+rot@Vector(p)) for p in outline],(0,0,.003),'edge')
    box('spatula_neck',tuple(origin+rot@Vector((0,.034,.004))),(.010,.039,.005),'steel',rotation=a)
    box('spatula_handle',tuple(origin+rot@Vector((0,.063,.007))),(.020,.050,.011),'black',0.002,rotation=a)

bpy.ops.object.select_all(action='DESELECT')
for ob in parts: ob.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.object.join()
body=bpy.context.object
body.name='Griddle_Body'
body.data.name='Griddle_Body_Mesh'
body.location=(0,0,0)
body.rotation_euler=(0,0,0)
body.scale=(1,1,1)

if STAGE>=3:
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.151917,island_margin=0.005,
                             area_weight=0.0,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    me=body.data
    me.calc_loop_triangles()
    uv=me.uv_layers.active.data
    # Rasterise each island with its flat part colour and a two-pixel bleed.
    # UVs are unique for every surface, including hidden faces.
    res=512
    pixels=np.zeros((res,res,4),dtype=np.float32)
    pixels[:]=(.18,.22,.23,1)
    occupied=np.zeros((res,res),dtype=bool)
    for tri in me.loop_triangles:
        a,b,c=np.array([tuple(uv[l].uv) for l in tri.loops])*res
        x0=max(0,int(math.floor(min(a[0],b[0],c[0]))))
        x1=min(res-1,int(math.ceil(max(a[0],b[0],c[0]))))
        y0=max(0,int(math.floor(min(a[1],b[1],c[1]))))
        y1=min(res-1,int(math.ceil(max(a[1],b[1],c[1]))))
        d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(d)<1e-12: continue
        xs,ys=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
        u=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/d
        v=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/d
        mask=(u>=-1e-6)&(v>=-1e-6)&(1-u-v>=-1e-6)
        col=tuple(me.materials[me.polygons[tri.polygon_index].material_index].diffuse_color)
        pixels[y0:y1+1,x0:x1+1][mask]=col
        occupied[y0:y1+1,x0:x1+1]|=mask
    for _ in range(2):
        old=occupied.copy()
        for dy,dx in [(-1,0),(1,0),(0,-1),(0,1)]:
            shifted=np.roll(old,(dy,dx),(0,1))
            mask=shifted & ~occupied
            pixels[mask]=np.roll(pixels,(dy,dx),(0,1))[mask]
            occupied[mask]=True
    img=bpy.data.images.get('T_Griddle_BaseColor.png')
    if img: bpy.data.images.remove(img)
    img=bpy.data.images.new('T_Griddle_BaseColor.png',width=res,height=res,alpha=False)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=OUT+'/T_Griddle_BaseColor.png'
    img.file_format='PNG'
    img.save()
    img.filepath=OUT+'/T_Griddle_BaseColor.png'
    mat=bpy.data.materials.get('M_Griddle') or bpy.data.materials.new('M_Griddle')
    mat.use_nodes=True
    nodes=mat.node_tree.nodes
    nodes.clear()
    output=nodes.new('ShaderNodeOutputMaterial')
    bsdf=nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value=.67
    tex=nodes.new('ShaderNodeTexImage')
    tex.image=img
    tex.interpolation='Linear'
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
    me.materials.clear()
    me.materials.append(mat)
    for poly in me.polygons: poly.material_index=0

if STAGE>=4:
    # A low-sided enclosing hull derived from the envelope corners. bmesh
    # convex_hull guarantees a convex, closed collision surface (12 triangles).
    bm=bmesh.new()
    for x in (-.197,.197):
        for y in (-.180,.172):
            for z in (0,.192): bm.verts.new((x,y,z))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    for key in ('geom_interior','geom_unused'):
        vs=[g for g in hull.get(key,[]) if isinstance(g,bmesh.types.BMVert) and g.is_valid]
        if vs: bmesh.ops.delete(bm,geom=vs,context='VERTS')
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('Griddle_Collision_Mesh')
    bm.to_mesh(me)
    bm.free()
    collision=bpy.data.objects.new('Griddle_Collision',me)
    bpy.context.collection.objects.link(collision)
    collision.hide_render=True
    collision.display_type='WIRE'
    collision.hide_set(True)

# Review presentation: camera from customer-facing -Y, 30 degrees above plate.
scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.device='CPU'
scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1000
scene.render.resolution_y=800
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.film_transparent=False
scene.world.use_nodes=True
scene.world.node_tree.nodes.get('Background').inputs[0].default_value=(.08,.10,.13,1)
scene.world.node_tree.nodes.get('Background').inputs[1].default_value=.6
scene.view_settings.view_transform='AgX'
cam_data=bpy.data.cameras.new('_G_Camera')
cam=bpy.data.objects.new('_G_Camera',cam_data)
scene.collection.objects.link(cam)
cam.location=(.53,-.84,.63)
cam.rotation_euler=(Vector((0,0,.083))-cam.location).to_track_quat('-Z','Y').to_euler()
cam_data.type='ORTHO'
cam_data.ortho_scale=.56
scene.camera=cam
for name,loc,power,size in [('Key',(.0,-.5,.9),5,.65),('Fill',(-.5,-.1,.4),2,.5),('Rim',(.2,.5,.6),4,.5)]:
    data=bpy.data.lights.new('_G_'+name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    ob=bpy.data.objects.new('_G_'+name,data)
    scene.collection.objects.link(ob)
    ob.location=loc
    ob.rotation_euler=(Vector((0,0,.08))-ob.location).to_track_quat('-Z','Y').to_euler()

if STAGE>=4:
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    collision.hide_set(False)
    collision.select_set(True)
    bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT+'/Griddle.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    collision.select_set(False)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Griddle.blend')
    img.filepath='//T_Griddle_BaseColor.png'
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Griddle.blend')

names={1:'01-blockout',2:'02-detail',3:'03-uv-texture',4:'04-final'}
scene.render.filepath=OUT+'/renders/'+names[STAGE]+'.png'
bpy.ops.render.render(write_still=True)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active
            space.region_3d.view_perspective='ORTHO'
            space.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
            space.region_3d.view_location=(0,0,.083)
            space.region_3d.view_distance=.70
            space.clip_start=.01
            space.overlay.show_overlays=False
            space.shading.type='SOLID'
            space.shading.color_type='TEXTURE'
if STAGE>=4:
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Griddle.blend')
result={'stage':STAGE,'body_triangles':len(body.data.polygons),'dimensions':tuple(body.dimensions)}

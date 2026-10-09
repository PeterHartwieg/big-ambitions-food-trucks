"""Run in the open metric Blender scene; preserves HumanRef_1.8m.

build_blockout(); add_detail(); texture_and_uv(); finish()
Each stage can be inspected before continuing. All output stays beside this file.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
PALETTE = {
    'Steel': (0.56, 0.62, 0.66, 1),
    'Edge': (0.76, 0.81, 0.84, 1),
    'Dark': (0.045, 0.065, 0.085, 1),
    'Blue': (0.015, 0.43, 0.95, 1),
    'BlueRim': (0.32, 0.75, 1.0, 1),
    'Red': (0.94, 0.045, 0.08, 1),
    'RedRim': (1.0, 0.42, 0.46, 1),
    'White': (0.94, 0.955, 0.96, 1),
}
PARTS = []

def active(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def colour(ob, key):
    name = 'SM_Stage_' + key
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = PALETTE[key]
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = PALETTE[key]
    bs.inputs['Roughness'].default_value = 0.72
    ob.data.materials.append(mat)
    PARTS.append(ob)
    return ob

def cube(name, loc, dims, key, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.dimensions = dims
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel:
        mod = ob.modifiers.new('Silhouette chamfer', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return colour(ob, key)

def rings(name, centre, profiles, key, n=16):
    # Each profile is (height, x radius, y radius); capped closed solids.
    vs = [(centre[0] + rx*math.cos(2*math.pi*i/n),
           centre[1] + ry*math.sin(2*math.pi*i/n), z)
          for z, rx, ry in profiles for i in range(n)]
    fs = [tuple(reversed(range(n)))]
    for j in range(len(profiles)-1):
        for i in range(n):
            k = (i+1) % n
            fs.append((j*n+i, j*n+k, (j+1)*n+k, (j+1)*n+i))
    fs.append(tuple(range((len(profiles)-1)*n, len(profiles)*n)))
    me = bpy.data.meshes.new(name)
    me.from_pydata(vs, [], fs)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return colour(ob, key)

def cylinder(name, a, b, radius, key, n=8):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=radius,
                                      depth=(b-a).length, location=(a+b)/2)
    ob = bpy.context.object
    ob.name = name
    ob.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return colour(ob, key)

def camera_setup():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (0.105, 0.125, 0.155, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = 0.45
    cam_data = bpy.data.cameras.new('SM_ReviewCamera')
    cam = bpy.data.objects.new('SM_ReviewCamera', cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.85, -1.6, 1.0)
    cam.rotation_euler = (Vector((0, 0, 0.30))-cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 0.81
    scene.camera = cam
    for name, loc, energy, size in [
        ('Key', (0.0,-1.8,2.3), 60, 2.0),
        ('Fill', (1.6,-0.5,1.3), 30, 1.8),
        ('Rim', (-1.2,1.0,1.7), 45, 1.4)]:
        data = bpy.data.lights.new('SM_'+name, 'AREA')
        data.energy, data.shape, data.size = energy, 'DISK', size
        ob = bpy.data.objects.new('SM_'+name, data)
        scene.collection.objects.link(ob)
        ob.location = loc
        ob.rotation_euler = (Vector((0,0,0.3))-ob.location).to_track_quat('-Z','Y').to_euler()
    # Keep the reference in view when inspecting scale, without changing it.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                space = area.spaces.active
                space.shading.type = 'MATERIAL'
                space.region_3d.view_rotation = cam.rotation_euler.to_quaternion()
                space.region_3d.view_location = (0,0,0.30)
                space.region_3d.view_distance = 1.25

def save_stage(filename):
    os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)
    bpy.context.scene.render.filepath = os.path.join(OUT,'renders',filename)
    bpy.ops.render.render(write_still=True)

def build_blockout():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    # On regeneration, remove only this build's objects, never the reference.
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('SM_', 'SlushieMachine_')):
            bpy.data.objects.remove(ob, do_unlink=True)
    for datablocks in [bpy.data.meshes, bpy.data.cameras, bpy.data.lights, bpy.data.materials, bpy.data.images]:
        for data in list(datablocks):
            if data.users == 0 and data.name.startswith(('SM_', 'SlushieMachine_', 'M_SlushieMachine', 'T_SlushieMachine')):
                datablocks.remove(data)
    PARTS.clear()
    cube('SM_FootPlinth', (0,0,0.009), (0.39,0.35,0.018), 'Dark', 0.003)
    cube('SM_SteelCabinet', (0,0.066,0.148), (0.376,0.212,0.26), 'Steel', 0.009)
    cube('SM_UpperDeck', (0,0.032,0.276), (0.384,0.284,0.018), 'Edge', 0.004)
    for x, k in [(0.098,'Blue'),(-0.098,'Red')]:
        rings('SM_'+k+'BowlSeat', (x,0.022), [(0.283,0.086,0.111),(0.300,0.086,0.111)], 'Dark',12)
        bowl = rings('SM_'+k+'Bowl', (x,0.022),
              [(0.295,0.066,0.086),(0.314,0.081,0.108),
               (0.526,0.081,0.108),(0.545,0.077,0.103)], k)
        rings('SM_'+k+'LightRim', (x,0.022), [(0.532,0.082,0.109),(0.552,0.080,0.107)], k+'Rim')
        rings('SM_'+k+'WhiteLid', (x,0.022),
              [(0.548,0.089,0.118),(0.564,0.089,0.118),(0.578,0.077,0.105)], 'White')
    camera_setup()
    save_stage('01-blockout.png')

def tray(x):
    # An eight-sided rectangular basin: raised rim, lowered solid floor.
    points = [(-1,-0.78),(-0.82,-1),(0.82,-1),(1,-0.78),
              (1,0.78),(0.82,1),(-0.82,1),(-1,0.78)]
    vs=[]
    for z,rx,ry in [(0.018,0.081,0.065),(0.049,0.081,0.065),
                    (0.049,0.070,0.054),(0.035,0.070,0.054)]:
        vs.extend((x+u*rx,-0.105+v*ry,z) for u,v in points)
    fs=[tuple(reversed(range(8)))]
    for j in range(3):
        for i in range(8):
            k=(i+1)%8
            fs.append((j*8+i,j*8+k,(j+1)*8+k,(j+1)*8+i))
    fs.append(tuple(range(24,32)))
    me=bpy.data.meshes.new('SM_Tray')
    me.from_pydata(vs,[],fs)
    ob=bpy.data.objects.new('SM_Tray',me)
    bpy.context.collection.objects.link(ob)
    colour(ob,'Edge')
    colour(ob,'Dark')
    PARTS.pop()  # colour also registers the existing part a second time.
    for p in me.polygons:
        if p.index >= 17:
            p.material_index=1
    for dx in [-0.043,0,0.043]:
        cube('SM_TrayGrate',(x+dx,-0.105,0.040),(0.012,0.104,0.008),'Steel')

def add_detail():
    for x,k in [(0.098,'Blue'),(-0.098,'Red')]:
        tray(x)
        cylinder('SM_TapFlange',(x,-0.080,0.307),(x,-0.105,0.307),0.026,'Edge')
        cylinder('SM_TapValve',(x,-0.102,0.307),(x,-0.137,0.307),0.020,'Dark')
        # Closed low-poly elbow, pointing towards the customer then down.
        vs=[]
        for y,z,theta in [(-0.126,0.294,0),(-0.153,0.280,math.pi/4),(-0.158,0.242,math.pi/2)]:
            for i in range(8):
                a=i*math.pi/4
                vs.append((x+0.012*math.cos(a), y-0.012*math.sin(a)*math.sin(theta),
                           z+0.012*math.sin(a)*math.cos(theta)))
        fs=[tuple(reversed(range(8)))]
        for j in range(2):
            for i in range(8):
                k2=(i+1)%8
                fs.append((j*8+i,j*8+k2,(j+1)*8+k2,(j+1)*8+i))
        fs.append(tuple(range(16,24)))
        me=bpy.data.meshes.new('SM_Spout')
        me.from_pydata(vs,[],fs)
        ob=bpy.data.objects.new('SM_Spout',me)
        bpy.context.collection.objects.link(ob)
        colour(ob,'Edge')
        cylinder('SM_NozzleOutlet',(x,-0.158,0.241),(x,-0.158,0.243),0.009,'Dark')
        cube('SM_TapLever',(x,-0.126,0.343),(0.013,0.014,0.062),'Dark')
        cube('SM_TapGrip',(x,-0.128,0.372),(0.042,0.027,0.020),'White',0.004)
        cube('SM_LidGrip',(x,0.022,0.584),(0.045,0.022,0.012),'White')
    cube('SM_FrontControlPanel',(0,-0.042,0.154),(0.280,0.005,0.057),'Dark',0.001)
    for x in [-0.098,0.098]:
        cube('SM_ControlToggle',(x,-0.046,0.154),(0.022,0.006,0.024),'White')
    for side in [-1,1]:
        for z in [0.09,0.112,0.134]:
            cube('SM_CoolingVent',(side*0.188,0.072,z),(0.0015,0.115,0.008),'Dark')
    save_stage('02-detail.png')

def texture_and_uv():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in PARTS:
        ob.select_set(True)
    bpy.context.view_layer.objects.active=PARTS[0]
    bpy.ops.object.join()
    body=bpy.context.object
    body.name='SlushieMachine_Body'
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    bm=bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    # Triangulate caps, retaining quads for efficient UV projection.
    bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>4])
    bm.to_mesh(body.data)
    bm.free()
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(65),island_margin=0.006,
                             margin_method='FRACTION',area_weight=0.0,scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Raster flat face colours into the atlas, with two pixels of edge padding.
    me=body.data
    me.calc_loop_triangles()
    uv=me.uv_layers.active.data
    labels=np.full((512,512),-1,dtype=np.int32)
    colors=np.array([tuple(m.diffuse_color) for m in me.materials],dtype=np.float32)
    for t in me.loop_triangles:
        a,b,c=np.array([tuple(uv[i].uv) for i in t.loops])*512
        x0=max(0,int(math.floor(min(a[0],b[0],c[0]))))
        x1=min(511,int(math.ceil(max(a[0],b[0],c[0]))))
        y0=max(0,int(math.floor(min(a[1],b[1],c[1]))))
        y1=min(511,int(math.ceil(max(a[1],b[1],c[1]))))
        d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(d)<1e-10: continue
        xs,ys=np.meshgrid(np.arange(x0,x1+1)+0.5,np.arange(y0,y1+1)+0.5)
        l1=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/d
        l2=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/d
        mask=(l1>=-1e-6)&(l2>=-1e-6)&((1-l1-l2)>=-1e-6)
        region=labels[y0:y1+1,x0:x1+1]
        region[mask]=me.polygons[t.polygon_index].material_index
    for _ in range(2):
        old=labels.copy()
        for dy,dx in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)]:
            shifted=np.roll(np.roll(old,dy,axis=0),dx,axis=1)
            if dy<0: shifted[dy:,:]=-1
            if dy>0: shifted[:dy,:]=-1
            if dx<0: shifted[:,dx:]=-1
            if dx>0: shifted[:,:dx]=-1
            take=(labels<0)&(shifted>=0)
            labels[take]=shifted[take]
    pixels=np.empty((512,512,4),dtype=np.float32)
    pixels[:]=(0.08,0.10,0.12,1)
    pixels[labels>=0]=colors[labels[labels>=0]]
    img=bpy.data.images.new('T_SlushieMachine_BaseColor',width=512,height=512,alpha=False)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=os.path.join(OUT,'T_SlushieMachine_BaseColor.png')
    img.file_format='PNG'
    img.save()
    mat=bpy.data.materials.get('M_SlushieMachine') or bpy.data.materials.new('M_SlushieMachine')
    mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Roughness'].default_value=0.72
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image=img
    tex.interpolation='Linear'
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index=0
        p.use_smooth=False
    save_stage('03-texture.png')

def finish():
    body=bpy.data.objects['SlushieMachine_Body']
    # A tapered convex envelope encloses the plinth, taps and bowl silhouettes.
    bm=bmesh.new()
    for z,rx,ymin,ymax in [(0,0.195,-0.175,0.175),
                           (0.280,0.195,-0.175,0.175),
                           (0.590,0.189,-0.099,0.144)]:
        for x in [-rx,rx]:
            for y in [ymin,ymax]:
                bm.verts.new((x,y,z))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[g for g in hull['geom_unused'] if isinstance(g,bmesh.types.BMVert)]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('SlushieMachine_Collision')
    bm.to_mesh(me)
    bm.free()
    collider=bpy.data.objects.new('SlushieMachine_Collision',me)
    bpy.context.scene.collection.objects.link(collider)
    collider.hide_render=True
    collider.display_type='WIRE'
    collider.hide_set(True)
    save_stage('04-final.png')
    # Relative base-colour reference ships alongside the FBX and .blend.
    for img in bpy.data.images:
        if img.name.startswith('T_SlushieMachine'):
            img.filepath='//T_SlushieMachine_BaseColor.png'
    bpy.ops.object.select_all(action='DESELECT')
    collider.hide_set(False)
    body.select_set(True)
    collider.select_set(True)
    bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT+'/SlushieMachine.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collider.hide_set(True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/SlushieMachine.blend')

if __name__=='__main__':
    build_blockout()
    add_detail()
    texture_and_uv()
    finish()

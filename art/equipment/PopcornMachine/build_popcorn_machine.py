"""Regenerate in the current metric Blender scene; preserves HumanRef_1.8m.

Run with Blender's Python console or --python. STAGE=1/2/3 selects a review
stage; default 4 creates the textured final asset and exports the FBX.
All generated files remain beside this script.
"""
import bpy, bmesh, math, random, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
STAGE = globals().get('STAGE', 4)
os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)
PALETTE = {
    'Red': (0.73, 0.045, 0.065, 1),
    'RedEdge': (0.90, 0.075, 0.09, 1),
    'RedDark': (0.42, 0.024, 0.038, 1),
    'Steel': (0.66, 0.72, 0.74, 1),
    'SteelLight': (0.83, 0.87, 0.86, 1),
    'Charcoal': (0.12, 0.145, 0.155, 1),
    'Corn': (1.0, 0.94, 0.76, 1),
    'CornLight': (1.0, 0.97, 0.83, 1),
    'CornGold': (0.98, 0.86, 0.58, 1),
}
parts = []
def linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4

def select(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def finish(ob, colour):
    ob.name = 'PM_Part'
    select(ob)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.data.materials.append(mats[colour])
    parts.append(ob)
    return ob

def box(loc, size, colour, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    ob = bpy.context.object
    ob.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = ob.modifiers.new('Small formed edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(ob, colour)

def mesh(vertices, faces, colour):
    me = bpy.data.meshes.new('PM_Geometry')
    me.from_pydata(vertices, [], faces)
    me.update()
    ob = bpy.data.objects.new('PM_Part', me)
    bpy.context.collection.objects.link(ob)
    return finish(ob, colour)

def profile(rings, n, colour, centre=(0,.018)):
    # A capped surface of revolution, kept deliberately faceted.
    verts = [(centre[0]+r*math.cos(2*math.pi*i/n),
              centre[1]+r*math.sin(2*math.pi*i/n), z)
             for r,z in rings for i in range(n)]
    faces = []
    for j in range(len(rings)-1):
        for i in range(n):
            k = (i+1)%n
            faces.append((j*n+i, j*n+k, (j+1)*n+k, (j+1)*n+i))
    verts += [(centre[0],centre[1],rings[0][1]),
              (centre[0],centre[1],rings[-1][1])]
    for i in range(n):
        k=(i+1)%n
        faces += [(len(verts)-2,k,i),
                  (len(verts)-1,(len(rings)-1)*n+i,(len(rings)-1)*n+k)]
    return mesh(verts,faces,colour)

def roof():
    rings=[(.20,.18,.531),(.20,.18,.555),(.166,.145,.592)]
    verts=[(sx*x,sy*y,z) for x,y,z in rings
           for sx,sy in [(-1,-1),(1,-1),(1,1),(-1,1)]]
    faces=[(3,2,1,0),(8,9,10,11)]
    for j in range(2):
        for i in range(4):
            faces.append((4*j+i,4*j+(i+1)%4,4*(j+1)+(i+1)%4,4*(j+1)+i))
    return mesh(verts,faces,'Red')

def heap():
    rng=random.Random(29)
    nx,ny=7,6
    verts=[]
    for j in range(ny):
        y=-.139+j*.278/(ny-1)
        for i in range(nx):
            x=-.159+i*.318/(nx-1)
            z=.225+.069*(1-(x/.21)**2-(y/.19)**2)+rng.uniform(-.009,.009)
            verts.append((x,y,z))
    faces=[]
    for j in range(ny-1):
        for i in range(nx-1):
            a=j*nx+i
            faces.extend([(a,a+1,a+nx+1),(a,a+nx+1,a+nx)])
    perimeter=list(range(nx))+[j*nx+nx-1 for j in range(1,ny)]
    perimeter += list(range((ny-1)*nx+nx-2,(ny-1)*nx-1,-1))
    perimeter += [j*nx for j in range(ny-2,0,-1)]
    start=len(verts)
    verts += [(verts[p][0],verts[p][1],.127) for p in perimeter]
    verts.append((0,0,.127))
    for k,p in enumerate(perimeter):
        kn=(k+1)%len(perimeter)
        faces.append((p,start+k,start+kn,perimeter[kn]))
        faces.append((len(verts)-1,start+kn,start+k))
    ob=mesh(verts,faces,'Corn')
    # Show kernels on every exposed face, rather than a smooth solid block.
    positions=[]
    for y in [-.09,0,.09]:
        for x in [-.12,-.06,0,.06,.12]:
            z=.225+.069*(1-(x/.21)**2-(y/.19)**2)+.006
            positions.append((x,y,z))
    for z in [.157,.216]:
        for x in [-.132,-.079,-.026,.026,.079,.132]:
            positions.append((x,-.129,z))
        for x in [-.148,.148]:
            for y in [-.09,0,.09]: positions.append((x,y,z))
    for idx,(x,y,z) in enumerate(positions):
        location=(x+rng.uniform(-.010,.010),y+rng.uniform(-.006,.006),z+rng.uniform(-.012,.012))
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=location)
        kernel=bpy.context.object
        kernel.scale=(rng.uniform(.027,.033),rng.uniform(.027,.032),rng.uniform(.029,.038))
        kernel.rotation_euler=[rng.random()*2 for _ in range(3)]
        finish(kernel, rng.choices(['CornLight','Corn','CornGold'],[7,3,1])[0])
        # Three small adjoining lobes break the regular sphere silhouettes.
        if idx in [2,7,12]:
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=.018,
                location=(location[0]+.021,location[1]-.012,location[2]+.014))
            finish(bpy.context.object,'CornLight')
    return ob

def presentation():
    scene=bpy.context.scene
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=1000
    scene.render.resolution_y=1000
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    scene.world.color=(.22,.22,.22)
    scene.world.use_nodes=True
    scene.world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.075,.095,.125,1)
    scene.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.55
    scene.view_settings.view_transform='AgX'
    bpy.ops.object.camera_add(location=(.94,-1.65,.86))
    cam=bpy.context.object
    cam.name='PM_ReviewCamera'
    cam.rotation_euler=(Vector((0,0,.30))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'
    cam.data.ortho_scale=.84
    scene.camera=cam
    for name,loc,energy,size in [('Key',(.1,-1.0,1.5),55,1.15),
                                  ('Fill',(-1,-.2,.8),30,1.0),
                                  ('Rim',(.6,.7,1.1),65,.8)]:
        bpy.ops.object.light_add(type='AREA',location=loc)
        light=bpy.context.object
        light.name='PM_'+name
        light.data.energy=energy
        light.data.shape='DISK'
        light.data.size=size
        light.rotation_euler=(Vector((0,0,.3))-light.location).to_track_quat('-Z','Y').to_euler()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.region_3d.view_distance=1.2
                space.region_3d.view_location=(0,0,.30)
                space.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
                space.shading.type='MATERIAL'

def atlas(body):
    select(body)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.005,
                              area_weight=.3,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    uv=body.data.uv_layers.active.data
    image=bpy.data.images.new('T_PopcornMachine_BaseColor',width=512,height=512,alpha=True)
    pixels=np.zeros((512,512,4),dtype=np.float32)
    pixels[:]=(.19,.045,.055,1)
    occupied=np.zeros((512,512),dtype=bool)
    body.data.calc_loop_triangles()
    for tri in body.data.loop_triangles:
        pts=np.array([tuple(uv[k].uv) for k in tri.loops])*512
        a,b,c=pts
        x0=max(int(np.floor(pts[:,0].min())),0); x1=min(int(np.ceil(pts[:,0].max())),511)
        y0=max(int(np.floor(pts[:,1].min())),0); y1=min(int(np.ceil(pts[:,1].max())),511)
        xx,yy=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
        d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(d)<1e-10: continue
        u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/d
        v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/d
        mask=(u>=-1e-6)&(v>=-1e-6)&(u+v<=1.000001)
        colour=PALETTE[body.data.materials[tri.material_index].name.replace('PM_','')]
        pixels[y0:y1+1,x0:x1+1][mask]=colour
        occupied[y0:y1+1,x0:x1+1][mask]=True
    # Two texels of edge padding, so flat blocks remain clean with filtering.
    for _ in range(2):
        old=occupied.copy(); src=pixels.copy()
        for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
            shifted=np.roll(old,(dy,dx),(0,1))
            fill=shifted&~occupied
            pixels[fill]=np.roll(src,(dy,dx),(0,1))[fill]
            occupied|=fill
    image.pixels.foreach_set(pixels.ravel())
    image.filepath_raw=os.path.join(OUT,'T_PopcornMachine_BaseColor.png')
    image.file_format='PNG'
    image.save()
    # Keep absolute until the scene has its final output-folder path.
    image.filepath=os.path.join(OUT,'T_PopcornMachine_BaseColor.png')
    material=bpy.data.materials.new('M_PopcornMachine')
    material.use_nodes=True
    nodes=material.node_tree.nodes
    tex=nodes.new('ShaderNodeTexImage'); tex.image=image
    tex.interpolation='Linear'
    bsdf=nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value=.68
    material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    body.data.materials.clear(); body.data.materials.append(material)
    for poly in body.data.polygons: poly.material_index=0

def export_asset(body):
    bm=bmesh.new()
    for x in [-.20,.20]:
        for y in [-.18,.18]:
            for z in [0,.592]: bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('PM_ConvexHull'); bm.to_mesh(me); bm.free()
    hull=bpy.data.objects.new('PopcornMachine_Collision',me)
    bpy.context.collection.objects.link(hull)
    hull.hide_render=True; hull.display_type='WIRE'
    select(body); hull.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT+'/PopcornMachine.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    hull.hide_set(True)

if bpy.context.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
for ob in list(bpy.data.objects):
    if ob.name.startswith('PM_') or ob.name in ['PopcornMachine_Body','PopcornMachine_Collision']:
        bpy.data.objects.remove(ob,do_unlink=True)
for mat in list(bpy.data.materials):
    if mat.name.startswith('PM_') or mat.name=='M_PopcornMachine': bpy.data.materials.remove(mat)
mats={}
for name,rgba in PALETTE.items():
    mat=bpy.data.materials.new('PM_'+name)
    mat.diffuse_color=(*[linear(v) for v in rgba[:3]],1)
    mat.use_nodes=True
    mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=mat.diffuse_color
    mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.68
    mats[name]=mat

# Flat underside through the placement point; full counter-sized cabinet.
box((0,0,.05),(.376,.334,.10),'Red',.004)
box((0,0,.009),(.365,.324,.018),'RedDark')
box((0,0,.108),(.385,.343,.016),'SteelLight')
for x in [-.172,.172]:
    for y in [-.152,.152]: box((x,y,.323),(.022,.022,.416),'RedEdge')
roof()
profile([(.052,.394),(.074,.411),(.078,.464)],12,'Steel')
box((-.070,.018,.491),(.012,.012,.080),'Steel')
box((.070,.018,.491),(.012,.012,.080),'Steel')
if STAGE==1:
    box((0,0,.205),(.32,.278,.17),'Corn')
else:
    # Thin cabinet edging, no transparent glass polygons.
    for y in [-.152,.152]:
        box((0,y,.126),(.326,.010,.018),'Steel')
        box((0,y,.521),(.326,.010,.014),'RedDark')
    for x in [-.172,.172]:
        box((x,0,.126),(.010,.286,.018),'Steel')
        box((x,0,.521),(.010,.286,.014),'RedDark')
    profile([(.081,.464),(.081,.473),(.048,.492)],12,'SteelLight')
    box((0,.018,.499),(.029,.021,.012),'Charcoal')
    box((.093,.018,.439),(.042,.014,.014),'Steel')
    box((.120,.018,.449),(.016,.019,.036),'Charcoal')
    box((0,-.1685,.052),(.302,.004,.062),'RedDark')
    box((0,-.171,.052),(.284,.003,.047),'Red')
    # Operator's two door frames and pull are on +Y, opposite customers.
    box((0,.152,.388),(.009,.010,.246),'Steel')
    for x in [-.026,.026]: box((x,.165,.392),(.008,.022,.049),'Steel')
    box((0,.175,.392),(.060,.008,.008),'Charcoal')
    heap()

select(parts[0])
for ob in parts: ob.select_set(True)
bpy.ops.object.join()
body=bpy.context.object; body.name='PopcornMachine_Body'
bm=bmesh.new(); bm.from_mesh(body.data)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bmesh.ops.triangulate(bm,faces=list(bm.faces))
bm.to_mesh(body.data); bm.free()
body.location=(0,0,0)
body['front_axis']='-Y'
body['dimensions_m']='0.400 x 0.360 x 0.592'
if STAGE>=3: atlas(body)
presentation()
if STAGE==4:
    export_asset(body)
    bpy.context.scene.render.filepath=os.path.join(OUT,'renders','04-final.png')
    bpy.ops.render.render(write_still=True)
    # Presentation helpers are not part of the deliverable scene.
    bpy.context.scene.camera=None
    for ob in list(bpy.data.objects):
        if ob.name.startswith('PM_') and ob.type in {'CAMERA','LIGHT'}:
            bpy.data.objects.remove(ob,do_unlink=True)
    for collection in [bpy.data.meshes,bpy.data.materials,bpy.data.images,bpy.data.cameras,bpy.data.lights]:
        for block in list(collection):
            if block.users==0 and (block.name.startswith('PM_') or block.name.startswith('T_PopcornMachine')):
                collection.remove(block)
    for image in bpy.data.images:
        if image.name.startswith('T_PopcornMachine') and image.users:
            image.filepath='//T_PopcornMachine_BaseColor.png'
    save_version=bpy.context.preferences.filepaths.save_version
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'PopcornMachine.blend'),relative_remap=False)
    bpy.context.preferences.filepaths.save_version=save_version
select(body)
bpy.context.view_layer.update()
result={'stage':STAGE,'triangles':len(body.data.polygons),'dimensions':list(body.dimensions),
        'reference_height':bpy.data.objects['HumanRef_1.8m'].dimensions.z}

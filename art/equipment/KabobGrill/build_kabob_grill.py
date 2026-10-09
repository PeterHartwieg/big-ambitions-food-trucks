"""Regenerate in the existing metric start scene; all outputs stay beside this script.
Run inside Blender: exec(compile(open(PATH).read(), PATH, 'exec')); build('all')
The scale reference is preserved. Only this asset and temporary studio objects change.
"""
import bpy, bmesh, math, os, random
import numpy as np
from mathutils import Vector
OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
PALETTE = {
    'Steel': (0.59, 0.67, 0.70), 'SteelLight': (0.76, 0.82, 0.83),
    'SteelDark': (0.32, 0.40, 0.44), 'Interior': (0.20, 0.25, 0.27),
    'Handle': (0.09, 0.12, 0.13), 'Coal': (0.055, 0.065, 0.075),
    'CoalAsh': (0.15, 0.17, 0.18), 'Ember': (1.0, 0.20, 0.025),
    'EmberHot': (1.0, 0.44, 0.055), 'Meat': (0.55, 0.245, 0.10),
    'MeatGold': (0.73, 0.37, 0.14), 'Sear': (0.25, 0.095, 0.035),
    'PepperGreen': (0.26, 0.50, 0.075), 'PepperRed': (0.78, 0.12, 0.055),
}
PARTS = []

def linear(c):
    return c / 12.92 if c < 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def provisional_material(key):
    name = 'Build_' + key
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*[linear(c) for c in PALETTE[key]], 1)
    return mat

def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def mesh_part(name, vertices, faces, colors):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    ob = bpy.data.objects.new('KG_' + name, mesh)
    bpy.context.collection.objects.link(ob)
    keys = list(dict.fromkeys(colors))
    for key in keys:
        mesh.materials.append(provisional_material(key))
    for face, key in zip(mesh.polygons, colors):
        face.material_index = keys.index(key)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free(); mesh.update()
    PARTS.append(ob)
    return ob

def box(name, center, size, color='Steel', spin=0):
    cx, cy, cz = center; sx, sy, sz = [s / 2 for s in size]
    verts = [(x*sx, y*sy, z*sz) for z in (-1,1) for y in (-1,1) for x in (-1,1)]
    verts = [(cx+x*math.cos(spin)-y*math.sin(spin), cy+x*math.sin(spin)+y*math.cos(spin), cz+z) for x,y,z in verts]
    faces = [(0,2,3,1),(4,5,7,6),(0,1,5,4),(1,3,7,5),(3,2,6,7),(2,0,4,6)]
    return mesh_part(name, verts, faces, [color]*6)

def oct_chunk(name, center, size, key, tilt=0, taper=0.92, sear=False):
    # A squared octagonal cross-section, extruded along each skewer (Y).
    x,y,z = center; sx,sy,sz = size
    ring = [(-.28,-.5),(.28,-.5),(.5,-.28),(.5,.28),(.28,.5),(-.28,.5),(-.5,.28),(-.5,-.28)]
    vs = []
    for j in (0,1):
        for a,b in ring:
            q = taper if j else 1
            xx,zz = a*sx*q, b*sz*q
            vs.append((x+xx*math.cos(tilt)-zz*math.sin(tilt), y+(-.5 if j==0 else .5)*sy, z+xx*math.sin(tilt)+zz*math.cos(tilt)))
    faces = [tuple(range(7,-1,-1)), tuple(range(8,16))]
    colors = [key,key]
    for i in range(8):
        faces.append((i,(i+1)%8,(i+1)%8+8,i+8))
        colors.append('Sear' if sear and i in (2,6) else key)
    return mesh_part(name,vs,faces,colors)

def join_body():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in PARTS: ob.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    ob = bpy.context.object; ob.name = 'KabobGrill_Body'; ob.data.name = 'KabobGrill_Mesh'
    bpy.context.scene.cursor.location = (0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    ob['front_direction'] = '-Y'; ob['dimensions_in_metres'] = True
    return ob

def blockout():
    global PARTS
    PARTS = []
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('KG_', 'KabobGrill_', 'Studio_')):
            bpy.data.objects.remove(ob, do_unlink=True)
    # Thick folded trough: outer shell, lip, recessed inner shell, closed floor.
    rings = [(.358,.176,.062),(.384,.208,.158),(.364,.188,.158),(.338,.150,.078)]
    vs=[]
    for w,d,z in rings:
        vs += [(-w/2,-d/2,z),(w/2,-d/2,z),(w/2,d/2,z),(-w/2,d/2,z)]
    faces=[]; colors=[]
    for k in range(3):
        for i in range(4):
            faces.append((4*k+i,4*k+(i+1)%4,4*(k+1)+(i+1)%4,4*(k+1)+i))
            colors.append(['Steel','SteelLight','Interior'][k])
    faces.extend([(3,2,1,0),(12,13,14,15)]); colors.extend(['SteelDark','Interior'])
    mesh_part('Trough',vs,faces,colors)
    # Four feet, two runners and a bottom tie ensure a surface at the placement origin.
    for x in (-.155,.155):
        for y in (-.070,.070): box('Leg', (x,y,.033), (.022,.022,.066), 'SteelDark')
    for y in (-.070,.070): box('Runner',(0,y,.006),(.340,.025,.012),'SteelDark')
    box('FloorTie',(0,0,.005),(.022,.160,.010),'SteelDark')
    for y in (-.101,.101): box('LongLip',(0,y,.158),(.388,.010,.009),'SteelLight')
    for x in (-.189,.189): box('EndLip',(x,0,.158),(.010,.198,.009),'SteelLight')
    # Low rear shield with clipped upper corners, facing the customer at -Y.
    outline=[(-.183,.154),(.183,.154),(.183,.240),(.168,.255),(-.168,.255),(-.183,.240)]
    vs=[(x,y,z) for y in (.100,.108) for x,z in outline]
    faces=[tuple(range(5,-1,-1)),tuple(range(6,12))]
    faces += [(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
    mesh_part('RearHeatShield',vs,faces,['Steel','SteelDark']+['SteelLight']*6)
    box('ShieldFold',(0,.098,.251),(.335,.017,.008),'SteelLight')
    body = join_body(); PARTS=[body]
    setup_view(body)
    return body

def detail():
    global PARTS
    body=bpy.data.objects['KabobGrill_Body']; PARTS=[body]
    rng=random.Random(71)
    # Three staggered rows: hot orange blocks below a dark, broken charcoal bed.
    for row,y in enumerate((-.055,-.003,.049)):
        for col in range(6):
            x=-.145+col*.058+(.005 if row%2 else 0)
            key=['Coal','EmberHot','CoalAsh','Ember','Coal','Ember'][ (col+row*2)%6 ]
            box('CoalBlock',(x,y,.122+rng.uniform(-.004,.004)),(.044,.036,.031),key,rng.uniform(-.18,.18))
    for i,x in enumerate((-.150,-.090,-.030,.030,.090,.150)):
        box('FlatSkewer',(x,-.009,.169),(.004,.288,.004),'SteelLight')
        oct_chunk('HeatGrip',(x,-.153,.169),(.011,.040,.012),'Handle',taper=.78)
        for j,y in enumerate((-.066,-.001,.064)):
            key='MeatGold' if (i+j)%3 else 'Meat'
            oct_chunk('KabobMeat',(x,y,.184),(.040,.039,.038),key,tilt=rng.uniform(-.15,.15),taper=rng.uniform(.83,.97),sear=True)
        for j,y in enumerate((-.034,.032)):
            key='PepperGreen' if (i+j)%2 else 'PepperRed'
            oct_chunk('Pepper',(x,y,.182),(.037,.018,.036),key,tilt=rng.uniform(-.15,.15),taper=.88)
    body=join_body(); PARTS=[body]
    setup_view(body)
    return body

def setup_view(body):
    activate(body)
    q=(Vector((0,0,.125))-Vector((.39,-.76,.60))).to_track_quat('-Z','Y')
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.region_3d.view_rotation=q
                space.region_3d.view_location=(0,0,.12)
                space.region_3d.view_distance=.73
                space.clip_start=.001
                space.overlay.show_floor=False
                space.shading.color_type='MATERIAL'
                space.shading.light='STUDIO'
                space.shading.show_shadows=True
                space.shading.show_cavity=True


def preview(filename, textured=False):
    scene=bpy.context.scene
    camera_data=bpy.data.cameras.new('Studio_Camera')
    camera=bpy.data.objects.new('Studio_Camera',camera_data); scene.collection.objects.link(camera)
    camera.location=(.39,-.76,.60)
    camera.rotation_euler=(Vector((0,0,.122))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type='ORTHO'; camera_data.ortho_scale=.55; camera_data.clip_start=.01
    scene.camera=camera
    scene.render.engine='BLENDER_WORKBENCH'
    scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    shading=scene.display.shading
    shading.light='STUDIO'; shading.studiolight_rotate_z=.4
    shading.color_type='TEXTURE' if textured else 'MATERIAL'
    shading.show_shadows=True; shading.show_cavity=True
    shading.cavity_type='BOTH'; shading.curvature_ridge_factor=1.2; shading.curvature_valley_factor=1.0
    shading.show_specular_highlight=True; shading.show_object_outline=False
    shading.background_type='WORLD'; scene.world.color=(.035,.045,.060)
    scene.view_settings.view_transform='Standard'
    scene.render.filepath=OUT+'/renders/'+filename
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera,do_unlink=True); bpy.data.cameras.remove(camera_data)
    scene.camera=None
    return scene.render.filepath


def uv_texture():
    body=bpy.data.objects['KabobGrill_Body']; activate(body)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.004,margin_method='FRACTION',scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh=body.data; mesh.uv_layers.active.name='UVMap'
    mesh.calc_loop_triangles(); uv=mesh.uv_layers.active.data
    # Rasterise flat per-face palette into the atlas, with padding for filtered sampling.
    pix=np.zeros((512,512,4),dtype=np.float32); pix[:,:,:3]=(.20,.24,.26); pix[:,:,3]=1
    mask=np.zeros((512,512),bool)
    for tri in mesh.loop_triangles:
        coords=np.array([uv[l].uv[:] for l in tri.loops])*512
        a,b,c=coords
        xmin=max(0,int(np.floor(coords[:,0].min()))); xmax=min(511,int(np.ceil(coords[:,0].max())))
        ymin=max(0,int(np.floor(coords[:,1].min()))); ymax=min(511,int(np.ceil(coords[:,1].max())))
        if xmax<xmin or ymax<ymin: continue
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1)+.5,np.arange(ymin,ymax+1)+.5)
        det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(det)<1e-10: continue
        l1=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/det
        l2=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/det
        inside=(l1>=-1e-6)&(l2>=-1e-6)&(1-l1-l2>=-1e-6)
        mat=mesh.materials[mesh.polygons[tri.polygon_index].material_index]
        color=PALETTE[mat.name.removeprefix('Build_')]
        pix[ymin:ymax+1,xmin:xmax+1,:3][inside]=color
        mask[ymin:ymax+1,xmin:xmax+1][inside]=True
    for _ in range(3):
        previous=mask.copy(); newpix=pix.copy()
        for dy,dx in ((-1,0),(1,0),(0,-1),(0,1)):
            shifted=np.roll(previous,(dy,dx),(0,1)); valid=shifted & ~mask
            if dy<0: valid[-1,:]=False
            if dy>0: valid[0,:]=False
            if dx<0: valid[:,-1]=False
            if dx>0: valid[:,0]=False
            newpix[valid]=np.roll(pix,(dy,dx),(0,1))[valid]; mask|=valid
        pix=newpix
    image=bpy.data.images.get('T_KabobGrill_BaseColor.png')
    if image: bpy.data.images.remove(image)
    image=bpy.data.images.new('T_KabobGrill_BaseColor.png',width=512,height=512,alpha=True)
    image.colorspace_settings.name='sRGB'
    image.pixels.foreach_set(pix.ravel()); image.update()
    image.filepath_raw=OUT+'/T_KabobGrill_BaseColor.png'; image.file_format='PNG'; image.save()
    image.filepath=OUT+'/T_KabobGrill_BaseColor.png'
    mat=bpy.data.materials.get('M_KabobGrill') or bpy.data.materials.new('M_KabobGrill')
    mat.use_nodes=True; mat.node_tree.nodes.clear()
    nodes=mat.node_tree.nodes
    tex=nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Linear'; nodes.active=tex
    shader=nodes.new('ShaderNodeBsdfPrincipled'); shader.inputs['Roughness'].default_value=.63
    output=nodes.new('ShaderNodeOutputMaterial')
    tex.location=(-350,80); shader.location=(0,80); output.location=(300,80)
    mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    mat.node_tree.links.new(shader.outputs['BSDF'],output.inputs['Surface'])
    mesh.materials.clear(); mesh.materials.append(mat)
    for face in mesh.polygons: face.material_index=0; face.use_smooth=False
    # Remove now-unused temporary palette materials from the final scene.
    for m in list(bpy.data.materials):
        if m.name.startswith('Build_') and m.users==0: bpy.data.materials.remove(m)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D': area.spaces.active.shading.color_type='TEXTURE'
    return body


def collision_export():
    body=bpy.data.objects['KabobGrill_Body']
    old=bpy.data.objects.get('KabobGrill_Collision')
    if old: bpy.data.objects.remove(old,do_unlink=True)
    bm=bmesh.new()
    # A conservative 8-corner envelope around all visible mesh points.
    lo=[min(v.co[i] for v in body.data.vertices) for i in range(3)]
    hi=[max(v.co[i] for v in body.data.vertices) for i in range(3)]
    for x in (lo[0],hi[0]):
        for y in (lo[1],hi[1]):
            for z in (lo[2],hi[2]): bm.verts.new((x,y,z))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new('KabobGrill_ConvexHull'); bm.to_mesh(mesh); bm.free()
    col=bpy.data.objects.new('KabobGrill_Collision',mesh); bpy.context.collection.objects.link(col)
    col.hide_render=True; col.display_type='WIRE'; col['construction']='bmesh.ops.convex_hull'
    activate(body); col.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT+'/KabobGrill.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    col.hide_set(True); activate(body)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/KabobGrill.blend')
    return {'body_triangles':len(body.data.polygons),'collision_triangles':len(col.data.polygons)}


def build(stage='all'):
    os.makedirs(OUT+'/renders',exist_ok=True)
    if stage in ('blockout','all'):
        blockout(); preview('01-blockout.png')
    if stage in ('detail','all'):
        detail(); preview('02-detail.png')
    if stage in ('texture','all'):
        uv_texture(); preview('03-uvs-texture.png',textured=True)
    if stage in ('final','all'):
        preview('04-final.png',textured=True)
        return collision_export()

"""Regenerate in art/start.blend. Call build(1), build(2), atlas(), finish().
Front is -Y; coordinates baked into vertices; HumanRef is never modified.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
OUT = REPO + '/art/equipment/PastryCase'
os.makedirs(OUT + '/renders', exist_ok=True)
PALETTE = {
    'steel': (0.37, 0.41, 0.44), 'drawer': (0.44, 0.48, 0.51),
    'shadow': (0.25, 0.29, 0.32), 'white': (0.88, 0.90, 0.89),
    'wood': (0.66, 0.40, 0.26), 'woodlight': (0.76, 0.49, 0.32),
    'wooddark': (0.51, 0.29, 0.18), 'gold': (0.85, 0.53, 0.23),
    'crust': (0.72, 0.38, 0.14), 'dough': (0.88, 0.62, 0.31),
    'pink': (0.91, 0.52, 0.57), 'chocolate': (0.28, 0.13, 0.08),
    'cream': (0.96, 0.84, 0.65), 'paper': (0.75, 0.58, 0.41),
}
KEYS = list(PALETTE)
verts, faces, colors = [], [], []

def part(v, f, color):
    offset = len(verts)
    verts.extend(v)
    faces.extend([tuple(offset+i for i in poly) for poly in f])
    colors.extend([KEYS.index(c) for c in color] if isinstance(color, list)
                  else [KEYS.index(color)]*len(f))

def box(center, size, color, slope=0):
    x, y, z = center
    sx, sy, sz = [d/2 for d in size]
    v = [(x+a*sx, y+b*sy, z+c*sz+b*sy*slope)
         for a,b,c in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                       (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    part(v, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], color)

def beam(a, b, width, color):
    a,b = Vector(a),Vector(b)
    along=(b-a).normalized()
    side=along.cross(Vector((0,0,1)))
    if side.length < .01: side=Vector((1,0,0))
    side.normalize()
    up=along.cross(side).normalized()
    v=[tuple(p+width/2*(s*side+t*up)) for p in (a,b)
       for s,t in [(-1,-1),(1,-1),(1,1),(-1,1)]]
    part(v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],color)

SLOPE=.18
def shelf_z(level, y): return [.146,.273,.386][level] + SLOPE*y

def croissant(x,y,z):
    # Six closed diamond cross-sections bent into a broad crescent.
    v=[]
    for i in range(6):
        a=math.radians(10+160*i/5)
        w=[.003,.012,.018,.018,.012,.003][i]
        cx=x+.035*math.cos(a); cy=y+.030*math.sin(a)-.015
        for side,up in [(1,0),(0,1),(-1,0),(0,-1)]:
            v.append((cx+side*w*math.cos(a),cy+side*w*math.sin(a),z+.017+up*w*.78))
    f=[(3,2,1,0)]; c=['crust']
    for i in range(5):
        for j in range(4):
            f.append((i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j))
            c.append('gold' if j in (0,1) and i%2==0 else 'crust' if j in (2,3) else 'dough')
    f.append((20,21,22,23));c.append('gold')
    part(v,f,c)

def donut(x,y,z,icing):
    # Octagonal ring, four cross-section vertices. Top half is icing.
    v=[]
    for i in range(8):
        a=2*math.pi*i/8
        for r,h in [(.038,.012),(.028,.026),(.015,.012),(.028,0)]:
            v.append((x+r*math.cos(a),y+r*math.sin(a),z+h))
    f=[];c=[]
    for i in range(8):
        for j in range(4):
            f.append((4*i+j,4*((i+1)%8)+j,4*((i+1)%8)+(j+1)%4,4*i+(j+1)%4))
            c.append(icing if j in (0,1) else 'dough')
    part(v,f,c)

def cupcake(x,y,z,icing):
    # One closed mesh: tapered paper cup, cake rim, angular frosting peak.
    rings=[(.024,0),(.030,.030),(.033,.035),(.027,.050),(.012,.066)]
    v=[(x+r*math.cos(i*math.pi/3),y+r*math.sin(i*math.pi/3),z+h)
       for r,h in rings for i in range(6)]
    v.extend([(x,y,z),(x,y,z+.071)])
    f=[];c=[]
    for i in range(6):
        f.append((30,(i+1)%6,i));c.append('paper')
    for k in range(4):
        for i in range(6):
            f.append((k*6+i,k*6+(i+1)%6,(k+1)*6+(i+1)%6,(k+1)*6+i))
            c.append(('paper' if i%2==0 else 'woodlight') if k==0 else 'dough' if k==1 else icing)
    for i in range(6):
        f.append((24+i,24+(i+1)%6,31));c.append(icing)
    part(v,f,c)

def setup_render():
    scene=bpy.context.scene
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=900;scene.render.resolution_y=900
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='None'
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.17,.20,.23,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    camdata=bpy.data.cameras.new('PastryCase_PreviewCamera')
    cam=bpy.data.objects.new('PastryCase_PreviewCamera',camdata)
    scene.collection.objects.link(cam)
    cam.location=(.78,-1.5,.77)
    cam.rotation_euler=(Vector((0,0,.27))-cam.location).to_track_quat('-Z','Y').to_euler()
    camdata.type='ORTHO';camdata.ortho_scale=.75;scene.camera=cam
    for name,loc,power,size in [('Key',(.1,-1.5,2),35,1.5),('Fill',(-1,-.4,1),15,1),('Rim',(1,1,1.7),30,1)]:
        ld=bpy.data.lights.new('PastryCase_'+name,'AREA')
        ob=bpy.data.objects.new('PastryCase_'+name,ld);scene.collection.objects.link(ob)
        ob.location=loc;ld.energy=power;ld.shape='DISK';ld.size=size
        ob.rotation_euler=(Vector((0,0,.25))-ob.location).to_track_quat('-Z','Y').to_euler()

def srgb_linear(x): return x/12.92 if x<=.04045 else ((x+.055)/1.055)**2.4

def build(stage=2):
    global verts,faces,colors
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for ob in list(bpy.data.objects):
        if ob.name.startswith('PastryCase_'): bpy.data.objects.remove(ob,do_unlink=True)
    verts,faces,colors=[],[],[]
    # Steel underside reaches z=0 over the origin; two front drawers.
    box((0,0,.0525),(.378,.316,.105),'steel')
    box((0,-.159,.052),(.365,.004,.088),'shadow')
    for x in [-.092,.092]:
        box((x,-.163,.053),(.178,.006,.084),'drawer')
        box((x,-.171,.073),(.087,.009,.009),'woodlight')
    # Shallow slats running across the width, with a continuous supporting deck.
    for level in range(3):
        box((0,0,shelf_z(level,0)-.010),(.384,.318,.014),'wooddark',SLOPE)
        for j in range(5):
            y=-.130+j*.065
            box((0,y,shelf_z(level,y)),(.380,.060,.008),'woodlight' if j%2==0 else 'wood',SLOPE)
        box((0,-.163,shelf_z(level,-.163)+.004),(.388,.011,.020),'wood',SLOPE)
    for x in [-.184,.184]:
        for y in [-.150,.150]:
            beam((x,y,.104),(x,y,.505+SLOPE*y),.009,'white')
        for level in range(3):
            beam((x,-.153,shelf_z(level,-.153)-.011),(x,.153,shelf_z(level,.153)-.011),.009,'white')
    box((0,0,.508),(.396,.334,.020),'woodlight',SLOPE)
    if stage>=2:
        for row,y in enumerate([-.091,.078]):
            for col,x in enumerate([-.115,0,.115]):
                croissant(x,y,shelf_z(0,y)+.006)
                donut(x,y,shelf_z(1,y)+.006,['pink','chocolate','cream'][(row+col)%3])
                cupcake(x,y,shelf_z(2,y)+.006,['cream','pink','chocolate'][(row+col)%3])
    mesh=bpy.data.meshes.new('PastryCase_Geometry')
    mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new('PastryCase_Body',mesh);bpy.context.scene.collection.objects.link(ob)
    for key in KEYS:
        mat=bpy.data.materials.get('PastryCase_Temp_'+key) or bpy.data.materials.new('PastryCase_Temp_'+key)
        mat.diffuse_color=(*[srgb_linear(v) for v in PALETTE[key]],1)
        mat.use_nodes=True
        bs=mat.node_tree.nodes.get('Principled BSDF')
        bs.inputs['Base Color'].default_value=mat.diffuse_color;bs.inputs['Roughness'].default_value=.78
        mesh.materials.append(mat)
    for poly,idx in zip(mesh.polygons,colors): poly.material_index=idx
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    bpy.context.view_layer.objects.active=ob;ob.select_set(True)
    setup_render()
    bpy.context.view_layer.update()
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_rotation=bpy.context.scene.camera.rotation_euler.to_quaternion()
            area.spaces.active.region_3d.view_distance=1.1
            area.spaces.active.region_3d.view_location=(0,0,.27)
            area.spaces.active.shading.type='MATERIAL'
    result={'stage':stage,'triangles':len(mesh.polygons),'dims':list(ob.dimensions),'reference_height':bpy.data.objects['HumanRef_1.8m'].dimensions.z}
    render('01-blockout' if stage==1 else '02-detail')
    return result

def render(name):
    bpy.context.scene.render.filepath=OUT+'/renders/'+name+'.png'
    bpy.ops.render.render(write_still=True)

def atlas():
    ob=bpy.data.objects['PastryCase_Body'];me=ob.data
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.006,area_weight=0,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Rasterise flat face colours into the unique UV islands; expand two pixels.
    pixels=np.zeros((512,512,4),dtype=np.float32);pixels[:]=(.4,.4,.4,1)
    mask=np.zeros((512,512),dtype=bool)
    uv=me.uv_layers.active.data
    for poly in me.polygons:
        t=np.array([uv[i].uv[:] for i in poly.loop_indices])*512
        a,b,c=t
        x0=max(0,int(np.floor(t[:,0].min())));x1=min(511,int(np.ceil(t[:,0].max())))
        y0=max(0,int(np.floor(t[:,1].min())));y1=min(511,int(np.ceil(t[:,1].max())))
        xs,ys=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
        d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(d)<1e-12: continue
        u=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/d
        v=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/d
        hit=(u>=-1e-6)&(v>=-1e-6)&(u+v<=1.000001)
        pixels[y0:y1+1,x0:x1+1][hit]=(*PALETTE[KEYS[poly.material_index]],1)
        mask[y0:y1+1,x0:x1+1]|=hit
    fill=float(mask.mean())
    for _ in range(2):
        old=mask.copy()
        for dy,dx in [(1,0),(-1,0),(0,1),(0,-1)]:
            shifted=np.roll(old,(dy,dx),(0,1));hit=shifted&~mask
            if dy==1:hit[0,:]=False
            if dy==-1:hit[-1,:]=False
            if dx==1:hit[:,0]=False
            if dx==-1:hit[:,-1]=False
            pixels[hit]=np.roll(pixels,(dy,dx),(0,1))[hit];mask|=hit
    img=bpy.data.images.new('T_PastryCase_BaseColor',width=512,height=512,alpha=True)
    img.colorspace_settings.name='sRGB';img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=OUT+'/T_PastryCase_BaseColor.png';img.file_format='PNG';img.save()
    mat=bpy.data.materials.new('M_PastryCase');mat.use_nodes=True
    nodes=mat.node_tree.nodes;tex=nodes.new('ShaderNodeTexImage');tex.image=img
    mat.node_tree.links.new(tex.outputs['Color'],nodes['Principled BSDF'].inputs['Base Color'])
    nodes['Principled BSDF'].inputs['Roughness'].default_value=.78
    nodes.active=tex
    for poly in me.polygons:poly.material_index=0
    me.materials.clear();me.materials.append(mat)
    render('03-uv-texture')
    return {'atlas_fill_512':fill,'material_count':len(me.materials)}

def finish():
    body=bpy.data.objects['PastryCase_Body']
    old=bpy.data.objects.get('PastryCase_Collision')
    if old: bpy.data.objects.remove(old,do_unlink=True)
    # Hull of eight outer envelope corners, created by convex_hull, encloses all details.
    bm=bmesh.new()
    for x in [-.198,.198]:
        for y in [-.177,.177]:
            for z in [0,.549]: bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('PastryCase_ConvexHull');bm.to_mesh(me);bm.free()
    hull=bpy.data.objects.new('PastryCase_Collision',me);bpy.context.scene.collection.objects.link(hull)
    hull.hide_render=True;hull.display_type='WIRE';hull.hide_set(True)
    render('04-final')
    bpy.ops.object.select_all(action='DESELECT');hull.hide_set(False)
    body.select_set(True);hull.select_set(True);bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT+'/PastryCase.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    hull.hide_set(True);hull.select_set(False)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/PastryCase.blend')
    return {'body_triangles':len(body.data.polygons),'collision_triangles':len(me.polygons)}

if __name__=='__main__':
    build(1);build(2);atlas();finish()

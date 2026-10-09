"""Regenerate the corn grill in the current Blender scene (keeps HumanRef intact).
Run in Blender's Python console: exec(compile(open(PATH).read(), PATH, 'exec')).
Stages are separate so each saved review image can be inspected before continuing.
"""
import bpy, bmesh, math, os, json
from mathutils import Vector
import numpy as np

OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
os.makedirs(OUT + '/renders', exist_ok=True)
PALETTE = {
    'iron': (0.115, 0.145, 0.165, 1),
    'rim': (0.32, 0.37, 0.39, 1),
    'steel': (0.53, 0.59, 0.58, 1),
    'coal': (0.052, 0.06, 0.06, 1),
    'ash': (0.24, 0.255, 0.25, 1),
    'corn': (1.0, 0.68, 0.075, 1),
    'gold': (0.9, 0.46, 0.045, 1),
    'light': (1.0, 0.82, 0.23, 1),
    'char': (0.225, 0.105, 0.035, 1),
    'husk': (0.275, 0.43, 0.115, 1),
    'leaf': (0.47, 0.60, 0.22, 1),
    'butter': (1.0, 0.88, 0.40, 1),
    'sauce': (0.87, 0.68, 0.235, 1),
}
COLORS = list(PALETTE)
V, F, C = [], [], []

def add(verts, faces, color):
    base = len(V)
    V.extend(verts)
    F.extend([tuple(base+i for i in f) for f in faces])
    C.extend([COLORS.index(color)]*len(faces))

def box(center, dims, color):
    x,y,z = center; a,b,c = [d/2 for d in dims]
    vs = [(x-a,y-b,z-c),(x+a,y-b,z-c),(x+a,y+b,z-c),(x-a,y+b,z-c),
          (x-a,y-b,z+c),(x+a,y-b,z+c),(x+a,y+b,z+c),(x-a,y+b,z+c)]
    add(vs, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], color)

def rectangle(cx, cy, w, d, z, bevel=0.008):
    a,b = w/2,d/2; c=bevel
    return [(cx-a+c,cy-b,z),(cx+a-c,cy-b,z),(cx+a,cy-b+c,z),(cx+a,cy+b-c,z),
            (cx+a-c,cy+b,z),(cx-a+c,cy+b,z),(cx-a,cy+b-c,z),(cx-a,cy-b+c,z)]

def trough(cx,cy,w,d,bottom,top,thick,color):
    # Closed eight-corner metal shell, with a visible inner floor.
    vs = rectangle(cx,cy,w-.022,d-.018,bottom) + rectangle(cx,cy,w,d,top)
    vs += rectangle(cx,cy,w-2*thick,d-2*thick,top) + rectangle(cx,cy,w-.022-2*thick,d-.018-2*thick,bottom+thick)
    fs=[]
    for i in range(8):
        j=(i+1)%8
        fs += [(i,j,j+8,i+8),(i+8,j+8,j+16,i+16),(i+16,j+16,j+24,i+24)]
    fs += [(0,i+1,i) for i in range(1,7)]
    fs += [(24,24+i,24+i+1) for i in range(1,7)]
    add(vs,fs,color)

def rod(p0,p1,radius,color,n=6):
    a,b=Vector(p0),Vector(p1); axis=(b-a).normalized()
    side=axis.cross(Vector((0,0,1)))
    if side.length < .01: side=axis.cross(Vector((0,1,0)))
    side.normalize(); up=axis.cross(side).normalized()
    vs=[tuple(p+radius*(side*math.cos(i*2*math.pi/n)+up*math.sin(i*2*math.pi/n))) for p in (a,b) for i in range(n)]
    fs=[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    fs += [(0,i+1,i) for i in range(1,n-1)] + [(n,n+i,n+i+1) for i in range(1,n-1)]
    add(vs,fs,color)

def orient_closed_islands(bm):
    # Recalc can choose the wrong side of a narrow folded leaf; enforce positive
    # signed volume separately for every disconnected closed component.
    seen=set()
    for face in bm.faces:
        if face in seen: continue
        todo=[face]; seen.add(face); faces=[]
        while todo:
            f=todo.pop(); faces.append(f)
            for edge in f.edges:
                for neighbor in edge.link_faces:
                    if neighbor not in seen: seen.add(neighbor); todo.append(neighbor)
        volume=0.0
        for f in faces:
            vs=[v.co for v in f.verts]
            for i in range(1,len(vs)-1): volume+=vs[0].dot(vs[i].cross(vs[i+1]))/6
        if volume<0: bmesh.ops.reverse_faces(bm,faces=faces)
    bm.normal_update()

def sync():
    old = bpy.data.objects.get('CornGrill_Body')
    if old: bpy.data.objects.remove(old, do_unlink=True)
    me=bpy.data.meshes.new('CornGrill_Geometry'); me.from_pydata(V,[],F); me.update()
    ob=bpy.data.objects.new('CornGrill_Body',me); bpy.context.collection.objects.link(ob)
    for name in COLORS:
        mat=bpy.data.materials.get('Preview_'+name) or bpy.data.materials.new('Preview_'+name)
        mat.diffuse_color=PALETTE[name]; mat.use_nodes=True
        bs=mat.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=PALETTE[name]; bs.inputs['Roughness'].default_value=.78
        me.materials.append(mat)
    attr=me.attributes.new('part_color','INT','FACE')
    for p,c in zip(me.polygons,C): p.material_index=c; attr.data[p.index].value=c
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); orient_closed_islands(bm); bm.to_mesh(me); bm.free()
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
    bpy.context.view_layer.update()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.shading.type='MATERIAL'
                space.region_3d.view_location=Vector((0,0,.09))
                space.region_3d.view_rotation=Vector((.48,-.72,.5)).to_track_quat('Z','Y')
                space.region_3d.view_distance=.85
    return ob

def presentation():
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    camdata=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',camdata); scene.collection.objects.link(cam)
    cam.location=(.52,-.78,.56); cam.rotation_euler=(Vector((0,0,.08))-cam.location).to_track_quat('-Z','Y').to_euler()
    camdata.type='ORTHO'; camdata.ortho_scale=.60; scene.camera=cam
    for name,loc,energy,size in [('Key',(1,-2,3),160,2.0),('Fill',(-2,-.8,1.5),85,2.0),('Rim',(.5,2,2),120,1.8)]:
        dat=bpy.data.lights.new(name,'AREA'); dat.energy=energy; dat.shape='DISK'; dat.size=size
        ob=bpy.data.objects.new(name,dat); scene.collection.objects.link(ob); ob.location=loc
        ob.rotation_euler=(Vector((0,0,.1))-ob.location).to_track_quat('-Z','Y').to_euler()
    scene.world.color=(.18,.18,.18)
    scene.world.use_nodes=True; scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.20,.235,.27,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
    scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
    scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    scene.render.filepath=OUT+'/renders/01-blockout.png'


def render_stage(name):
    bpy.context.scene.render.filepath=OUT+'/renders/'+name+'.png'
    bpy.ops.render.render(write_still=True)
    return {'render':bpy.context.scene.render.filepath, 'triangles':sum(len(p.vertices)-2 for p in bpy.data.objects['CornGrill_Body'].data.polygons)}

def blockout():
    global V,F,C
    V,F,C=[],[],[]
    for ob in list(bpy.data.objects):
        if not ob.name.startswith('HumanRef'): bpy.data.objects.remove(ob,do_unlink=True)
    # Low H-shaped sled base. Its cross member stands directly over the origin.
    for x in (-.143,.047): box((x,0,.006),(.022,.273,.012),'iron')
    box((-.048,0,.006),(.212,.019,.012),'iron')
    for x in (-.143,.047):
        for y in (-.108,.108): box((x,y,.037),(.019,.021,.054),'iron')
    trough(-.047,0,.296,.300,.061,.137,.010,'iron')
    # Silver grate surround, with black firebox visibly recessed under it.
    for x in (-.188,.094): box((x,0,.138),(.011,.288,.009),'rim')
    for y in (-.144,.144): box((-.047,y,.138),(.280,.010,.009),'rim')
    # End shelf and butter dish, at the customer's left (+X).
    box((.146,0,.119),(.098,.288,.012),'iron')
    trough(.151,-.012,.078,.202,.126,.145,.005,'steel')
    box((.151,-.012,.133),(.057,.175,.005),'sauce')
    box((.151,-.030,.145),(.036,.072,.016),'butter')
    sync(); presentation()
    return render_stage('01-blockout')


def corn(cx,cy,length,index):
    # Eight-sided tapered cob with seven ring stations: broad kernel-like bands.
    n=8; base=len(V); z=.1575
    profile=[(-.5,.0085),(-.40,.0148),(-.24,.017),(-.07,.0178),(.12,.0172),(.32,.0147),(.5,.0065)]
    vs=[]
    skew=(-.018 if index%2 else .014)
    for t,r in profile:
        for j in range(n):
            angle=2*math.pi*j/n + math.pi/8
            vs.append((cx+skew*t+r*math.cos(angle), cy+t*length, z+r*.86*math.sin(angle)))
    fs=[]; cols=[]
    for k in range(6):
        for j in range(n):
            fs.append((k*n+j,k*n+(j+1)%n,(k+1)*n+(j+1)%n,(k+1)*n+j))
            # Sparse toasted facets rather than a continuous black stripe.
            toasted=(j in (1,2,3) and ((k*3+j+index*2)%11 in (0,1)))
            cols.append('char' if toasted else ('light' if (k+j+index)%5==0 else ('gold' if j in (4,5,6) or (k+j)%7==0 else 'corn')))
    fs += [(0,j+1,j) for j in range(1,n-1)]
    fs += [(6*n,6*n+j,6*n+j+1) for j in range(1,n-1)]
    cols += ['gold']*6+['light']*6
    V.extend(vs); F.extend([tuple(base+i for i in f) for f in fs]); C.extend(COLORS.index(c) for c in cols)
    # Two closed folded leaves rise at the back and taper to distinct pointed tips.
    end=cy+length*.42
    for sign in (-1,1):
        x=cx+sign*.006
        verts=[(x-.008,end-.031,z-.004),(x+.008,end-.031,z-.004),(x+sign*.013,end+.025,z+.001),
               (x+sign*.018,end+.042,z+.025),(x,end+.003,z+.011), (x,end+.002,z+.004)]
        faces=[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(1,0,5),(2,1,5),(3,2,5),(0,3,5)]
        add(verts,faces,'husk' if sign<0 else 'leaf')


def detail():
    # Grate bars run across the ears. Few broad bars stay readable at game distance.
    for y in [-.124+i*.025 for i in range(11)]: box((-.047,y,.139),(.264,.0055,.006),'steel')
    # A visible charcoal bed, faceted and dark: no flame particles or hidden details.
    for i in range(8):
        x=-.15+(i%4)*.065; y=-.067+(i//4)*.128
        verts=[(x-.022,y-.017,.079),(x+.025,y-.017,.079),(x+.021,y+.022,.079),(x-.020,y+.020,.079), (x+.002,y+.002,.110)]
        add(verts,[(0,3,2),(0,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)],'coal' if i%3 else 'ash')
    for i in range(7):
        corn(-.166+i*.0365, -.006+(i%3-1)*.007, .218-(i%3)*.009,i)
    # Three subdued metal air slots on the -Y customer face.
    for x in (-.105,-.047,.011): box((x,-.148,.106),(.031,.003,.010),'coal')
    # Small end handles tucked inside the footprint.
    for y in (-.12,.12): box((.139,y,.142),(.027,.010,.010),'rim')
    sync()
    return render_stage('02-detail')


def texture():
    ob=bpy.data.objects['CornGrill_Body']; me=ob.data
    # Each face owns a unique atlas cell, providing zero-overlap UVs for flat colour.
    count=len(me.polygons); cols=math.ceil(math.sqrt(count)); rows=math.ceil(count/cols)
    for layer in list(me.uv_layers): me.uv_layers.remove(layer)
    uv=me.uv_layers.new(name='UVMap')
    pixels=np.zeros((512,512,4),dtype=np.float32); pixels[:]=(.10,.12,.13,1)
    part=me.attributes['part_color']
    pad=.8/512
    for p in me.polygons:
        i=p.index; cx=i%cols; cy=i//cols
        u0=cx/cols+pad; u1=(cx+1)/cols-pad; v0=cy/rows+pad; v1=(cy+1)/rows-pad
        coords=[(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
        if len(p.loop_indices)==3: coords=[(u0,v0),(u1,v0),(u0,v1)]
        for li,co in zip(p.loop_indices,coords): uv.data[li].uv=co
        rgba=PALETTE[COLORS[part.data[i].value]]
        x0=round(cx*512/cols); x1=round((cx+1)*512/cols)
        y0=round(cy*512/rows); y1=round((cy+1)*512/rows)
        pixels[y0:y1,x0:x1]=rgba
        # Corn uses only flat rectangular colour blocks: two broad kernel rows
        # on each facet, with subtle golden gaps. No normals or extra materials.
        color_name=COLORS[part.data[i].value]
        if color_name in ('corn','gold','light','char') and len(p.vertices)==4:
            gap=list(rgba)
            gap[:3]=[component*.76 for component in rgba[:3]]
            pixels[y0:y1,x0:x1]=gap
            xa=max(x0+2,math.ceil(u0*512)); xb=min(x1-2,math.floor(u1*512))
            ya=max(y0+2,math.ceil(v0*512)); yb=min(y1-2,math.floor(v1*512))
            mid=(ya+yb)//2
            pixels[ya:mid-1,xa:xb]=rgba
            pixels[mid+1:yb,xa:xb]=rgba
    old_image=bpy.data.images.get('T_CornGrill_BaseColor')
    if old_image: bpy.data.images.remove(old_image)
    image=bpy.data.images.new('T_CornGrill_BaseColor',width=512,height=512,alpha=False)
    image.pixels.foreach_set(pixels.ravel()); image.filepath_raw=OUT+'/T_CornGrill_BaseColor.png'; image.file_format='PNG'; image.save()
    mat=bpy.data.materials.get('M_CornGrill') or bpy.data.materials.new('M_CornGrill'); mat.use_nodes=True
    for node in list(mat.node_tree.nodes):
        if node.type=='TEX_IMAGE': mat.node_tree.nodes.remove(node)
    bs=mat.node_tree.nodes.get('Principled BSDF'); bs.inputs['Roughness'].default_value=.75
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Closest'; tex.label='512px flat colour atlas'
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons: p.material_index=0
    image.pack()
    image.filepath='//T_CornGrill_BaseColor.png'
    return render_stage('03-texture')


def finish():
    ob=bpy.data.objects['CornGrill_Body']
    # Conservative eight-corner hull encloses every visible part, including husk tips.
    old=bpy.data.objects.get('CornGrill_Collision')
    if old: bpy.data.objects.remove(old,do_unlink=True)
    bm=bmesh.new()
    for x in (-.195,.195):
        for y in (-.151,.172):
            for z in (0,.196): bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new('CornGrill_ConvexHull'); bm.to_mesh(mesh); bm.free()
    hull=bpy.data.objects.new('CornGrill_Collision',mesh); bpy.context.collection.objects.link(hull)
    hull.hide_render=True; hull.display_type='WIRE'; hull.hide_set(True)
    for item in (ob,hull):
        item.location=(0,0,0); item.rotation_euler=(0,0,0); item.scale=(1,1,1)
    # Orient the viewport to the customer (-Y); camera uses the same front quadrant.
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); hull.hide_set(False); hull.select_set(True)
    bpy.context.view_layer.objects.active=ob
    bpy.ops.export_scene.fbx(filepath=OUT+'/CornGrill.fbx', use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
    hull.hide_set(True); hull.select_set(False)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/CornGrill.blend')
    result=render_stage('04-final')
    result['collision_triangles']=sum(len(p.vertices)-2 for p in mesh.polygons)
    return result

if __name__=='__main__':
    blockout(); detail(); texture(); finish()

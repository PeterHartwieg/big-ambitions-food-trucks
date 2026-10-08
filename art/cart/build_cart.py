"""Regenerate the metre-scale NYC cart in the open start scene.
Run inside Blender: exec(compile(open(PATH).read(), PATH, 'exec')).
BUILD_STAGE can be 'blockout', 'detail', or 'final' (default).
Preserves HumanRef_1.8m; all generated files stay next to this script.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = 'art/cart'
PALETTE = [(0.66,0.72,0.75,1), (0.37,0.43,0.47,1), (0.047,0.058,0.068,1),
           (0.78,0.065,0.095,1), (0.99,0.64,0.075,1), (0.80,0.85,0.86,1),
           (0.21,0.26,0.29,1)]
NAMES = ['Cart_Body','Cart_Wheel_L','Cart_Wheel_R','Cart_Lid','Cart_Collision']
os.makedirs(OUT+'/renders', exist_ok=True)

class MeshBuild:
    def __init__(self): self.v=[]; self.f=[]; self.c=[]
    def add(self, verts, faces, color=0):
        n=len(self.v); self.v.extend([tuple(v) for v in verts])
        self.f.extend([tuple(n+i for i in f) for f in faces])
        self.c.extend(color if isinstance(color,list) else [color]*len(faces))
    def box(self, center, dims, color=0, bevel=0):
        bm=bmesh.new(); bmesh.ops.create_cube(bm, size=1)
        for v in bm.verts: v.co=Vector(center)+Vector([v.co[i]*dims[i] for i in range(3)])
        if bevel:
            bevel=min(bevel,min(dims)*.30)
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        # Chamfer corners can be triangles; triangulate only polygons with >4 corners.
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts)>4])
        bm.verts.ensure_lookup_table(); bm.verts.index_update()
        self.add([v.co for v in bm.verts], [[v.index for v in f.verts] for f in bm.faces], color)
        bm.free()
    def cyl(self, p1, p2, radius, color=0, sides=8):
        a,b=Vector(p1),Vector(p2); axis=(b-a).normalized()
        ref=Vector((0,0,1)) if abs(axis.z)<0.9 else Vector((1,0,0))
        u=axis.cross(ref).normalized(); v=axis.cross(u).normalized()
        verts=[p+radius*(u*math.cos(i*2*math.pi/sides)+v*math.sin(i*2*math.pi/sides)) for p in (a,b) for i in range(sides)]
        faces=[]
        for i in range(sides):
            j=(i+1)%sides; faces.append((i,j,sides+j,sides+i))
        # Fan caps without centre vertices, no n-gons.
        for i in range(1,sides-1):
            faces.extend([(0,i+1,i),(sides,sides+i,sides+i+1)])
        self.add(verts,faces,color)
    def ring_x(self, hub, profile, sides, color):
        verts=[]; faces=[]; p=len(profile)
        for i in range(sides):
            t=i*2*math.pi/sides
            for x,r in profile: verts.append((hub[0]+x, hub[1]+r*math.cos(t), hub[2]+r*math.sin(t)))
        for i in range(sides):
            for j in range(p): faces.append((i*p+j,((i+1)%sides)*p+j,((i+1)%sides)*p+(j+1)%p,i*p+(j+1)%p))
        self.add(verts,faces,color)
    def radial_bar(self, hub, angle, r0, r1, color=5):
        # Closed narrow rectangular spoke, with depth along X.
        u=Vector((0,math.cos(angle),math.sin(angle))); v=Vector((0,-math.sin(angle),math.cos(angle)))
        centre=Vector(hub); verts=[]
        for r in (r0,r1):
            for x,w in [(-.008,-.004),(.008,-.004),(.008,.004),(-.008,.004)]:
                verts.append(centre+r*u+Vector((x,0,0))+w*v)
        self.add(verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],color)
    def obj(self,name,origin=(0,0,0)):
        origin=Vector(origin); me=bpy.data.meshes.new(name+'_Mesh')
        me.from_pydata([Vector(v)-origin for v in self.v],[],self.f); me.update()
        attr=me.attributes.new('part_colour','INT','FACE')
        for a,c in zip(attr.data,self.c): a.value=c
        ob=bpy.data.objects.new(name,me); bpy.context.scene.collection.objects.link(ob); ob.location=origin
        bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
        return ob

def wheel(x,name):
    w=MeshBuild(); hub=(x,0,.25)
    w.ring_x(hub,[(-.025,.226),(-.017,.25),(.017,.25),(.025,.226),(.018,.216),(-.018,.216)],16,2)
    w.ring_x(hub,[(-.016,.221),(.016,.221),(0,.195)],16,5)
    for i in range(6): w.radial_bar(hub,i*math.pi/3,.017,.209)
    w.cyl((x-.024,0,.25),(x+.024,0,.25),.026,1,8)
    return w.obj(name,hub)

def canopy(m):
    # Twelve alternating panels, each three facets wide. A closed thin shell.
    n=36; profile=[(.036,2.288),(.42,2.235),(.80,2.079),(.80,2.030)]
    verts=[]
    for inner in (False,True):
        for r,z in profile:
            for i in range(n):
                t=2*math.pi*i/n
                scallop=.006*math.sin((i%3)*math.pi/3) if r==.80 and z==2.030 else 0
                rr=r-(.004 if inner else 0)
                verts.append((rr*math.cos(t),-.60+rr*math.sin(t),z-scallop-(.004 if inner else 0)))
    faces=[]; colors=[]; layers=len(profile); stride=layers*n
    for side in range(2):
        for j in range(layers-1):
            for i in range(n):
                k=(i+1)%n; a=side*stride+j*n
                faces.append((a+i,a+k,a+n+k,a+n+i)); colors.append(3+(i//3)%2)
    for j in (0,layers-1):
        for i in range(n):
            k=(i+1)%n; a=j*n
            faces.append((a+i,a+k,stride+a+k,stride+a+i)); colors.append(3+(i//3)%2)
    m.add(verts,faces,colors)
    m.cyl((0,-.60,.95),(0,-.60,2.294),.016,5,10)
    m.cyl((0,-.60,2.275),(0,-.60,2.300),.012,1,8)
    m.cyl((0,-.60,.95),(0,-.60,1.025),.029,1,10)

def fender(m,sign):
    # Swept steel strip, clear of the complete R=.25 wheel volume.
    n=12; verts=[]; faces=[]
    for i in range(n+1):
        t=math.radians(12+156*i/n)
        for x,r in [(.399,.268),(.499,.268),(.499,.285),(.399,.285)]:
            verts.append((sign*x,r*math.cos(t),.25+r*math.sin(t)))
    for i in range(n):
        for j in range(4): faces.append((i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j))
    faces.extend([(3,2,1,0),(n*4,n*4+1,n*4+2,n*4+3)])
    m.add(verts,faces,0)
    for y in (-.19,.19): m.box((sign*.410,y,.485),(.022,.018,.070),1)

def build(stage):
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for ob in list(bpy.data.objects):
        if ob.name in NAMES or ob.name.startswith('Preview_'): bpy.data.objects.remove(ob,do_unlink=True)
    for ma in list(bpy.data.materials):
        if ma.name.startswith('Cart_') and not ma.users: bpy.data.materials.remove(ma)
    s=bpy.context.scene; s.unit_settings.system='METRIC'; s.unit_settings.scale_length=1
    b=MeshBuild()
    # Main steel shell: four closed plates plus a floor, all within exact body dimensions.
    b.box((0,0,.262),(.80,1.40,.024),1,.006)
    for x in (-.391,.391): b.box((x,0,.59),(.018,1.40,.66),0,.004)
    for y in (-.691,.691): b.box((0,y,.59),(.764,.018,.66),0,.004)
    if stage=='blockout': b.box((0,0,.936),(.80,1.40,.028),5,.004)
    else:
        # Counter assembled around a real recessed steam well. Top stays at .950.
        for x in (-.2785,.2785): b.box((x,0,.936),(.243,1.40,.028),5,.003)
        b.box((0,-.491,.936),(.314,.418,.028),5,.003)
        b.box((0,.341,.936),(.314,.718,.028),5,.003)
        # Steam well floor and interior walls (concealed with closed lid).
        b.box((0,-.150,.875),(.314,.264,.012),1)
        for x in (-.1535,.1535): b.box((x,-.150,.909),(.007,.264,.068),1)
        for y in (-.2785,-.0215): b.box((0,y,.909),(.300,.007,.068),1)
        # Dark flush rim, never above .965.
        for x in (-.154,.154): b.box((x,-.15,.952),(.008,.260,.004),1)
        for y in (-.278,-.022): b.box((0,y,.952),(.30,.006,.004),1)
        # Fixed second lid with flush pull, set behind the hinged one.
        b.box((0,.295,.952),(.306,.266,.007),1,.002)
        b.box((0,.295,.957),(.29,.25,.004),0,.001)
        b.box((0,.370,.961),(.065,.012,.004),6,.001)
        b.cyl((-.12,-.025,.950),(.12,-.025,.950),.006,1,8)
    # Correct single rear push bar, with grip centres at the required hand points.
    b.cyl((-.375,.75,1.16),(.375,.75,1.16),.0175,5,10)
    for x in (-.30,.30): b.cyl((x-.053,.75,1.16),(x+.053,.75,1.16),.020,2,10)
    for x in (-.355,.355):
        b.cyl((x,.69,.914),(x,.75,1.16),.014,0,8)
        b.box((x,.684,.930),(.05,.025,.045),1,.004)
    # The front swivel caster and rear folding prop both reach the floor.
    b.cyl((0,-.60,.11),(0,-.60,.27),.021,1,8)
    b.cyl((-.019,-.617,.060),(.019,-.617,.060),.060,2,12)
    b.cyl((-.022,-.617,.060),(.022,-.617,.060),.021,5,8)
    for x in (-.025,.025): b.box((x,-.605,.111),(.012,.065,.11),0,.003)
    b.cyl((0,.59,.245),(0,.615,.025),.016,1,8)
    b.box((0,.615,.012),(.11,.09,.024),2,.006)
    b.box((0,.59,.239),(.085,.05,.028),5,.003)
    canopy(b)
    if stage!='blockout':
        for sign in (-1,1): fender(b,sign)
        # Lower front/rear folded seams and subtle side reinforcement.
        for y in (-.701,.701):
            b.box((0,y,.293),(.76,.007,.038),1,.002)
            b.box((0,y,.888),(.76,.007,.014),1,.002)
        # Serving door on the right (-X), ahead of the wheel. No branding.
        b.box((-.403,-.434,.598),(.008,.458,.526),6,.005)
        b.box((-.409,-.434,.598),(.010,.444,.512),0,.006)
        for z in (.402,.778):
            b.box((-.417,-.656,z),(.014,.028,.061),1,.003)
            b.cyl((-.423,-.660,z-.030),(-.423,-.660,z+.030),.008,5,8)
        # Small side pull, with two standoffs.
        for y in (-.300,-.246): b.box((-.426,y,.645),(.025,.010,.021),1,.002)
        b.cyl((-.441,-.306,.645),(-.441,-.240,.645),.007,5,8)
        # Blank pressed panel on the other face, placed above the wheel.
        b.box((.404,.05,.754),(.008,.74,.248),1,.004)
        b.box((.410,.05,.754),(.006,.716,.224),0,.003)
        # Fine ribs under canopy remain wholly above headroom.
        for i in range(12):
            t=i*math.pi/6
            b.cyl((.036*math.cos(t),-.60+.036*math.sin(t),2.283),(.785*math.cos(t),-.60+.785*math.sin(t),2.074),.003,1,5)
    body=b.obj('Cart_Body')
    wl=wheel(.455,'Cart_Wheel_L'); wr=wheel(-.455,'Cart_Wheel_R')
    lid=MeshBuild()
    lid.box((0,-.150,.952),(.30,.25,.010),0,.002)
    lid.box((0,-.223,.959),(.068,.017,.006),6,.001)
    li=lid.obj('Cart_Lid',(0,-.025,.95))
    # Build the strictly convex hull explicitly with the prescribed bmesh operator.
    points=[(x,y,z) for x in (-.4,.4) for y in (-.7,.7) for z in (.25,.95)]
    points += [(x,.25*math.cos(i*math.pi/8),.25+.25*math.sin(i*math.pi/8)) for x in (-.48,.48) for i in range(16)]
    points += [(x,y,0) for x in (-.055,.055) for y in (-.66,.66)]
    bm=bmesh.new()
    for p in points: bm.verts.new(p)
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('Cart_Collision_Mesh'); bm.to_mesh(me); bm.free()
    co=bpy.data.objects.new('Cart_Collision',me); s.collection.objects.link(co)
    co.hide_render=True; co.display_type='WIRE'; co.hide_set(True)
    # Temporary palette materials support the blockout/detail reviews.
    mats=[]
    for i,c in enumerate(PALETTE):
        ma=bpy.data.materials.new('Cart_Review_'+str(i)); ma.diffuse_color=c; mats.append(ma)
    visible=[body,wl,wr,li]
    for o in visible:
        for ma in mats: o.data.materials.append(ma)
        for p,a in zip(o.data.polygons,o.data.attributes['part_colour'].data): p.material_index=a.value
    setup_preview()
    return visible

def setup_preview():
    s=bpy.context.scene
    s.render.resolution_x=1100; s.render.resolution_y=1100; s.render.resolution_percentage=100
    s.render.image_settings.file_format='PNG'; s.render.film_transparent=False
    s.world.color=(.12,.12,.12)
    camdata=bpy.data.cameras.new('Preview_Camera'); cam=bpy.data.objects.new('Preview_Camera',camdata)
    s.collection.objects.link(cam); cam.location=(-3.8,-4.9,3.1)
    target=Vector((0,-.23,1.12)); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    camdata.type='ORTHO'; camdata.ortho_scale=3.08; s.camera=cam
    for name,loc,power,size in [('Key',(-3,-4,6),850,5),('Fill',(3,-1,4),650,4),('Rim',(0,4,5),900,3)]:
        ld=bpy.data.lights.new('Preview_'+name,'AREA'); ld.energy=power*.55; ld.shape='DISK'; ld.size=size
        ob=bpy.data.objects.new('Preview_'+name,ld); s.collection.objects.link(ob); ob.location=loc
        ob.rotation_euler=(Vector((0,0,1))-ob.location).to_track_quat('-Z','Y').to_euler()
    s.view_settings.view_transform='AgX'
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=3.5
                area.spaces.active.region_3d.view_location=(0,-.2,1.1)
                area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
                area.spaces.active.shading.type='SOLID'
                area.spaces.active.shading.color_type='MATERIAL'


def review(name, textured=False):
    s=bpy.context.scene
    if textured:
        s.render.engine='BLENDER_EEVEE'
        s.world.use_nodes=True; s.world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.19,.23,.28,1)
        s.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.55
    else:
        s.render.engine='BLENDER_WORKBENCH'
        s.display.shading.light='STUDIO'; s.display.shading.studiolight_rotate_z=.35
        s.display.shading.color_type='MATERIAL'; s.display.shading.show_shadows=True
        s.display.shading.show_cavity=True; s.display.shading.cavity_type='BOTH'
        s.display.shading.background_type='WORLD'; s.world.color=(.085,.11,.145)
        s.display.shading.show_object_outline=False
    s.render.filepath=OUT+'/renders/'+name
    bpy.ops.render.render(write_still=True)


def texture_and_uv(visible):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in visible: ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active=visible[0]
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.004,margin_method='FRACTION', area_weight=.1,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Rasterise flat part colours into a single atlas with 3px gutter dilation.
    n=1024; tex=np.zeros((n,n,4),dtype=np.float32); tex[:]=(.30,.36,.40,1)
    mask=np.zeros((n,n),dtype=bool)
    for ob in visible:
        me=ob.data; me.calc_loop_triangles(); uv=me.uv_layers.active.data; colors=me.attributes['part_colour'].data
        for t in me.loop_triangles:
            a,b,c=np.array([uv[k].uv[:] for k in t.loops])*n
            x0=max(0,int(math.floor(min(a[0],b[0],c[0])))); x1=min(n-1,int(math.ceil(max(a[0],b[0],c[0]))))
            y0=max(0,int(math.floor(min(a[1],b[1],c[1])))); y1=min(n-1,int(math.ceil(max(a[1],b[1],c[1]))))
            d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(d)<1e-12: continue
            xs,ys=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
            l1=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/d
            l2=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/d
            inside=(l1>=-1e-7)&(l2>=-1e-7)&(1-l1-l2>=-1e-7)
            tex[y0:y1+1,x0:x1+1][inside]=PALETTE[colors[t.polygon_index].value]
            mask[y0:y1+1,x0:x1+1]|=inside
    fill=float(mask.mean())
    for iteration in range(3):
        old=mask.copy(); expanded=mask.copy()
        for dy,dx in [(1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)]:
            near=np.roll(old,(dy,dx),(0,1)); put=near&~expanded
            tex[put]=np.roll(tex,(dy,dx),(0,1))[put]; expanded|=put
        mask=expanded
    old=bpy.data.images.get('T_FoodCart_BaseColor')
    if old: bpy.data.images.remove(old)
    img=bpy.data.images.new('T_FoodCart_BaseColor',width=n,height=n,alpha=True)
    img.colorspace_settings.name='sRGB'; img.pixels.foreach_set(tex.ravel()); img.update()
    img.filepath_raw=OUT+'/T_FoodCart_BaseColor.png'; img.file_format='PNG'; img.save(); img.reload()
    ma=bpy.data.materials.new('Cart_Atlas'); ma.use_nodes=True; ma.diffuse_color=PALETTE[0]
    nt=ma.node_tree; bsdf=nt.nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=.63; bsdf.inputs['Metallic'].default_value=.18
    node=nt.nodes.new('ShaderNodeTexImage'); node.name='FoodCart_BaseColor'; node.image=img
    node.interpolation='Linear'; nt.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
    for ob in visible:
        ob.data.materials.clear(); ob.data.materials.append(ma)
        for p in ob.data.polygons: p.material_index=0
    for mat in list(bpy.data.materials):
        if mat.name.startswith('Cart_Review_') and not mat.users: bpy.data.materials.remove(mat)
    return fill

def export_save():
    bpy.ops.object.select_all(action='DESELECT')
    for name in NAMES:
        ob=bpy.data.objects[name]; ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active=bpy.data.objects['Cart_Body']
    bpy.ops.export_scene.fbx(filepath=OUT+'/FoodCart.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    bpy.data.objects['Cart_Collision'].hide_set(True)
    bpy.ops.object.select_all(action='DESELECT'); bpy.data.objects['Cart_Body'].select_set(True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/FoodCart.blend')

stage=globals().get('BUILD_STAGE','final')
visible=build(stage)
if stage=='blockout':
    review('01-blockout.png')
elif stage=='detail':
    review('02-detail.png')
else:
    fill=texture_and_uv(visible)
    review('03-uv-texture.png',True)
    review('04-final.png',True)
    export_save()
result={'stage':stage,'triangles':{ob.name:sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in visible},'uv_fill':globals().get('fill')}

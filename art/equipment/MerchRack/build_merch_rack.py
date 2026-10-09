"""Regenerate in the open art/start.blend scene (metres, front = -Y).
Run in Blender: exec(compile(open(PATH).read(), PATH, 'exec')).
Optional RACK_STAGE=1..4 saves the corresponding review render.
Only generated rack objects and its own preview rig are replaced; HumanRef is untouched.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector, Matrix
OUT = os.path.dirname(os.path.abspath(__file__))
STAGE = globals().get('RACK_STAGE', 4)
os.makedirs(OUT + '/renders', exist_ok=True)
PAL = {'frame':(0.065,0.15,0.20), 'wood':(0.76,0.60,0.39), 'gold':(0.98,0.70,0.18),
       'cream':(0.95,0.90,0.76), 'coral':(0.90,0.22,0.23), 'teal':(0.09,0.66,0.57),
       'blue':(0.20,0.43,0.85), 'purple':(0.60,0.29,0.70), 'snow':(0.94,0.98,1.0),
       'globe':(0.48,0.80,0.91), 'city':(0.12,0.31,0.40), 'glass':(0.16,0.38,0.48),
       'rubber':(0.045,0.065,0.08)}
V, F, C = [], [], []
def part(vs, fs, color):
    off=len(V); V.extend(vs)
    F.extend(tuple(off+i for i in f) for f in fs)
    C.extend([color]*len(fs))
def box(center, size, color, angle=0):
    x,y,z=center; a,b,c=[s/2 for s in size]
    m=Matrix.Rotation(angle,3,'Z')
    vs=[tuple(Vector(center)+m@Vector(p)) for p in [(-a,-b,-c),(a,-b,-c),(a,b,-c),(-a,b,-c),(-a,-b,c),(a,-b,c),(a,b,c),(-a,b,c)]]
    part(vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],color)
def prism(center, size, bevel, color):
    x,y,z=center; a,b,c=[s/2 for s in size]; t=bevel
    ring=[(-a+t,-b),(a-t,-b),(a,-b+t),(a,b-t),(a-t,b),(-a+t,b),(-a,b-t),(-a,-b+t)]
    vs=[(x+u,y+v,z+h) for h in (-c,c) for u,v in ring]
    fs=[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    fs += [(0,i+1,i) for i in range(1,7)] + [(8,8+i,8+i+1) for i in range(1,7)]
    part(vs,fs,color)
def lathe(cx,cy,profile,n,color,closed_ring=False):
    vs=[(cx+r*math.cos(2*math.pi*i/n),cy+r*math.sin(2*math.pi*i/n),z) for r,z in profile for i in range(n)]
    fs=[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(profile)-1) for i in range(n)]
    if closed_ring:
        a=(len(profile)-1)*n
        fs.extend((a+i,a+(i+1)%n,(i+1)%n,i) for i in range(n))
    else:
        # No redundant centre vertices on planar convex caps.
        fs += [(0,i+1,i) for i in range(1,n-1)]
        a=(len(profile)-1)*n
        fs += [(a,a+i,a+i+1) for i in range(1,n-1)]
    part(vs,fs,color)
def sphere(center,r,n=10,rings=4,color='globe'):
    cx,cy,cz=center
    vs=[(cx,cy,cz-r),(cx,cy,cz+r)]
    for j in range(1,rings+1):
        th=math.pi*j/(rings+1)
        for i in range(n):
            ph=2*math.pi*i/n
            vs.append((cx+r*math.sin(th)*math.cos(ph),cy+r*math.sin(th)*math.sin(ph),cz-r*math.cos(th)))
    fs=[(0,2+(i+1)%n,2+i) for i in range(n)]
    for j in range(rings-1):
        a=2+j*n;b=a+n
        fs.extend((a+i,a+(i+1)%n,b+(i+1)%n,b+i) for i in range(n))
    fs.extend((1,2+(rings-1)*n+i,2+(rings-1)*n+(i+1)%n) for i in range(n))
    part(vs,fs,color)
def hook(x,y,z):
    # Four-sided tube following a J curve; curved handles read in silhouette.
    points=[(x,y,z),(x+.008,y,z+.029),(x+.021,y,z+.026),(x+.026,y,z+.013),(x+.021,y,z+.005)]
    vs=[]; n=4
    for j,p in enumerate(points):
        d=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
        d.normalize(); side=Vector((0,1,0)); other=d.cross(side).normalized()
        for i in range(n):
            co=Vector(p)+.004*(math.cos(i*2*math.pi/n)*side+math.sin(i*2*math.pi/n)*other)
            vs.append(tuple(co))
    fs=[(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i) for j in range(len(points)-1) for i in range(n)]
    fs.extend([(3,2,1,0),(16,17,18,19)])
    part(vs,fs,'cream')
def gift(x,y,z,w,d,h,color,ribbon):
    box((x,y,z+h/2),(w,d,h),color)
    # Two continuous wrapping bands, offset just beyond the paper.
    box((x,y,z+h/2),(.009,d+.0018,h+.0018),ribbon)
    box((x,y,z+h/2),(w+.0018,.009,h+.0018),ribbon)
    # Two folded ribbon loops, intentionally chunky at game scale.
    for s in (-1,1):
        vv=[(x,y-.004,z+h+.003),(x+s*.019,y-.004,z+h+.010),(x+s*.014,y-.004,z+h+.020)]
        vs=vv+[(a,b+.009,c) for a,b,c in vv]
        part(vs,[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],ribbon)
def octa(center,r,color):
    x,y,z=center
    part([(x-r,y,z),(x+r,y,z),(x,y-r,z),(x,y+r,z),(x,y,z-r),(x,y,z+r)],
         [(0,2,4),(2,1,4),(1,3,4),(3,0,4),(2,0,5),(1,2,5),(3,1,5),(0,3,5)],color)
def build_geometry():
    # A single broad foot stands directly on the counter and covers the origin.
    prism((0,0,.014),(.388,.344,.028),.009,'frame')
    # Three exposed shallow trays rise towards +Y, away from the customer.
    for y,z in [(-.116,.081),(-.011,.183),(.096,.285)]:
        prism((-.057,y,z-.009),(.264,.105,.018),.004,'wood')
        # Color the tray's own front and chamfer faces instead of extra hidden rail boxes.
        for offset in (0,1,7): C[-20+offset]='gold'
    # Two continuous stepped side supports.
    outline=[(-.122,.028),(.051,.028),(.051,.273),(.033,.273),(.033,.220),(-.020,.220),(-.020,.094),(-.122,.094)]
    # Ear-clipped end faces for this concave stair profile (no n-gons).
    from mathutils.geometry import tessellate_polygon
    captris=tessellate_polygon([[Vector((0,y,z)) for y,z in outline]])
    for x in (-.170,.056):
        vv=[(xx,y,z) for xx in (x-.0085,x+.0085) for y,z in outline]
        fs=[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
        for t in captris:
            ids=list(t) if isinstance(t[0],int) else [min(range(8),key=lambda j:(Vector((0,*outline[j]))-v).length) for v in t]
            fs.extend([tuple(reversed(ids)),tuple(j+8 for j in ids)])
        part(vv,fs,'frame')
    # Hollow eight-sided umbrella pot, floor and wall form one closed solid.
    lathe(.133,.025,[(.051,.028),(.057,.245),(.050,.245),(.045,.043)],8,'frame')
    lathe(.133,.025,[(.058,.231),(.058,.247),(.049,.247),(.049,.231)],8,'gold',closed_ring=True)
    # Four separated folded umbrellas. Tips down, J handles up.
    for x,y,h,color in [(.111,.003,.610,'coral'),(.155,.003,.648,'teal'),(.111,.047,.636,'gold'),(.155,.047,.602,'blue')]:
        lathe(x,y,[(.003,.079),(.003,h-.027)],4,'cream')
        lathe(x,y,[(.003,.258),(.012,.298),(.017,h-.065),(.010,h-.035)],6,color)
        if STAGE>=2:
            lathe(x,y,[(.018,h-.17),(.018,h-.16)],6,'frame')
            hook(x,y,h-.027)
    if STAGE == 1:
        for x,h,color in [(-.145,.075,'coral'),(-.061,.063,'teal'),(.019,.085,'purple')]:
            box((x,-.116,.081+h/2),(.064,.068,h),color)
        for x in (-.13,-.019):
            lathe(x,.092,[(.041,.285),(.041,.303)],10,'frame')
            sphere((x,.092,.344),.043)
        box((-.131,-.011,.203),(.078,.035,.037),'gold')
        box((-.017,-.012,.21),(.079,.032,.055),'city')
        return
    for x,h,color,rib in [(-.145,.075,'coral','cream'),(-.061,.063,'teal','gold'),(.019,.085,'purple','cream')]:
        gift(x,-.116,.081,.065,.069,h,color,rib)
    # Snow globes: icy blue globe silhouette, snowy rim, and miniature city relief.
    for x in (-.13,-.019):
        lathe(x,.092,[(.041,.285),(.041,.298)],10,'frame')
        lathe(x,.092,[(.039,.298),(.039,.306)],10,'snow')
        sphere((x,.092,.346),.043)
        # Bold miniature skyline relief sits against the customer-facing glass.
        for dx,w,h in [(-.019,.012,.017),(0,.014,.033),(.018,.011,.023)]:
            box((x+dx,.053,.322+h/2),(w,.009,h),'city')
        octa((x-.018,.057,.368),.006,'snow')
        octa((x+.017,.057,.359),.004,'snow')
    # A generic taxi, nose pointing towards -X. No signage or identity marks.
    box((-.130,-.012,.193),(.088,.038,.018),'gold')
    box((-.129,-.012,.211),(.045,.034,.022),'glass')
    box((-.129,-.012,.223),(.050,.037,.005),'gold')
    box((-.172,-.013,.194),(.003,.028,.007),'cream')
    for x in (-.158,-.102):
        for y in (-.034,.010):
            # Tiny wheels are six-sided capped cylinders along Y.
            start=len(V);lathe(0,0,[(.008,-.004),(.008,.004)],5,'rubber')
            for k in range(start,len(V)):
                a,b,c=V[k];V[k]=(x+a,y+c,.191+b)
    # Separate skyline souvenir: three simple towers on a blue block.
    box((-.015,-.009,.19),(.083,.038,.014),'cream')
    for x,w,h,col in [(-.044,.021,.042,'city'),(-.014,.024,.069,'blue'),(.013,.020,.050,'city')]:
        box((x,-.007,.197+h/2),(w,.025,h),col)
    box((-.014,-.007,.275),(.007,.007,.019),'gold')

def setup_rig():
    scene=bpy.context.scene
    camera_data=bpy.data.cameras.new('MerchPreview_Camera');cam=bpy.data.objects.new('MerchPreview_Camera',camera_data);scene.collection.objects.link(cam)
    cam.location=(.84,-1.48,1.02);target=Vector((0,0,.335))
    cam.rotation_euler=(target-Vector(cam.location)).to_track_quat('-Z','Y').to_euler()
    camera_data.type='ORTHO';camera_data.ortho_scale=.83;scene.camera=cam
    for name,loc,power,size in [('Key',(.4,-.7,1.3),24,1.0),('Fill',(-.8,-.1,.65),14,.8),('Rim',(.1,.8,1.1),28,.7)]:
        ld=bpy.data.lights.new('MerchPreview_'+name,'AREA');lo=bpy.data.objects.new(ld.name,ld);scene.collection.objects.link(lo)
        lo.location=loc;lo.rotation_euler=(Vector((0,0,.30))-Vector(loc)).to_track_quat('-Z','Y').to_euler();ld.energy=power;ld.shape='DISK';ld.size=size
    scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.world.use_nodes=True;scene.world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.17,.21,.25,1)
    scene.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.6
    scene.view_settings.view_transform='AgX'
    # Make the saved viewport match the front review angle.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
                area.spaces.active.region_3d.view_distance=1.1
                area.spaces.active.region_3d.view_location=target
                area.spaces.active.shading.type='MATERIAL'

def uv_texture(body):
    # Each face owns its own disjoint atlas cell. Flat colors need no distortion-free unwrap;
    # full-cell quad islands keep UV occupancy high without color sharing/overlaps.
    mesh=body.data;layer=mesh.uv_layers.new(name='UVMap');count=len(mesh.polygons)
    cols=math.ceil(math.sqrt(count));rows=math.ceil(count/cols)
    img=bpy.data.images.new('T_MerchRack_BaseColor',width=512,height=512,alpha=False)
    pixels=np.ones((512,512,4),dtype=np.float32)
    pixels[:,:,:3]=(.065,.15,.20)
    pad=.55/512
    for idx,p in enumerate(mesh.polygons):
        col=idx%cols;row=idx//cols
        u0=col/cols+pad;u1=(col+1)/cols-pad;v0=row/rows+pad;v1=(row+1)/rows-pad
        # Triangles consume half a cell. Their own cell's color also forms padding.
        coords=[(u0,v0),(u1,v0),(u1,v1),(u0,v1)] if len(p.vertices)==4 else [(u0,v0),(u1,v0),(u0,v1)]
        for li,co in zip(p.loop_indices,coords):layer.data[li].uv=co
        a=int(round(col*512/cols));b=int(round((col+1)*512/cols));c=int(round(row*512/rows));d=int(round((row+1)*512/rows))
        pixels[c:d,a:b,:3]=PAL[C[idx]]
    img.pixels.foreach_set(pixels.ravel());img.filepath_raw=OUT+'/T_MerchRack_BaseColor.png';img.file_format='PNG';img.save()
    img.filepath='//T_MerchRack_BaseColor.png'
    mat=bpy.data.materials.new('M_MerchRack');mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.7
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img;tex.interpolation='Closest';tex.extension='EXTEND'
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    mesh.materials.clear();mesh.materials.append(mat)
    for p in mesh.polygons:p.material_index=0
    body['atlas_face_cells']=count;body['atlas_layout']=f'{cols} x {rows}'

def run():
    if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    # Regeneration only replaces objects created by this file.
    for o in list(bpy.data.objects):
        if o.name in ('MerchRack_Body','MerchRack_Collision') or o.name.startswith('MerchPreview_'):
            bpy.data.objects.remove(o,do_unlink=True)
    for m in list(bpy.data.materials):
        if m.name.startswith('MerchColor_') or m.name.startswith('M_MerchRack'):bpy.data.materials.remove(m)
    for i in list(bpy.data.images):
        if i.name.startswith('T_MerchRack_BaseColor'):bpy.data.images.remove(i)
    build_geometry()
    me=bpy.data.meshes.new('MerchRack_Mesh');me.from_pydata(V,[],F);me.update()
    body=bpy.data.objects.new('MerchRack_Body',me);bpy.context.scene.collection.objects.link(body)
    # Recalculate each closed island outwards, then triangulate any builder cap n-gons.
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    # Facet tags retain face order through normals recalculation.
    for name,rgb in PAL.items():
        mat=bpy.data.materials.new('MerchColor_'+name);mat.diffuse_color=(*rgb,1);mat.use_nodes=True
        mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(*rgb,1)
        mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.7
        me.materials.append(mat)
    keys=list(PAL)
    for p,color in zip(me.polygons,C):p.material_index=keys.index(color);p.use_smooth=False
    if STAGE>=3:uv_texture(body)
    if STAGE>=4:
        # Enclosing tapered hull: bottom footprint, shoulder box and umbrella top.
        points=[(x,y,z) for z,xs,ys in [(0,(-.194,.194),(-.172,.172)),(.40,(-.194,.194),(-.172,.172)),(.660,(.085,.193),(-.025,.079))] for x in xs for y in ys]
        bm=bmesh.new();verts=[bm.verts.new(p) for p in points]
        hull=bmesh.ops.convex_hull(bm,input=verts,use_existing_faces=False)
        unused=[v for v in bm.verts if not v.link_faces]
        if unused:bmesh.ops.delete(bm,geom=unused,context='VERTS')
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces))
        cm=bpy.data.meshes.new('MerchRack_Hull');bm.to_mesh(cm);bm.free()
        co=bpy.data.objects.new('MerchRack_Collision',cm);bpy.context.scene.collection.objects.link(co);co.hide_render=True;co.display_type='WIRE';co.hide_set(True)
        co['construction']='bmesh.ops.convex_hull; enclosing tapered counter footprint'
    setup_rig()
    bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
    body['front']='-Y';body['customer_left']='+X';body['scale_metres']='0.388 x 0.344 x 0.6525 countertop stand'
    bpy.context.scene.render.image_settings.file_format='PNG'
    bpy.context.scene.render.filepath=OUT+'/renders/'+['01-blockout.png','02-detail.png','03-texture.png','04-final.png'][STAGE-1]
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/MerchRack.blend')
    bpy.ops.render.render(write_still=True)
    if STAGE>=4:
        co.hide_set(False);bpy.ops.object.select_all(action='DESELECT');body.select_set(True);co.select_set(True);bpy.context.view_layer.objects.active=body
        bpy.ops.export_scene.fbx(filepath=OUT+'/MerchRack.fbx', use_selection=True, object_types={'MESH'}, global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True, mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False, add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
        co.hide_set(True);co.select_set(False);body.select_set(True)
        # The delivered scene contains only the asset and untouched scale reference.
        for o in list(bpy.data.objects):
            if o.name.startswith('MerchPreview_'):bpy.data.objects.remove(o,do_unlink=True)
        for m in list(bpy.data.materials):
            if m.name.startswith('MerchColor_') and m.users==0:bpy.data.materials.remove(m)
        bpy.ops.wm.save_as_mainfile(filepath=OUT+'/MerchRack.blend')
    return {'stage':STAGE,'body_triangles':sum(len(p.vertices)-2 for p in me.polygons),'body_faces':len(me.polygons),'render':bpy.context.scene.render.filepath}
result=run()

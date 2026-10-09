"""Regenerate the low-poly Dumplings asset in the open starter scene.
Run in Blender: exec(compile(open(__file__).read(), __file__, 'exec')).
Call build(1), build(2), texture(), and finish() for the recorded stages.
All generated files stay beside this script; HumanRef_1.8m is preserved.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector
OUT = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)
V, F, COLORS = [], [], []
WHITE=(0.94,0.95,0.92,1)
DOUGH=(0.95,0.86,0.68,1)
SEAM=(0.97,0.89,0.73,1)
GOLD=(0.80,0.48,0.18,1)
SAUCE=(0.19,0.065,0.026,1)

def vert(p):
    V.append(tuple(p)); return len(V)-1

def face(ids, color):
    F.append(tuple(ids)); COLORS.append(color)

def ring_rect(x,y,c,z):
    return [vert(p) for p in [(-x+c,-y,z),(x-c,-y,z),(x,-y+c,z),(x,y-c,z),(x-c,y,z),(-x+c,y,z),(-x,y-c,z),(-x,-y+c,z)]]

def bridge(a,b,color):
    for i in range(len(a)):
        j=(i+1)%len(a)
        face((a[i],a[j],b[j],b[i]),color)

def cap(r,color):
    for j in range(1,len(r)-1): face((r[0],r[j],r[j+1]),color)

def tray():
    rings=[ring_rect(*p) for p in [(.102,.082,.013,0),(.120,.101,.019,.024),(.113,.094,.016,.024),(.098,.079,.012,.006)]]
    for a,b in zip(rings,rings[1:]): bridge(a,b,WHITE)
    cap(rings[0][::-1],WHITE); cap(rings[-1],WHITE)

def dumpling(cx,cy,angle,fried,detail):
    # Ten longitudinal stations. Five-sided cross sections pinch into a
    # folded top seam; the two end points taper the crescent silhouette.
    cs,sn=math.cos(angle),math.sin(angle)
    def p(x,y,z): return vert((cx+cs*x-sn*y,cy+sn*x+cs*y,z))
    tips=[p(-.032,-.004,.014),p(.032,-.004,.014)]
    rings=[]
    for i,t in enumerate([-.87,-.63,-.38,-.12,.12,.38,.63,.87]):
        bulge=math.sqrt(1-t*t)
        w=.019*bulge
        bend=.005*(1-t*t)-.004
        pleat=(1 if i%2 else -1)*.004 if detail else 0
        h=.011+.027*bulge
        r=[p(t*.032,bend-w*.57,.007),
           p(t*.032,bend-w,.013+.012*bulge),
           p(t*.032,bend+pleat,h),
           p(t*.032,bend+w*.72,.016+.014*bulge),
           p(t*.032,bend+w*.48,.007)]
        rings.append(r)
    for k in range(5):
        j=(k+1)%5
        col=GOLD if fried and k in (0,4) else SEAM if k==2 else DOUGH
        face((tips[0],rings[0][j],rings[0][k]),col)
        face((tips[1],rings[-1][k],rings[-1][j]),col)
    for a,b in zip(rings,rings[1:]):
        for k in range(5):
            j=(k+1)%5
            col=GOLD if fried and k in (0,4) else SEAM if k==2 else DOUGH
            face((a[k],a[j],b[j],b[k]),col)

def cup():
    # Closed container with recessed dark sauce surface, broad white rim.
    rings=[]
    for rad,z in [(.014,.0065),(.019,.028),(.0165,.028),(.016,.023)]:
        rings.append([vert((rad*math.cos(2*math.pi*i/6),-.068+rad*math.sin(2*math.pi*i/6),z)) for i in range(6)])
    for a,b in zip(rings,rings[1:]): bridge(a,b,WHITE)
    cap(rings[0][::-1],WHITE); cap(rings[-1],SAUCE)

def select(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.hide_set(False); ob.select_set(True); bpy.context.view_layer.objects.active=ob

def preview_materials(body):
    palette=list(dict.fromkeys(COLORS))
    for i,c in enumerate(palette):
        mat=bpy.data.materials.new('Preview_%02d'%i); mat.diffuse_color=c; mat.use_nodes=True
        mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=c
        mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.8
        body.data.materials.append(mat)
    for poly,col in zip(body.data.polygons,COLORS): poly.material_index=palette.index(col)
    # Keep color attributes per face through triangulation.
    attr=body.data.attributes.new('PartColor','FLOAT_COLOR','FACE')
    for dst,col in zip(attr.data,COLORS): dst.color=col
    part=body.data.attributes.new('PartIndex','INT','FACE')
    palette=[WHITE,DOUGH,SEAM,GOLD,SAUCE]
    for dst,col in zip(part.data,COLORS): dst.value=palette.index(col)

def setup_render():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=48
    scene.cycles.use_denoising=True
    scene.render.resolution_x=960; scene.render.resolution_y=960; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    scene.world.use_nodes=True
    scene.world.node_tree.nodes.get('Background').inputs[0].default_value=(.22,.22,.22,1)
    scene.world.node_tree.nodes.get('Background').inputs[1].default_value=.4
    camera_data=bpy.data.cameras.new('PresentationCamera')
    cam=bpy.data.objects.new('PresentationCamera',camera_data); scene.collection.objects.link(cam)
    cam.location=(.27,-.43,.38)
    cam.rotation_euler=(Vector((0,-.003,.018))-cam.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type='ORTHO'; camera_data.ortho_scale=.315; camera_data.lens=50
    scene.camera=cam
    for name,loc,energy,size in [('Key',(.05,-.16,.40),9,.24),('Fill',(-.25,-.02,.22),5,.28),('Rim',(.04,.24,.27),7,.20)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=energy*.065; data.shape='DISK'; data.size=size
        ob=bpy.data.objects.new(name,data); scene.collection.objects.link(ob); ob.location=loc
        ob.rotation_euler=(Vector((0,0,.02))-ob.location).to_track_quat('-Z','Y').to_euler()
    # Frame the asset in the live solid/material viewport, leaving reference untouched.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=.36
                area.spaces.active.region_3d.view_location=(0,0,.02)
                area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
                area.spaces.active.shading.type='MATERIAL'
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1

def render(stage):
    bpy.context.scene.render.filepath=os.path.join(OUT,'renders',stage+'.png')
    bpy.ops.render.render(write_still=True)

def build(stage=2):
    global V,F,COLORS
    V,F,COLORS=[],[],[]
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    # Remove only generated asset/presentation objects on a repeated build.
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('Dumplings_','PresentationCamera','Key','Fill','Rim')): bpy.data.objects.remove(ob,do_unlink=True)
    tray()
    for row,cy in enumerate([-.021,.038]):
        for i,cx in enumerate([-.069,0,.069]):
            angle=math.radians([9,-5,-10][i] if row==0 else [-7,5,10][i])
            dumpling(cx,cy,angle,row==0,stage>=2)
    cup()
    mesh=bpy.data.meshes.new('Dumplings_Geometry'); mesh.from_pydata(V,[],F); mesh.update()
    body=bpy.data.objects.new('Dumplings_Body',mesh); bpy.context.scene.collection.objects.link(body)
    preview_materials(body)
    bm=bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='BEAUTY')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free()
    select(body)
    setup_render()
    render('01-blockout' if stage==1 else '02-detail')
    return {'triangles':len(mesh.polygons),'dimensions':list(body.dimensions)}

def texture():
    body=bpy.data.objects['Dumplings_Body']; select(body)
    me=body.data
    # Pair triangles of the same colour in rectangular UV islands.
    # These analytical islands have no overlaps and generous 256px gutters.
    # Flat colour lets us prioritise coverage rather than texel distortion.
    uv=me.uv_layers.active or me.uv_layers.new(name='UVMap')
    palette=[WHITE,DOUGH,SEAM,GOLD,SAUCE]
    parts=me.attributes['PartIndex'].data
    groups={tuple(c):[] for c in palette}
    for poly in me.polygons:
        c=palette[parts[poly.index].value]
        groups[tuple(c)].append(poly)
    cells=sum((len(g)+1)//2 for g in groups.values())
    cols=17; rows=math.ceil(cells/cols)
    pixels=np.empty((256,256,4),dtype=np.float32); pixels[:]=WHITE
    cell=0
    for color,polys in groups.items():
        for j in range(0,len(polys),2):
            col=cell%cols; row=cell//cols
            x0=.01+col*.98/cols; x1=.01+(col+1)*.98/cols
            y0=.01+row*.98/rows; y1=.01+(row+1)*.98/rows
            # Whole cell is uniform; inset UVs leave a full texel of padding.
            px0=max(0,round(x0*256)); px1=min(256,round(x1*256))
            py0=max(0,round(y0*256)); py1=min(256,round(y1*256))
            pixels[py0:py1,px0:px1]=color
            pad=.0045
            a=(x0+pad,y0+pad); b=(x1-pad,y0+pad)
            c=(x1-pad,y1-pad); d=(x0+pad,y1-pad)
            for poly,coords in zip(polys[j:j+2],[(a,b,c),(a,c,d)]):
                for loop,coord in zip(poly.loop_indices,coords): uv.data[loop].uv=coord
            cell+=1
    img=bpy.data.images.new('T_Dumplings_BaseColor',width=256,height=256,alpha=False)
    img.colorspace_settings.name='sRGB'; img.pixels.foreach_set(pixels.ravel()); img.update()
    img.filepath_raw=os.path.join(OUT,'T_Dumplings_BaseColor.png'); img.file_format='PNG'; img.save()
    mat=bpy.data.materials.new('M_Dumplings'); mat.use_nodes=True
    nodes=mat.node_tree.nodes; bsdf=nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=.48
    tex=nodes.new('ShaderNodeTexImage'); tex.image=img; tex.interpolation='Closest'; tex.label='256px flat-colour atlas'
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    me.materials.clear(); me.materials.append(mat)
    for poly in me.polygons: poly.material_index=0; poly.use_smooth=False
    render('03-texture')
    return {'materials':len(me.materials),'uv_layers':len(me.uv_layers)}

def finish():
    body=bpy.data.objects['Dumplings_Body']
    old=bpy.data.objects.get('Dumplings_Collision')
    if old: bpy.data.objects.remove(old,do_unlink=True)
    bm=bmesh.new()
    # Chamfered prism contains the entire tray/food with 16 hull vertices.
    for z in (0,.040):
        for x,y in [(-.101,-.101),(.101,-.101),(.120,-.082),(.120,.082),(.101,.101),(-.101,.101),(-.120,.082),(-.120,-.082)]: bm.verts.new((x,y,z))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    for v in list(bm.verts):
        if not v.link_faces: bm.verts.remove(v)
    bmesh.ops.triangulate(bm,faces=list(bm.faces)); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('Dumplings_ConvexHull'); bm.to_mesh(me); bm.free()
    collision=bpy.data.objects.new('Dumplings_Collision',me); bpy.context.scene.collection.objects.link(collision)
    collision.hide_render=True; collision.display_type='WIRE'
    collision['construction']='bmesh.ops.convex_hull'
    body['front']='-Y'; body['portion']='Six pleated dumplings; three pan-fried; sauce cup'
    render('04-final')
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True); collision.select_set(True); bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT+'/Dumplings.fbx',use_selection=True,object_types={'MESH'},global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    for img in bpy.data.images:
        if img.name.startswith('T_Dumplings_BaseColor'):
            img.filepath='//T_Dumplings_BaseColor.png'
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Dumplings.blend')
    return {'body_triangles':len(body.data.polygons),'collision_triangles':len(me.polygons)}

if __name__=='__main__':
    build(1)
    build(2)
    texture()
    finish()

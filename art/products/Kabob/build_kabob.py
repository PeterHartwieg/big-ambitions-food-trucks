"""Regenerate using Blender --python, or import this module and call run_all().

Only Kabob objects and its temporary render rig are replaced. HumanRef is untouched.
Front is -Y, left +X; all mesh coordinates are baked in metres about floor origin.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix
from mathutils.geometry import tessellate_polygon

OUT = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)
PALETTE = {
    'paper': (0.91, 0.86, 0.72), 'paper_inner': (0.98, 0.95, 0.86),
    'paper_edge': (0.99, 0.97, 0.91), 'paper_under': (0.77, 0.70, 0.56),
    'wood': (0.72, 0.48, 0.24), 'wood_end': (0.84, 0.64, 0.36),
    'chicken': (0.68, 0.36, 0.15), 'chicken_bevel': (0.83, 0.48, 0.22),
    'chicken_top': (0.92, 0.61, 0.31), 'chicken_bottom': (0.50, 0.25, 0.11),
    'char': (0.22, 0.105, 0.047), 'red': (0.79, 0.12, 0.075),
    'red_edge': (0.94, 0.24, 0.13), 'green': (0.25, 0.49, 0.095),
    'green_edge': (0.44, 0.66, 0.17), 'onion': (0.91, 0.87, 0.72),
    'onion_edge': (0.98, 0.96, 0.85),
}
KEYS = list(PALETTE)
MEAT = [(-0.059, -0.001, 0.028, 0.033, 0.027, -0.10),
        (-0.020, 0.002, 0.029, 0.032, 0.030, 0.12),
        (0.022, -0.002, 0.030, 0.034, 0.028, -0.08),
        (0.064, 0.001, 0.028, 0.032, 0.027, 0.13)]

def linear(rgb):
    return tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb)

class Builder:
    def __init__(self):
        self.verts, self.faces, self.colors = [], [], []
    def vertex(self, xyz):
        self.verts.append(tuple(xyz))
        return len(self.verts)-1
    def face(self, ids, color):
        if len(ids) <= 4:
            self.faces.append(tuple(ids)); self.colors.append(KEYS.index(color))
        else:
            coords = [Vector(self.verts[i]) for i in ids]
            for tri in tessellate_polygon([coords]):
                self.face([ids[v if isinstance(v,int) else coords.index(v)] for v in tri], color)
    def ring(self, points):
        return [self.vertex(v) for v in points]
    def bridge(self, a, b, color):
        for i in range(len(a)):
            j = (i+1) % len(a)
            self.face([a[i],a[j],b[j],b[i]], color)
    def finish(self):
        mesh = bpy.data.meshes.new('Kabob_Geometry')
        mesh.from_pydata(self.verts, [], self.faces); mesh.update()
        for p,c in zip(mesh.polygons,self.colors): p.material_index=c
        bm=bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh); bm.free(); mesh.update()
        ob=bpy.data.objects.new('Kabob_Body',mesh); bpy.context.collection.objects.link(ob)
        for key in KEYS:
            mat=bpy.data.materials.get('Preview_'+key) or bpy.data.materials.new('Preview_'+key)
            mat.diffuse_color=(*linear(PALETTE[key]),1); mat.use_nodes=True
            bs=mat.node_tree.nodes.get('Principled BSDF')
            bs.inputs['Base Color'].default_value=mat.diffuse_color
            bs.inputs['Roughness'].default_value=.84
            mesh.materials.append(mat)
        return ob

def octagon(hx,hy,c):
    return [(-hx+c,-hy),(hx-c,-hy),(hx,-hy+c),(hx,hy-c),
            (hx-c,hy),(-hx+c,hy),(-hx,hy-c),(-hx,-hy+c)]

def tray(b):
    def ring(hx,hy,c,z): return b.ring([(x,y,z) for x,y in octagon(hx,hy,c)])
    lower=ring(.090,.032,.008,0)
    outside=ring(.101,.044,.009,.016)
    inside=ring(.0985,.0415,.0085,.016)
    floor=ring(.088,.030,.007,.0025)
    b.bridge(lower,outside,'paper')
    b.bridge(outside,inside,'paper_edge')
    b.bridge(inside,floor,'paper_inner')
    b.face(list(reversed(lower)),'paper_under'); b.face(floor,'paper_inner')

def box(b,center,size,color,angle=0):
    cx,cy,cz=center; dx,dy,dz=[s/2 for s in size]
    rot=Matrix.Rotation(angle,3,'Z')
    a=b.ring([Vector(center)+rot@Vector((x,y,-dz)) for x,y in [(-dx,-dy),(dx,-dy),(dx,dy),(-dx,dy)]])
    c=b.ring([Vector(center)+rot@Vector((x,y,dz)) for x,y in [(-dx,-dy),(dx,-dy),(dx,dy),(-dx,dy)]])
    b.bridge(a,c,color); b.face(list(reversed(a)),color); b.face(c,color)

def chicken(b,params,detail):
    x,y,dx,dy,h,ang=params
    bottom=.0168; top=bottom+h
    if not detail:
        box(b,(x,y,(bottom+top)/2),(dx,dy,h),'chicken_top',ang); return
    rot=Matrix.Rotation(ang,3,'Z')
    def ring(hx,hy,c,z):
        return b.ring([Vector((x,y,z))+rot@Vector((px,py,0)) for px,py in octagon(hx,hy,c)])
    a=ring(dx/2,dy/2,.004,bottom)
    c=ring(dx/2,dy/2,.004,top-.004)
    d=ring(dx/2-.003,dy/2-.003,.003,top)
    b.bridge(a,c,'chicken'); b.bridge(c,d,'chicken_bevel')
    b.face(list(reversed(a)),'chicken_bottom'); b.face(d,'chicken_top')
    # Three dark, closed rectangular char bands lie on the flat top, inside bevel.
    for px in [-.0065,0,.0065]:
        offset=rot@Vector((px,0,0))
        box(b,(x+offset.x,y+offset.y,top+.00025),(.0019,.018,.0005),'char',ang-.18)

def vegetable(b,x,width,kind,angle,detail):
    center=Vector((x,0,.033))
    # Curved angular cross sections read as pepper panels / thick onion petals.
    profile=[(-.017,-.010),(-.019,.004),(-.013,.014),(.001,.017),
             (.015,.008),(.017,-.005),(.009,-.012),(-.005,-.014)]
    if kind=='onion':
        profile=[(-.017,-.008),(-.019,.004),(-.011,.015),(.003,.016),
                 (.014,.010),(.014,-.002),(.008,-.010),(-.006,-.012)]
    if not detail:
        box(b,center,(width,.031,.028),kind,0); return
    rot=Matrix.Rotation(angle,3,'X')
    a=b.ring([center+rot@Vector((-width/2,yy,zz)) for yy,zz in profile])
    c=b.ring([center+rot@Vector((width/2,yy,zz)) for yy,zz in profile])
    b.bridge(a,c,kind+'_edge')
    b.face(list(reversed(a)),kind); b.face(c,kind)

def skewer(b):
    def ring(x):
        return b.ring([(x,.0015*math.cos(i*math.tau/6),.033+.0015*math.sin(i*math.tau/6)) for i in range(6)])
    a,c=ring(-.118),ring(.110)
    b.bridge(a,c,'wood'); b.face(list(reversed(a)),'wood_end')
    tip=b.vertex((.118,0,.033))
    for i in range(6): b.face([c[i],c[(i+1)%6],tip],'wood_end')

def build(detail=False):
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for name in ['Kabob_Body','Kabob_Collision']:
        old=bpy.data.objects.get(name)
        if old: bpy.data.objects.remove(old,do_unlink=True)
    b=Builder(); tray(b); skewer(b)
    for params in MEAT: chicken(b,params,detail)
    for x,w,k,a in [(-.038,.008,'red',-.22),(-.031,.006,'onion',.08),
                    (.001,.008,'green',.23),(.009,.006,'onion',-.12),
                    (.043,.008,'red',.18),(.051,.007,'green',-.24)]:
        vegetable(b,x,w,k,a,detail)
    ob=b.finish()
    ob['front']='-Y'; ob['left']='+X'; ob['portion_length_m']=.236
    ob['description']='Grilled chicken, peppers and onion on wooden skewer in plain paper tray'
    bpy.context.view_layer.update()
    setup_render()
    return {'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),'size_m':list(ob.dimensions)}

def setup_render():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=48
    scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    scene.world=scene.world or bpy.data.worlds.new('Kabob_Studio')
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.105,.135,.17,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
    cam=bpy.data.objects.get('Kabob_RenderCamera')
    if not cam:
        cam=bpy.data.objects.new('Kabob_RenderCamera',bpy.data.cameras.new('Kabob_RenderCamera'))
        scene.collection.objects.link(cam)
    cam.location=(.095,-.32,.255)
    cam.rotation_euler=(Vector((0,0,.019))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'; cam.data.ortho_scale=.285; cam.data.lens=50
    scene.camera=cam
    for name,loc,energy,size in [('Kabob_Key',(-.12,-.19,.32),.7,.24),('Kabob_Fill',(.15,.04,.20),.35,.20)]:
        light=bpy.data.objects.get(name)
        if not light:
            data=bpy.data.lights.new(name,'AREA'); light=bpy.data.objects.new(name,data); scene.collection.objects.link(light)
        light.location=loc; light.rotation_euler=(-light.location).to_track_quat('-Z','Y').to_euler()
        light.data.energy=energy; light.data.shape='DISK'; light.data.size=size
    # Mirror customer angle into the interactive viewport without touching HumanRef.
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active; space.region_3d.view_location=(0,0,.022)
            space.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
            space.region_3d.view_distance=.35; space.clip_start=.001
            space.overlay.show_overlays=False; space.shading.color_type='MATERIAL'

def texture_uv():
    ob=bpy.data.objects['Kabob_Body']; me=ob.data
    color_ids=[p.material_index for p in me.polygons]
    grid=math.ceil(math.sqrt(len(me.polygons)))
    uv=me.uv_layers.new(name='UVMap')
    # Flat-color facets need no stretching-sensitive texel density. Independent
    # face charts reserve unique atlas area; no stacked islands or shared UVs.
    image=bpy.data.images.new('T_Kabob_BaseColor',width=256,height=256,alpha=False)
    image.colorspace_settings.name='sRGB'
    pixels=[]
    for yy in range(256):
        row=min(grid-1,int((yy+.5)/256*grid))
        for xx in range(256):
            col=min(grid-1,int((xx+.5)/256*grid)); idx=row*grid+col
            color=PALETTE[KEYS[color_ids[idx]]] if idx<len(color_ids) else PALETTE['paper_under']
            pixels.extend((*color,1))
    image.pixels.foreach_set(pixels)
    image.filepath_raw=os.path.join(OUT,'T_Kabob_BaseColor.png'); image.file_format='PNG'; image.save()
    # One-pixel gutter on both sides, while retaining over 40% UV coverage.
    margin=1/256
    for i,p in enumerate(me.polygons):
        col,row=i%grid,i//grid
        x0,y0=col/grid+margin,row/grid+margin
        x1,y1=(col+1)/grid-margin,(row+1)/grid-margin
        corners=[(x0,y0),(x1,y0),(x1,y1),(x0,y1)]
        for li,co in zip(p.loop_indices,corners): uv.data[li].uv=co
    mat=bpy.data.materials.get('M_Kabob') or bpy.data.materials.new('M_Kabob')
    mat.use_nodes=True; nodes=mat.node_tree.nodes; nodes.clear()
    bs=nodes.new('ShaderNodeBsdfPrincipled'); bs.inputs['Roughness'].default_value=.84
    tex=nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Closest'; tex.extension='EXTEND'
    output=nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    mat.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface'])
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons: p.material_index=0
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(me); bm.free(); me.update()
    return {'atlas_grid':grid,'face_charts':len(color_ids),'triangles':len(me.polygons),'texture':[256,256]}

def collision():
    # Enclosing box, calculated from body extrema, constructed via convex_hull.
    body=bpy.data.objects['Kabob_Body']
    lo=Vector(tuple(min(v.co[i] for v in body.data.vertices) for i in range(3)))
    hi=Vector(tuple(max(v.co[i] for v in body.data.vertices) for i in range(3)))
    bm=bmesh.new()
    points=[bm.verts.new((x,y,z)) for x in [lo.x,hi.x] for y in [lo.y,hi.y] for z in [lo.z,hi.z]]
    bmesh.ops.convex_hull(bm,input=points,use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new('Kabob_ConvexHull'); bm.to_mesh(mesh); bm.free()
    ob=bpy.data.objects.new('Kabob_Collision',mesh); bpy.context.collection.objects.link(ob)
    ob.hide_render=True; ob.display_type='WIRE'; ob.hide_set(True)
    ob['construction']='bmesh.ops.convex_hull'; ob['convex']=True
    return len(mesh.polygons)

def render_stage(name):
    bpy.context.scene.render.filepath=os.path.join(OUT,'renders',name)
    bpy.ops.render.render(write_still=True)

def save_export():
    # The saved asset contains only its two meshes and the unchanged scale reference.
    for name in ['Kabob_RenderCamera','Kabob_Key','Kabob_Fill']:
        ob=bpy.data.objects.get(name)
        if ob: bpy.data.objects.remove(ob,do_unlink=True)
    for mat in list(bpy.data.materials):
        if mat.name.startswith('Preview_') and mat.users==0: bpy.data.materials.remove(mat)
    bpy.ops.object.select_all(action='DESELECT')
    body=bpy.data.objects['Kabob_Body']; hull=bpy.data.objects['Kabob_Collision']
    hull.hide_set(False); body.select_set(True); hull.select_set(True)
    bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT + '/Kabob.fbx', use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
    hull.select_set(False); hull.hide_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Kabob.blend')
    bpy.data.materials['M_Kabob'].node_tree.nodes.get('Image Texture').image.filepath='//T_Kabob_BaseColor.png'
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Kabob.blend')
    return {'body_triangles':len(body.data.polygons),'collision_triangles':len(hull.data.polygons)}

def run_all():
    build(False); render_stage('01-blockout.png')
    build(True); render_stage('02-detail.png')
    texture_uv(); render_stage('03-uv-texture.png')
    collision(); render_stage('04-final.png')
    return save_export()

if __name__=='__main__':
    result=run_all()

"""Run in the existing metric start scene. Stages allow visual review before proceeding.
Use build_blockout(); build_detail(); build_atlas(); finish() in that order.
All generated files stay beside this script; HumanRef_1.8m is untouched.
"""
import bpy, bmesh, math, os
from mathutils import Vector
import numpy as np

OUT = os.path.dirname(os.path.abspath(__file__))
PALETTE = {
    'paint': (0.86, 0.28, 0.42, 1),
    'trim': (0.97, 0.86, 0.75, 1),
    'steel': (0.64, 0.72, 0.77, 1),
    'inner': (0.43, 0.52, 0.59, 1),
    'dark': (0.045, 0.06, 0.08, 1),
    'pink': (1.0, 0.42, 0.69, 1),
    'blue': (0.29, 0.73, 0.98, 1),
    'paper': (1.0, 0.96, 0.84, 1),
    'glass': (0.78, 0.94, 1.0, 0.075),
    'red': (0.87, 0.075, 0.075, 1),
}

def active(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def mat(role):
    name = 'Build_' + role
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = PALETTE[role]
    p = m.node_tree.nodes.get('Principled BSDF')
    c=PALETTE[role]
    p.inputs['Base Color'].default_value = tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in c[:3])+(c[3],)
    p.inputs['Roughness'].default_value = .36 if role in ('steel','inner') else .64
    p.inputs['Metallic'].default_value = .65 if role in ('steel','inner') else 0
    p.inputs['Alpha'].default_value = PALETTE[role][3]
    if role == 'glass':
        m.surface_render_method = 'DITHERED'
        p.inputs['Roughness'].default_value = .18
    return m

def finish_part(ob, role):
    active(ob)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.data.materials.clear()
    ob.data.materials.append(mat(role))
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts)>4])
    bm.to_mesh(ob.data); bm.free()
    return ob

def box(name, centre, dims, role, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=centre)
    ob=bpy.context.object; ob.name=name; ob.dimensions=dims
    active(ob); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        m=ob.modifiers.new('Small corner chamfers','BEVEL'); m.width=bevel; m.segments=1
        bpy.ops.object.modifier_apply(modifier=m.name)
    return finish_part(ob,role)

def lathe(name, centre, profile, role, n=24):
    # Closed profile revolves about Z. Each profile point is (radius,z).
    vs=[]; fs=[]
    for r,z in profile:
        for i in range(n):
            a=2*math.pi*i/n
            vs.append((centre[0]+r*math.cos(a),centre[1]+r*math.sin(a),z))
    for j in range(len(profile)):
        k=(j+1)%len(profile)
        for i in range(n):
            q=(i+1)%n
            fs.append((j*n+i,j*n+q,k*n+q,k*n+i))
    me=bpy.data.meshes.new(name); me.from_pydata(vs,[],fs); me.update()
    ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob)
    return finish_part(ob,role)

def cylinder(name, centre, radius, depth, role, n=12, axis='Z'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=radius, depth=depth, location=centre)
    ob=bpy.context.object; ob.name=name
    if axis=='Y': ob.rotation_euler[0]=math.pi/2
    return finish_part(ob,role)

def candy(name,x,y,bottom,top,role,phase):
    # Soft, slightly irregular spun-sugar silhouette; no surface noise or extra lobes.
    n=10; vs=[(x,y,bottom)]; fs=[]
    for j,(t,r) in enumerate(((.17,.022),(.40,.035),(.67,.036),(.86,.027))):
        for i in range(n):
            a=2*math.pi*i/n+phase
            wobble=1+.07*math.sin(3*a+j*.8)
            vs.append((x+r*wobble*math.cos(a),y+r*wobble*math.sin(a),bottom+(top-bottom)*t))
    vs.append((x-.002,y+.002,top)); cap=len(vs)-1
    for i in range(n):
        q=(i+1)%n
        fs.append((0,1+q,1+i))
        for j in range(3):
            a=1+j*n; b=a+n
            fs.append((a+i,a+q,b+q,b+i))
        fs.append((1+3*n+i,1+3*n+q,cap))
    me=bpy.data.meshes.new(name); me.from_pydata(vs,[],fs); me.update()
    ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob)
    return finish_part(ob,role)

def join_body():
    parts=[o for o in bpy.context.scene.objects if o.type=='MESH' and not o.name.startswith('HumanRef') and o.name!='CottonCandyMachine_Collision']
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts: ob.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join()
    ob=bpy.context.object; ob.name='CottonCandyMachine_Body'
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    ob.data.update()
    return ob

def studio():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.render.resolution_x=900; scene.render.resolution_y=900; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.world.use_nodes=True
    scene.world.node_tree.nodes.get('Background').inputs[0].default_value=(.16,.20,.27,1)
    scene.world.node_tree.nodes.get('Background').inputs[1].default_value=.45
    scene.view_settings.view_transform='AgX'
    ca=bpy.data.cameras.new('ReviewCamera'); ob=bpy.data.objects.new('ReviewCamera',ca); scene.collection.objects.link(ob)
    ob.location=(.65,-1.25,.81); ob.rotation_euler=(Vector((.015,0,.245))-ob.location).to_track_quat('-Z','Y').to_euler()
    ca.type='ORTHO'; ca.ortho_scale=.61; scene.camera=ob
    for name,loc,power,size in [('Key',(.2,-.7,1.1),70,.8),('Fill',(-.6,-.2,.65),40,.65),('Rim',(.25,.6,.9),85,.6)]:
        ld=bpy.data.lights.new(name,'AREA'); ld.energy=power; ld.shape='DISK'; ld.size=size
        lo=bpy.data.objects.new(name,ld); scene.collection.objects.link(lo); lo.location=loc
        lo.rotation_euler=(Vector((0,0,.22))-lo.location).to_track_quat('-Z','Y').to_euler()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_rotation=ob.rotation_euler.to_quaternion()
                area.spaces.active.region_3d.view_distance=.85
                area.spaces.active.region_3d.view_location=(0,0,.24)
                area.spaces.active.shading.type='MATERIAL'

def checkpoint(name):
    os.makedirs(os.path.join(OUT,'renders'),exist_ok=True)
    bpy.context.scene.render.filepath=os.path.join(OUT,'renders',name+'.png')
    bpy.ops.render.render(write_still=True)

def build_blockout():
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    # On regeneration, remove only the asset and its review rig.
    for ob in list(bpy.data.objects):
        if not ob.name.startswith('HumanRef'): bpy.data.objects.remove(ob,do_unlink=True)
    box('Flat rubber plinth',(0,0,.008),(.250,.250,.016),'dark')
    box('Square painted cabinet',(0,0,.092),(.266,.266,.160),'paint',.009)
    box('Cream top cap',(0,0,.174),(.270,.270,.018),'trim',.004)
    lathe('Spun steel bowl',(-.025,0),[(.022,.18),(.146,.226),(.151,.258),(.145,.258),(.140,.230),(.022,.189)],'steel',20)
    cylinder('Bowl floor',(-.025,0,.193),.040,.008,'inner',16)
    cylinder('Spinner drum',(-.025,0,.218),.029,.042,'dark',12)
    cylinder('Spinner cap',(-.025,0,.240),.032,.010,'steel',12)
    box('Side candy rack',(.151,.016,.240),(.038,.214,.023),'trim')
    box('Rack support',(.139,.016,.199),(.018,.036,.082),'steel')
    for i,(y,b,t,role) in enumerate([(-.073,.343,.451,'pink'),(.009,.371,.495,'blue'),(.091,.339,.458,'pink')]):
        cylinder('Paper stick '+str(i),(.153,y,(.25+b+.035)/2),.0032,b+.035-.25,'paper',8)
        candy('Cotton candy '+str(i),.153,y,b,t,role,i*.24)
    join_body(); studio(); checkpoint('01-blockout')

def build_detail():
    lathe('Clear curved bubble guard',(-.025,0),[(.151,.258),(.160,.297),(.137,.341),(.134,.340),(.157,.297),(.148,.260)],'glass',16)
    lathe('Steel rolled lip',(-.025,0),[(.144,.254),(.153,.254),(.153,.261),(.144,.261)],'steel',16)
    lathe('Guard opening bead',(-.025,0),[(.133,.338),(.138,.338),(.138,.343),(.133,.343)],'glass',12)
    box('Front control inset',(0,-.132,.093),(.171,.010,.064),'dark')
    cylinder('Cream control dial',(-.040,-.143,.095),.018,.012,'trim',12,'Y')
    box('Dial pointer',(-.040,-.150,.102),(.003,.002,.013),'dark')
    box('Switch surround',(.034,-.143,.094),(.025,.011,.032),'trim')
    box('Red rocker switch',(.034,-.150,.095),(.017,.006,.022),'red')
    for i,y in enumerate((-.073,.009,.091)):
        cylinder('Rack stick socket '+str(i),(.153,y,.255),.008,.009,'dark',8)
    # Three broad slots read as vents without expensive cutouts.
    for z in (.058,.072,.086): box('Side cooling vent',(-.133,.033,z),(.002,.100,.004),'dark')
    ob=join_body(); checkpoint('02-detail')
    return sum(len(f.vertices)-2 for f in ob.data.polygons)

def build_atlas():
    ob=bpy.data.objects['CottonCandyMachine_Body']; me=ob.data
    colors=[tuple(me.materials[p.material_index].diffuse_color) for p in me.polygons]
    count=len(me.polygons); grid=math.ceil(math.sqrt(count)); size=512
    # A unique tile for every polygon, padded >1 texel. Quads use the entire tile.
    # Triangle islands consume half a tile. Disconnected islands cannot overlap.
    for old in list(me.uv_layers): me.uv_layers.remove(old)
    uv=me.uv_layers.new(name='UVMap')
    pixels=np.zeros((size,size,4),dtype=np.float32); pixels[:]=(.045,.060,.080,1)
    for i,p in enumerate(me.polygons):
        col=i%grid; row=i//grid
        x0=round(col*size/grid); x1=round((col+1)*size/grid)
        y0=round(row*size/grid); y1=round((row+1)*size/grid)
        pixels[y0:y1,x0:x1]=colors[i]
        u0=(x0+1.2)/size; u1=(x1-1.2)/size; v0=(y0+1.2)/size; v1=(y1-1.2)/size
        coords=[(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
        for li,co in zip(p.loop_indices,coords): uv.data[li].uv=co
    image=bpy.data.images.new('T_CottonCandyMachine_BaseColor',width=size,height=size,alpha=True)
    image.pixels.foreach_set(pixels.ravel()); image.filepath_raw=os.path.join(OUT,'T_CottonCandyMachine_BaseColor.png'); image.file_format='PNG'; image.save()
    image=bpy.data.images.load(os.path.join(OUT,'T_CottonCandyMachine_BaseColor.png'),check_existing=False)
    # Keep the absolute path until the first scene save relocates the blend.
    image.filepath=os.path.join(OUT,'T_CottonCandyMachine_BaseColor.png')
    m=bpy.data.materials.new('M_CottonCandyMachine'); m.use_nodes=True; m.surface_render_method='DITHERED'
    nt=m.node_tree; p=nt.nodes.get('Principled BSDF'); tex=nt.nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Closest'
    nt.links.new(tex.outputs['Color'],p.inputs['Base Color']); nt.links.new(tex.outputs['Alpha'],p.inputs['Alpha'])
    p.inputs['Roughness'].default_value=.43
    # Steel pixels have almost equal R/G; painted candy and cabinet do not.
    sep=nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(tex.outputs['Color'],sep.inputs['Color'])
    diff=nt.nodes.new('ShaderNodeMath'); diff.operation='SUBTRACT'; nt.links.new(sep.outputs['Green'],diff.inputs[0]); nt.links.new(sep.outputs['Red'],diff.inputs[1])
    absolute=nt.nodes.new('ShaderNodeMath'); absolute.operation='ABSOLUTE'; nt.links.new(diff.outputs[0],absolute.inputs[0])
    close=nt.nodes.new('ShaderNodeMath'); close.operation='LESS_THAN'; close.inputs[1].default_value=.13; nt.links.new(absolute.outputs[0],close.inputs[0])
    strength=nt.nodes.new('ShaderNodeMath'); strength.operation='MULTIPLY'; strength.inputs[1].default_value=.55; nt.links.new(close.outputs[0],strength.inputs[0]); nt.links.new(strength.outputs[0],p.inputs['Metallic'])
    me.materials.clear(); me.materials.append(m)
    for f in me.polygons: f.material_index=0
    checkpoint('03-texture')
    return {'faces':count,'grid':grid,'triangles':sum(len(f.vertices)-2 for f in me.polygons)}

def finish():
    body=bpy.data.objects['CottonCandyMachine_Body']
    old=bpy.data.objects.get('CottonCandyMachine_Collision')
    if old: bpy.data.objects.remove(old,do_unlink=True)
    # A small conservative hull encloses bowl, guard, rack and all three candies.
    # Hull vertices come from tiered envelope boxes; the result is strictly convex.
    points=[]
    for z,x0,x1,y in [(0,-.135,.173,.160),(.26,-.189,.194,.163),(.345,-.189,.194,.163),(.497,.110,.194,.136)]:
        points.extend((x,sy*y,z) for x in (x0,x1) for sy in (-1,1))
    bm=bmesh.new()
    for p in points: bm.verts.new(p)
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.delete(bm,geom=hull.get('geom_unused',[])+hull.get('geom_interior',[]),context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('CottonCandyMachine_Collision'); bm.to_mesh(me); bm.free()
    me.update()
    assert max((v.co-p.center).dot(p.normal) for p in me.polygons for v in body.data.vertices)<1e-5, 'Hull must enclose the whole body'
    col=bpy.data.objects.new('CottonCandyMachine_Collision',me); bpy.context.collection.objects.link(col)
    col.hide_render=True; col.display_type='WIRE'; col.hide_set(True)
    checkpoint('04-final')
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); col.hide_set(False); col.select_set(True); bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT + '/CottonCandyMachine.fbx', use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
    col.hide_set(True); active(body)
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/CottonCandyMachine.blend')
    for n in body.data.materials[0].node_tree.nodes:
        if n.type=='TEX_IMAGE': n.image.filepath='//T_CottonCandyMachine_BaseColor.png'
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/CottonCandyMachine.blend')
    return {'body_triangles':sum(len(p.vertices)-2 for p in body.data.polygons),'collision_triangles':len(me.polygons)}

if __name__=='__main__':
    build_blockout(); build_detail(); build_atlas(); finish()

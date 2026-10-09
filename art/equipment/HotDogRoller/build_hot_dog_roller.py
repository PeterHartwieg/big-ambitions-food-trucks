"""Build in the running supplied scene; preserve HumanRef_1.8m.

Run with Blender Python. STOP_AFTER=1, 2, 3 or 4 selects a review stage.
All generated files stay next to this script. Front is Blender -Y.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
STAGE = globals().get('STOP_AFTER', 4)
os.makedirs(OUT + '/renders', exist_ok=True)
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
for ob in list(bpy.data.objects):
    if ob.name.startswith(('HotDogRoller_', '_HDR_')):
        bpy.data.objects.remove(ob, do_unlink=True)

PALETTE = {
    'steel': (.59, .65, .68, 1),
    'edge': (.78, .82, .83, 1),
    'roller': (.70, .75, .77, 1),
    'shadow': (.16, .20, .22, 1),
    'black': (.026, .032, .039, 1),
    'dog': (.34, .070, .030, 1),
    'dog_warm': (.41, .105, .045, 1),
    'red': (.85, .045, .025, 1),
}
temp = {}
for key, color in PALETTE.items():
    mat = bpy.data.materials.get('_HDR_' + key) or bpy.data.materials.new('_HDR_' + key)
    mat.diffuse_color = color
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = color
    metal = key in ('steel', 'edge', 'roller')
    bsdf.inputs['Metallic'].default_value = .75 if metal else 0
    bsdf.inputs['Roughness'].default_value = .30 if metal else .45
    temp[key] = mat
parts = []

def part(label, vs, fs, color, bevel=0, smooth_axis=None):
    me = bpy.data.meshes.new('_HDR_' + label)
    me.from_pydata(vs, [], fs)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES')
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    if smooth_axis is not None:
        for p in me.polygons:
            p.use_smooth = abs(p.normal[smooth_axis]) < .9
    ob = bpy.data.objects.new('_HDR_' + label, me)
    bpy.context.collection.objects.link(ob)
    me.materials.append(temp[color])
    parts.append(ob)
    return ob

FACES = [(0,3,2,1), (4,5,6,7), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7)]
def box(label, c, dims, color, bevel=0):
    vs = [(c[0]+x*dims[0]/2, c[1]+y*dims[1]/2, c[2]+z*dims[2]/2)
          for x,y,z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                        (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    return part(label, vs, FACES, color, bevel)

def prism(label, x0, x1, y0, y1, bottom, front_top, rear_top, color):
    vs = [(x0,y0,bottom),(x1,y0,bottom),(x1,y1,bottom),(x0,y1,bottom),
          (x0,y0,front_top),(x1,y0,front_top),(x1,y1,rear_top),(x0,y1,rear_top)]
    return part(label, vs, FACES, color)

def cylinder(label, c, r, depth, color, axis='X', n=8):
    vs = []
    for d in (-depth/2, depth/2):
        for i in range(n):
            a = 2*math.pi*i/n
            p = (d,r*math.cos(a),r*math.sin(a)) if axis=='X' else (r*math.cos(a),d,r*math.sin(a))
            vs.append(tuple(Vector(c)+Vector(p)))
    fs = [tuple(reversed(range(n))), tuple(range(n,2*n))]
    fs += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return part(label, vs, fs, color, smooth_axis=0 if axis=='X' else 1)

def hotdog(c, color):
    # Six-sided softened capsules. End rings give the rounded sausage silhouette.
    n = 6
    vs = []
    for x,r in [(-.050,.0055),(-.044,.0125),(.044,.0125),(.050,.0055)]:
        for i in range(n):
            a = 2*math.pi*i/n + math.pi/6
            vs.append((c[0]+x,c[1]+r*math.cos(a),c[2]+r*math.sin(a)))
    fs = [tuple(reversed(range(n))), tuple(range(3*n,4*n))]
    for ring in range(3):
        fs += [(ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i) for i in range(n)]
    return part('hotdog',vs,fs,color,smooth_axis=0)

# Structural underside covers the placement point, flush with the counter.
# Four short feet at the outer corners remain visible beneath the front fascia.
box('underside',(0,.008,.016),(.298,.238,.032),'shadow')
for x in (-.171,.171):
    for y in (-.125,.137):
        box('short_foot',(x,y,.016),(.030,.032,.032),'black')
box('chassis',(0,.004,.083),(.386,.308,.102),'steel',bevel=.0025)
# Open stainless end housings, sloping upward towards the rear. No glass cover.
for x0,x1 in [(-.195,-.174),(.174,.195)]:
    prism('side_housing',x0,x1,-.150,.166,.032,.160,.218,'steel')
prism('rear_lip',-.174,.174,.154,.168,.125,.209,.212,'edge')
box('front_rim',(0,-.144,.141),(.348,.017,.016),'edge')
prism('dark_deck',-.174,.174,-.132,.153,.123,.138,.177,'shadow')
# Seven full-width rollers; the deck rises 3 cm from front to back.
for i in range(7):
    y = -.126 + i*.043
    z = .151 + .115*(y+.126)
    cylinder('steel_roller',(0,y,z),.0105,.351,'roller')

if STAGE >= 2:
    # Three columns across six rows, recognizable from the street without a sign.
    for row in range(6):
        y = -.1045 + row*.043
        z = .151 + .115*(y+.126) + .0175
        for col,x in enumerate((-.114,0,.114)):
            hotdog((x,y,z),'dog_warm' if (row+col)%4==0 else 'dog')
    # Blank black inset on the customer-facing -Y side. Controls at customer right.
    box('blank_front_inset',(0,-.1515,.091),(.342,.004,.071),'black')
    for x in (-.128,-.067):
        cylinder('knob_mount',(x,-.155,.084),.023,.004,'edge',axis='Y')
        cylinder('control_knob',(x,-.165,.084),.019,.018,'black',axis='Y')
        box('knob_pointer',(x,-.175,.090),(.003,.002,.014),'edge')
        box('pilot_light',(x,-.155,.116),(.008,.005,.008),'red')
    # A shallow drip drawer is slightly pulled forward, still inside the footprint.
    box('drawer_shadow',(0,-.152,.043),(.330,.007,.012),'black')
    box('tray_floor',(0,-.155,.036),(.324,.041,.005),'steel')
    box('tray_front',(0,-.174,.043),(.332,.007,.017),'edge')
    for x in (-.163,.163):
        box('tray_side',(x,-.155,.043),(.006,.041,.017),'edge')
    box('drawer_grip',(0,-.178,.044),(.100,.004,.009),'shadow')

bpy.ops.object.select_all(action='DESELECT')
for ob in parts:
    ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
body = bpy.context.object
body.name = 'HotDogRoller_Body'
body.data.name = 'HotDogRoller_Body_Mesh'
body.location = (0,0,0)
body.rotation_euler = (0,0,0)
body.scale = (1,1,1)

if STAGE >= 3:
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.151917,island_margin=.003,
        area_weight=0,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    me = body.data
    me.uv_layers.active.name = 'UVMap'
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    res = 512
    pixels = np.zeros((res,res,4),dtype=np.float32)
    pixels[:] = (.18,.21,.23,1)
    occupied = np.zeros((res,res),dtype=bool)
    for tri in me.loop_triangles:
        a,b,c = np.array([tuple(uv[l].uv) for l in tri.loops])*res
        x0 = max(0,int(math.floor(min(a[0],b[0],c[0]))))
        x1 = min(res-1,int(math.ceil(max(a[0],b[0],c[0]))))
        y0 = max(0,int(math.floor(min(a[1],b[1],c[1]))))
        y1 = min(res-1,int(math.ceil(max(a[1],b[1],c[1]))))
        d = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
        if abs(d)<1e-12:
            continue
        xs,ys = np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
        u = ((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/d
        v = ((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/d
        mask = (u>=-1e-6)&(v>=-1e-6)&(1-u-v>=-1e-6)
        color = tuple(me.materials[me.polygons[tri.polygon_index].material_index].diffuse_color)
        pixels[y0:y1+1,x0:x1+1][mask] = color
        occupied[y0:y1+1,x0:x1+1] |= mask
    # Two pixel colour bleed for filtering; unique face UVs remain non-overlapping.
    for _ in range(2):
        old = occupied.copy()
        for dy,dx in [(-1,0),(1,0),(0,-1),(0,1)]:
            shifted = np.roll(old,(dy,dx),(0,1))
            mask = shifted & ~occupied
            pixels[mask] = np.roll(pixels,(dy,dx),(0,1))[mask]
            occupied[mask] = True
    old_img = bpy.data.images.get('T_HotDogRoller_BaseColor.png')
    if old_img:
        bpy.data.images.remove(old_img)
    img = bpy.data.images.new('T_HotDogRoller_BaseColor.png',width=res,height=res,alpha=False)
    img.colorspace_settings.name = 'sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw = OUT + '/T_HotDogRoller_BaseColor.png'
    img.file_format = 'PNG'
    img.save()
    mat = bpy.data.materials.get('M_HotDogRoller') or bpy.data.materials.new('M_HotDogRoller')
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    bsdf = nodes.new('ShaderNodeBsdfPrincipled')
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = 'Linear'
    links = mat.node_tree.links
    links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    # This preview shader distinguishes steel from food using only the colour map.
    # No extra images: Unity receives the single neutral base-colour texture.
    sep = nodes.new('ShaderNodeSeparateColor')
    links.new(tex.outputs['Color'],sep.inputs[0])
    diff = nodes.new('ShaderNodeMath'); diff.operation = 'SUBTRACT'
    links.new(sep.outputs['Red'],diff.inputs[0]); links.new(sep.outputs['Blue'],diff.inputs[1])
    neutral = nodes.new('ShaderNodeMath'); neutral.operation = 'LESS_THAN'; neutral.inputs[1].default_value = .08
    links.new(diff.outputs[0],neutral.inputs[0])
    bright = nodes.new('ShaderNodeMath'); bright.operation = 'GREATER_THAN'; bright.inputs[1].default_value = .23
    links.new(sep.outputs['Blue'],bright.inputs[0])
    metal = nodes.new('ShaderNodeMath'); metal.operation = 'MULTIPLY'
    links.new(neutral.outputs[0],metal.inputs[0]); links.new(bright.outputs[0],metal.inputs[1])
    amount = nodes.new('ShaderNodeMath'); amount.operation = 'MULTIPLY'; amount.inputs[1].default_value = .75
    links.new(metal.outputs[0],amount.inputs[0]); links.new(amount.outputs[0],bsdf.inputs['Metallic'])
    bsdf.inputs['Roughness'].default_value = .33
    links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0

if STAGE >= 4:
    # Hull from actual body vertices, with interior geometry removed by bmesh.
    bm = bmesh.new()
    for v in body.data.vertices:
        bm.verts.new(v.co)
    hull = bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    for key in ('geom_interior','geom_unused'):
        vs = [g for g in hull.get(key,[]) if isinstance(g,bmesh.types.BMVert) and g.is_valid]
        if vs:
            bmesh.ops.delete(bm,geom=vs,context='VERTS')
    bmesh.ops.dissolve_limit(bm,angle_limit=.0001,verts=list(bm.verts),edges=list(bm.edges))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me = bpy.data.meshes.new('HotDogRoller_Collision_Mesh')
    bm.to_mesh(me); bm.free()
    collision = bpy.data.objects.new('HotDogRoller_Collision',me)
    bpy.context.collection.objects.link(collision)
    collision.hide_render = True
    collision.display_type = 'WIRE'

# Review lighting and camera from -Y, slightly above, looking towards the front.
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.resolution_x = 1000
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.world.use_nodes = True
scene.world.node_tree.nodes.get('Background').inputs[0].default_value = (.13,.17,.22,1)
scene.world.node_tree.nodes.get('Background').inputs[1].default_value = .6
scene.view_settings.view_transform = 'AgX'
cam_data = bpy.data.cameras.new('_HDR_Camera')
cam = bpy.data.objects.new('_HDR_Camera',cam_data)
scene.collection.objects.link(cam)
cam.location = (.48,-.86,.67)
cam.rotation_euler = (Vector((0,0,.105))-cam.location).to_track_quat('-Z','Y').to_euler()
cam_data.type = 'ORTHO'; cam_data.ortho_scale = .57
scene.camera = cam
for name,loc,power,size in [('Key',(.1,-.4,.8),9,.55),('Fill',(-.5,-.2,.4),5,.45),('Rim',(.2,.45,.7),12,.45)]:
    data = bpy.data.lights.new('_HDR_'+name,'AREA')
    data.energy = power; data.shape = 'DISK'; data.size = size
    ob = bpy.data.objects.new('_HDR_'+name,data)
    scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (Vector((0,0,.10))-ob.location).to_track_quat('-Z','Y').to_euler()

if STAGE >= 4:
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True); collision.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(filepath=OUT+'/HotDogRoller.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.select_set(False); collision.hide_set(True)
    img.filepath = '//T_HotDogRoller_BaseColor.png'

for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space = area.spaces.active
            space.region_3d.view_perspective = 'ORTHO'
            space.region_3d.view_rotation = cam.rotation_euler.to_quaternion()
            space.region_3d.view_location = (0,0,.105)
            space.region_3d.view_distance = .75
            space.clip_start = .01
            space.shading.type = 'MATERIAL' if STAGE>=3 else 'SOLID'
            space.shading.color_type = 'MATERIAL'
            space.overlay.show_overlays = False
names = {1:'01-blockout',2:'02-detail',3:'03-uv-texture',4:'04-final'}
scene.render.filepath = OUT+'/renders/'+names[STAGE]+'.png'
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/HotDogRoller.blend')
bpy.ops.render.render(write_still=True)
result = {'stage':STAGE,'body_triangles':len(body.data.polygons),
          'dimensions':list(body.dimensions),
          'collision_triangles':len(collision.data.polygons) if STAGE>=4 else None}

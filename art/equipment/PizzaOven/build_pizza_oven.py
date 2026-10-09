"""Compact deck oven. Run in the supplied metric start scene.
Staged functions allow inspecting each render before continuing.
All generated files are written beside this script.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
PARTS = []
MATS = []
PALETTE = [
    ('Steel', (.56, .61, .65, 1)),
    ('EdgeSteel', (.73, .77, .79, 1)),
    ('DarkSteel', (.32, .37, .41, 1)),
    ('Gasket', (.075, .091, .103, 1)),
    ('Glass', (.18, .25, .30, 1)),
    ('Control', (.67, .69, .67, 1)),
    ('Green', (.26, .66, .48, 1)),
    ('Red', (.70, .27, .24, 1)),
    ('Display', (.105, .15, .16, 1)),
]

def select(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def materials():
    MATS.clear()
    for name, color in PALETTE:
        mat = bpy.data.materials.get('PizzaPreview_' + name) or bpy.data.materials.new('PizzaPreview_' + name)
        mat.diffuse_color = color
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = color
        bsdf.inputs['Roughness'].default_value = .68
        MATS.append(mat)

def box(name, center, size, color, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    ob = bpy.context.object
    ob.name = 'PizzaPart_' + name
    ob.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel:
        mod = ob.modifiers.new('Small steel edge chamfer', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.data.materials.append(MATS[color])
    PARTS.append(ob)
    return ob

def cylinder(name, center, radius, length, color, axis='X', vertices=8):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=length, end_fill_type='TRIFAN', location=center)
    ob = bpy.context.object
    ob.name = 'PizzaPart_' + name
    if axis == 'X': ob.rotation_euler.y = math.pi/2
    if axis == 'Y': ob.rotation_euler.x = math.pi/2
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    ob.data.materials.append(MATS[color])
    PARTS.append(ob)
    return ob

def join_parts():
    body = bpy.data.objects.get('PizzaOven_Body')
    obs = list(PARTS)
    if body and body not in obs: obs.insert(0, body)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs: ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.object.join()
    body = bpy.context.object
    body.name = 'PizzaOven_Body'
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bm = bmesh.new(); bm.from_mesh(body.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(body.data); bm.free()
    PARTS.clear()
    return body

def presentation():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get('Background')
    bg.inputs[0].default_value = (.095, .125, .155, 1)
    bg.inputs[1].default_value = .65
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    data = bpy.data.cameras.get('PizzaStudio_Camera') or bpy.data.cameras.new('PizzaStudio_Camera')
    cam = bpy.data.objects.get('PizzaStudio_Camera')
    if not cam:
        cam = bpy.data.objects.new('PizzaStudio_Camera', data); scene.collection.objects.link(cam)
    cam.location = (.70, -1.25, .84)
    target = Vector((0, 0, .248))
    cam.rotation_euler = (target-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = .70
    scene.camera = cam
    for name, loc, energy, size in [
        ('Key', (.45, -.75, 1.35), 11, .9),
        ('Fill', (-.7, -.25, .8), 5, .8),
        ('Rim', (.1, .75, 1.1), 10, .65)]:
        name = 'PizzaStudio_' + name
        light = bpy.data.objects.get(name)
        if not light:
            data = bpy.data.lights.new(name, 'AREA')
            light = bpy.data.objects.new(name, data); scene.collection.objects.link(light)
        light.location = loc
        light.rotation_euler = (target-light.location).to_track_quat('-Z','Y').to_euler()
        light.data.energy = energy; light.data.shape='DISK'; light.data.size=size
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                space = area.spaces.active
                space.region_3d.view_location = target
                space.region_3d.view_distance = .95
                space.region_3d.view_rotation = cam.rotation_euler.to_quaternion()
                space.shading.type = 'MATERIAL'

def render(stage):
    os.makedirs(OUT+'/renders', exist_ok=True)
    presentation()
    bpy.context.scene.render.filepath = OUT+'/renders/'+stage+'.png'
    bpy.ops.render.render(write_still=True)

def build_blockout():
    assert 'HumanRef_1.8m' in bpy.data.objects
    if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('PizzaOven_', 'PizzaPart_', 'PizzaStudio_')):
            bpy.data.objects.remove(ob, do_unlink=True)
    PARTS.clear(); materials()
    # Continuous lower tray underside reaches z=0 and covers the mounting point.
    box('LowerTray', (0,.007,.009), (.376,.324,.018), 0, .002)
    for x in [-.177,.177]:
        for y in [-.143,.157]:
            box('StandLeg', (x,y,.112), (.021,.021,.218), 0)
    box('OvenCase', (0,.005,.344), (.396,.326,.266), 0, .004)
    box('FlatTop', (0,.005,.484), (.396,.326,.018), 0, .003)
    # Both doors face -Y; the customer's right is -X.
    for z in [.277, .391]:
        box('DoorBacking', (.030,-.161,z), (.292,.008,.090), 3)
        box('GlazedDoor', (.030,-.166,z), (.275,.004,.066), 4, .001)
        box('ControlPanel', (-.159,-.163,z), (.046,.009,.091), 5)
    body = join_parts()
    presentation()
    return {'triangles':sum(len(p.vertices)-2 for p in body.data.polygons), 'dimensions':list(body.dimensions)}

def add_details():
    materials()
    # Three tray runners on each side of the short open stand.
    for x in [-.157,.157]:
        for z in [.058,.104,.150]:
            box('TrayRunner', (x,.009,z), (.032,.279,.008), 1)
    box('RearCrossRail', (0,.156,.108), (.345,.014,.015), 2)
    box('StandTopFrontRail', (0,-.146,.204), (.365,.021,.020), 1)
    # Thin bright door frames, black handle mounts, substantial steel grip bars.
    for z in [.277,.391]:
        for zz in [z-.040,z+.040]:
            box('DoorHorizontalFrame', (.030,-.167,zz), (.294,.006,.009), 1)
        for x in [-.114,.174]:
            box('DoorVerticalFrame', (x,-.167,z), (.009,.006,.074), 0)
        for x in [-.099,.159]:
            box('HandleMount', (x,-.169,z-.019), (.011,.010,.022), 3)
        cylinder('SteelDoorHandle', (.030,-.175,z-.015), .0045, .275, 1)
        for x in [-.102,.161]:
            box('Hinge', (x,-.164,z-.049), (.023,.008,.010), 2)
        box('ControlDisplay', (-.159,-.169,z+.025), (.030,.004,.014), 8)
        cylinder('TemperatureDial', (-.159,-.171,z+.001), .009, .006, 3, 'Y')
        cylinder('DialCentre', (-.159,-.175,z+.001), .005, .002, 1, 'Y')
        box('GreenStatus', (-.171,-.170,z-.029), (.009,.004,.011), 6)
        box('RedStatus', (-.150,-.170,z-.029), (.009,.004,.011), 7)
    box('DeckDivider', (0,-.159,.334), (.384,.007,.010), 2)
    box('LowerFascia', (0,-.161,.218), (.385,.010,.017), 1)
    # Broad dark vent slats read on the customer's left without fine geometry.
    for z in [.276,.390]:
        box('SideVentRecess', (.198,.012,z), (.002,.214,.068), 3)
        for zz in [z-.023,z-.007,z+.009,z+.025]:
            box('SideVentSlat', (.199,.012,zz), (.002,.202,.007), 2)
    body = join_parts()
    return {'triangles':sum(len(p.vertices)-2 for p in body.data.polygons)}

def make_atlas():
    body = bpy.data.objects['PizzaOven_Body']; me = body.data
    # Unique padded cells: flat part colours do not require proportional texel density.
    # Adjacent triangles created on export share the same quad, never other UV islands.
    uv = me.uv_layers.active or me.uv_layers.new(name='UVMap')
    uv.name = 'UVMap'
    count=len(me.polygons); side=math.ceil(math.sqrt(count)); cell=512//side
    pixels=np.empty((512,512,4),dtype=np.float32); pixels[:]=PALETTE[3][1]
    for i, p in enumerate(me.polygons):
        x=(i%side)*cell; y=(i//side)*cell
        pixels[y:y+cell,x:x+cell]=me.materials[p.material_index].diffuse_color[:]
        lo=((x+1.5)/512,(y+1.5)/512); hi=((x+cell-1.5)/512,(y+cell-1.5)/512)
        coords=[lo,(hi[0],lo[1]),hi,(lo[0],hi[1])]
        assert len(p.loop_indices)<=4
        for j, k in enumerate(p.loop_indices): uv.data[k].uv=coords[j]
    old=bpy.data.images.get('T_PizzaOven_BaseColor.png')
    if old: bpy.data.images.remove(old)
    img=bpy.data.images.new('T_PizzaOven_BaseColor.png',width=512,height=512,alpha=False)
    img.colorspace_settings.name='sRGB'; img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=OUT+'/T_PizzaOven_BaseColor.png'; img.file_format='PNG'; img.save()
    img.filepath=OUT+'/T_PizzaOven_BaseColor.png'
    mat=bpy.data.materials.get('M_PizzaOven') or bpy.data.materials.new('M_PizzaOven')
    mat.use_nodes=True; nodes=mat.node_tree.nodes; nodes.clear()
    tex=nodes.new('ShaderNodeTexImage'); tex.image=img; tex.interpolation='Closest'; tex.location=(-350,0)
    bsdf=nodes.new('ShaderNodeBsdfPrincipled'); bsdf.location=(-50,0)
    bsdf.inputs['Roughness'].default_value=.68
    output=nodes.new('ShaderNodeOutputMaterial'); output.location=(250,0)
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
    for p in me.polygons: p.material_index=0; p.use_smooth=False
    me.materials.clear(); me.materials.append(mat)
    return {'faces':count,'cell_pixels':cell,'cells_per_row':side}

def prepare_final():
    body=bpy.data.objects['PizzaOven_Body']
    bm=bmesh.new(); bm.from_mesh(body.data)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(body.data); bm.free()
    # A simple convex enclosing box keeps collision independent of small trim details.
    bm=bmesh.new()
    lo=[min(v.co[i] for v in body.data.vertices) for i in range(3)]
    hi=[max(v.co[i] for v in body.data.vertices) for i in range(3)]
    for x in [lo[0],hi[0]]:
        for y in [lo[1],hi[1]]:
            for z in [lo[2],hi[2]]: bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.dissolve_limit(bm,angle_limit=.001,verts=list(bm.verts),edges=list(bm.edges))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('PizzaOven_Collision'); bm.to_mesh(me); bm.free()
    collision=bpy.data.objects.new('PizzaOven_Collision',me); bpy.context.collection.objects.link(collision)
    collision.hide_render=True; collision.display_type='WIRE'; collision.hide_set(True)
    collision['construction']='bmesh.ops.convex_hull from the eight enclosing bounds corners'
    body['customer_front']='-Y'; body['customer_left']='+X'
    body['style']='Compact two-deck steel pizza oven with dark glazing, neutral unbranded art'
    return {o.name:len(o.data.polygons) for o in [body,collision]}

def export_and_save():
    body=bpy.data.objects['PizzaOven_Body']; collision=bpy.data.objects['PizzaOven_Collision']
    for ob in list(bpy.data.objects):
        if ob.name.startswith('PizzaStudio_'): bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.scene.camera=None
    collision.hide_set(False); select(body); collision.select_set(True)
    bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
    bpy.ops.export_scene.fbx(filepath=OUT+'/PizzaOven.fbx', use_selection=True, object_types={'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type='OFF', use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False)
    collision.hide_set(True); select(body)
    bpy.context.preferences.filepaths.save_version=0
    bpy.data.images['T_PizzaOven_BaseColor.png'].filepath='//T_PizzaOven_BaseColor.png'
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/PizzaOven.blend')
    return {o.name:len(o.data.polygons) for o in [body,collision]}

if __name__=='__main__':
    build_blockout(); render('01-blockout')
    add_details(); render('02-detail')
    make_atlas(); render('03-texture')
    prepare_final(); render('04-final')
    export_and_save()

"""Regenerate in the already-open metric start scene; writes only beside this file.
Run build_blockout(), add_details(), make_atlas(), finish() in order.
The four functions save staged renders for visual inspection between steps.
"""
import bpy, bmesh, math, os
from mathutils import Vector
import numpy as np

OUT = os.path.dirname(os.path.abspath(__file__))
PARTS = []
PALETTE = [
    ('Steel', (0.62, 0.68, 0.70, 1)),
    ('EdgeSteel', (0.79, 0.83, 0.82, 1)),
    ('Charcoal', (0.055, 0.067, 0.071, 1)),
    ('Recess', (0.105, 0.128, 0.137, 1)),
    ('Paper', (0.88, 0.83, 0.70, 1)),
    ('PaperRim', (0.98, 0.95, 0.85, 1)),
    ('Interior', (0.43, 0.36, 0.26, 1)),
    ('Status', (0.30, 0.60, 0.45, 1)),
]
MATS = []

def select(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def materials():
    MATS.clear()
    for name, color in PALETTE:
        mat = bpy.data.materials.get('Preview_' + name) or bpy.data.materials.new('Preview_' + name)
        mat.diffuse_color = color
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = color
        bsdf.inputs['Roughness'].default_value = .72
        MATS.append(mat)

def register(ob, color):
    ob.data.materials.append(MATS[color])
    PARTS.append(ob)
    return ob

def box(name, center, size, color, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    ob = bpy.context.object
    ob.name = name
    ob.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel:
        mod = ob.modifiers.new('Silhouette chamfer', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return register(ob, color)

def lathe(name, xy, profile, colors, n=12):
    """Closed lathed shell. Profile travels bottom->outside->inside->bottom.
    Endpoint centres are single vertices; all remaining faces are quads.
    """
    verts, rings, faces, slots = [], [], [], []
    for index, (radius, z) in enumerate(profile):
        if index==len(profile)-1 and profile[-1]==profile[0]:
            rings.append(rings[0])
            continue
        if radius == 0:
            rings.append([len(verts)])
            verts.append((xy[0], xy[1], z))
        else:
            ring = []
            for j in range(n):
                a = 2*math.pi*j/n + math.pi/n
                ring.append(len(verts))
                verts.append((xy[0]+radius*math.cos(a),xy[1]+radius*math.sin(a),z))
            rings.append(ring)
    for i, (a,b) in enumerate(zip(rings, rings[1:])):
        for j in range(n):
            k=(j+1)%n
            if len(a)==1:
                faces.append((a[0],b[k],b[j]))
            elif len(b)==1:
                faces.append((a[j],a[k],b[0]))
            else:
                faces.append((a[j],a[k],b[k],b[j]))
            slots.append(colors[min(i,len(colors)-1)])
    me=bpy.data.meshes.new(name)
    me.from_pydata(verts,[],faces)
    me.update()
    ob=bpy.data.objects.new(name,me)
    bpy.context.collection.objects.link(ob)
    for mat in MATS: me.materials.append(mat)
    for p,c in zip(me.polygons,slots): p.material_index=c
    PARTS.append(ob)
    return ob

def cylinder(name, xy, radius, bottom, top, color, n=12):
    return lathe(name,xy,[(0,bottom),(radius,bottom),(radius,top),(0,top)],[color]*3,n)

def join_parts():
    body=bpy.data.objects.get('CoffeeStation_Body')
    obs=list(PARTS)
    if body and body not in obs: obs.insert(0,body)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs: ob.select_set(True)
    bpy.context.view_layer.objects.active=obs[0]
    bpy.ops.object.join()
    body=bpy.context.object
    body.name='CoffeeStation_Body'
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bm=bmesh.new(); bm.from_mesh(body.data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(body.data); bm.free()
    PARTS.clear()
    return body

def presentation():
    scene=bpy.context.scene
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=1000
    scene.render.resolution_y=1000
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.085,.115,.145,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='None'
    cam=bpy.data.objects.get('Presentation_Camera')
    if not cam:
        data=bpy.data.cameras.new('Presentation_Camera')
        cam=bpy.data.objects.new('Presentation_Camera',data)
        scene.collection.objects.link(cam)
    cam.location=(-.62,-1.25,.76)
    target=Vector((0,0,.215))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'; cam.data.ortho_scale=.68
    scene.camera=cam
    for name,loc,power,size in [
        ('Presentation_Key',(-.6,-.8,1.4),9,1.1),
        ('Presentation_Fill',(.8,-.35,.8),4,.8),
        ('Presentation_Rim',(.1,.65,.95),7,.7)]:
        light=bpy.data.objects.get(name)
        if not light:
            data=bpy.data.lights.new(name,'AREA')
            light=bpy.data.objects.new(name,data)
            scene.collection.objects.link(light)
        light.location=loc
        light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
        light.data.energy=power; light.data.shape='DISK'; light.data.size=size
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.region_3d.view_location=target
                space.region_3d.view_distance=.9
                space.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
                space.shading.type='MATERIAL'

def render(stage):
    os.makedirs(OUT+'/renders',exist_ok=True)
    presentation()
    bpy.context.scene.render.filepath=OUT+'/renders/'+stage+'.png'
    bpy.ops.render.render(write_still=True)

def build_blockout():
    assert 'HumanRef_1.8m' in bpy.data.objects, 'Run from the supplied start scene'
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    # Only replace objects owned by this script; never touch the human reference.
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('CoffeeStation_', 'Presentation_', 'Part_')): bpy.data.objects.remove(ob,do_unlink=True)
    PARTS.clear(); materials()
    box('Part_Base',(0,0,.011),(.386,.348,.022),2,.003)
    box('Part_Back',(.098,.089,.191),(.174,.122,.338),0,.008)
    box('Part_Head',(.098,.015,.385),(.180,.266,.100),0,.008)
    box('Part_Tray',(.098,-.090,.034),(.179,.166,.027),0,.004)
    for y in [-.019,.111]:
        lathe('Part_Urn',(-.095,y),[(0,.022),(.050,.022),(.058,.035),(.058,.269),(.050,.289),(0,.289)],[2,2,0,0,0],12)
        cylinder('Part_Lid',(-.095,y),.055,.286,.312,2,12)
    cylinder('Part_CupBlock',(-.095,-.132),.036,.022,.144,4,12)
    body=join_parts()
    render('01-blockout')
    return body

def add_details():
    materials()
    # Front (-Y) brew cavity and high-contrast control strip.
    box('Part_Recess',(.098,.025,.199),(.151,.007,.271),3)
    box('Part_ControlStrip',(.098,-.121,.388),(.148,.006,.042),2)
    for x in [.051,.094]:
        ob=cylinder('Part_Button',(0,0),.009,-.003,.003,1,8)
        ob.rotation_euler[0]=math.pi/2
        ob.location=(x,-.126,.388)
        select(ob); bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    box('Part_Status',(.143,-.126,.388),(.012,.004,.008),7)
    cylinder('Part_Group',(.098,-.058),.028,.310,.340,1,12)
    cylinder('Part_Nozzle',(.098,-.058),.008,.291,.313,2,8)
    # Espresso portafilter's dark grip projects towards the customer.
    box('Part_Grip',(.098,-.108,.317),(.022,.075,.016),2)
    box('Part_TrayInset',(.098,-.092,.048),(.149,.143,.003),2)
    for x in [.042,.070,.098,.126,.154]:
        box('Part_TrayRail',(x,-.092,.052),(.007,.127,.005),1)
    # Pump heads, projecting pour spouts, and rear carry loops.
    for y in [-.019,.111]:
        cylinder('Part_PumpStem',(-.095,y),.020,.310,.337,0,10)
        lathe('Part_Pump',(-.095,y),[(0,.331),(.032,.331),(.038,.338),(.038,.351),(.030,.356),(0,.356)],[2]*5,8)
        box('Part_PourSpout',(-.095,y-.055,.307),(.025,.046,.019),2)
        cylinder('Part_SpoutTip',(-.095,y-.072),.008,.290,.307,2,8)
        # An arch across X avoids extending behind the footprint.
        for x in [-.134,-.056]:
            box('Part_HandleLeg',(x,y+.028,.325),(.011,.013,.049),2)
        box('Part_HandleTop',(-.095,y+.028,.350),(.086,.013,.011),2)
    # Replace the cup block by a hollow tapered nested stack; four rolled rims.
    body=bpy.data.objects['CoffeeStation_Body']
    # Blockout component is identified by its Paper material and removed from the joined mesh.
    bm=bmesh.new(); bm.from_mesh(body.data)
    paper_slots={i for i,m in enumerate(body.data.materials) if m and m.name=='Preview_Paper'}
    doomed=[f for f in bm.faces if f.material_index in paper_slots]
    verts=set(v for f in doomed for v in f.verts)
    bmesh.ops.delete(bm,geom=list(verts),context='VERTS')
    bm.to_mesh(body.data); bm.free()
    profile=[(0,.022),(.025,.022),(.036,.124),(.037,.131),(.034,.136),(.032,.131),(.023,.036),(0,.036)]
    lathe('Part_CupShell',(-.095,-.132),profile,[4,4,5,5,5,6,6],10)
    for z in [.103,.111,.119]:
        r=.025+(.036-.025)*(z-.022)/(.124-.022)
        lathe('Part_CupRim',(-.095,-.132),[(r-.001,z-.002),(r+.002,z-.002),(r+.003,z+.001),(r-.001,z+.003),(r-.001,z-.002)],[5]*4,10)
    body=join_parts()
    render('02-detail')
    return body

def make_atlas():
    body=bpy.data.objects['CoffeeStation_Body']
    me=body.data
    # A unique padded atlas cell per face. Flat colours need no texel-density distortion correction.
    # Quads consume the full cell, triangle endcaps consume half, with no UV sharing.
    uv=me.uv_layers.active or me.uv_layers.new(name='UVMap')
    uv.name='UVMap'
    count=len(me.polygons); side=math.ceil(math.sqrt(count))
    cell=512//side
    pixels=np.zeros((512,512,4),dtype=np.float32)
    pixels[:]=(.055,.067,.071,1)
    for i,p in enumerate(me.polygons):
        x=(i%side)*cell; y=(i//side)*cell
        color=me.materials[p.material_index].diffuse_color[:]
        # Image pixel values are encoded sRGB, matching artist palette values.
        pixels[y:y+cell,x:x+cell]=color
        lo=((x+1.5)/512,(y+1.5)/512)
        hi=((x+cell-1.5)/512,(y+cell-1.5)/512)
        coords=[lo,(hi[0],lo[1]),hi,(lo[0],hi[1])]
        assert len(p.loop_indices)<=4
        for j,k in enumerate(p.loop_indices): uv.data[k].uv=coords[j]
    img=bpy.data.images.get('T_CoffeeStation_BaseColor.png')
    if img: bpy.data.images.remove(img)
    img=bpy.data.images.new('T_CoffeeStation_BaseColor.png',width=512,height=512,alpha=False)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=OUT+'/T_CoffeeStation_BaseColor.png'; img.file_format='PNG'; img.save()
    img.filepath=OUT+'/T_CoffeeStation_BaseColor.png'
    mat=bpy.data.materials.get('M_CoffeeStation') or bpy.data.materials.new('M_CoffeeStation')
    mat.use_nodes=True
    nodes=mat.node_tree.nodes; nodes.clear()
    tex=nodes.new('ShaderNodeTexImage'); tex.image=img; tex.interpolation='Closest'; tex.location=(-350,0)
    bsdf=nodes.new('ShaderNodeBsdfPrincipled'); bsdf.location=(-50,0)
    bsdf.inputs['Roughness'].default_value=.72
    out=nodes.new('ShaderNodeOutputMaterial'); out.location=(260,0)
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
    for p in me.polygons: p.material_index=0; p.use_smooth=False
    me.materials.clear(); me.materials.append(mat)
    render('03-texture')
    return {'faces':count,'atlas_cells_per_row':side,'cell_pixels':cell}

def finish():
    body=bpy.data.objects['CoffeeStation_Body']
    bm=bmesh.new(); bm.from_mesh(body.data)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(body.data); bm.free()
    # Simplified envelope around the complete station, generated as a true convex hull.
    bm=bmesh.new()
    for z,w,d in [(0,.194,.175),(.36,.194,.175),(.436,.189,.156)]:
        for x,y in [(-w,-d),(w,-d),(w,d),(-w,d)]: bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('CoffeeStation_Collision'); bm.to_mesh(me); bm.free()
    collision=bpy.data.objects.new('CoffeeStation_Collision',me)
    bpy.context.collection.objects.link(collision)
    collision.hide_render=True; collision.display_type='WIRE'
    collision['construction']='bmesh.ops.convex_hull; simplified enclosing envelope'
    collision.hide_set(True)
    body['customer_front']='-Y'; body['dimensions_metres']='0.386 x 0.348 x 0.435'
    body['style']='Flat colour, unbranded, compact espresso brewer with two pump airpots and cups'
    render('04-final')
    # Final scene contains only the delivered meshes and the untouched scale reference.
    for ob in list(bpy.data.objects):
        if ob.name.startswith('Presentation_'): bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.scene.camera=None
    collision.hide_set(False)
    select(body); collision.select_set(True)
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1.0
    bpy.ops.export_scene.fbx(filepath=OUT+'/CoffeeStation.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    select(body)
    bpy.context.preferences.filepaths.save_version=0
    bpy.data.images['T_CoffeeStation_BaseColor.png'].filepath='//T_CoffeeStation_BaseColor.png'
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/CoffeeStation.blend')
    return {o.name:len(o.data.polygons) for o in [body,collision]}

if __name__=='__main__':
    build_blockout()
    add_details()
    make_atlas()
    finish()

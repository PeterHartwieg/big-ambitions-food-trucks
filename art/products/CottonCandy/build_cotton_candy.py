"""Regenerate the cotton candy in the existing metric start scene.
Run in Blender's Python console: exec(compile(open(PATH).read(), PATH, 'exec')).
The human reference is preserved. All output stays beside this script.
"""
import bpy, bmesh, math, os
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(OUT)))
N = 22
RINGS = 12
PINK = (0.96, 0.48, 0.69, 1.0)
PAPER = (0.96, 0.95, 0.91, 1.0)


def active(ob):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def material(name, rgba):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = rgba
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = rgba
    bs.inputs['Roughness'].default_value = 0.86
    bs.inputs['Specular IOR Level'].default_value = 0.18
    return m


def cloud_point(theta, phi, detailed=False):
    d = Vector((math.sin(theta)*math.cos(phi), math.sin(theta)*math.sin(phi), math.cos(theta)))
    radial = 1.0
    if detailed:
        # Smooth union of five broad rounded puffs. Solve each sphere's outer
        # ray intersection, then gently blend joins; no fine surface noise.
        puffs = [((.46,-.08,.05),.67), ((-.43,-.10,.20),.65),
                 ((.06,.02,.49),.64), ((0,-.21,-.43),.66),
                 ((0,.32,.03),.66)]
        radial = .78
        for centre, radius in puffs:
            c = Vector(centre)
            dot = d.dot(c)
            disc = radius*radius-c.length_squared+dot*dot
            if disc >= 0:
                t = dot+math.sqrt(disc)
                k = .075
                h = max(k-abs(radial-t),0)/k
                radial = max(radial,t)+h*h*k*.25
    # Egg profile: top shoulders a little broader than the lower taper.
    width = 1.0 + .14*d.z
    return (d.x*.040*width*radial, d.y*.035*width*radial, .094 + d.z*.045*radial)


def mesh_body(detailed=False):
    vertices, faces, uvs = [], [], []
    def face(v, uv):
        faces.append(v)
        uvs.append(uv)
    # One closed continuous pink surface. Seam lies on the rear (+Y).
    vertices.append(cloud_point(0, 0, detailed))
    for j in range(1,RINGS+1):
        theta = math.pi*j/(RINGS+1)
        for i in range(N):
            phi = math.pi/2 + 2*math.pi*i/N
            vertices.append(cloud_point(theta,phi,detailed))
    bottom = len(vertices)
    vertices.append(cloud_point(math.pi,0,detailed))
    def idx(j,i): return 1+(j-1)*N+(i%N)
    def uv(j,i): return (.015+.790*i/N, .985-.970*j/(RINGS+1))
    for i in range(N):
        face((0,idx(1,i),idx(1,i+1)), [uv(0,i+.5),uv(1,i),uv(1,i+1)])
    for j in range(1,RINGS):
        for i in range(N):
            face((idx(j,i),idx(j+1,i),idx(j+1,i+1),idx(j,i+1)),
                 [uv(j,i),uv(j+1,i),uv(j+1,i+1),uv(j,i+1)])
    for i in range(N):
        face((idx(RINGS,i),bottom,idx(RINGS,i+1)),[uv(RINGS,i),uv(RINGS+1,i+.5),uv(RINGS,i+1)])
    cloud_faces = len(faces)
    # A truncated paper cone: the closed, flat bottom includes the origin.
    off = len(vertices)
    for z,radius in [(0,.0027),(.061,.009)]:
        for i in range(12):
            a = 2*math.pi*i/12
            vertices.append((radius*math.cos(a),radius*math.sin(a),z))
    botcentre, topcentre = len(vertices),len(vertices)+1
    vertices.extend([(0,0,0),(0,0,.061)])
    for i in range(12):
        k = (i+1)%12
        u0,u1 = .835+.15*i/12,.835+.15*(i+1)/12
        face((off+i,off+k,off+12+k,off+12+i),[(u0,.035),(u1,.035),(u1,.835),(u0,.835)])
        def capuv(t, centre, radius):
            a = 2*math.pi*t/12
            return (centre[0]+radius*math.cos(a),centre[1]+radius*math.sin(a))
        face((botcentre,off+k,off+i),[(.868,.915),capuv(k,(.868,.915),.024),capuv(i,(.868,.915),.024)])
        face((topcentre,off+12+i,off+12+k),[(.946,.915),capuv(i,(.946,.915),.035),capuv(k,(.946,.915),.035)])
    me = bpy.data.meshes.new('CottonCandy_Surface')
    me.from_pydata(vertices,[],faces)
    me.update()
    ob = bpy.data.objects.get('CottonCandy_Body')
    if ob:
        old = ob.data
        ob.data = me
        if old.users == 0: bpy.data.meshes.remove(old)
    else:
        ob = bpy.data.objects.new('CottonCandy_Body',me)
        bpy.context.collection.objects.link(ob)
    layer = me.uv_layers.new(name='UVMap')
    for p, coords in zip(me.polygons, uvs):
        for loop,coord in zip(p.loop_indices,coords): layer.data[loop].uv = coord
        p.use_smooth = p.index < cloud_faces or (p.index-cloud_faces)%3 == 0
    me.materials.append(material('CottonCandy_PinkPreview',PINK))
    me.materials.append(material('CottonCandy_PaperPreview',PAPER))
    for p in me.polygons: p.material_index = 0 if p.index < cloud_faces else 1
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(me)
    bm.free()
    ob['front_direction'] = '-Y'
    ob['height_limit_m'] = .15
    ob['design'] = 'Pink spun-sugar cloud on a white paper cone; neutral street-food prop'
    return ob


def setup_render():
    os.makedirs(os.path.join(OUT,'renders'),exist_ok=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 800
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    scene.world = bpy.data.worlds.new('CottonCandy_Studio')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.16,.19,.23,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .65
    camera_data = bpy.data.cameras.new('CottonCandy_ReviewCamera')
    camera = bpy.data.objects.new('CottonCandy_ReviewCamera',camera_data)
    scene.collection.objects.link(camera)
    camera.location = (.19,-.43,.23)
    camera.rotation_euler = (Vector((0,0,.077))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = .185
    camera_data.lens = 50
    scene.camera = camera
    for name,loc,power,size in [('Key',(-.16,-.24,.31),1.1,.23),('Fill',(.23,-.08,.15),.45,.20),('Rim',(.02,.16,.22),.6,.18)]:
        light_data = bpy.data.lights.new('CottonCandy_'+name,'AREA')
        light_data.energy = power
        light_data.shape = 'DISK'
        light_data.size = size
        light = bpy.data.objects.new(light_data.name,light_data)
        scene.collection.objects.link(light)
        light.location = loc
        light.rotation_euler = (Vector((0,0,.09))-light.location).to_track_quat('-Z','Y').to_euler()
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                area.spaces.active.region_3d.view_perspective = 'CAMERA'
    active(bpy.data.objects['CottonCandy_Body'])


def blockout():
    mesh_body(False)
    setup_render()


def detail():
    return mesh_body(True)


def atlas():
    ob = bpy.data.objects['CottonCandy_Body']
    image = bpy.data.images.new('T_CottonCandy_BaseColor',width=256,height=256,alpha=True)
    # Flat colour blocks; UV seams and padding remain inside their part's block.
    pixels = []
    for y in range(256):
        for x in range(256): pixels.extend(PINK if x < 210 else PAPER)
    image.pixels.foreach_set(pixels)
    image.filepath_raw = os.path.join(OUT,'T_CottonCandy_BaseColor.png')
    image.file_format = 'PNG'
    image.save()
    image.pack()
    image.filepath = '//T_CottonCandy_BaseColor.png'
    mat = material('M_CottonCandy', (1,1,1,1))
    nodes = mat.node_tree.nodes
    texture = nodes.new('ShaderNodeTexImage')
    texture.name = 'BaseColor'
    texture.label = '256 x 256 flat colour atlas'
    texture.image = image
    texture.interpolation = 'Closest'
    mat.node_tree.links.new(texture.outputs['Color'],nodes.get('Principled BSDF').inputs['Base Color'])
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    for p in ob.data.polygons: p.material_index = 0
    # Explicit triangles, preserving the UV loops and smooth normals.
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='BEAUTY')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()


def collision():
    body = bpy.data.objects['CottonCandy_Body']
    # 20 support vertices, 36 triangles. Three octagonal support levels
    # taper to a square cone base; the hull encloses every body vertex.
    bm = bmesh.new()
    supports = []
    for x,y in [(-.004,-.004),(.004,-.004),(.004,.004),(-.004,.004)]:
        supports.append((x,y,0))
    for z,rx,ry in [(.070,.055,.050),(.151,.047,.043)]:
        for i in range(8):
            a=2*math.pi*i/8+math.pi/8
            supports.append((rx*math.cos(a),ry*math.sin(a),z))
    for co in supports: bm.verts.new(co)
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused = [v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me = bpy.data.meshes.new('CottonCandy_ConvexHull')
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new('CottonCandy_Collision',me)
    bpy.context.collection.objects.link(ob)
    ob.hide_render = True
    ob.display_type = 'WIRE'
    ob.hide_set(True)
    ob['construction'] = 'bmesh.ops.convex_hull; 20 support vertices'
    return ob


def finish():
    body = bpy.data.objects['CottonCandy_Body']
    hull = bpy.data.objects.get('CottonCandy_Collision') or collision()
    # All visible mesh coordinates are baked around the floor origin.
    for ob in (body,hull):
        active(ob)
        bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
        ob.location=(0,0,0)
    hull.hide_set(False)
    active(body)
    hull.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT+'/CottonCandy.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    hull.hide_set(True)
    active(body)
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/CottonCandy.blend')
    return {ob.name:sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in (body,hull)}


if __name__ == '__main__':
    blockout()
    for stage,step in [('01-blockout',None),('02-detail',detail),('03-texture',atlas),('04-final',finish)]:
        if step: step()
        bpy.context.scene.render.filepath=os.path.join(OUT,'renders',stage+'.png')
        bpy.ops.render.render(write_still=True)

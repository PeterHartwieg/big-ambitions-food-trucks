"""Run in the existing metric Blender scene. build(1..4) generates checkpoints.
No external assets; preserves HumanRef_1.8m. Final stage saves and exports.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

OUT = os.path.dirname(os.path.abspath(__file__))
PALETTE = {
    'paper': (0.83, 0.77, 0.62, 1), 'paper_inside': (0.97, 0.93, 0.82, 1),
    'corn': (0.88, 0.61, 0.23, 1), 'corn_inside': (1.0, 0.77, 0.36, 1),
    'meat': (0.29, 0.105, 0.046, 1), 'meat_light': (0.43, 0.19, 0.075, 1),
    'onion': (0.97, 0.97, 0.86, 1), 'cilantro': (0.07, 0.37, 0.055, 1),
    'cilantro_light': (0.19, 0.53, 0.07, 1),
    'rind': (0.18, 0.48, 0.04, 1), 'pith': (0.86, 0.94, 0.51, 1),
    'lime': (0.62, 0.83, 0.18, 1), 'lime_light': (0.75, 0.91, 0.29, 1),
}
V, F, C = [], [], []

def part(verts, faces, colours):
    offset = len(V)
    V.extend(verts)
    F.extend([tuple(i + offset for i in f) for f in faces])
    C.extend([colours] * len(faces) if isinstance(colours, str) else colours)

def octagon(x, y, z, c):
    return [(-x+c,-y,z),(x-c,-y,z),(x,-y+c,z),(x,y-c,z),
            (x-c,y,z),(-x+c,y,z),(-x,y-c,z),(-x,-y+c,z)]

def tray():
    # Four connected rings: underside, outer rim, inner rim, inner floor.
    verts = (octagon(.100,.077,0,.018) + octagon(.119,.103,.023,.024)
             + octagon(.1175,.1015,.023,.023) + octagon(.099,.076,.002,.018))
    faces, colours = [], []
    for j in range(3):
        for i in range(8):
            faces.append((j*8+i,j*8+(i+1)%8,(j+1)*8+(i+1)%8,(j+1)*8+i))
            colours.append('paper' if j == 0 else 'paper_inside')
    for i in range(1,7):
        faces.extend([(0,i+1,i),(24,24+i,25+i)])
        colours.extend(['paper','paper_inside'])
    part(verts,faces,colours)

def tortilla(cx):
    # Folded disc with round-ended silhouette, a shallow U and real thickness.
    surface = [(cx,-.063,.025)]
    for y, width in [(-.056,.57),(-.032,.90),(0,1),(.032,.90),(.056,.57)]:
        for t in [-1,-.6,0,.6,1]:
            surface.append((cx+.026*math.sin(t*math.pi/2)*width,
                            y+.012,.021+.048*(1-math.cos(t*math.pi/2))*width))
    surface.append((cx,.087,.025))
    faces = []
    for i in range(4):
        faces.append((0,1+i,2+i))
        faces.append((22+i,21+i,26))
    for row in range(4):
        for i in range(4):
            a=1+row*5+i
            faces.append((a,a+5,a+6,a+1))
    # A vertical thickness shift gives one closed shell without degenerate tips.
    n=len(surface)
    verts=surface+[(x,y,z+.0018) for x,y,z in surface]
    full=faces+[tuple(i+n for i in reversed(f)) for f in faces]
    colours=['corn']*len(faces)+['corn_inside']*len(faces)
    perimeter=[0,1,6,11,16,21,26,25,20,15,10,5]
    for a,b in zip(perimeter,perimeter[1:]+perimeter[:1]):
        full.append((a,b,b+n,a+n)); colours.append('corn_inside')
    part(verts,full,colours)

def diamond(center, size, angle, colour, highlight=None):
    # Closed six-vertex chunk / leaf, eight triangles.
    x,y,z=center; sx,sy,sz=size
    pts=[(-sx,0,0),(0,-sy,0),(sx,0,0),(0,sy,0),(.001,0,sz),(0,0,-sz*.65)]
    m=Matrix.Rotation(angle,3,'Z')
    verts=[tuple(Vector(center)+m@Vector(p)) for p in pts]
    faces=[(i,(i+1)%4,4) for i in range(4)]+[((i+1)%4,i,5) for i in range(4)]
    colours=[highlight if highlight and i%2==0 else colour for i in range(8)]
    part(verts,faces,colours)

def onion(center,angle):
    sx,sy,sz=.004,.0035,.0035
    m=Matrix.Rotation(angle,3,'Z')
    pts=[(-sx,-sy,-sz),(sx,-sy,-sz),(sx,sy,-sz),(-sx,sy,-sz),
         (-sx,-sy,sz),(sx,-sy,sz),(sx,sy,sz),(-sx,sy,sz)]
    faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    part([tuple(Vector(center)+m@Vector(p)) for p in pts],faces,'onion')

def toppings():
    for j,cx in enumerate([-.066,0,.066]):
        for i,y in enumerate([-.035,0,.035]):
            z=.059 if i in (0,2) else .064
            diamond((cx+(-.002 if i%2 else .002),y+.012,z),
                    (.018,.026,.012),.17*(i+j),'meat','meat_light')
        for i,y in enumerate([-.018,.025]):
            z=.072
            onion((cx+(-.005 if (i+j)%2 else .006),y+.012,z),.25+i*.42+j*.23)
            diamond((cx+(.006 if (i+j)%2 else -.006),y+.006,z+.002),
                    (.007,.010,.0015),.7+i+j*.55,'cilantro','cilantro_light')

def lime():
    # A quarter-cylinder wedge resting on its rind with a raised juicy cut face.
    cx,cy,cz=.067,-.068,.011
    radius=.024; length=.032
    ang=[0,math.pi/8,math.pi/4,3*math.pi/8,math.pi/2]
    # Axis along X; radial Y/Z plane. Fan caps form four visible citrus segments.
    verts=[]
    for x in [-length/2,length/2]:
        verts.append((cx+x,cy,cz))
        verts.extend((cx+x,cy-radius*math.cos(a),cz+radius*math.sin(a)) for a in ang)
    faces=[]; colours=[]
    for i in range(4):
        faces.extend([(0,1+i,2+i),(6,8+i,7+i)])
        colours.extend(['lime' if i%2 else 'lime_light']*2)
        faces.append((1+i,7+i,8+i,2+i)); colours.append('rind')
    faces.extend([(0,6,7,1),(5,11,6,0)])
    colours.extend(['pith','lime_light'])
    part([(cx-(y-cy),cy+(x-cx),z) for x,y,z in verts],faces,colours)

def setup_camera():
    cam=bpy.data.objects.get('Tacos_PreviewCamera')
    if not cam:
        cam=bpy.data.objects.new('Tacos_PreviewCamera',bpy.data.cameras.new('Tacos_PreviewCamera'))
        bpy.context.collection.objects.link(cam)
    cam.location=(.26,-.42,.31)
    cam.rotation_euler=(Vector((0,0,.035))-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'; cam.data.ortho_scale=.31
    bpy.context.scene.camera=cam
    scene=bpy.context.scene
    scene.render.resolution_x=960; scene.render.resolution_y=800
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.light='STUDIO'
    scene.display.shading.studio_light='paint.sl'
    scene.display.shading.studiolight_rotate_z=.5
    scene.display.shading.color_type='MATERIAL'
    scene.display.shading.show_shadows=True
    scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH'
    scene.display.shading.curvature_ridge_factor=.4
    scene.display.shading.curvature_valley_factor=.5
    scene.display.shading.background_type='WORLD'
    scene.world.color=(.075,.085,.1)
    scene.view_settings.view_transform='Standard'
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=.40
                area.spaces.active.region_3d.view_location=(0,0,.035)
                area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
                area.spaces.active.clip_start=.001
                area.spaces.active.shading.color_type='MATERIAL'

def atlas(body):
    me=body.data
    uv=me.uv_layers.new(name='UVMap')
    cols=math.ceil(math.sqrt(len(me.polygons)))
    rows=math.ceil(len(me.polygons)/cols)
    image=bpy.data.images.get('T_Tacos_BaseColor')
    if image: bpy.data.images.remove(image)
    image=bpy.data.images.new('T_Tacos_BaseColor',width=256,height=256,alpha=True)
    # Palette-only cells with dedicated, inset, non-overlapping face islands.
    pixels=[0.12,0.12,0.12,1.0]*(256*256)
    for p,colour in zip(me.polygons,C):
        col,row=p.index%cols,p.index//cols
        u0,u1=col/cols,(col+1)/cols; v0,v1=row/rows,(row+1)/rows
        margin=.045
        a,b=u0+(u1-u0)*margin,u1-(u1-u0)*margin
        c,d=v0+(v1-v0)*margin,v1-(v1-v0)*margin
        coords=[(a,c),(b,c),(b,d),(a,d)] if len(p.vertices)==4 else [(a,c),(b,c),(a,d)]
        for li,co in zip(p.loop_indices,coords): uv.data[li].uv=co
        rgba=PALETTE[colour]
        for py in range(max(0,math.floor(v0*256)),min(256,math.ceil(v1*256))):
            for px in range(max(0,math.floor(u0*256)),min(256,math.ceil(u1*256))):
                pixels[(py*256+px)*4:(py*256+px)*4+4]=rgba
    image.pixels.foreach_set(pixels)
    image.filepath_raw=OUT+'/T_Tacos_BaseColor.png'
    image.file_format='PNG'; image.save()
    image.filepath=OUT+'/T_Tacos_BaseColor.png'
    mat=bpy.data.materials.get('M_Tacos') or bpy.data.materials.new('M_Tacos')
    mat.use_nodes=True
    mat.node_tree.nodes.clear()
    output=mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    shader=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Roughness'].default_value=.85
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Closest'
    tex.label='Flat part colours / packed face atlas'
    mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    mat.node_tree.links.new(shader.outputs['BSDF'],output.inputs['Surface'])
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons: p.material_index=0
    return {'columns':cols,'rows':rows,'faces':len(me.polygons)}

def collision():
    bm=bmesh.new()
    # Four-point floor encloses the tray underside; octagonal rim and raised cap.
    floor=[(-.101,-.078,0),(.101,-.078,0),(.101,.078,0),(-.101,.078,0)]
    for co in floor+octagon(.121,.105,.024,.024)+octagon(.099,.090,.080,.020):
        bm.verts.new(co)
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.dissolve_limit(bm,angle_limit=.001,verts=list(bm.verts),edges=list(bm.edges))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new('Tacos_Collision'); bm.to_mesh(mesh); bm.free()
    ob=bpy.data.objects.new('Tacos_Collision',mesh); bpy.context.collection.objects.link(ob)
    ob.hide_render=True; ob.hide_set(True); ob.display_type='WIRE'
    return ob

def render(stage):
    os.makedirs(OUT+'/renders',exist_ok=True)
    names={1:'01-blockout',2:'02-detail',3:'03-texture',4:'04-final'}
    bpy.context.scene.render.filepath=OUT+'/renders/'+names[stage]+'.png'
    bpy.ops.render.render(write_still=True)

def build(stage=4):
    global V,F,C
    V,F,C=[],[],[]
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for name in ['Tacos_Body','Tacos_Collision']:
        ob=bpy.data.objects.get(name)
        if ob: bpy.data.objects.remove(ob,do_unlink=True)
    tray()
    for cx in [-.066,0,.066]: tortilla(cx)
    if stage>=2: toppings(); lime()
    mesh=bpy.data.meshes.new('Tacos_Body'); mesh.from_pydata(V,[],F); mesh.update()
    bm=bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free()
    body=bpy.data.objects.new('Tacos_Body',mesh); bpy.context.collection.objects.link(body)
    for key,rgba in PALETTE.items():
        mat=bpy.data.materials.get('Preview_'+key) or bpy.data.materials.new('Preview_'+key)
        mat.diffuse_color=rgba; mesh.materials.append(mat)
    for p,col in zip(mesh.polygons,C): p.material_index=list(PALETTE).index(col)
    setup_camera()
    uv_info=None
    if stage>=3:
        uv_info=atlas(body)
        bpy.context.scene.display.shading.color_type='TEXTURE'
        for screen in bpy.data.screens:
            for area in screen.areas:
                if area.type=='VIEW_3D': area.spaces.active.shading.color_type='TEXTURE'
        # Bind the texture to the active node for Workbench texture display.
        mat=body.data.materials[0]
        mat.node_tree.nodes.active=next(n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE')
    if stage>=4:
        col=collision()
        bpy.ops.object.select_all(action='DESELECT')
        body.select_set(True); col.hide_set(False); col.select_set(True)
        bpy.context.view_layer.objects.active=body
        bpy.ops.export_scene.fbx(filepath=OUT+'/Tacos.fbx',use_selection=True,object_types={'MESH'},
            global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
            axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
            mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
            add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
        col.hide_set(True)
        bpy.context.scene.unit_settings.system='METRIC'
        bpy.context.scene.unit_settings.scale_length=1
        bpy.data.images['T_Tacos_BaseColor'].filepath='//T_Tacos_BaseColor.png'
        bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Tacos.blend',relative_remap=False)
        bpy.data.images['T_Tacos_BaseColor'].reload()
    render(stage)
    return {'stage':stage,'triangles':sum(len(p.vertices)-2 for p in mesh.polygons),'uv':uv_info,
            'bounds':[[min(v.co[i] for v in mesh.vertices),max(v.co[i] for v in mesh.vertices)] for i in range(3)]}

if __name__=='__main__':
    print(build(4))

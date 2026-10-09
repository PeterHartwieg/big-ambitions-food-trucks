"""Regenerate in Blender's existing metric scene, preserving HumanRef_1.8m.
exec(compile(open(PATH).read(), PATH, 'exec')); build('all')
Stages can also be run individually for visual review. Outputs stay beside this file.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector
OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
PALETTE = {
 'Bamboo': (.78,.57,.29), 'BambooLight': (.89,.72,.43),
 'BambooDark': (.49,.31,.13), 'BambooRim': (.66,.44,.20),
 'Steel': (.53,.61,.63), 'SteelLight': (.73,.79,.78),
 'SteelDark': (.25,.32,.34), 'Burner': (.105,.14,.15),
 'Tray': (.37,.48,.48), 'TrayInner': (.52,.64,.62),
 'Dough': (.97,.91,.73), 'Pleat': (.85,.76,.54),
 'Chopstick': (.33,.17,.07), 'Knob': (.15,.19,.20),
}
PARTS=[]
def linear(c):
    return c / 12.92 if c < 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def provisional_material(key):
    name = 'Build_' + key
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*[linear(c) for c in PALETTE[key]], 1)
    return mat

def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob

def mesh_part(name, vertices, faces, colors):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    ob = bpy.data.objects.new('DS_' + name, mesh)
    bpy.context.collection.objects.link(ob)
    keys = list(dict.fromkeys(colors))
    for key in keys:
        mesh.materials.append(provisional_material(key))
    for face, key in zip(mesh.polygons, colors):
        face.material_index = keys.index(key)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free(); mesh.update()
    PARTS.append(ob)
    return ob

def box(name, center, size, color='Steel', spin=0):
    cx, cy, cz = center; sx, sy, sz = [s / 2 for s in size]
    verts = [(x*sx, y*sy, z*sz) for z in (-1,1) for y in (-1,1) for x in (-1,1)]
    verts = [(cx+x*math.cos(spin)-y*math.sin(spin), cy+x*math.sin(spin)+y*math.cos(spin), cz+z) for x,y,z in verts]
    faces = [(0,2,3,1),(4,5,7,6),(0,1,5,4),(1,3,7,5),(3,2,6,7),(2,0,4,6)]
    return mesh_part(name, verts, faces, [color]*6)

def join_body():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in PARTS: ob.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    ob = bpy.context.object; ob.name = 'DumplingSteamer_Body'; ob.data.name = 'DumplingSteamer_Mesh'
    bpy.context.scene.cursor.location = (0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    ob['front_direction'] = '-Y'; ob['dimensions_in_metres'] = True
    return ob


def lathe(name, profiles, colors, n=18, center=(0,.037), alternating=False):
    # Solid capped surface of revolution; profiles travel bottom to top.
    cx,cy=center
    vs=[(cx+r*math.cos(2*math.pi*i/n),cy+r*math.sin(2*math.pi*i/n),z)
        for r,z in profiles for i in range(n)]
    faces=[tuple(range(n-1,-1,-1))]; keys=[colors[0]]
    for j in range(len(profiles)-1):
        for i in range(n):
            faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
            key=colors[j]
            if alternating and j==1 and i%4==0: key='BambooLight'
            keys.append(key)
    faces.append(tuple(range((len(profiles)-1)*n,len(profiles)*n))); keys.append(colors[-1])
    return mesh_part(name,vs,faces,keys)

def rounded_rect(name,rings,colors,center=(0,0)):
    # Eight-sided rectangles with clipped corners, also used as a closed tray shell.
    vs=[]
    for w,d,z,clip in rings:
        x,y=w/2,d/2
        vs += [(center[0]+a,center[1]+b,z) for a,b in
               ((-x+clip,-y),(x-clip,-y),(x,-y+clip),(x,y-clip),
                (x-clip,y),(-x+clip,y),(-x,y-clip),(-x,-y+clip))]
    faces=[tuple(range(7,-1,-1))]; keys=[colors[0]]
    for j in range(len(rings)-1):
        for i in range(8):
            faces.append((8*j+i,8*j+(i+1)%8,8*(j+1)+(i+1)%8,8*(j+1)+i))
            keys.append(colors[min(j,len(colors)-1)])
    faces.append(tuple(range(8*(len(rings)-1),8*len(rings)))); keys.append(colors[-1])
    return mesh_part(name,vs,faces,keys)

def blockout():
    global PARTS
    PARTS=[]
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('DS_','DumplingSteamer_','Studio_')):
            bpy.data.objects.remove(ob,do_unlink=True)
    # Broad placement base: continuous flat underside covers the origin.
    rounded_rect('BurnerBase',[(.372,.344,0,.022),(.384,.354,.010,.026),
                             (.384,.354,.029,.026),(.370,.340,.035,.022)],
                 ['Burner','SteelDark','Steel'])
    lathe('BurnerRing',[(.114,.034),(.123,.040),(.123,.059)],['Burner','Burner'],n=16)
    lathe('SteamerPot',[(.122,.054),(.132,.065),(.132,.106),(.142,.114)],
          ['SteelDark','Steel','SteelLight'],n=16)
    for k in range(3):
        z=.114+k*.080
        lathe('BambooBasket_%d'%(k+1),[(.135,z),(.140,z+.008),(.136,z+.065),
                                       (.140,z+.071),(.140,z+.080)],
              ['BambooRim','Bamboo','BambooDark','BambooLight'],alternating=True)
    lathe('BambooLid',[(.143,.354),(.143,.365),(.118,.387),(.038,.400)],
          ['BambooRim','BambooLight','Bamboo'],n=18)
    # Customer side is -Y. Tray sits low, in front of the basket stack.
    rounded_rect('ServingTray',[(.344,.085,.035,.012),(.355,.091,.052,.016),
                              (.337,.074,.052,.012),(.321,.059,.042,.008)],
                 ['Tray','SteelLight','TrayInner'],center=(0,-.128))
    body=join_body(); PARTS=[body]; setup_view(body)
    return body

def dumpling(name, center, length=.064, width=.038, height=.034, angle=0):
    # A closed crescent with a deliberately scalloped pinched top seam.
    # Two sides meet at six alternating peaks; broad belly facets read at game scale.
    profile=[(-.5,0),(-.39,-.40),(-.12,-.52),(.18,-.49),(.42,-.32),
             (.5,0),(.36,.28),(.13,.34),(-.16,.33),(-.39,.23)]
    cx,cy,cz=center; n=len(profile)
    vs=[]
    for j in range(2):
        for x,y in profile:
            xx=x*length*(.76 if j==0 else 1)
            yy=y*width*(.68 if j==0 else 1)
            z=cz+(0 if j==0 else height*.42)
            vs.append((cx+xx*math.cos(angle)-yy*math.sin(angle),
                       cy+xx*math.sin(angle)+yy*math.cos(angle),z))
    # Seam follows the crown from left to right and gives visible folded dough.
    seam=[]
    for i in range(6):
        x=(-.41+.164*i)*length
        y=(.05 if i%2==0 else -.07)*width
        z=cz+height*([.42,.84,.93,1,.84,.42][i])
        seam.append(len(vs)); vs.append((cx+x*math.cos(angle)-y*math.sin(angle),
                                        cy+x*math.sin(angle)+y*math.cos(angle),z))
    faces=[tuple(range(n-1,-1,-1))]; colors=['Dough']
    for i in range(n):
        faces.append((i,(i+1)%n,(i+1)%n+n,i+n)); colors.append('Dough')
    # Stitch belly crown to pinched ridge with a matched five-segment fan each side.
    for i in range(5):
        faces.append((n+i,n+i+1,seam[i+1],seam[i])); colors.append('Dough')
        back=[0,9,8,7,6,5]
        faces.append((n+back[i+1],n+back[i],seam[i],seam[i+1])); colors.append('Pleat' if i%2==0 else 'Dough')
    # Front and rear fans share their end edges and close the crown.
    return mesh_part(name,vs,faces,colors)

def detail():
    global PARTS
    PARTS=[bpy.data.objects['DumplingSteamer_Body']]
    # Compact carry/lift handles on the metal pot, retained inside the footprint.
    for x in (-.149,.149):
        box('PotHandle',(x,.037,.093),(.040,.052,.016),'Burner')
        box('HandleMount',(x*.88,.037,.089),(.020,.040,.018),'SteelDark')
    # Bamboo lid has an honest squared arch rather than a tall generic knob.
    for x in (-.029,.029): box('LidGripUpright',(x,.037,.409),(.011,.018,.029),'BambooDark')
    box('LidGripTop',(0,.037,.425),(.069,.023,.010),'BambooRim')
    # Five closed, broad bamboo slats draped over the faceted lid.
    for dy in (-.064,-.032,0,.032,.064):
        half=math.sqrt(.107**2-dy**2)
        vs=[]
        for i in range(5):
            x=-half+2*half*i/4
            for yy,depth in ((dy-.003,-.002),(dy+.003,-.002),(dy+.003,.0015),(dy-.003,.0015)):
                r=math.sqrt(x*x+yy*yy)
                z=.400-max(0,r-.038)*.013/.080+depth
                vs.append((x,.037+yy,z))
        faces=[(3,2,1,0),(16,17,18,19)]
        faces += [(i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j) for i in range(4) for j in range(4)]
        mesh_part('LidSlat',vs,faces,['BambooLight']*len(faces))
    # Clearly visible control on the forward-left corner of the burner base.
    lathe('GasControl',[(.014,.035),(.014,.048)],['Knob'],n=8,center=(.162,-.069))
    box('ControlMark',(.162,-.069,.049),(.017,.003,.003),'SteelLight')
    # Three pale, hand-folded dumplings in the forward serving tray.
    dumpling('Dumpling_A',(-.103,-.132,.043),angle=-.18)
    dumpling('Dumpling_B',(-.023,-.131,.043),angle=.17)
    dumpling('Dumpling_C',(.055,-.132,.043),angle=-.13)
    # Pair of narrow tapered chopsticks rests diagonally on the right tray lip.
    for offset in (0,.017):
        # Tapered square section, preserving identity transforms on the mesh object.
        a=Vector((.087+offset,-.168,.057)); b=Vector((.166+offset,-.091,.061))
        tangent=(b-a).normalized(); side=tangent.cross(Vector((0,0,1))).normalized()
        up=side.cross(tangent).normalized()
        vs=[tuple(p+side*s*r+up*t*r) for p,r in ((a,.0015),(b,.0026)) for s,t in ((-1,-1),(1,-1),(1,1),(-1,1))]
        faces=[(3,2,1,0),(4,5,6,7)]+[(i,(i+1)%4,(i+1)%4+4,i+4) for i in range(4)]
        mesh_part('Chopstick',vs,faces,['Chopstick']*6)
    body=join_body(); PARTS=[body]; setup_view(body)
    return body

def setup_view(body):
    activate(body)
    q=(Vector((0,0,.190))-Vector((.54,-1.12,.79))).to_track_quat('-Z','Y')
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active
                space.region_3d.view_rotation=q
                space.region_3d.view_location=(0,0,.190)
                space.region_3d.view_distance=.88
                space.clip_start=.001
                space.overlay.show_floor=False
                space.shading.color_type='MATERIAL'
                space.shading.light='STUDIO'
                space.shading.show_shadows=True
                space.shading.show_cavity=True


def preview(filename, textured=False):
    scene=bpy.context.scene
    camera_data=bpy.data.cameras.new('Studio_Camera')
    camera=bpy.data.objects.new('Studio_Camera',camera_data); scene.collection.objects.link(camera)
    camera.location=(.54,-1.12,.79)
    camera.rotation_euler=(Vector((0,0,.190))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.type='ORTHO'; camera_data.ortho_scale=.72; camera_data.clip_start=.01
    scene.camera=camera
    scene.render.engine='BLENDER_WORKBENCH'
    scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    shading=scene.display.shading
    shading.light='STUDIO'; shading.studiolight_rotate_z=.4
    shading.color_type='TEXTURE' if textured else 'MATERIAL'
    shading.show_shadows=True; shading.show_cavity=True
    shading.cavity_type='BOTH'; shading.curvature_ridge_factor=1.2; shading.curvature_valley_factor=1.0
    shading.show_specular_highlight=True; shading.show_object_outline=False
    shading.background_type='WORLD'; scene.world.color=(.035,.045,.060)
    scene.view_settings.view_transform='Standard'
    scene.render.filepath=OUT+'/renders/'+filename
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera,do_unlink=True); bpy.data.cameras.remove(camera_data)
    scene.camera=None
    return scene.render.filepath


def uv_texture():
    body=bpy.data.objects['DumplingSteamer_Body']; activate(body)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.004,margin_method='FRACTION',scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh=body.data; mesh.uv_layers.active.name='UVMap'
    mesh.calc_loop_triangles(); uv=mesh.uv_layers.active.data
    # Rasterise flat per-face palette into the atlas, with padding for filtered sampling.
    pix=np.zeros((512,512,4),dtype=np.float32); pix[:,:,:3]=(.20,.24,.26); pix[:,:,3]=1
    mask=np.zeros((512,512),bool)
    for tri in mesh.loop_triangles:
        coords=np.array([uv[l].uv[:] for l in tri.loops])*512
        a,b,c=coords
        xmin=max(0,int(np.floor(coords[:,0].min()))); xmax=min(511,int(np.ceil(coords[:,0].max())))
        ymin=max(0,int(np.floor(coords[:,1].min()))); ymax=min(511,int(np.ceil(coords[:,1].max())))
        if xmax<xmin or ymax<ymin: continue
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1)+.5,np.arange(ymin,ymax+1)+.5)
        det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(det)<1e-10: continue
        l1=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/det
        l2=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/det
        inside=(l1>=-1e-6)&(l2>=-1e-6)&(1-l1-l2>=-1e-6)
        mat=mesh.materials[mesh.polygons[tri.polygon_index].material_index]
        color=PALETTE[mat.name.removeprefix('Build_')]
        pix[ymin:ymax+1,xmin:xmax+1,:3][inside]=color
        mask[ymin:ymax+1,xmin:xmax+1][inside]=True
    for _ in range(3):
        previous=mask.copy(); newpix=pix.copy()
        for dy,dx in ((-1,0),(1,0),(0,-1),(0,1)):
            shifted=np.roll(previous,(dy,dx),(0,1)); valid=shifted & ~mask
            if dy<0: valid[-1,:]=False
            if dy>0: valid[0,:]=False
            if dx<0: valid[:,-1]=False
            if dx>0: valid[:,0]=False
            newpix[valid]=np.roll(pix,(dy,dx),(0,1))[valid]; mask|=valid
        pix=newpix
    image=bpy.data.images.get('T_DumplingSteamer_BaseColor.png')
    if image: bpy.data.images.remove(image)
    image=bpy.data.images.new('T_DumplingSteamer_BaseColor.png',width=512,height=512,alpha=True)
    image.colorspace_settings.name='sRGB'
    image.pixels.foreach_set(pix.ravel()); image.update()
    image.filepath_raw=OUT+'/T_DumplingSteamer_BaseColor.png'; image.file_format='PNG'; image.save()
    image.filepath=OUT+'/T_DumplingSteamer_BaseColor.png'
    mat=bpy.data.materials.get('M_DumplingSteamer') or bpy.data.materials.new('M_DumplingSteamer')
    mat.use_nodes=True; mat.node_tree.nodes.clear()
    nodes=mat.node_tree.nodes
    tex=nodes.new('ShaderNodeTexImage'); tex.image=image; tex.interpolation='Linear'; nodes.active=tex
    shader=nodes.new('ShaderNodeBsdfPrincipled'); shader.inputs['Roughness'].default_value=.63
    output=nodes.new('ShaderNodeOutputMaterial')
    tex.location=(-350,80); shader.location=(0,80); output.location=(300,80)
    mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    mat.node_tree.links.new(shader.outputs['BSDF'],output.inputs['Surface'])
    mesh.materials.clear(); mesh.materials.append(mat)
    for face in mesh.polygons: face.material_index=0; face.use_smooth=False
    # Remove now-unused temporary palette materials from the final scene.
    for m in list(bpy.data.materials):
        if m.name.startswith('Build_') and m.users==0: bpy.data.materials.remove(m)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D': area.spaces.active.shading.color_type='TEXTURE'
    return body


def collision_export():
    body=bpy.data.objects['DumplingSteamer_Body']
    old=bpy.data.objects.get('DumplingSteamer_Collision')
    if old: bpy.data.objects.remove(old,do_unlink=True)
    bm=bmesh.new()
    # Broad lower envelope encloses the base and tray; upper octagon encloses the stack.
    # 24 support points produce fewer than 60 triangles; hull construction is explicit.
    for z,w,d,cy,clip in ((0,.384,.354,0,.026),(.077,.384,.357,.0015,.026),(.430,.286,.286,.037,.083)):
        x,y=w/2,d/2
        for a,b in ((-x+clip,-y),(x-clip,-y),(x,-y+clip),(x,y-clip),(x-clip,y),(-x+clip,y),(-x,y-clip),(-x,-y+clip)):
            bm.verts.new((a,b+cy,z))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.normal_update()
    outside=max((v.co-f.verts[0].co).dot(f.normal) for v in body.data.vertices for f in bm.faces)
    assert outside < 1e-6, 'Collision hull does not fully enclose the body'
    assert len(bm.faces) <= 60, 'Collision hull exceeds the triangle budget'
    mesh=bpy.data.meshes.new('DumplingSteamer_ConvexHull'); bm.to_mesh(mesh); bm.free()
    col=bpy.data.objects.new('DumplingSteamer_Collision',mesh); bpy.context.collection.objects.link(col)
    col.hide_render=True; col.display_type='WIRE'; col['construction']='bmesh.ops.convex_hull'
    activate(body); col.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT+'/DumplingSteamer.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    col.hide_set(True); activate(body)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/DumplingSteamer.blend')
    return {'body_triangles':len(body.data.polygons),'collision_triangles':len(col.data.polygons)}


def build(stage='all'):
    os.makedirs(OUT+'/renders',exist_ok=True)
    if stage in ('blockout','all'):
        blockout(); preview('01-blockout.png')
    if stage in ('detail','all'):
        detail(); preview('02-detail.png')
    if stage in ('texture','all'):
        uv_texture(); preview('03-uvs-texture.png',textured=True)
    if stage in ('final','all'):
        preview('04-final.png',textured=True)
        return collision_export()

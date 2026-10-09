"""Run in the existing metric start scene; BUILD_STAGE=1..4 selects a review step.

Default rebuild produces the final texture, review render, .blend and exact FBX.
The HumanRef is never changed. All output remains in this asset's directory.
"""
import bpy, bmesh, math, random, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
STAGE = globals().get('BUILD_STAGE', 4)
os.makedirs(OUT + '/renders', exist_ok=True)
PALETTE = {
    'crust': (0.72, 0.36, 0.105, 1),
    'crust_light': (0.89, 0.53, 0.205, 1),
    'crumb': (0.98, 0.77, 0.42, 1),
    'score': (1.0, 0.83, 0.52, 1),
    'beef': (0.26, 0.115, 0.055, 1),
    'beef_light': (0.41, 0.205, 0.085, 1),
    'beef_dark': (0.18, 0.075, 0.038, 1),
    'cheese': (1.0, 0.66, 0.055, 1),
    'cheese_light': (1.0, 0.79, 0.13, 1),
    'lettuce': (0.24, 0.52, 0.075, 1),
    'lettuce_light': (0.43, 0.72, 0.13, 1),
    'tomato': (0.82, 0.09, 0.035, 1),
    'tomato_flesh': (1.0, 0.26, 0.105, 1),
    'paper': (0.91, 0.905, 0.85, 1),
    'paper_light': (0.99, 0.985, 0.945, 1),
    'paper_fold': (0.72, 0.745, 0.70, 1),
}

verts, faces, colors, parts = [], [], [], []
def face(indices, color, part):
    faces.append(tuple(indices)); colors.append(color); parts.append(part)

def append_mesh(vs, fs, color, part):
    off = len(verts); verts.extend(vs)
    for i, f in enumerate(fs):
        face([off+j for j in f], color[i] if isinstance(color,list) else color, part)

def rings(xs, profile, part, side_colors, yshift=0, zcenter=None):
    """Closed eight/six-sided roll or filling, rounded through six X stations."""
    vs=[]; fs=[]; cs=[]; n=len(profile)
    zc = zcenter if zcenter is not None else sum(z for y,z in profile)/n
    for x,s in xs:
        vs.extend((x, y*s+yshift, zc+(z-zc)*s) for y,z in profile)
    for i in range(len(xs)-1):
        for j in range(n):
            fs.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
            cs.append(side_colors[j])
    for k in range(1,n-1):
        fs.append((0,k+1,k)); cs.append(side_colors[0])
        q=(len(xs)-1)*n
        fs.append((q,q+k,q+k+1)); cs.append(side_colors[0])
    append_mesh(vs,fs,cs,part)

def prism(outline, thickness, color, part, side_color=None, simple_caps=False):
    """Closed thin polygon, with triangulated top/bottom and quad walls."""
    n=len(outline); vs=list(outline)+[(x,y,z-thickness) for x,y,z in outline]
    fs=[]; cs=[]
    # Fan from a centre supports concave outlines that are star-shaped.
    cx=sum(p[0] for p in outline)/n; cy=sum(p[1] for p in outline)/n; cz=sum(p[2] for p in outline)/n
    if not simple_caps: vs.extend([(cx,cy,cz),(cx,cy,cz-thickness)])
    for i in range(n):
        j=(i+1)%n
        if not simple_caps:
            fs.extend([(2*n,i,j),(2*n+1,n+j,n+i)])
            cs.extend([color,side_color or color])
        fs.append((i,n+i,n+j,j)); cs.append(side_color or color)
    if simple_caps:
        for i in range(1,n-1):
            fs.extend([(0,i,i+1),(n,n+i+1,n+i)])
            cs.extend([color,side_color or color])
    append_mesh(vs,fs,cs,part)

def ribbon(xs, back_y, front_ys, zs, drops, color, part):
    """Solid draped ribbon: inner edge, outer lip, drooping edge, rear closure."""
    profiles=[]
    for x,fy,z,d in zip(xs,front_ys,zs,drops):
        profiles.append([(x,back_y,z), (x,fy,z-0.001), (x,fy+0.001,z-d), (x,back_y,z-0.004)])
    vs=[v for p in profiles for v in p]; fs=[]; cs=[]
    for i in range(len(xs)-1):
        for j in range(4):
            fs.append((i*4+j,i*4+(j+1)%4,(i+1)*4+(j+1)%4,(i+1)*4+j))
            cs.append('cheese_light' if color=='cheese' and j==0 else color)
    fs.extend([(3,2,1,0),(4*(len(xs)-1),4*(len(xs)-1)+1,4*(len(xs)-1)+2,4*(len(xs)-1)+3)])
    cs.extend([color,color]); append_mesh(vs,fs,cs,part)

def geometry():
    roll_x=[(-.100,.27),(-.088,.78),(-.064,1),(.063,1),(.087,.78),(.100,.27)]
    rings([(-.100,.27),(-.081,1),(.081,1),(.100,.27)],
          [(-.027,.010),(-.038,.022),(-.033,.039),(.033,.039),(.038,.022),(.027,.010)],
          'bottom_roll',['crust','crust_light','crumb','crust_light','crust','crust'])
    rings(roll_x,[(-.031,.066),(-.037,.078),(-.027,.096),(0,.104),(.028,.096),(.037,.078),(.031,.066),(0,.065)],
          'top_roll',['crust_light','crust_light','crust_light','crust_light','crust','crust','crumb','crumb'],yshift=.009,zcenter=.078)
    prism([(-.088,-.015,.054),(-.073,-.035,.056),(.073,-.035,.056),(.088,-.015,.054),
           (.088,.015,.054),(.073,.030,.055),(-.073,.030,.055),(-.088,.015,.054)],
          .016,'beef','chopped_beef_bed','beef_dark',simple_caps=True)
    # Paper sleeve: flat octagonal underside contains (0,0,0).
    outline=[(-.102,-.024),(-.085,-.043),(.085,-.043),(.102,-.024),(.102,.024),(.085,.043),(-.085,.043),(-.102,.024)]
    vs=[(x*.96,y*.93,0) for x,y in outline]
    vs += [(x,y,.017) for x,y in outline]
    rim=[.034,.028,.027,.035,.038,.035,.036,.038]
    vs += [(x*.985,y*.97,z) for (x,y),z in zip(outline,rim)]
    fs=[]; cs=[]
    for row in range(2):
        for j in range(8):
            a=row*8+j; b=row*8+(j+1)%8; c=b+8; d=a+8
            # The large customer-facing side is divided into broad diagonal folds.
            if row==1 and j in (0,1,2,6,7):
                fs.extend([(a,b,c),(a,c,d)])
                cs.extend(['paper_light','paper' if j==1 else 'paper_fold'])
            else:
                fs.append((a,b,c,d)); cs.append('paper' if row==0 else 'paper_light')
    for j in range(1,7):
        fs.extend([(0,j+1,j),(16,16+j,17+j)]); cs.extend(['paper','paper'])
    append_mesh(vs,fs,cs,'deli_paper')
    if STAGE < 2: return
    rng=random.Random(11)
    # Nine large chopped beef pieces, varying shape and height; no micro-detail.
    for i in range(9):
        x=-.076+i*.019; y=-.035+rng.uniform(-.004,.003); z=.048+rng.uniform(-.003,.005)
        dx=rng.uniform(.006,.011); dy=rng.uniform(.007,.011); dz=rng.uniform(.005,.008)
        angle=rng.uniform(-.75,.75)
        vs=[]
        for zz in (-dz,dz):
            for xx,yy in [(-dx,-dy),(dx,-dy),(dx,dy),(-dx,dy)]:
                xx += rng.uniform(-.0025,.0025); yy += rng.uniform(-.0025,.0025)
                vs.append((x+xx*math.cos(angle)-yy*math.sin(angle),y+xx*math.sin(angle)+yy*math.cos(angle),z+zz+rng.uniform(-.002,.002)))
        append_mesh(vs,[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],
                    ['beef_dark','beef','beef_light','beef','beef','beef_light' if i%3 else 'beef'], 'beef_chunks')
    # Melted American cheese drapes over the chopped beef in three connected-looking sheets.
    ribbon([-.087,-.065,-.047,-.025], .020,[-.035,-.046,-.047,-.043],
           [.059,.061,.061,.060],[.004,.008,.014,.006],'cheese','melted_cheese')
    ribbon([-.028,-.009,.023,.040], .018,[-.044,-.047,-.049,-.043],
           [.060,.060,.061,.060],[.005,.012,.015,.004],'cheese','melted_cheese')
    ribbon([.037,.055,.073,.088], .018,[-.043,-.045,-.043,-.031],
           [.060,.059,.060,.059],[.006,.009,.013,.004],'cheese','melted_cheese')
    # Frilled lettuce outline, thickened into a closed leaf for export.
    leaf=[(-.084,-.009,.064),(-.066,-.046,.065),(-.052,-.040,.066),
          (-.039,-.049,.063),(-.024,-.042,.066),(-.008,-.051,.063),(.007,-.042,.067),
          (.024,-.048,.064),(.040,-.039,.066),(.056,-.046,.063),(.073,-.036,.066),
          (.083,-.016,.064),(.057,.018,.065)]
    prism(leaf,.0025,'lettuce_light','lettuce_frill','lettuce')
    # Three tomato slices lie diagonally in the split, visible on the customer side.
    for i,x in enumerate([-.054,.0,.055]):
        outline=[]
        for j in range(8):
            a=2*math.pi*j/8
            xx=.018*math.cos(a); yy=.019*math.sin(a)
            outline.append((x+xx,-.023+yy,.069+yy*.22))
        prism(outline,.004,'tomato_flesh','tomato_slices','tomato',simple_caps=True)
    # Broad pale baked slashes, conforming to the faceted dome; six triangles each.
    for x in [-.045,0,.045]:
        outline=[(x-.005,-.016,.0970),(x+.002,-.016,.0970),(x+.010,.009,.1045),
                 (x+.009,.025,.0999),(x+.003,.025,.0999),(x-.004,.009,.1045)]
        vs=outline+[(xx,yy,zz-.0006) for xx,yy,zz in outline]
        fs=[(0,1,2,5),(5,2,3,4),(11,8,7,6),(10,9,8,11)]
        fs.extend((i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6))
        append_mesh(vs,fs,'score','bread_slashes')

def normals(mesh):
    bm=bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free(); mesh.update()

def select_only(objects):
    if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects: ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]

def make_body():
    geometry()
    me=bpy.data.meshes.new('ChoppedCheese_Mesh'); me.from_pydata(verts,[],faces); me.update()
    ob=bpy.data.objects.new('ChoppedCheese_Body',me); bpy.context.collection.objects.link(ob)
    normals(me)
    # Review stages use temporary swatches; final uses precisely one texture material.
    for key,color in PALETTE.items():
        m=bpy.data.materials.new('Preview_'+key); m.diffuse_color=color; me.materials.append(m)
    keys=list(PALETTE)
    for p,c in zip(me.polygons,colors): p.material_index=keys.index(c); p.use_smooth=False
    ob['front']='-Y'; ob['length_m']=.204
    ob['ingredients']='Hero roll, chopped beef, melted American cheese, lettuce, tomato, white deli paper'
    if STAGE>=3: atlas(ob)
    return ob

def atlas(ob):
    me=ob.data; uv=me.uv_layers.new(name='UVMap')
    count=len(me.polygons); cols=math.ceil(math.sqrt(count)); rows=math.ceil(count/cols)
    # Each original quad is a rectangular island; triangles occupy half a cell.
    # Texture cells have solid flat colours and full-cell bleed around UV gutters.
    pixels=np.ones((256,256,4),dtype=np.float32)
    pixels[:,:,:3]=(.9,.88,.8)
    for i,(poly,color) in enumerate(zip(me.polygons,colors)):
        c=i%cols; r=i//cols
        left=c/cols; right=(c+1)/cols; bottom=r/rows; top=(r+1)/rows
        pixels[math.floor(bottom*256):math.ceil(top*256),math.floor(left*256):math.ceil(right*256)]=PALETTE[color]
        gutter=.09
        u0=(c+gutter)/cols; u1=(c+1-gutter)/cols
        v0=(r+gutter)/rows; v1=(r+1-gutter)/rows
        coords=[(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
        for li,coord in zip(poly.loop_indices,coords): uv.data[li].uv=coord
    img=bpy.data.images.new('T_ChoppedCheese_BaseColor',256,256,alpha=True)
    img.pixels.foreach_set(pixels.ravel()); img.filepath_raw=OUT+'/T_ChoppedCheese_BaseColor.png'; img.file_format='PNG'; img.save()
    img.filepath='//T_ChoppedCheese_BaseColor.png'
    mat=bpy.data.materials.new('M_ChoppedCheese'); mat.use_nodes=True
    bsdf=mat.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=.85
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=img; tex.interpolation='Closest'; tex.extension='EXTEND'
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.nodes.active=tex
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons: p.material_index=0
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='FIXED'); bm.to_mesh(me); bm.free()
    # Remove unused preview materials, keeping the saved scene small and unambiguous.
    for m in list(bpy.data.materials):
        if m.name.startswith('Preview_') and m.users==0: bpy.data.materials.remove(m)

def collision(body):
    # A 12-point beveled prism conservatively encloses every visible vertex.
    # Convex hull is generated from points, not a hand-authored face shell.
    bm=bmesh.new()
    footprint=[(-.108,-.027),(-.089,-.055),(.089,-.055),(.108,-.027),(.108,.052),(-.108,.052)]
    for z in [0,.108]:
        for x,y in footprint: bm.verts.new((x,y,z))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused: bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('ChoppedCheese_Hull'); bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new('ChoppedCheese_Collision',me); bpy.context.collection.objects.link(ob)
    ob.hide_render=True; ob.display_type='WIRE'; ob['construction']='bmesh.ops.convex_hull'; return ob

def review(path):
    scene=bpy.context.scene
    scene.render.engine='BLENDER_WORKBENCH'; scene.display.render_aa='32'
    shade=scene.display.shading; shade.light='STUDIO'; shade.studio_light='paint.sl'
    shade.color_type='TEXTURE' if STAGE>=3 else 'MATERIAL'
    shade.show_shadows=True; shade.show_cavity=True; shade.cavity_type='WORLD'
    shade.cavity_ridge_factor=1.1; shade.cavity_valley_factor=1.1
    shade.show_specular_highlight=False; shade.show_object_outline=False
    shade.background_type='WORLD'; scene.world.color=(.12,.14,.16)
    scene.view_settings.view_transform='Standard'
    scene.display.matcap_ssao_distance=.015
    scene.display.matcap_ssao_samples=64
    cam_data=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',cam_data)
    scene.collection.objects.link(cam)
    target=Vector((0,0,.05)); cam.location=(.18,-.40,.245)
    cam.rotation_euler=(target-Vector(cam.location)).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'; cam.data.ortho_scale=.29; cam.data.clip_start=.001
    scene.camera=cam
    scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.image_settings.color_mode='RGBA'
    scene.render.filepath=path; bpy.ops.render.render(write_still=True)
    # Preserve only the requested mesh objects and the untouched reference.
    scene.camera=None; bpy.data.objects.remove(cam,do_unlink=True); bpy.data.cameras.remove(cam_data)
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active; space.overlay.show_overlays=False
                space.shading.type='SOLID'; space.shading.color_type=shade.color_type
                space.shading.light='STUDIO'; space.shading.studio_light='paint.sl'
                space.shading.show_cavity=True; space.shading.cavity_type='WORLD'
                space.region_3d.view_rotation=(Vector((.18,-.40,.195))).to_track_quat('Z','Y')
                space.region_3d.view_distance=.34; space.region_3d.view_location=target
                space.region_3d.view_perspective='ORTHO'

def export(body,hull):
    select_only([body,hull])
    bpy.ops.export_scene.fbx(filepath=OUT+'/ChoppedCheese.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    hull.hide_set(True); select_only([body])
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/ChoppedCheese.blend')

if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
for name in ['ChoppedCheese_Body','ChoppedCheese_Collision']:
    ob=bpy.data.objects.get(name)
    if ob:
        me=ob.data; bpy.data.objects.remove(ob,do_unlink=True)
        if me.users==0: bpy.data.meshes.remove(me)
for img in list(bpy.data.images):
    if img.name.startswith('T_ChoppedCheese_BaseColor'): bpy.data.images.remove(img)
for mat in list(bpy.data.materials):
    if (mat.name.startswith('M_ChoppedCheese') or mat.name.startswith('Preview_')) and mat.users==0: bpy.data.materials.remove(mat)
body=make_body()
stage_names={1:'01-blockout.png',2:'02-detail.png',3:'03-texture.png',4:'04-final.png'}
hull=collision(body) if STAGE>=4 else None
select_only([body]); review(OUT+'/renders/'+stage_names[STAGE])
if STAGE>=4: export(body,hull)
else: bpy.ops.wm.save_as_mainfile(filepath=OUT+'/ChoppedCheese.blend')
result={'stage':STAGE,'body_triangles':sum(len(p.vertices)-2 for p in body.data.polygons),
        'hull_triangles':len(hull.data.polygons) if hull else None,'render':stage_names[STAGE]}

"""Regenerate in the open metric start scene; all output stays beside this file.

Run in Blender: exec(compile(open(PATH).read(), PATH, 'exec')); main()
The four review stages can also be called individually.
"""
import bpy
import bmesh
import math
import os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
PALETTE = {
    'White': (0.87, 0.90, 0.91, 1),
    'Steel': (0.48, 0.56, 0.61, 1),
    'SteelLight': (0.70, 0.77, 0.80, 1),
    'Shadow': (0.075, 0.095, 0.11, 1),
    'Red': (0.66, 0.025, 0.034, 1),
    'Vanilla': (0.98, 0.86, 0.57, 1),
    'Strawberry': (0.96, 0.36, 0.46, 1),
    'Chocolate': (0.30, 0.12, 0.06, 1),
    'Mint': (0.29, 0.77, 0.52, 1),
}
COLLECTION = 'IceCreamCabinet_Asset'

def collection():
    c = bpy.data.collections.get(COLLECTION)
    if c is None:
        c = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(c)
    return c

def select_only(obs):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs:
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]

def material(key):
    name = 'IC_Preview_' + key
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = PALETTE[key]
    return m

def mesh_part(name, verts, faces, key):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts)>4])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    collection().objects.link(ob)
    me.materials.append(material(key))
    return ob

def box(name, center, dims, key, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    ob = bpy.context.object
    ob.name = name
    ob.dimensions = dims
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    collection().objects.link(ob)
    ob.data.materials.append(material(key))
    if bevel:
        mod = ob.modifiers.new('Small edge chamfer', 'BEVEL')
        # Keep thin sheet chamfers below half the thickness to avoid collapsed faces.
        mod.width = min(bevel, min(dims)*.24)
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts)>4])
        bm.to_mesh(ob.data)
        bm.free()
    return ob

def rr(w, d, r):
    # Eight corners, counterclockwise in XY.
    return [(-w/2+r,-d/2),(w/2-r,-d/2),(w/2,-d/2+r),
            (w/2,d/2-r),(w/2-r,d/2),(-w/2+r,d/2),
            (-w/2,d/2-r),(-w/2,-d/2+r)]

def ring(name, x, y, loops, key):
    verts = [(x+u,y+v,z) for w,d,r,z in loops for u,v in rr(w,d,r)]
    n = 8
    faces = []
    for k in range(len(loops)):
        nxt = (k+1)%len(loops)
        for j in range(n):
            faces.append((k*n+j,k*n+(j+1)%n,nxt*n+(j+1)%n,nxt*n+j))
    return mesh_part(name, verts, faces, key)

def hood_side(x):
    points = [Vector((-.159+.306*(1-math.cos(i*math.pi/12)),
                      .289+.183*math.sin(i*math.pi/12))) for i in range(7)]
    verts=[]
    for i,p in enumerate(points):
        tangent = (points[min(i+1,6)]-points[max(i-1,0)]).normalized()
        normal = Vector((-tangent.y,tangent.x)) * .0065
        for dx, sg in [(-.007,-1),(.007,-1),(.007,1),(-.007,1)]:
            yz=p+sg*normal
            verts.append((x+dx,yz.x,yz.y))
    faces=[(3,2,1,0),(24,25,26,27)]
    for k in range(6):
        for j in range(4):
            faces.append((k*4+j,k*4+(j+1)%4,(k+1)*4+(j+1)%4,(k+1)*4+j))
    return mesh_part('Hood_CurvedRib', verts, faces, 'Red')

def review(filename, textured=False):
    """Temporary camera: never saved or exported, leaves reference untouched."""
    scene=bpy.context.scene
    scene.render.engine='BLENDER_WORKBENCH'
    scene.render.resolution_x=1024
    scene.render.resolution_y=1024
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.film_transparent=False
    shading=scene.display.shading
    shading.light='STUDIO'
    shading.studiolight_rotate_z=.3
    shading.color_type='TEXTURE' if textured else 'MATERIAL'
    shading.show_shadows=True
    shading.show_cavity=True
    shading.cavity_type='BOTH'
    shading.curvature_ridge_factor=1.15
    shading.curvature_valley_factor=.8
    shading.cavity_ridge_factor=1.15
    shading.cavity_valley_factor=.9
    shading.show_specular_highlight=True
    shading.background_type='WORLD'
    if scene.world is None:
        scene.world=bpy.data.worlds.new('IceCreamCabinet_ReviewWorld')
    scene.world.color=(.12,.145,.17)
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='None'
    scene.view_settings.exposure=0
    scene.view_settings.gamma=1
    cam_data=bpy.data.cameras.new('IC_ReviewCamera')
    cam=bpy.data.objects.new('IC_ReviewCamera',cam_data)
    scene.collection.objects.link(cam)
    target=Vector((0,0,.237))
    eye=Vector((.68,-1.12,.92))
    cam.location=eye
    cam.rotation_euler=(target-eye).to_track_quat('-Z','Y').to_euler()
    cam_data.type='ORTHO'
    cam_data.ortho_scale=.72
    old_camera=scene.camera
    scene.camera=cam
    os.makedirs(os.path.join(OUT,'renders'),exist_ok=True)
    scene.render.filepath=os.path.join(OUT,'renders',filename)
    bpy.context.view_layer.update()
    bpy.ops.render.render(write_still=True)
    scene.camera=old_camera
    bpy.data.objects.remove(cam,do_unlink=True)
    bpy.data.cameras.remove(cam_data)
    for a in bpy.context.screen.areas if bpy.context.screen else []:
        if a.type=='VIEW_3D':
            a.spaces.active.overlay.show_overlays=False
            a.spaces.active.region_3d.view_location=target
            a.spaces.active.region_3d.view_rotation=(eye-target).to_track_quat('Z','Y')
            a.spaces.active.region_3d.view_distance=.9
            a.spaces.active.region_3d.view_perspective='ORTHO'
            a.spaces.active.shading.color_type=shading.color_type
    return scene.render.filepath

def blockout():
    # Only the asset collection is rebuilt. HumanRef_1.8m is never modified.
    for ob in list(collection().objects):
        bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1
    box('Cabinet_WhiteShell',(0,0,.131),(.392,.344,.262),'White',.004)
    box('Cabinet_Plinth',(0,0,.009),(.390,.340,.018),'Steel',.002)
    # Chamfered steel counter ring, opening under the four trays.
    ring('Well_CounterLip',0,0,[(.398,.352,.009,.255),(.398,.352,.009,.272),
                                (.360,.280,.008,.272),(.360,.280,.008,.255)],'SteelLight')
    box('Well_DarkFloor',(0,0,.263),(.36,.280,.009),'Shadow')
    for x in (-.184,.184):
        hood_side(x)
        box('Hood_RearUpright',(x,.147,.365),(.014,.014,.214),'Red')
    box('Hood_TopRail',(0,.147,.472),(.382,.016,.015),'Red')
    box('Hood_FrontRail',(0,-.159,.283),(.388,.016,.020),'Red')
    box('Hood_RearBase',(0,.147,.277),(.38,.016,.020),'Red')
    # Temporary simple coloured fills establish the four-flavour reading.
    for x,key in zip((.132,.044,-.044,-.132),('Vanilla','Strawberry','Chocolate','Mint')):
        box('Blockout_'+key,(x,-.005,.285),(.074,.228,.030),key,.004)
    bpy.context.view_layer.update()
    return review('01-blockout.png')

def ice_fill(x,key):
    # Three low ridges along a single closed ice cream fill, not separate scoops.
    w,d=.069,.219
    nx,ny=5,9
    verts=[]
    for j in range(ny):
        v=-d/2+d*j/(ny-1)
        for i in range(nx):
            u=-w/2+w*i/(nx-1)
            edge=math.sin(math.pi*i/(nx-1))
            mound=(.006+.006*(.5+.5*math.cos(v/d*6*math.pi)))
            z=.281+edge*mound*(.45+.55*math.sin(math.pi*j/(ny-1)))
            verts.append((x+u,-.005+v,z))
    faces=[]
    for j in range(ny-1):
        for i in range(nx-1):
            a=j*nx+i
            faces.append((a,a+1,a+1+nx,a+nx))
    boundary=list(range(nx))+[j*nx+nx-1 for j in range(1,ny)]+list(range((ny-1)*nx+nx-2,(ny-1)*nx-1,-1))+[j*nx for j in range(ny-2,0,-1)]
    base=[]
    for i in boundary:
        p=verts[i]
        base.append(len(verts))
        verts.append((p[0],p[1],.269))
    for i in range(len(boundary)):
        j=(i+1)%len(boundary)
        faces.append((boundary[i],base[i],base[j],boundary[j]))
    # Bottom fan has outward normals corrected with the rest of the closed part.
    center=len(verts)
    verts.append((x,-.005,.269))
    for i in range(len(base)):
        faces.append((base[i],center,base[(i+1)%len(base)]))
    return mesh_part('IceCream_'+key,verts,faces,key)

def details():
    for ob in list(collection().objects):
        if ob.name.startswith('Blockout_'):
            bpy.data.objects.remove(ob,do_unlink=True)
    # Reference's broad clean front and four steel strips; no lettering.
    for z in (.050,.080,.110,.140):
        box('Front_DarkStripe',(0,-.1735,z),(.369,.003,.012),'Shadow')
        box('Front_SteelStripe',(0,-.176,z+.002),(.369,.004,.006),'SteelLight')
    box('Front_UpperTrim',(0,-.173,.235),(.377,.004,.008),'Steel')
    # A shallow rear cold-wall, below the display sightline.
    box('Well_RearColdWall',(0,.132,.300),(.354,.013,.066),'Steel')
    for z in (.293,.306,.319):
        box('Well_RearGroove',(0,.1246,z),(.346,.002,.003),'Shadow')
    for x,key in zip((.132,.044,-.044,-.132),('Vanilla','Strawberry','Chocolate','Mint')):
        # Continuous steel folded rim with four loops, no hidden open boundaries.
        ring('Tub_'+key,x,-.005,[(.080,.239,.008,.266),(.080,.239,.008,.286),
                               (.070,.221,.006,.286),(.070,.221,.006,.266)],'SteelLight')
        ice_fill(x,key)
    # An unobtrusive operator-side inset with grip, above the rear ventilation.
    box('Rear_Panel',(0,.173,.155),(.318,.003,.147),'SteelLight',.002)
    box('Rear_Grip',(0,.177,.200),(.084,.006,.011),'Shadow',.002)
    for z in (.074,.091,.108):
        box('Rear_Vent',(0,.175,z),(.271,.004,.006),'Shadow')
    parts=list(collection().objects)
    select_only(parts)
    bpy.ops.object.join()
    body=bpy.context.object
    body.name='IceCreamCabinet_Body'
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    body['front_direction']='-Y'
    body['flavour_order_customer_left_to_right']='vanilla, strawberry, chocolate, mint'
    body['glass_representation']='Red frame only; intentionally empty glazing'
    body.data.calc_loop_triangles()
    assert len(body.data.loop_triangles)<=1500, len(body.data.loop_triangles)
    return review('02-detail.png')

def texture():
    body=bpy.data.objects['IceCreamCabinet_Body']
    select_only([body])
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.008,
                             area_weight=.2,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    me=body.data
    me.uv_layers.active.name='UVMap'
    me.calc_loop_triangles()
    # Rasterise the actual packed UVs into one flat-colour atlas, then pad seams.
    res=512
    pixels=np.zeros((res,res,4),dtype=np.float32)
    pixels[:,:,:]=(.25,.28,.30,1)
    mask=np.zeros((res,res),dtype=bool)
    uv=me.uv_layers.active.data
    for tri in me.loop_triangles:
        a,b,c=np.array([tuple(uv[i].uv) for i in tri.loops])*res
        x0=max(int(math.floor(min(a[0],b[0],c[0]))),0)
        x1=min(int(math.ceil(max(a[0],b[0],c[0]))),res-1)
        y0=max(int(math.floor(min(a[1],b[1],c[1]))),0)
        y1=min(int(math.ceil(max(a[1],b[1],c[1]))),res-1)
        denom=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(denom)<1e-10:
            continue
        xs,ys=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
        p=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/denom
        q=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/denom
        covered=(p>=-1e-6)&(q>=-1e-6)&(p+q<=1+1e-6)
        key=me.materials[me.polygons[tri.polygon_index].material_index].name.replace('IC_Preview_','')
        # Palette numbers are display/sRGB colors; write those values into PNG.
        pixels[y0:y1+1,x0:x1+1][covered]=PALETTE[key]
        mask[y0:y1+1,x0:x1+1]|=covered
    fill=float(mask.mean())
    assert fill>=.4, fill
    for _ in range(3):
        old=mask.copy()
        for dy,dx in ((0,1),(0,-1),(1,0),(-1,0)):
            shifted=np.roll(old,(dy,dx),(0,1))
            take=shifted & ~mask
            if dy==1: take[0,:]=False
            if dy==-1: take[-1,:]=False
            if dx==1: take[:,0]=False
            if dx==-1: take[:,-1]=False
            pixels[take]=np.roll(pixels,(dy,dx),(0,1))[take]
            mask[take]=True
    name='T_IceCreamCabinet_BaseColor'
    img=bpy.data.images.get(name) or bpy.data.images.new(name,width=res,height=res,alpha=False)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel())
    img.filepath_raw=os.path.join(OUT,name+'.png')
    img.file_format='PNG'
    img.save()
    # Keep the absolute image path until save_as remaps from art/start.blend.
    img.filepath=os.path.join(OUT,name+'.png')
    mat=bpy.data.materials.get('M_IceCreamCabinet') or bpy.data.materials.new('M_IceCreamCabinet')
    mat.use_nodes=True
    mat.node_tree.nodes.clear()
    output=mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    bsdf=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value=.7
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.name='BaseColor'
    tex.image=img
    tex.interpolation='Closest'
    mat.node_tree.nodes.active=tex
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index=0
        p.use_smooth=False
    body['texture_coverage_raster_512']=fill
    # Remove the unused temporary color materials from the delivered scene.
    for m in list(bpy.data.materials):
        if m.name.startswith('IC_Preview_') and m.users==0:
            bpy.data.materials.remove(m)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'IceCreamCabinet.blend'))
    img.filepath='//'+name+'.png'
    img.reload()
    return {'review':review('03-texture.png',True),'coverage_512':fill}

def finish():
    body=bpy.data.objects['IceCreamCabinet_Body']
    # Review before FBX export mutates evaluation/visibility caches.
    review('04-final.png',True)
    existing=bpy.data.objects.get('IceCreamCabinet_Collision')
    if existing:
        bpy.data.objects.remove(existing,do_unlink=True)
    # Envelope follows the curved hood in six segments; 20 hull vertices / 36 tris.
    # Slightly larger than the visible frame, still inside the specified footprint.
    points=[(-.18,0),(.18,0),(.18,.480)]
    for i in range(6,-1,-1):
        theta=i*math.pi/12
        points.append((-.166+.315*(1-math.cos(theta)),.294+.190*math.sin(theta)))
    points.append((-.18,.276))
    bm=bmesh.new()
    for x in (-.20,.20):
        for y,z in points:
            bm.verts.new((x,y,min(z,.49)))
    hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    unused=[v for v in bm.verts if not v.link_faces]
    if unused:
        bmesh.ops.delete(bm,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('IceCreamCabinet_Collision')
    bm.to_mesh(me)
    bm.free()
    collision=bpy.data.objects.new('IceCreamCabinet_Collision',me)
    collection().objects.link(collision)
    collision.hide_render=True
    collision.display_type='WIRE'
    collision['construction']='bmesh.ops.convex_hull'
    collision['purpose']='Convex equipment collision hull'
    select_only([body,collision])
    bpy.ops.export_scene.fbx(filepath=OUT+'/IceCreamCabinet.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    select_only([body])
    # Only the mesh objects (and the untouched human reference) remain in the scene.
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/IceCreamCabinet.blend')
    counts={}
    for ob in (body,collision):
        ob.data.calc_loop_triangles()
        counts[ob.name]=len(ob.data.loop_triangles)
    return counts

def main():
    blockout()
    details()
    texture()
    return finish()

if __name__=='__main__':
    main()

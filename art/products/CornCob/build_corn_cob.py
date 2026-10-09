"""Run inside Blender's existing metric scene. build(1..3), finish().

The only existing object retained is the untouched HumanRef_1.8m.
All generated files stay beside this script. Customer front is -Y.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(OUT, '../../..'))
ZC = 0.026
N, ROWS = 10, 11

def build(stage=3):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for ob in list(bpy.data.objects):
        if ob.name.startswith(('CornCob_', 'Preview_')):
            bpy.data.objects.remove(ob, do_unlink=True)
    verts, faces, face_uv = [], [], []
    def v(co):
        verts.append(co)
        return len(verts)-1
    def face(indices, uv):
        faces.append(indices)
        face_uv.append(uv)
    small_cell = 0
    def small_uv(count):
        nonlocal small_cell
        col, row = small_cell % 20, small_cell // 20
        small_cell += 1
        x, y = .015+col*.0485, .014+row*.033
        a, b = .043, .027
        if count == 4:
            return [(x,y),(x+a,y),(x+a,y+b),(x,y+b)]
        return [(x,y),(x+a,y),(x+a*.5,y+b)]
    # Seamless shell: each kernel is four facets rising from a rectangular cell.
    # Shared groove vertices make a closed manifold rather than loose kernel cubes.
    xs = [-.047 + .158*j/ROWS for j in range(ROWS+1)]
    radii = [.019, .022, .024, .0245, .0245, .0245,
             .0245, .024, .0235, .0225, .020, .014]
    rings = []
    for j,x in enumerate(xs):
        rings.append([v((x, radii[j]*math.cos(2*math.pi*(k-.5)/N),
                             ZC+radii[j]*math.sin(2*math.pi*(k-.5)/N))) for k in range(N)])
    for j in range(ROWS):
        for k in range(N):
            a = 2*math.pi*k/N
            rr = (radii[j]+radii[j+1])*.5 + (0.0015 if stage>=2 else 0)
            centre = v(((xs[j]+xs[j+1])*.5,rr*math.cos(a),ZC+rr*math.sin(a)))
            q = [rings[j][k],rings[j+1][k],rings[j+1][(k+1)%N],rings[j][(k+1)%N]]
            u0,u1 = .02+.96*j/ROWS, .02+.96*(j+1)/ROWS
            t0,t1 = .20+.78*k/N, .20+.78*(k+1)/N
            uv = [(u0,t0),(u1,t0),(u1,t1),(u0,t1)]
            mid = ((u0+u1)*.5,(t0+t1)*.5)
            for h in range(4):
                face([q[h],q[(h+1)%4],centre],[uv[h],uv[(h+1)%4],mid])
    for j in (0,ROWS):
        tip = v((xs[j]+(-.001 if j==0 else .003),0,ZC))
        for k in range(N):
            face([rings[j][k],rings[j][(k+1)%N],tip],small_uv(3))
    # Paper sleeve: thin closed wall, open mouth around the food, folded bottom rim.
    sleeve_rings=[]
    for x,r in [(-.075,.016),(-.038,.0254),(-.038,.0245),(-.075,.0151)]:
        sleeve_rings.append([v((x,r*math.cos(2*math.pi*(k-.5)/N),
                                  ZC+r*math.sin(2*math.pi*(k-.5)/N))) for k in range(N)])
    sleeve_start=len(faces)
    for h in range(4):
        for k in range(N):
            face([sleeve_rings[h][k],sleeve_rings[(h+1)%4][k],
                  sleeve_rings[(h+1)%4][(k+1)%N],sleeve_rings[h][(k+1)%N]],small_uv(4))
    stick_start=len(faces)
    stick_rings=[]
    for x,r in [(-.110,.0025),(-.108,.0034),(-.064,.0034)]:
        stick_rings.append([v((x,r*math.cos(2*math.pi*k/6),ZC+r*math.sin(2*math.pi*k/6))) for k in range(6)])
    for j in range(2):
        for k in range(6):
            face([stick_rings[j][k],stick_rings[j+1][k],stick_rings[j+1][(k+1)%6],stick_rings[j][(k+1)%6]],small_uv(4))
    for j in (0,2):
        for k in range(1,5):
            face([stick_rings[j][0],stick_rings[j][k],stick_rings[j][k+1]],small_uv(3))
    # Put the lowest broad sleeve facet on the shelf, directly under the footprint.
    # The cob's bottom groove also forms a flat facet covering (0,0,0).
    verts=[(x,y,max(0,z-.003)) for x,y,z in verts]
    for index in sleeve_rings[2]:
        x,y,z=verts[index]
        if z==0: verts[index]=(x,y,.0009)
    me=bpy.data.meshes.new('CornCob_Mesh')
    me.from_pydata(verts,[],faces)
    me.update()
    ob=bpy.data.objects.new('CornCob_Body',me)
    bpy.context.collection.objects.link(ob)
    uv=me.uv_layers.new(name='UVMap')
    for poly,coords in zip(me.polygons,face_uv):
        for li,co in zip(poly.loop_indices,coords): uv.data[li].uv=co
    bm=bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(me); bm.free()
    # One baked colour atlas, no procedural nodes or extra material slots.
    pix=np.zeros((256,256,4),dtype=np.float32)
    pix[:]=(.80,.57,.16,1)
    for y in range(256):
        for x in range(256):
            u,t=(x+.5)/256,(y+.5)/256
            if .195<=t<=.985:
                jj=min(ROWS-1,max(0,int((u-.02)/.96*ROWS)))
                kk=min(N-1,max(0,int((t-.20)/.78*N)))
                fu=((u-.02)/.96*ROWS)%1
                ft=((t-.20)/.78*N)%1
                col=(1.0,.70,.07,1)
                if stage>=3:
                    col=[(1.0,.73,.09,1),(1.0,.68,.045,1),(.98,.76,.13,1)][(jj+kk*2)%3]
                    if min(fu,1-fu)<.055 or min(ft,1-ft)<.055:
                        col=(.80,.45,.035,1)
                    # Three diagonal sear bands across the customer-facing half.
                    front = 2<=kk<=6
                    band=jj+fu+.24*(kk+ft-4)
                    if front and any(abs(band-p)<.18 for p in (3.2,6.3,8.7)):
                        col=(.20,.095,.025,1)
                pix[y,x]=col
    # Flat per-part rectangles with generous gutters in the lower atlas strip.
    for idx,coords in enumerate(face_uv):
        if idx<ROWS*N*4: continue
        xs_uv=[p[0] for p in coords]; ys_uv=[p[1] for p in coords]
        x0=max(0,int(min(xs_uv)*256)-1); x1=min(256,int(max(xs_uv)*256)+2)
        y0=max(0,int(min(ys_uv)*256)-1); y1=min(256,int(max(ys_uv)*256)+2)
        if idx< sleeve_start: colour=(1,.70,.07,1)
        elif idx<stick_start:
            colour=(.94,.90,.75,1) if (idx-sleeve_start)//N!=2 else (.80,.76,.62,1)
        else: colour=(.66,.40,.17,1) if idx%3==0 else (.82,.60,.32,1)
        pix[y0:y1,x0:x1]=colour
    old=bpy.data.images.get('T_CornCob_BaseColor.png')
    if old: bpy.data.images.remove(old)
    img=bpy.data.images.new('T_CornCob_BaseColor.png',width=256,height=256,alpha=False)
    img.pixels.foreach_set(pix.ravel())
    img.filepath_raw=OUT+'/T_CornCob_BaseColor.png'; img.file_format='PNG'; img.save()
    mat=bpy.data.materials.get('M_CornCob') or bpy.data.materials.new('M_CornCob')
    mat.use_nodes=True; nodes=mat.node_tree.nodes; nodes.clear()
    bs=nodes.new('ShaderNodeBsdfPrincipled'); bs.inputs['Roughness'].default_value=.8
    tex=nodes.new('ShaderNodeTexImage'); tex.image=img; tex.interpolation='Closest'
    out=nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    mat.node_tree.links.new(bs.outputs['BSDF'],out.inputs['Surface'])
    me.materials.append(mat)
    # A 20-triangle convex hull built from twelve bounding support vertices.
    cb=bmesh.new()
    mn=np.min(np.array(verts),axis=0); mx=np.max(np.array(verts),axis=0)
    for x,ry,low,high in [(mn[0]-.001,.005,.018,.028),(-.055,.029,0,.053),(mx[0]+.001,.029,0,.053)]:
        for yy,zz in [(-1,-1),(-1,1),(1,1),(1,-1)]:
            cb.verts.new((x,yy*ry,high if zz==1 else low))
    hull=bmesh.ops.convex_hull(cb,input=list(cb.verts),use_existing_faces=False)
    unused=[v for v in cb.verts if not v.link_faces]
    if unused: bmesh.ops.delete(cb,geom=unused,context='VERTS')
    bmesh.ops.recalc_face_normals(cb,faces=list(cb.faces))
    bmesh.ops.triangulate(cb,faces=list(cb.faces))
    cm=bpy.data.meshes.new('CornCob_ConvexHull'); cb.to_mesh(cm); cb.free()
    co=bpy.data.objects.new('CornCob_Collision',cm); bpy.context.collection.objects.link(co)
    co.hide_render=True; co.display_type='WIRE'; co.hide_set(True)
    setup_preview()
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active=ob
    bpy.context.view_layer.update()
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
    me.calc_loop_triangles(); cm.calc_loop_triangles()
    return {'stage':stage,'body_tris':len(me.loop_triangles),'collision_tris':len(cm.loop_triangles),'bounds':list(ob.dimensions)}

def setup_preview():
    scene=bpy.context.scene
    camera=bpy.data.cameras.new('Preview_Camera')
    cam=bpy.data.objects.new('Preview_Camera',camera); scene.collection.objects.link(cam)
    cam.location=(.095,-.32,.225)
    target=Vector((.002,0,.021))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    camera.type='ORTHO'; camera.ortho_scale=.275; scene.camera=cam
    for name,position,power,size in [('Key',(-.08,-.20,.32),1.8,.22),('Fill',(.15,.10,.24),.9,.18)]:
        ld=bpy.data.lights.new('Preview_'+name,'AREA'); lo=bpy.data.objects.new('Preview_'+name,ld)
        scene.collection.objects.link(lo); lo.location=position
        lo.rotation_euler=(target-lo.location).to_track_quat('-Z','Y').to_euler()
        ld.energy=power; ld.shape='DISK'; ld.size=size
    scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=32
    scene.render.resolution_x=900; scene.render.resolution_y=600; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    world=scene.world or bpy.data.worlds.new('Preview_World'); scene.world=world
    world.use_nodes=True; world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.16,.20,.24,1)
    world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.45
    scene.view_settings.view_transform='Standard'
    scene.render.filepath=OUT+'/renders/04-final.png'
    scene.render.use_file_extension=True

def finish():
    bpy.ops.object.select_all(action='DESELECT')
    body=bpy.data.objects['CornCob_Body']; collision=bpy.data.objects['CornCob_Collision']
    collision.hide_set(False)
    body.select_set(True); collision.select_set(True)
    bpy.context.view_layer.objects.active=body
    bpy.ops.export_scene.fbx(filepath=OUT+'/CornCob.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    bpy.data.images['T_CornCob_BaseColor.png'].filepath='//T_CornCob_BaseColor.png'
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=OUT+'/CornCob.blend')
    return {'blend':OUT+'/CornCob.blend','fbx':OUT+'/CornCob.fbx'}

if __name__=='__main__':
    build(3)
    finish()

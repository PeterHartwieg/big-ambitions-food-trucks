"""Regenerate the StorageCabinet ("Extra storage") piece. Writes only beside this file.

Run headless from the repo root:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python art/equipment/StorageCabinet/build_storage_cabinet.py

Brushed stainless under-counter cabinet: two lower doors with bar handles,
an open upper shelf holding stacked cardboard boxes and a lidded tub, and a
recessed dark plinth. Front is -Y, origin at the bottom centre, Z up.
Every face gets its own atlas cell in a 512 px texture (flat colours, steel
cells carry faint horizontal brushing streaks).
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)
PALETTE = {
    'steel': (0.70, 0.72, 0.73, 1),
    'steel_light': (0.82, 0.84, 0.85, 1),
    'steel_dark': (0.46, 0.48, 0.50, 1),
    'inside': (0.30, 0.32, 0.34, 1),
    'plinth': (0.10, 0.11, 0.12, 1),
    'handle': (0.90, 0.91, 0.92, 1),
    'gap': (0.06, 0.06, 0.07, 1),
    'card': (0.66, 0.47, 0.27, 1),
    'card_dark': (0.55, 0.38, 0.21, 1),
    'tape': (0.80, 0.68, 0.45, 1),
    'tub': (0.92, 0.92, 0.88, 1),
    'lid': (0.16, 0.45, 0.72, 1),
}
BRUSHED = {'steel', 'steel_light', 'steel_dark'}

W = .196      # half width (0.392 m)
YF = -.140    # carcass front face
YB = .170     # carcass back face
H = .600      # overall height (worktop top)
T = .016      # sheet thickness
PL = .045     # plinth height
DIV = .330    # top of the divider between the doors and the open shelf


class Geometry:
    def __init__(self):
        self.v, self.f, self.colors = [], [], []

    def add(self, verts, faces, colors):
        start = len(self.v)
        self.v.extend(verts)
        self.f.extend([tuple(start+i for i in face) for face in faces])
        self.colors.extend([colors]*len(faces) if isinstance(colors, str) else colors)

    def box(self, lo, hi, color):
        x,y,z = lo; X,Y,Z = hi
        self.add([(x,y,z),(X,y,z),(X,Y,z),(x,Y,z),
                  (x,y,Z),(X,y,Z),(X,Y,Z),(x,Y,Z)],
                 [(3,2,1,0),(0,1,5,4),(1,2,6,5),(2,3,7,6),
                  (3,0,4,7),(4,5,6,7)], color)

    def chamfer_slab(self, lo, hi, cut, color):
        x,y,z=lo; X,Y,Z=hi
        ring=[(x+cut,y),(X-cut,y),(X,y+cut),(X,Y-cut),
              (X-cut,Y),(x+cut,Y),(x,Y-cut),(x,y+cut)]
        verts=[(a,b,h) for h in (z,Z) for a,b in ring]
        faces=[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
        for i in range(1,7):
            faces.extend([(0,i+1,i),(8,8+i,8+i+1)])
        self.add(verts,faces,color)

    def bar_z(self, x, y, z0, z1, r, n, color):
        # Vertical round bar (handle), n-sided, capped.
        verts=[(x+r*math.cos(2*math.pi*i/n), y+r*math.sin(2*math.pi*i/n), z)
               for z in (z0,z1) for i in range(n)]
        faces=[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        # Fan caps (no n-gons): one centre vertex per end.
        verts += [(x,y,z0),(x,y,z1)]
        for i in range(n):
            faces.append((2*n,(i+1)%n,i))
            faces.append((2*n+1,n+i,n+(i+1)%n))
        self.add(verts,faces,color)

    def cardboard(self, lo, hi):
        # Box with a darker flap seam and a tape strip across the top.
        x,y,z=lo; X,Y,Z=hi
        self.box(lo,hi,['card_dark','card','card_dark','card_dark','card_dark','card'])
        cx=(x+X)/2
        self.box((cx-.012,y-.001,Z),(cx+.012,Y+.001,Z+.002),'tape')
        self.box((cx-.012,y-.002,Z-.030),(cx+.012,y,Z),'tape')
        self.box((x+.004,y-.002,Z-.050),(X-.004,y,Z-.046),'card_dark')


def select_only(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active=ob


def build(stage=2):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for name in ('StorageCabinet_Body','StorageCabinet_Collision'):
        ob=bpy.data.objects.get(name)
        if ob: bpy.data.objects.remove(ob,do_unlink=True)
    g=Geometry()
    # Recessed dark plinth; its underside covers the placement point.
    g.box((-W+.012,YF+.022,0),(W-.012,YB-.006,PL),'plinth')
    # Carcass: base, two sides, back, divider, worktop.
    g.box((-W,YF,PL),(W,YB,PL+T),'steel_dark')
    g.box((-W,YF,PL),(-W+T,YB,H-.022),'steel')
    g.box((W-T,YF,PL),(W,YB,H-.022),'steel')
    g.box((-W+T,YB-T,PL+T),(W-T,YB,H-.022),'steel')
    g.box((-W+T,YF,DIV-T),(W-T,YB-T,DIV),'steel_light')
    g.chamfer_slab((-W,YF-.008,H-.022),(W,YB,H),.006,'steel_light')
    # Open shelf interior: darker liner so the opening reads from the front.
    g.box((-W+T,YB-T-.004,DIV),(W-T,YB-T,H-.022),'inside')
    g.box((-W+T,YF+.010,H-.030),(W-T,YB-T,H-.022),'inside')
    if stage >= 2:
        # Two doors, proud of the carcass, with a dark gap between and around them.
        g.box((-W+T,YF-.002,PL+T),(W-T,YF,DIV-T),'gap')
        for x0,x1 in ((-W+.004,-.003),(.003,W-.004)):
            g.box((x0,YF-.014,PL+T+.004),(x1,YF-.002,DIV-T-.004),'steel')
            # Pressed panel: a slightly raised lighter inset.
            g.box((x0+.022,YF-.017,PL+T+.030),(x1-.022,YF-.014,DIV-T-.030),'steel_light')
        # Bar handles beside the centre gap, on stand-offs.
        for x in (-.024,.024):
            for z in (.150,.270):
                g.box((x-.005,YF-.026,z-.005),(x+.005,YF-.014,z+.005),'steel_dark')
            g.bar_z(x,YF-.030,.140,.280,.0065,6,'handle')
        # Hinges on the outer edges.
        for x in (-W+.004,W-.010):
            for z in (.085,.270):
                g.box((x,YF-.018,z),(x+.006,YF-.010,z+.030),'steel_dark')
        # Front lip of the open shelf and a toe-kick vent.
        g.box((-W+T,YF-.004,DIV-.006),(W-T,YF,DIV+.006),'steel_dark')
        for i in range(4):
            z=.012+i*.007
            g.box((-.120,YF+.020,z),(.120,YF+.022,z+.003),'steel_dark')
        # Stock on the open shelf: two stacked cartons, a smaller carton, a lidded tub.
        g.cardboard((-.170,-.110,DIV),(-.040,.130,DIV+.105))
        g.cardboard((-.160,-.095,DIV+.105),(-.050,.115,DIV+.190))
        g.cardboard((-.030,-.090,DIV),(.065,.120,DIV+.080))
        g.box((.080,-.115,DIV),(.172,.010,DIV+.120),'tub')
        g.box((.077,-.118,DIV+.120),(.175,.013,DIV+.135),'lid')
        g.box((.080,.030,DIV),(.172,.140,DIV+.120),'tub')
        g.box((.077,.027,DIV+.120),(.175,.143,DIV+.135),'lid')
    me=bpy.data.meshes.new('StorageCabinet_Mesh')
    me.from_pydata(g.v,[],g.f); me.update()
    bm=bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new('StorageCabinet_Body',me)
    bpy.context.collection.objects.link(ob)
    attr=me.attributes.new('palette_index','INT','FACE')
    names=list(PALETTE)
    for name in names:
        mat=bpy.data.materials.get('Preview_'+name) or bpy.data.materials.new('Preview_'+name)
        mat.diffuse_color=PALETTE[name]; ob.data.materials.append(mat)
    for p,color in zip(me.polygons,g.colors):
        idx=names.index(color); p.material_index=idx; attr.data[p.index].value=idx
    select_only(ob)
    ob['front_direction']='-Y'
    ob['dimensions_m']='%.3f x %.3f x %.3f' % tuple(ob.dimensions)
    configure_view(ob)
    return {'stage':stage,'triangles':sum(len(p.vertices)-2 for p in me.polygons),
            'dimensions':list(ob.dimensions),'origin':list(ob.location)}


def configure_view(ob):
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                s=area.spaces.active
                s.shading.type='SOLID'
                s.shading.color_type='MATERIAL'
                s.overlay.show_floor=False
                s.region_3d.view_distance=1.0
                s.region_3d.view_location=Vector((0,0,.30))
                s.region_3d.view_rotation=(Vector((.75,-1.6,.95))-Vector((0,0,.30))).to_track_quat('Z','Y')


def review(filename, texture_mode=False):
    scene=bpy.context.scene
    camdata=bpy.data.cameras.new('ReviewCamera')
    cam=bpy.data.objects.new('ReviewCamera',camdata)
    scene.collection.objects.link(cam)
    cam.location=(.52,-1.85,.98)
    cam.rotation_euler=(Vector((0,0,.300))-cam.location).to_track_quat('-Z','Y').to_euler()
    camdata.type='ORTHO'; camdata.ortho_scale=.82
    scene.camera=cam
    scene.render.engine='BLENDER_WORKBENCH'
    scene.render.resolution_x=900; scene.render.resolution_y=900
    scene.render.resolution_percentage=100
    sh=scene.display.shading
    sh.light='STUDIO'; sh.studiolight_rotate_z=.25
    sh.color_type='TEXTURE' if texture_mode else 'MATERIAL'
    sh.show_shadows=True; sh.show_cavity=True
    sh.cavity_type='BOTH'; sh.curvature_ridge_factor=1.15
    sh.curvature_valley_factor=1.05
    sh.show_specular_highlight=True
    sh.background_type='WORLD'
    if scene.world is None: scene.world=bpy.data.worlds.new('World')
    scene.world.color=(.11,.145,.18)
    scene.view_settings.view_transform='Standard'
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=os.path.join(OUT,'renders',filename)
    try: bpy.ops.render.render(write_still=True)
    finally:
        scene.camera=None
        bpy.data.objects.remove(cam,do_unlink=True)
        bpy.data.cameras.remove(camdata)
    return scene.render.filepath


def texture():
    ob=bpy.data.objects['StorageCabinet_Body']; me=ob.data
    names=list(PALETTE)
    count=len(me.polygons); cols=math.ceil(math.sqrt(count)); rows=math.ceil(count/cols)
    while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
    uv=me.uv_layers.new(name='UVMap')
    pixels=np.ones((512,512,4),dtype=np.float32)
    pixels[:,:,:3]=PALETTE['steel'][:3]
    rng=np.random.default_rng(7)
    for p in me.polygons:
        x=p.index%cols; y=p.index//cols
        x0=round(x*512/cols); x1=round((x+1)*512/cols)
        y0=round(y*512/rows); y1=round((y+1)*512/rows)
        name=names[me.attributes['palette_index'].data[p.index].value]
        color=np.array(PALETTE[name],dtype=np.float32)
        pixels[y0:y1,x0:x1,:]=color
        if name in BRUSHED:
            # Brushing runs along the face's U direction: faint per-row streaks.
            streak=rng.normal(0,.008,(y1-y0,1,1)).astype(np.float32)
            pixels[y0:y1,x0:x1,:3]=np.clip(color[:3]+streak,0,1)
        lo=((x0+1.25)/512,(y0+1.25)/512)
        hi=((x1-1.25)/512,(y1-1.25)/512)
        if len(p.loop_indices)==4:
            coords=[lo,(hi[0],lo[1]),hi,(lo[0],hi[1])]
        else:
            # Triangles (handle caps): fit the corners on a circle inside the cell.
            n=len(p.loop_indices); cx=(lo[0]+hi[0])/2; cy=(lo[1]+hi[1])/2
            rx=(hi[0]-lo[0])/2; ry=(hi[1]-lo[1])/2
            coords=[(cx+rx*math.cos(2*math.pi*j/n),cy+ry*math.sin(2*math.pi*j/n)) for j in range(n)]
        for j,li in enumerate(p.loop_indices): uv.data[li].uv=coords[j]
    old=bpy.data.images.get('T_StorageCabinet_BaseColor')
    if old: bpy.data.images.remove(old)
    img=bpy.data.images.new('T_StorageCabinet_BaseColor',width=512,height=512,alpha=True)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel()); img.update()
    img.filepath_raw=os.path.join(OUT,'T_StorageCabinet_BaseColor.png')
    img.file_format='PNG'; img.save()
    mat=bpy.data.materials.get('M_StorageCabinet') or bpy.data.materials.new('M_StorageCabinet')
    mat.use_nodes=True; mat.node_tree.nodes.clear()
    nodes=mat.node_tree.nodes; links=mat.node_tree.links
    out=nodes.new('ShaderNodeOutputMaterial'); out.location=(480,0)
    bs=nodes.new('ShaderNodeBsdfPrincipled'); bs.location=(170,0)
    bs.inputs['Roughness'].default_value=.45
    bs.inputs['Metallic'].default_value=.6
    bs.inputs['Alpha'].default_value=1
    tex=nodes.new('ShaderNodeTexImage'); tex.location=(-150,0); tex.image=img
    tex.interpolation='Closest'; nodes.active=tex
    links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    links.new(bs.outputs['BSDF'],out.inputs['Surface'])
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons: p.material_index=0
    return {'atlas_faces':count,'grid':[cols,rows],'texture':list(img.size),'materials':len(me.materials)}


def finish():
    body=bpy.data.objects['StorageCabinet_Body']
    bm=bmesh.new()
    # Convex outer envelope: carcass, worktop lip and the projecting handles.
    for x in (-W,W):
        for y in (YF-.037,YB):
            for z in (0,H): bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('StorageCabinet_ConvexHull'); bm.to_mesh(me); bm.free()
    collision=bpy.data.objects.new('StorageCabinet_Collision',me)
    bpy.context.collection.objects.link(collision)
    collision.hide_render=True; collision.display_type='WIRE'
    collision['construction']='bmesh.ops.convex_hull'
    collision['purpose']='Unity convex collision hull'
    select_only(body); collision.select_set(True)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    for mat in list(bpy.data.materials):
        if mat.name.startswith('Preview_') and not mat.users: bpy.data.materials.remove(mat)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'StorageCabinet.blend'))
    img=bpy.data.images['T_StorageCabinet_BaseColor']
    img.filepath='//T_StorageCabinet_BaseColor.png'
    img.reload()
    bpy.ops.export_scene.fbx(filepath=OUT+'/StorageCabinet.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    review('04-final.png',True)
    select_only(body)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'StorageCabinet.blend'))
    return {ob.name:sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in (body,collision)}


if __name__=='__main__':
    # Start from an empty metric scene so the result doesn't depend on the startup file.
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1.0
    print('BUILD', build(1)); review('01-blockout.png')
    print('BUILD', build(2)); review('02-detail.png')
    print('TEXTURE', texture()); review('03-texture.png',True)
    print('FINISH', finish())

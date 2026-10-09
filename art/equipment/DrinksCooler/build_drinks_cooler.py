"""Regenerate in the supplied metric start scene. Writes only beside this file.

Call build(1), build(2), texture(), and finish() individually for review,
or execute this file from Blender's text editor for all four stages.
Opaque drink geometry supplies the glass-door display; no transparent shader.
"""
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)
PALETTE = {
    'case': (0.075, 0.095, 0.115, 1),
    'trim': (0.20, 0.25, 0.29, 1),
    'frame': (0.72, 0.79, 0.80, 1),
    'inside': (0.18, 0.29, 0.34, 1),
    'shelf': (0.61, 0.72, 0.75, 1),
    'light': (0.94, 0.96, 0.83, 1),
    'metal': (0.76, 0.83, 0.85, 1),
    'red': (0.88, 0.18, 0.13, 1),
    'orange': (1.0, 0.55, 0.10, 1),
    'blue': (0.08, 0.48, 0.77, 1),
    'green': (0.20, 0.68, 0.39, 1),
    'violet': (0.55, 0.29, 0.70, 1),
    'reflection': (0.51, 0.70, 0.76, 1),
}

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

    def profile(self, x, y, z, rings, n, colors):
        # Watertight faceted cans/bottles. Each ring is (height, radius).
        verts = [(x+r*math.cos(2*math.pi*i/n+math.pi/n),
                  y+r*math.sin(2*math.pi*i/n+math.pi/n), z+h)
                 for h,r in rings for i in range(n)]
        faces, cols = [], []
        for j in range(len(rings)-1):
            for i in range(n):
                faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
                cols.append(colors[j])
        for j, reverse, color in [(0,True,'metal'),(len(rings)-1,False,colors[-1])]:
            centre=len(verts); verts.append((x,y,z+rings[j][0]))
            for i in range(n):
                edge=(j*n+i,j*n+(i+1)%n)
                faces.append((centre, *edge[::-1]) if reverse else (centre,*edge))
                cols.append(color)
        self.add(verts,faces,cols)

    def chamfer_slab(self, lo, hi, cut, color):
        x,y,z=lo; X,Y,Z=hi
        ring=[(x+cut,y),(X-cut,y),(X,y+cut),(X,Y-cut),
              (X-cut,Y),(x+cut,Y),(x,Y-cut),(x,y+cut)]
        verts=[(a,b,h) for h in (z,Z) for a,b in ring]
        faces=[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
        for i in range(1,7):
            faces.extend([(0,i+1,i),(8,8+i,8+i+1)])
        self.add(verts,faces,color)

def select_only(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.hide_set(False); ob.select_set(True)
    bpy.context.view_layer.objects.active=ob

def build(stage=2):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for name in ('DrinksCooler_Body','DrinksCooler_Collision'):
        ob=bpy.data.objects.get(name)
        if ob: bpy.data.objects.remove(ob,do_unlink=True)
    g=Geometry()
    # Full, flat underside through the placement point. Width 392 mm.
    g.box((-.196,-.151,0),(.196,.174,.043),'case')
    g.box((-.196,-.151,.043),(-.171,.174,.512),'case')
    g.box((.171,-.151,.043),(.196,.174,.512),'case')
    g.box((-.171,.112,.043),(.171,.174,.512),'case')
    g.chamfer_slab((-.196,-.151,.506),(.196,.174,.548),.006,'case')
    # Opaque blue-grey interior backdrop and liner.
    g.box((-.171,.102,.043),(.171,.112,.506),'inside')
    # Visible three stock shelves, recessed behind the front door frame.
    for z in (.066,.204,.342):
        g.box((-.168,-.131,z-.010),(.168,.102,z),'shelf')
        g.box((-.166,-.138,z-.013),(.166,-.129,z-.002),'frame')
    # Gasket then light-coloured door rails: front is explicitly -Y.
    for x in (-.179,.155):
        g.box((x,-.158,.044),(x+.024,-.148,.508),'trim')
    for x in (-.176,.154):
        g.box((x,-.166,.051),(x+.022,-.158,.502),'frame')
    g.box((-.176,-.166,.043),(.176,-.148,.064),'frame')
    g.box((-.176,-.166,.481),(.176,-.148,.507),'frame')
    # Blank, pale top light strip, no lettering.
    g.box((-.155,-.154,.512),(.155,-.149,.536),'light')
    if stage >= 2:
        # Vertical handle on customer's right, within the 360 mm footprint.
        for z in (.175,.350):
            g.box((-.169,-.178,z),(-.151,-.160,z+.016),'trim')
        g.box((-.169,-.178,.173),(-.151,-.170,.369),'case')
        # Two hinges and a small lower ventilation grille.
        for z in (.115,.428):
            g.box((.171,-.169,z),(.184,-.156,z+.025),'trim')
        for i in range(5):
            z=.012+i*.005
            g.box((-.110,-.153,z),(.110,-.151,z+.002),'trim')
        # Fifteen drinks: five clearly separated silhouettes per shelf.
        hues=['red','orange','blue','green','violet']
        for row,z in enumerate((.066,.204,.342)):
            for i,x in enumerate((-.122,-.061,0,.061,.122)):
                color=hues[(i+2*row)%5]
                is_bottle = row==1 or (row==2 and i in (1,3))
                if is_bottle:
                    g.profile(x,-.105,z,
                        [(0,.018),(.005,.022),(.066,.022),(.083,.010),
                         (.100,.010),(.106,.011)],6,
                        [color,color,color,color,'metal'])
                else:
                    g.profile(x,-.105,z,
                        [(0,.018),(.003,.022),(.084,.022),(.087,.018)],8,
                        ['metal',color,'metal'])
        # Narrow solid glass glints in clear edge areas, avoid masking drinks.
        g.box((.139,-.150,.379),(.144,-.148,.462),'reflection')
        g.box((-.145,-.150,.088),(-.141,-.148,.157),'reflection')
    me=bpy.data.meshes.new('DrinksCooler_Mesh')
    me.from_pydata(g.v,[],g.f); me.update()
    bm=bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(me); bm.free()
    ob=bpy.data.objects.new('DrinksCooler_Body',me)
    bpy.context.collection.objects.link(ob)
    # Face colours are retained as a mesh attribute for deterministic atlas bake.
    attr=me.attributes.new('palette_index','INT','FACE')
    names=list(PALETTE)
    for name in names:
        mat=bpy.data.materials.get('Preview_'+name) or bpy.data.materials.new('Preview_'+name)
        mat.diffuse_color=PALETTE[name]; ob.data.materials.append(mat)
    for p,color in zip(me.polygons,g.colors):
        idx=names.index(color); p.material_index=idx; attr.data[p.index].value=idx
    select_only(ob)
    ob['front_direction']='-Y'
    ob['dimensions_m']='0.392 x 0.352 x 0.548'
    ob['display']='Opaque coloured drinks inside glass-door frame; no transparency'
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
                s.region_3d.view_distance=.95
                s.region_3d.view_location=Vector((0,0,.27))
                s.region_3d.view_rotation=(Vector((.75,-1.6,.90))-Vector((0,0,.27))).to_track_quat('Z','Y')

def review(filename, texture_mode=False):
    # Temporary render camera is removed immediately after the review image.
    scene=bpy.context.scene
    camdata=bpy.data.cameras.new('ReviewCamera')
    cam=bpy.data.objects.new('ReviewCamera',camdata)
    scene.collection.objects.link(cam)
    cam.location=(.52,-1.85,.91)
    cam.rotation_euler=(Vector((0,0,.270))-cam.location).to_track_quat('-Z','Y').to_euler()
    camdata.type='ORTHO'; camdata.ortho_scale=.76
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
    sh.background_type='WORLD'; scene.world.color=(.11,.145,.18)
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
    ob=bpy.data.objects['DrinksCooler_Body']; me=ob.data
    names=list(PALETTE)
    count=len(me.polygons); cols=math.ceil(math.sqrt(count)); rows=math.ceil(count/cols)
    while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
    uv=me.uv_layers.new(name='UVMap')
    pixels=np.ones((512,512,4),dtype=np.float32)
    pixels[:,:,:3]=(.075,.095,.115)
    # Every original quad/triangle has its own padded non-overlapping atlas cell.
    for p in me.polygons:
        x=p.index%cols; y=p.index//cols
        x0=round(x*512/cols); x1=round((x+1)*512/cols)
        y0=round(y*512/rows); y1=round((y+1)*512/rows)
        color=PALETTE[names[me.attributes['palette_index'].data[p.index].value]]
        pixels[y0:y1,x0:x1,:]=color
        lo=((x0+1.25)/512,(y0+1.25)/512)
        hi=((x1-1.25)/512,(y1-1.25)/512)
        coords=[lo,(hi[0],lo[1]),hi,(lo[0],hi[1])]
        for j,li in enumerate(p.loop_indices): uv.data[li].uv=coords[j]
    old=bpy.data.images.get('T_DrinksCooler_BaseColor')
    if old: bpy.data.images.remove(old)
    img=bpy.data.images.new('T_DrinksCooler_BaseColor',width=512,height=512,alpha=True)
    img.colorspace_settings.name='sRGB'
    img.pixels.foreach_set(pixels.ravel()); img.update()
    img.filepath_raw=os.path.join(OUT,'T_DrinksCooler_BaseColor.png')
    img.file_format='PNG'; img.save()
    mat=bpy.data.materials.get('M_DrinksCooler') or bpy.data.materials.new('M_DrinksCooler')
    mat.use_nodes=True; mat.node_tree.nodes.clear()
    nodes=mat.node_tree.nodes; links=mat.node_tree.links
    out=nodes.new('ShaderNodeOutputMaterial'); out.location=(480,0)
    bs=nodes.new('ShaderNodeBsdfPrincipled'); bs.location=(170,0)
    bs.inputs['Roughness'].default_value=.72
    bs.inputs['Alpha'].default_value=1
    tex=nodes.new('ShaderNodeTexImage'); tex.location=(-150,0); tex.image=img
    tex.interpolation='Closest'; nodes.active=tex
    links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    links.new(bs.outputs['BSDF'],out.inputs['Surface'])
    me.materials.clear(); me.materials.append(mat)
    for p in me.polygons: p.material_index=0
    # Keep absolute until the scene has been saved into the output folder.
    return {'atlas_faces':count,'grid':[cols,rows],'texture':list(img.size),'materials':len(me.materials)}

def finish():
    body=bpy.data.objects['DrinksCooler_Body']
    bm=bmesh.new()
    # A convex outer envelope covers cabinet and the projecting handle.
    for x in (-.196,.196):
        for y in (-.178,.174):
            for z in (0,.548): bm.verts.new((x,y,z))
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    me=bpy.data.meshes.new('DrinksCooler_ConvexHull'); bm.to_mesh(me); bm.free()
    collision=bpy.data.objects.new('DrinksCooler_Collision',me)
    bpy.context.collection.objects.link(collision)
    collision.hide_render=True; collision.display_type='WIRE'
    collision['construction']='bmesh.ops.convex_hull'
    collision['purpose']='Unity convex collision hull'
    select_only(body); collision.select_set(True)
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    # Remove unneeded temporary material datablocks.
    for mat in list(bpy.data.materials):
        if mat.name.startswith('Preview_') and not mat.users: bpy.data.materials.remove(mat)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'DrinksCooler.blend'))
    img=bpy.data.images['T_DrinksCooler_BaseColor']
    img.filepath='//T_DrinksCooler_BaseColor.png'
    img.reload()
    bpy.ops.export_scene.fbx(filepath=OUT+'/DrinksCooler.fbx',use_selection=True,object_types={'MESH'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,
        mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
    collision.hide_set(True)
    review('04-final.png',True)
    select_only(body)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'DrinksCooler.blend'))
    return {ob.name:sum(len(p.vertices)-2 for p in ob.data.polygons) for ob in (body,collision)}

if __name__=='__main__':
    build(1); review('01-blockout.png')
    build(2); review('02-detail.png')
    texture(); review('03-texture.png',True)
    finish()

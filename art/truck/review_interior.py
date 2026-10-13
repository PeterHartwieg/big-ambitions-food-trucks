"""Interior review for version 5: clearances and renders with equipment and stand-in people.
Run from the repository root, after build_truck.py:
  blender -b art/truck/FoodTruck.blend --python art/truck/review_interior.py
Never saves the blend. Writes art/truck/renders/interior-*.png and prints CLEAR/HIT lines.
  ... review_interior.py -- check MerchRack PizzaOven HotDogRoller CornGrill   (other pieces, no renders)
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
OUT='art/truck/renders'
EQ='art/equipment'
os.makedirs(OUT,exist_ok=True)
# Must match build_truck.py (LINE_X, LINE_Y, COUNTER_TOP) and TruckAssets.EquipmentSpots (Unity = (-x, z, -y)).
LINE_X,TOP=.62,1.75
LINE_Y=(.425,.875,1.325,1.775)
# The tallest and the widest pieces, the new Extra Storage cabinet among them.
ARGS=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
# '-- check A B C D' checks those four pieces without rendering.
CHECK_ONLY=ARGS[:1]==['check']
PIECES=ARGS[1:] if CHECK_ONLY else ['Griddle','StorageCabinet','PopcornMachine','DrinksCooler']
# Player in the middle of the hatch (y 1.10), crew 0.45 m either side (WindowCrew, Core WindowPlaces).
PEOPLE=[(-.59,1.10,'player'),(-.59,.65,'crew'),(-.59,1.55,'crew')]
# TruckAssets.RegisterPosition/Scale: in front of the server, cab-side of the clear middle; front faces inside.
REGISTER_POSITION=(-.90,.82,1.80)
REGISTER_SCALE=2.2
body=bpy.data.objects['Truck_Body']; hatch=bpy.data.objects['Truck_Hatch']
dg=bpy.context.evaluated_depsgraph_get()
def tree(ob):
 bm=bmesh.new(); bm.from_object(ob,dg); bm.transform(ob.matrix_world); t=BVHTree.FromBMesh(bm); bm.free(); return t
def box_object(name,lo,hi,colour):
 me=bpy.data.meshes.new(name); bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1); bm.to_mesh(me); bm.free()
 ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob)
 ob.scale=[hi[i]-lo[i] for i in range(3)]; ob.location=[(hi[i]+lo[i])/2 for i in range(3)]
 m=bpy.data.materials.new(name+'_M'); m.diffuse_color=(*colour,1); m.use_nodes=True
 m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(*colour,1); me.materials.append(m)
 bpy.context.view_layer.update(); return ob
body_tree=tree(body)
def check(label,ob):
 hits=body_tree.overlap(tree(ob))
 print(('HIT ' if hits else 'CLEAR ')+label+(f' ({len(hits)} face pairs)' if hits else ''))
 return not hits
ok=True
# Aisle: three bodies at the hatch and the waiting places behind them.
for label,lo,hi in [('aisle at the hatch',(-.74,.20,.95),(-.44,2.00,2.74)),('waiting places',(-.74,2.00,.95),(-.44,2.95,2.74))]:
 ob=box_object('Check_'+label,lo,hi,(1,0,0)); ok&=check(label,ob); bpy.data.objects.remove(ob,do_unlink=True)
# Equipment, each lifted 2 mm off the worktop so resting on it isn't a hit.
placed=[]
for i,name in enumerate(PIECES):
 before=set(bpy.data.objects)
 bpy.ops.import_scene.fbx(filepath=f'{EQ}/{name}/{name}.fbx')
 new=[o for o in bpy.data.objects if o not in before]
 for o in new:
  if o.name.endswith('_Collision'): bpy.data.objects.remove(o,do_unlink=True); continue
  # Keep the importer's axis conversion; turn the front (-Y) to the hatch (-X) and move it onto the spot.
  o.matrix_world=Matrix.Translation((LINE_X,LINE_Y[i],TOP+.002))@Matrix.Rotation(math.radians(-90),4,'Z')@o.matrix_world
  img=bpy.data.images.load(os.path.abspath(f'{EQ}/{name}/T_{name}_BaseColor.png'),check_existing=True)
  m=bpy.data.materials.new(name+'_Review'); m.use_nodes=True; nt=m.node_tree
  tex=nt.nodes.new('ShaderNodeTexImage'); tex.image=img
  nt.links.new(tex.outputs['Color'],nt.nodes['Principled BSDF'].inputs['Base Color'])
  o.data.materials.clear(); o.data.materials.append(m); placed.append((name,o,i+1))
bpy.context.view_layer.update(); dg=bpy.context.evaluated_depsgraph_get()
for name,o,spot in placed: ok&=check(f'{name} on spot {spot}',o)
for (a,oa,_),(b,ob,_) in zip(placed,placed[1:]):
 hits=tree(oa).overlap(tree(ob)); print(('HIT ' if hits else 'CLEAR ')+f'{a} / {b}'); ok&=not hits
for name,o,_ in placed:
 pts=[o.matrix_world@v.co for v in o.data.vertices]
 print(f'EXTENT {name}: x {min(p.x for p in pts):.3f}..{max(p.x for p in pts):.3f} y {min(p.y for p in pts):.3f}..{max(p.y for p in pts):.3f} z top {max(p.z for p in pts):.3f}')
# Register, with the same FBX and uniform scale as the prefab. Its drawer slightly overhangs the shallow counter.
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath='art/props/CashRegister/CashRegister.fbx')
register=None
for o in [o for o in bpy.data.objects if o not in before]:
 if o.name.endswith('_Collision'): bpy.data.objects.remove(o,do_unlink=True); continue
 o.matrix_world=Matrix.Translation(REGISTER_POSITION)@Matrix.Rotation(math.radians(90),4,'Z')@Matrix.Scale(REGISTER_SCALE,4)@o.matrix_world
 register=o
 img=bpy.data.images.load(os.path.abspath('art/props/CashRegister/T_CashRegister_BaseColor.png'),check_existing=True)
 m=bpy.data.materials.new('CashRegister_Review'); m.use_nodes=True; nt=m.node_tree
 tex=nt.nodes.new('ShaderNodeTexImage'); tex.image=img
 nt.links.new(tex.outputs['Color'],nt.nodes['Principled BSDF'].inputs['Base Color'])
 o.data.materials.clear(); o.data.materials.append(m)
assert register is not None
bpy.context.view_layer.update(); dg=bpy.context.evaluated_depsgraph_get()
pts=[register.matrix_world@v.co for v in register.data.vertices]
lo=Vector([min(p[i] for p in pts) for i in range(3)]); hi=Vector([max(p[i] for p in pts) for i in range(3)])
print(f'EXTENT CashRegister: x {lo.x:.3f}..{hi.x:.3f} y {lo.y:.3f}..{hi.y:.3f} z {lo.z:.3f}..{hi.z:.3f}')
# The source drawer is 0.13 m deep; its centre and 84% of its depth rest on the counter, bottom exactly at 1.80.
supported=min(-.78,REGISTER_POSITION[0]+.065*REGISTER_SCALE)-max(-1.02,REGISTER_POSITION[0]-.065*REGISTER_SCALE)
rests=abs(lo.z-1.80)<1e-5 and supported/(.13*REGISTER_SCALE)>.80
inside=lo.y>.20 and hi.y<2.00 and hi.z<2.82 and lo.x> -1.10 and hi.x<-.74 and hi.y<1.00
print(('CLEAR ' if rests and inside else 'HIT ')+'register support, crew clearance and open middle'); ok&=rests and inside
# Lift only during intersection checks so the intended contact with the counter is not a hit.
register.location.z+=.002
bpy.context.view_layer.update(); ok&=check('CashRegister on serving counter',register)
rt=tree(register)
for name,o,_ in placed:
 hits=rt.overlap(tree(o)); print(('HIT ' if hits else 'CLEAR ')+f'CashRegister / {name}'); ok&=not hits
saved_rotation=hatch.rotation_euler.copy()
for angle in range(0,81,10):
 hatch.rotation_euler=(0,math.radians(angle),0)
 hits=rt.overlap(tree(hatch)); print(('HIT ' if hits else 'CLEAR ')+f'CashRegister / hatch {angle} degrees'); ok&=not hits
hatch.rotation_euler=saved_rotation
register.location.z-=.002
print('INTERIOR '+('CLEAR' if ok else 'BLOCKED'))
def render():
 # Stand-in people: body, head, face patch towards the hatch.
 crew=[]
 for x,y,who in PEOPLE:
  shirt=(.55,.10,.10) if who=='player' else (.12,.30,.30)
  parts=[box_object('Person_body',(x-.13,y-.20,.94),(x+.13,y+.20,2.40),shirt),
         box_object('Person_head',(x-.11,y-.09,2.42),(x+.09,y+.09,2.70),(.80,.62,.48)),
         box_object('Person_eyes',(x-.115,y-.06,2.58),(x-.105,y+.06,2.61),(.05,.05,.05))]
  if who=='crew': crew.extend(parts)
 # Renders.
 sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.device='CPU'; sc.cycles.samples=40; sc.cycles.use_denoising=True
 sc.render.resolution_x,sc.render.resolution_y=1100,800
 ground=bpy.data.meshes.new('G'); bm=bmesh.new(); bmesh.ops.create_grid(bm,x_segments=1,y_segments=1,size=60); bm.to_mesh(ground); bm.free()
 g=bpy.data.objects.new('ReviewGround',ground); bpy.context.collection.objects.link(g); g.location.z=-.05
 gm=bpy.data.materials.new('GroundM'); gm.diffuse_color=(.30,.31,.32,1); g.data.materials.append(gm)
 light=bpy.data.lights.new('Interior_Light','AREA'); light.energy=120; light.size=1.6
 lo=bpy.data.objects.new('Interior_Light',light); bpy.context.collection.objects.link(lo); lo.location=(.0,1.1,2.93)
 cam=bpy.data.objects.get('Review_Camera')
 if cam is None:
  cam=bpy.data.objects.new('Review_Camera',bpy.data.cameras.new('Review_Camera')); bpy.context.collection.objects.link(cam)
 sc.camera=cam; cam.data.type='PERSP'
 views=[('interior-pavement-open.png',True,(-3.60,1.10,1.65),(0,1.10,2.15),30),
        ('interior-pavement-oblique.png',True,(-3.40,-1.30,1.65),(0,1.20,2.10),30),
        ('interior-pavement-closed.png',False,(-3.60,1.10,1.65),(0,1.10,2.00),30),
        ('interior-game-camera.png',True,(-5.20,-1.40,4.30),(0,1.10,1.90),32),
        ('interior-inside.png',True,(-.25,2.90,2.45),(.10,.30,1.85),16)]
 # Last: the player alone, so the line shows either side of them.
 views.append(('interior-pavement-player-only.png',True,(-3.60,1.10,1.65),(0,1.10,2.15),30))
 for filename,opened,loc,target,lens in views:
  if filename.endswith('player-only.png'):
   for o in crew: o.hide_render=True
  hatch.rotation_euler=(0,math.radians(80) if opened else 0,0)
  cam.location=loc; cam.rotation_euler=(Vector(target)-Vector(loc)).to_track_quat('-Z','Y').to_euler(); cam.data.lens=lens
  sc.render.filepath=os.path.abspath(f'{OUT}/{filename}'); bpy.ops.render.render(write_still=True)
  print('RENDER',sc.render.filepath)
if not CHECK_ONLY: render()

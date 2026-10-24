"""Verify both logo panels (0.55 x 0.55 m) against the exported truck body.
Run with Blender -b --factory-startup --python art/tools/review_truck_logos.py.
No source scene or asset is changed.
"""
from pathlib import Path
import bpy,json
ROOT = Path(__file__).resolve().parents[2]
from mathutils import Vector
from mathutils.bvhtree import BVHTree
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'art/truck/FoodTruck.fbx'))
body=next(o for o in bpy.context.scene.objects if o.name=='Truck_Body')
tree=BVHTree.FromPolygons([body.matrix_world@v.co for v in body.data.vertices], [list(p.vertices) for p in body.data.polygons])
for side in [-1,1]:
 hits=[]
 for i in range(101):
  for j in range(101):
   y=.575+.55*i/100; z=(2.375 if side == 1 else 1.045)+.55*j/100
   loc,n,face,d=tree.ray_cast(Vector((side*2,y,z)), Vector((-side,0,0)), 3)
   mount = 1.104 # Blender X is the opposite of Unity X
   assert loc is not None and .003 <= mount-abs(loc.x) <= .005, (side,y,z,loc)
   hits.append(list(loc))
 print('PASS logo side',side,len(hits),'samples; surface X range',min(abs(h[0]) for h in hits),max(abs(h[0]) for h in hits))

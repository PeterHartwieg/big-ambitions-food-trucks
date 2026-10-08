"""Deterministically rebuild the detailed asset in an empty or existing scene.
Run in Blender via exec(compile(open(path).read(), path, 'exec')).
TRUCK_STAGE: geometry, textures, final, or all (default rebuild + textures + export).
Hatch is saved closed; +80 degrees about local Y displays the serving awning.
Marker/work lights have independent UV islands using EM_1.R (headlight circuit).
The original wheel geometry, pivots, opening and FBX settings are unchanged.
"""
import bpy, bmesh, math, os, json
import numpy as np
from mathutils import Vector, Matrix
OUT = 'art/truck'
os.makedirs(OUT+'/renders', exist_ok=True)
PALETTE = {
 'paint': ((.94,.79,.52),0,.60,1), 'stripe': ((.12,.37,.36),0,.60,1),
 'rubber': ((.025,.032,.038),0,.20,0), 'plastic': ((.10,.13,.15),0,.28,0),
 'glass': ((.065,.15,.19),0,.90,0), 'steel': ((.63,.70,.72),1,.90,0),
 'rim': ((.70,.74,.74),1,.75,0), 'interior': ((.14,.18,.18),0,.25,0),
 'head': ((1,.88,.65),0,.70,0), 'tail': ((.65,.018,.025),0,.65,0),
 'reverse': ((.95,.89,.74),0,.70,0), 'right': ((1,.38,.025),0,.60,0),
 'left': ((1,.38,.025),0,.60,0),
 'marker': ((1,.48,.055),0,.60,0), 'worklight': ((1,.91,.70),0,.70,0),
 'trim': ((.27,.34,.34),1,.62,0), 'seam': ((.49,.47,.39),0,.50,1),
 'warm': ((.90,.57,.24),0,.45,0), 'chalk': ((.88,.84,.69),0,.40,0),
 'tank': ((.72,.76,.72),1,.65,0)
}
KEYS=list(PALETTE)
class Builder:
 def __init__(self): self.v=[]; self.f=[]; self.tags=[]
 def part(self, verts, faces, tag):
  off=len(self.v); self.v.extend(verts)
  for f in faces: self.f.append(tuple(off+i for i in f)); self.tags.append(tag)
 def box(self, lo, hi, tag):
  x,y,z=lo; X,Y,Z=hi
  self.part([(x,y,z),(X,y,z),(X,Y,z),(x,Y,z),(x,y,Z),(X,y,Z),(X,Y,Z),(x,Y,Z)],[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],tag)
 def prism(self, xmin,xmax, yz,tag):
  n=len(yz); verts=[(x,y,z) for x in (xmin,xmax) for y,z in yz]
  faces=[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
  # fan caps; convex yz outlines
  faces += [(0,i+1,i) for i in range(1,n-1)] + [(n,n+i,n+i+1) for i in range(1,n-1)]
  self.part(verts,faces,tag)
 def cyl(self, centre, radius,depth,axis,tag,n=16):
  c=Vector(centre); d=Vector(axis).normalized(); u=d.cross(Vector((0,0,1)))
  if u.length<.1: u=d.cross(Vector((0,1,0)))
  u.normalize(); w=d.cross(u); verts=[]
  for sign in (-1,1):
   for i in range(n): verts.append(tuple(c+d*(sign*depth/2)+radius*(u*math.cos(i*2*math.pi/n)+w*math.sin(i*2*math.pi/n))))
  faces=[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
  faces += [(0,i+1,i) for i in range(1,n-1)] + [(n,n+i,n+i+1) for i in range(1,n-1)]
  self.part(verts,faces,tag)
 def rod(self,a,b,r,tag,n=8):
  a,b=Vector(a),Vector(b); self.cyl((a+b)/2,r,(b-a).length,b-a,tag,n)
 def object(self,name,origin=(0,0,0)):
  old=bpy.data.objects.get(name)
  if old: bpy.data.objects.remove(old,do_unlink=True)
  origin=Vector(origin); me=bpy.data.meshes.new(name+'_Mesh')
  me.from_pydata([tuple(Vector(v)-origin) for v in self.v],[],self.f); me.update()
  ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob); ob.location=origin
  # Recalculate each disconnected closed primitive outwards.
  bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
  attr=me.attributes.new('surface_class','INT','FACE')
  for i,t in enumerate(self.tags): attr.data[i].value=KEYS.index(t)
  for key in KEYS:
   mat=bpy.data.materials.get('Preview_'+key) or bpy.data.materials.new('Preview_'+key)
   mat.diffuse_color=(*PALETTE[key][0],1); mat.use_nodes=True
   bs=mat.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*PALETTE[key][0],1); bs.inputs['Roughness'].default_value=1-PALETTE[key][2]
   bs.inputs['Metallic'].default_value=PALETTE[key][1]
   me.materials.append(mat)
  for p in me.polygons: p.material_index=attr.data[p.index].value
  return ob

def add_working_details(b):
 # Faceted black flares: closed ribbon cross sections, outside wheel clearance.
 for sign in (-1,1):
  xa,xb=sorted((sign*1.101,sign*1.155))
  for axle in (-1.8,1.8):
   outer=[(-.53,.35),(-.53,.69),(-.38,.94),(.38,.94),(.53,.69),(.53,.35)]
   inner=[(-.455,.35),(-.455,.67),(-.335,.86),(.335,.86),(.455,.67),(.455,.35)]
   verts=[(x,axle+y,z) for x in (xa,xb) for profile in (outer,inner) for y,z in profile]
   faces=[]
   for i in range(5):
    j=i+1
    faces.extend([(i,j,6+j,6+i),(12+i,18+i,18+j,12+j),(i,12+i,12+j,j),(6+i,6+j,18+j,18+i)])
   faces.extend([(0,6,18,12),(5,17,23,11)])
   b.part(verts,faces,'rubber')
  # Mud flaps sit behind rear wheels, clear of the suspension envelope.
  xa,xb=sorted((sign*.76,sign*1.14)); b.box((xa,2.32,.20),(xb,2.355,.60),'rubber')
  xa,xb=sorted((sign*1.10,sign*1.20));b.box((xa,-1.31,.39),(xb,-.91,.44),'steel')
  # Low protective rail and upper drip channel.
  xa,xb=sorted((sign*1.102,sign*1.125))
  b.box((xa,-.89,.96),(xb,2.95,1.005),'trim')
  b.box((xa,-1.88,2.805),(xb,3.02,2.85),'steel')
  # Body panel joins kept away from the opening.
  for y in (-.86,2.75): b.box((xa,y,1.035),(xb,y+.012,2.77),'seam')
  # Separate marker lens islands, using EM1 headlights channel (running lights).
  for y in (-.75,2.73):
   xa,xb=sorted((sign*1.105,sign*1.136));b.box((xa,y-.075,1.065),(xb,y+.075,1.15),'rubber')
   xa,xb=sorted((sign*1.136,sign*1.145));b.box((xa,y-.054,1.082),(xb,y+.054,1.133),'marker')
  for y in (-1.73,2.88):
   xa,xb=sorted((sign*1.105,sign*1.132));b.box((xa,y-.065,2.72),(xb,y+.065,2.78),'rubber')
   xa,xb=sorted((sign*1.132,sign*1.14));b.box((xa,y-.048,2.731),(xb,y+.048,2.769),'marker')
 # Chassis rails and left utility equipment, between fixed axles.
 for x in (-.66,.66):b.box((x-.055,-1.30,.31),(x+.055,1.29,.42),'plastic')
 b.box((.78,-.58,.37),(1.16,.39,.65),'tank')
 for y in (-.40,.19):b.box((.77,y,.36),(1.17,y+.045,.66),'trim')
 b.rod((1.14,.50,.41),(1.14,1.25,.41),.035,'steel',8)
 b.rod((1.14,1.25,.41),(1.21,1.30,.38),.035,'plastic',8)
 # Generator service hatch, louvres and recessed pull.
 b.box((1.101,.12,1.29),(1.113,1.41,2.15),'trim')
 b.box((1.113,.155,1.325),(1.124,1.375,2.115),'paint')
 for z in (1.49,1.59,1.69,1.79,1.89):
  b.box((1.124,.28,z),(1.129,1.15,z+.036),'interior')
  b.box((1.129,.28,z+.033),(1.139,1.15,z+.047),'steel')
 b.box((1.124,1.23,1.72),(1.145,1.32,1.82),'rubber')
 b.box((1.145,1.25,1.743),(1.15,1.30,1.78),'steel')
 # Broad left side interruption: a small stainless backsplash access panel.
 b.box((1.102,1.72,1.45),(1.119,2.50,2.38),'steel')
 b.box((1.119,1.76,1.49),(1.124,2.46,2.34),'tank')
 # Curb menu panel with large, abstract burger and cup pictograms.
 b.box((-1.13,2.14,1.29),(-1.102,2.68,2.16),'steel')
 b.box((-1.139,2.17,1.32),(-1.13,2.65,2.13),'interior')
 b.prism(-1.147,-1.14,[(2.25,1.87),(2.28,1.94),(2.36,1.98),(2.48,1.98),(2.55,1.94),(2.58,1.87)],'warm')
 b.box((-1.147,2.25,1.825),(-1.14,2.58,1.855),'chalk')
 b.box((-1.147,2.27,1.78),(-1.14,2.56,1.815),'warm')
 b.prism(-1.147,-1.14,[(2.32,1.46),(2.49,1.46),(2.53,1.67),(2.28,1.67)],'chalk')
 b.rod((-1.15,2.47,1.66),(-1.15,2.48,1.74),.012,'warm',6)
 # Two sheltered serving lamps.
 for y in (.43,1.77):
  b.box((-1.16,y-.10,2.30),(-1.105,y+.10,2.36),'plastic')
  b.box((-1.164,y-.073,2.302),(-1.158,y+.073,2.337),'worklight')
 # Sliding window panes sit inside the room beyond the required clear recess.
 # Both pane frames parked on rear half, leaving front half open.
 for x,ya,yb in [(-.795,1.10,1.94),(-.77,1.04,1.88)]:
  for y in (ya,yb):b.box((x,y,1.24),(x+.016,y+.026,2.105),'steel')
  for z in (1.235,2.075):b.box((x,ya,z),(x+.016,yb+.026,z+.028),'steel')
  # Tint face in the parked outer pane only; separate inner-pane frame stays visible.
  if x<-.78:b.box((x+.004,ya+.026,1.27),(x+.009,yb,2.075),'glass')
 b.box((-.815,.20,1.205),(-.785,2.00,1.235),'steel')
 # Interior stainless workstation, hood, shelves and flat cooking surface.
 b.box((-.59,.28,1.16),(.21,.99,1.33),'plastic')
 b.box((-.57,.30,1.33),(.19,.97,1.38),'steel')
 for y in (.37,.47,.57,.67,.77,.87):b.box((-.55,y,1.379),(.17,y+.023,1.39),'plastic')
 for y in (.40,.63,.86):b.cyl((-.595,y,1.27),.028,.016,(1,0,0),'rubber',8)
 b.prism(-.45,.80,[(.22,2.02),(1.06,2.02),(1.06,2.21),(.22,2.21)],'steel')
 b.box((-.45,.27,2.019),(.61,1.01,2.027),'trim')
 b.box((.87,.22,1.35),(.925,1.05,2.10),'tank')
 b.box((-.50,.26,1.70),(.25,.95,1.745),'steel')
 for y in (.40,.71):
  b.cyl((-.05,y,1.827),.063,.16,(0,0,1),'tank',8)
  b.cyl((-.05,y,1.913),.066,.014,(0,0,1),'plastic',8)
 for z in (1.64,1.95):
  b.box((.49,1.23,z),(.90,2.58,z+.045),'steel')
  for y in (1.25,2.49):b.box((.865,y,z-.15),(.90,y+.035,z),'trim')
 for y in (1.38,1.68,1.98):
  b.cyl((.68,y,1.75),.074,.16,(0,0,1),'tank',8)
  b.cyl((.68,y,1.833),.077,.016,(0,0,1),'plastic',8)
 # Front bumper recess, corner protectors and hood pressed seam.
 b.box((-.29,-2.656,.50),(.29,-2.650,.62),'rubber')
 b.box((-.24,-2.659,.518),(.24,-2.656,.60),'tank')
 for x in (-.99,.91):b.box((x,-2.655,.47),(x+.08,-2.648,.66),'rubber')
 # Rear door glazing; ladder on one corner and shallow two-cylinder rack opposite.
 for x in (-.36,.36):
  b.box((x-.22,3.058,1.88),(x+.22,3.068,2.29),'rubber')
  b.box((x-.19,3.068,1.91),(x+.19,3.073,2.26),'glass')
  for z in (.97,1.70,2.38):
   b.cyl(((-.67 if x<0 else .67),3.09,z),.028,.17,(0,0,1),'steel',8)
 for x in (-.89,-.58):b.rod((x,3.125,.65),(x,3.125,2.86),.021,'steel',8)
 for z in (.76,1.05,1.34,1.63,1.92,2.21,2.50,2.79):b.rod((-.89,3.125,z),(-.58,3.125,z),.018,'steel',6)
 # Narrow tanks fit entirely inside the original rear bumper extent.
 b.box((.13,3.065,.60),(.70,3.15,.65),'plastic')
 for x in (.29,.55):
  b.cyl((x,3.092,.99),.057,.61,(0,0,1),'tank',10)
  b.cyl((x,3.092,1.31),.023,.038,(0,0,1),'steel',8)
  b.rod((x-.042,3.092,1.337),(x+.042,3.092,1.337),.010,'warm',6)
 b.box((.12,3.143,.89),(.72,3.153,.93),'trim')
 b.box((-.23,3.151,.423),(.23,3.155,.52),'rubber')
 b.box((-.19,3.155,.44),(.19,3.158,.503),'tank')
 # Roof equipment: deliberately shallow, maximum z=3.05m.
 b.box((-.53,-.75,2.90),(.53,.35,2.93),'rubber')
 b.prism(-.49,.49,[(-.70,2.93),(.30,2.93),(.30,3.015),(.22,3.045),(-.59,3.045),(-.70,3.005)],'tank')
 for y in (-.51,-.39,-.27,-.15,-.03,.09):b.box((-.32,y,3.046),(.32,y+.038,3.049),'trim')
 b.box((-.40,1.27,2.90),(.40,1.98,2.97),'steel')
 b.prism(-.46,.46,[(1.20,2.975),(2.05,2.975),(1.98,3.05),(1.27,3.05)],'steel')
 b.box((-.34,1.20,2.973),(.34,1.214,3.007),'interior')
 for y in (-1.87,3.02):b.box((-1.10,y,2.80),(1.10,y+.02,2.845),'steel')

def add_hatch_details(h):
 # Underside teal bands and paired diagonal folding stays, all move with the hatch.
 for ya in (.39,.94,1.49):h.box((-1.099,ya,1.24),(-1.096,ya+.24,2.11),'stripe')
 for y in (.27,1.93):
  h.rod((-1.09,y,2.09),(-.98,y,1.36),.012,'steel',8)
  h.box((-1.093,y-.023,1.32),(-.97,y+.023,1.36),'trim')

def make_geometry(detail=False):
 if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
 for o in list(bpy.data.objects):
  if o.name.startswith('Truck_'): bpy.data.objects.remove(o,do_unlink=True)
 bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
 b=Builder()
 # Roof and enclosed food compartment with a genuine curb-side opening.
 b.box((-1.10,-1.93,2.76),(1.10,3.04,2.90),'paint')
 b.box((1.02,-.91,.91),(1.10,2.97,2.76),'paint')
 b.box((-1.10,2.00,1.17),(-1.02,2.97,2.76),'paint')
 b.box((-1.10,-.91,1.17),(-1.02,.20,2.76),'paint')
 b.box((-1.10,.20,2.15),(-1.02,2.00,2.76),'paint')
 b.box((-1.10,-.91,.91),(-1.02,2.97,1.17),'paint')
 b.box((-1.10,2.97,.91),(1.10,3.04,2.76),'paint')
 b.box((-.72,-2.42,.35),(.72,3.04,.85),'plastic')
 b.box((-1.02,-.90,.85),(1.02,2.97,.94),'interior')
 # Solid skirts between wheel wells; arch cuts through full width at both axles.
 for xa,xb in [(-1.10,-.72),(.72,1.10)]:
  for ya,yb in [(-2.48,-2.26),(-1.34,1.34),(2.26,3.04)]:
   b.box((xa,ya,.35),(xb,yb,.65),'stripe'); b.box((xa,ya,.65),(xb,yb,.85),'paint')
  b.box((xa,-2.48,.85),(xb,3.04,.91),'stripe')
 # Sloping snub nose, upright cab with flat windscreen.
 b.prism(-1.04,1.04,[(-2.51,.85),(-1.91,.85),(-1.91,1.42),(-2.51,1.14)],'paint')
 b.box((-1.10,-1.93,.91),(1.10,-.91,1.48),'paint')
 b.box((-1.02,-1.87,1.48),(1.02,-.91,2.76),'interior')
 # Window frames in front and on both doors.
 b.box((-1.08,-1.945,1.46),(1.08,-1.905,1.61),'paint')
 b.box((-1.08,-1.945,2.58),(1.08,-1.905,2.76),'paint')
 for lo,hi in [((-1.08,-1.945,1.61),(-.97,-1.905,2.58)),((.97,-1.945,1.61),(1.08,-1.905,2.58))]: b.box(lo,hi,'paint')
 b.box((-.96,-1.951,1.62),(.96,-1.928,2.57),'rubber')
 b.box((-.92,-1.959,1.66),(.92,-1.950,2.53),'glass')
 for s in (-1,1):
  xa,xb=sorted([s*1.02,s*1.10]); b.box((xa,-1.90,1.48),(xb,-1.78,2.76),'paint'); b.box((xa,-1.02,1.48),(xb,-.91,2.76),'paint')
  b.box((xa,-1.78,2.57),(xb,-1.02,2.76),'paint')
  xa,xb=sorted([s*1.095,s*1.105]); b.box((xa,-1.77,1.51),(xb,-1.03,2.57),'rubber')
  xa,xb=sorted([s*1.105,s*1.109]); b.box((xa,-1.73,1.56),(xb,-1.07,2.53),'glass')
 b.box((-1.09,-2.65,.47),(1.09,-2.51,.66),'plastic')
 b.box((-1.10,3.04,.40),(1.10,3.15,.56),'plastic')
 # Frame, counter and rear dark inner wall deliberately beyond .30m recess.
 for ya,yb in [(.155,.20),(2.00,2.045)]: b.box((-1.115,ya,1.17),(-1.01,yb,2.20),'steel')
 b.box((-1.115,.20,1.17),(-1.01,2.,1.20),'steel')
 b.box((-1.115,.20,2.15),(-1.01,2.,2.19),'steel')
 b.box((-1.26,.25,1.105),(-1.10,1.95,1.15),'steel')
 b.box((-.45,-.70,1.05),(.98,2.80,1.15),'steel')
 b.box((.93,-.80,1.15),(.99,2.85,2.67),'interior')
 # Hinge rail spans specified line.
 b.cyl((-1.10,1.10,2.15),.018,1.86,(0,1,0),'steel',12)
 if detail:
  # Round sealed beam headlights with simple metal bezels.
  for s in (-1,1):
   b.cyl((s*.77,-2.525,.995),.143,.032,(0,1,0),'steel',16)
   b.cyl((s*.77,-2.548,.995),.113,.020,(0,1,0),'head',16)
   # Front corner lamps / rear stacked lenses, independently classified UV islands.
   turn='left' if s>0 else 'right'
   b.box((s*.83-.09,-2.535,1.23),(s*.83+.09,-2.517,1.31),turn)
   b.box((s*.92-.105,3.039,.77),(s*.92+.105,3.061,1.30),'rubber')
   b.box((s*.92-.075,3.061,1.10),(s*.92+.075,3.073,1.26),turn)
   b.box((s*.92-.075,3.061,.92),(s*.92+.075,3.073,1.08),'tail')
   b.box((s*.92-.075,3.061,.81),(s*.92+.075,3.073,.90),'reverse')
   # Mirrors, supports, door outlines and handles. Side extrema = 1.25.
   b.rod((s*1.10,-1.72,1.71),(s*1.21,-1.92,1.86),.018,'plastic')
   xa,xb=sorted((s*1.18,s*1.25)); b.box((xa,-2.02,1.80),(xb,-1.86,2.18),'plastic')
   xa,xb=sorted((s*1.185,s*1.245)); b.box((xa,-1.858,1.84),(xb,-1.852,2.14),'steel')
   xa,xb=sorted((s*1.100,s*1.114))
   b.box((xa,-1.32,.96),(xb,-1.30,1.47),'plastic')
   b.box((xa,-.96,.94),(xb,-.94,1.48),'plastic')
   b.box((xa,-1.31,.94),(xb,-.94,.96),'plastic')
   b.box((xa,-1.20,1.35),(xb,-1.02,1.39),'steel')
  b.box((-.49,-2.532,.86),(.49,-2.515,1.18),'rubber')
  for z in (.89,.955,1.02,1.085,1.15): b.box((-.46,-2.542,z),(.46,-2.534,z+.017),'steel')
  # windshield split and discreet wipers
  b.box((-.018,-1.965,1.66),(.018,-1.957,2.53),'rubber')
  for x in (-.43,.43): b.rod((x-.26,-1.974,1.70),(x+.20,-1.974,1.76),.011,'plastic')
  # Hood's side lower painted line and front trim, no lettering.
  b.box((-.97,-2.515,1.15),(.97,-2.510,1.185),'stripe')
  # Rear flush access door and stainless hinge leaves.
  b.box((-.69,3.041,.68),(.69,3.047,2.53),'rubber')
  b.box((-.66,3.047,.72),(.66,3.057,2.50),'paint')
  b.box((-.012,3.057,.72),(.012,3.063,2.50),'plastic')
  b.box((-.17,3.063,1.50),(-.05,3.080,1.54),'steel')
  for x in (-.67,.67):
   for z in (1.0,2.2): b.box((x-.038,3.058,z-.07),(x+.038,3.072,z+.07),'steel')
  b.box((-.85,3.04,.56),(.85,3.15,.595),'steel')
  b.cyl((1.105,2.30,.98),.076,.018,(1,0,0),'rubber',16)
  b.cyl((1.118,2.30,.98),.060,.012,(1,0,0),'steel',16)
  # Small interior back counter / backsplash, enough to read through open hatch.
  b.box((.74,-.30,1.15),(.86,2.50,1.33),'steel')
  for y in (.43,1.75): b.box((.56,y,1.15),(.73,y+.30,1.34),'plastic')
  # Shelf supports outside the recess, well above the rear wheel envelope.
  for y in (.34,1.84): b.rod((-1.105,y,.97),(-1.235,y,1.10),.014,'steel')
 if detail: add_working_details(b)
 body=b.object('Truck_Body')
 hatch=Builder(); hatch.box((-1.14,.20,1.20),(-1.10,2.00,2.15),'paint')
 # Edge folds give a tiny visible border, all inside the 0.04m thickness.
 if detail:
  hatch.box((-1.143,.20,1.20),(-1.140,2.,1.23),'steel')
  for y in (.20,1.97): hatch.box((-1.143,y,1.23),(-1.140,y+.03,2.15),'steel')
  add_hatch_details(hatch)
 hatch.object('Truck_Hatch',(-1.10,1.10,2.15))
 for side,s in [('L',1),('R',-1)]:
  for end,y in [('F',-1.80),('R',1.80)]:
   w=Builder(); n=16
   # Six lathed rings form a closed annular tyre; rim and cap do not overlap.
   rings=[(-.115,.248),(-.115,.32),(-.09,.38),(.09,.38),(.115,.32),(.115,.248)]
   verts=[(x,r*math.sin(i*2*math.pi/n),r*math.cos(i*2*math.pi/n)) for x,r in rings for i in range(n)]
   faces=[]
   for j in range(6):
    k=(j+1)%6
    faces += [(j*n+i,j*n+(i+1)%n,k*n+(i+1)%n,k*n+i) for i in range(n)]
   w.part(verts,faces,'rubber')
   for sign in (-1,1):
    w.cyl((sign*.104,0,0),.248,.008,(1,0,0),'rim',16)
    w.cyl((sign*.111,0,0),.12,.008,(1,0,0),'steel',12)
   ob=w.object('Truck_Wheel_'+end+side,(s*.87,y,.38))
   # builder contains local wheel geometry: object() expects world points.
   for v in ob.data.vertices: v.co += ob.location
 body['front_direction']='-Y'; body['serving_side']='-X'
 bpy.context.view_layer.update()
 return body

def setup_render():
 sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.device='CPU'; sc.cycles.samples=48; sc.cycles.use_denoising=True
 sc.render.resolution_x=1100; sc.render.resolution_y=850; sc.render.resolution_percentage=100
 sc.render.image_settings.file_format='PNG'; sc.view_settings.view_transform='AgX'
 if sc.world is None: sc.world=bpy.data.worlds.new('Review_World')
 sc.world.use_nodes=True; sc.world.node_tree.nodes.get('Background').inputs[0].default_value=(.14,.19,.23,1); sc.world.node_tree.nodes.get('Background').inputs[1].default_value=.55
 def obj(name,data):
  ob=bpy.data.objects.get(name)
  if ob: bpy.data.objects.remove(ob,do_unlink=True)
  ob=bpy.data.objects.new(name,data); sc.collection.objects.link(ob); return ob
 cam=obj('Review_Camera',bpy.data.cameras.new('Review_Camera')); cam.data.type='ORTHO'; cam.data.ortho_scale=8.3; cam.location=(-7,-8,5.2); cam.rotation_euler=(Vector((0,.20,1.35))-cam.location).to_track_quat('-Z','Y').to_euler(); sc.camera=cam
 for name,loc,power,size in [('Key',(-4,-5,8),1800,6),('Fill',(5,-1,5),1400,5),('Rim',(-2,6,7),2000,4)]:
  l=obj('Review_'+name,bpy.data.lights.new('Review_'+name,'AREA')); l.location=loc; l.data.energy=power; l.data.shape='DISK'; l.data.size=size; l.rotation_euler=(Vector((0,0,1.2))-l.location).to_track_quat('-Z','Y').to_euler()
 # Review-only kitchen fill represents interior fluorescent lighting.
 l=obj('Review_Kitchen',bpy.data.lights.new('Review_Kitchen','AREA'));l.location=(.15,.70,2.35);l.data.energy=45;l.data.size=1.0;l.rotation_euler=(Vector((-.55,.75,1.35))-l.location).to_track_quat('-Z','Y').to_euler()
 # Useful material viewport on reopen.
 for screen in bpy.data.screens:
  for area in screen.areas:
   if area.type=='VIEW_3D':
    area.spaces.active.shading.type='MATERIAL'; area.spaces.active.region_3d.view_distance=9; area.spaces.active.region_3d.view_location=(0,.2,1.35); area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()

def render_stage(filename, opened=False, rear=False):
 sc=bpy.context.scene; h=bpy.data.objects['Truck_Hatch']; h.rotation_euler[1]=math.radians(80) if opened else 0
 cam=sc.camera; prev=cam.location.copy(); prot=cam.rotation_euler.copy()
 if rear: cam.location=(-7,8,4.8); cam.rotation_euler=(Vector((0,.35,1.3))-cam.location).to_track_quat('-Z','Y').to_euler()
 ground=Builder(); ground.box((-200,-200,-.055),(200,200,-.045),'plastic'); go=ground.object('ReviewGround')
 mat=bpy.data.materials.get('ReviewGround_Mat') or bpy.data.materials.new('ReviewGround_Mat'); mat.diffuse_color=(.16,.21,.25,1); go.data.materials.clear(); go.data.materials.append(mat)
 try:
  sc.render.filepath=OUT+'/renders/'+filename; bpy.ops.render.render(write_still=True)
 finally:
  bpy.data.objects.remove(go,do_unlink=True); h.rotation_euler=(0,0,0); cam.location=prev; cam.rotation_euler=prot

def face_pixels(ob,p,tag,size,pad=2):
 """Paint procedural flat-colour detail in each island using surface coordinates."""
 rgb,metal,smooth,alpha=PALETTE[tag]
 t=np.clip((np.arange(size)+.5-pad)/(size-2*pad),0,1)
 u,v=np.meshgrid(t,t)
 vs=np.array([tuple(ob.matrix_world @ ob.data.vertices[i].co) for i in p.vertices])
 if len(vs)==4:
  world=(1-u)[...,None]*(1-v)[...,None]*vs[0]+u[...,None]*(1-v)[...,None]*vs[1]+u[...,None]*v[...,None]*vs[2]+(1-u)[...,None]*v[...,None]*vs[3]
 else:
  world=vs[0]+u[...,None]*(vs[1]-vs[0])+v[...,None]*(vs[2]-vs[0])
 x,y,z=world[:,:,0],world[:,:,1],world[:,:,2]
 diffuse=np.zeros((size,size,4),np.float32);diffuse[:]=[*rgb,alpha]
 ao=np.ones((size,size),np.float32)
 if tag in ('paint','stripe'):
  # Soft road-side darkening is encoded into both colour and mask G, with no noise.
  low=np.clip((1.10-z)/.78,0,1)
  shade=1-.10*low;ao=1-.16*low
  if abs(p.normal.x)>.8 and np.max(np.abs(x))>1.095:
   lines=(-.86,2.75,1.60) if np.mean(x)>0 else (-.86,-.18,2.75)
   for seam in lines:
    dist=np.abs(y-seam)
    line=(dist<.006)&(z>1.02)&(z<2.77)
    shade=np.where(line,.79,shade);ao=np.where(line,.84,ao)
    # Tiny painted rivet heads, lighter centres with a subtle occlusion ring.
    dz=np.abs((z-1.08+.10)%.20-.10)
    d=np.sqrt((y-seam-.028)**2+dz**2)
    ring=(d<.011)&(z>1.04)&(z<2.72)
    shade=np.where(ring,.84,shade);ao=np.where(ring,.87,ao)
    shade=np.where((d<.005)&ring,.99,shade)
  diffuse[:,:,:3]*=shade[:,:,None]
 elif tag in ('rubber','plastic','interior','trim'):
  ao[:]=.78 if tag=='interior' else .9
  diffuse[:,:,:3]*=.95 if tag=='interior' else 1
 # Painted tread diamonds and restrained scuff edges on steps and bumpers.
 if tag=='steel' and p.normal.z>.8 and np.mean(z)<.66:
  diag=np.minimum(np.abs((x*8+y*8)%1-.5),np.abs((x*8-y*8)%1-.5))
  tread=diag<.07
  diffuse[:,:,:3]*=np.where(tread,.72,1)[:,:,None]
  ao=np.where(tread,.84,1)
 if tag in ('steel','plastic','trim') and np.mean(z)<.68:
  edge=(u<.04)|(u>.96)|(v<.045)|(v>.955)
  diffuse[:,:,:3]=np.where(edge[:,:,None],np.minimum(diffuse[:,:,:3]*1.06+.018,1),diffuse[:,:,:3])
 mask=np.zeros((size,size,4),np.float32);mask[:,:,0]=metal;mask[:,:,1]=ao;mask[:,:,3]=smooth
 return diffuse,mask

def texture_and_uv():
 obs=[bpy.data.objects[n] for n in ['Truck_Body','Truck_Wheel_FL','Truck_Wheel_FR','Truck_Wheel_RL','Truck_Wheel_RR','Truck_Hatch']]
 # A deterministic square-bin atlas: large body surfaces get extra pixels for seams.
 entries=[]
 for ob in obs:
  for p in ob.data.polygons:
   tag=KEYS[ob.data.attributes['surface_class'].data[p.index].value]
   size=16
   if tag in ('paint','stripe','glass','steel','tank','plastic'):
    size=256 if p.area>3 else 160 if p.area>1 else 96 if p.area>.40 else 16
   entries.append((size,ob,p,tag))
 entries.sort(key=lambda e:-e[0]);occupied=np.zeros((128,128),dtype=bool);placements={}
 for size,ob,p,tag in entries:
  cells=size//16;found=False
  if cells==1:
   free=np.flatnonzero(~occupied)
   if not len(free):raise RuntimeError('Texture atlas full')
   row,col=divmod(int(free[0]),128);occupied[row,col]=True;placements[(ob.name,p.index)]=(col*16,row*16,size);continue
  for row in range(129-cells):
   for col in range(129-cells):
    if not occupied[row:row+cells,col:col+cells].any():
     occupied[row:row+cells,col:col+cells]=True;placements[(ob.name,p.index)]=(col*16,row*16,size);found=True;break
   if found:break
  if not found:raise RuntimeError('Texture atlas full')
 arr={k:np.zeros((2048,2048,4),dtype=np.float32) for k in ['D','MaskMap','N','E','EM_1','EM_2']}
 arr['N'][:]=[128/255,128/255,1,1];arr['MaskMap'][:]=[0,1,0,.2]
 for k in ['E','EM_1','EM_2']:arr[k][:,:,3]=1
 lights=[]
 for ob in obs:
  me=ob.data
  for old in list(me.uv_layers):me.uv_layers.remove(old)
  uv=me.uv_layers.new(name='UVMap')
  for p in me.polygons:
   tag=KEYS[me.attributes['surface_class'].data[p.index].value]
   x,y,size=placements[(ob.name,p.index)];pad=1 if size==16 else 2
   diffuse,mask=face_pixels(ob,p,tag,size,pad)
   arr['D'][y:y+size,x:x+size]=diffuse;arr['MaskMap'][y:y+size,x:x+size]=mask
   emission=[0,0,0,1]
   if tag in ('head','reverse','worklight'):emission=[1,.88,.65,1]
   elif tag=='tail':emission=[1,.025,.012,1]
   elif tag in ('right','left','marker'):emission=[1,.35,.015,1]
   arr['E'][y:y+size,x:x+size]=emission
   arr['EM_1'][y:y+size,x:x+size]=[int(tag in ('head','marker','worklight')),int(tag=='tail'),int(tag=='reverse'),1]
   arr['EM_2'][y:y+size,x:x+size]=[int(tag=='right'),int(tag=='left'),0,1]
   low=((x+pad)/2048,(y+pad)/2048);high=((x+size-pad)/2048,(y+size-pad)/2048)
   coords=[low,(high[0],low[1]),high,(low[0],high[1])]
   if len(p.loop_indices)==3:coords=[low,(high[0],low[1]),(low[0],high[1])]
   for li,c in zip(p.loop_indices,coords):uv.data[li].uv=c
   if tag in ('head','tail','reverse','left','right','marker','worklight'):
    lights.append({'object':ob.name,'face':p.index,'class':tag,'uv_center':[(x+size/2)/2048,(y+size/2)/2048],'mask':'EM_2.'+('G' if tag=='left' else 'R') if tag in ('left','right') else 'EM_1.'+('G' if tag=='tail' else 'B' if tag=='reverse' else 'R')})
  uv2=me.uv_layers.new(name='UV2');uv2.data.foreach_set('uv',[c for d in uv.data for c in d.uv]);me.uv_layers.active_index=0;uv.active_render=True
 images={}
 for suffix,pixels in arr.items():
  filename='T_FoodTruck_'+suffix+'.png';old=bpy.data.images.get(filename)
  if old:bpy.data.images.remove(old)
  img=bpy.data.images.new(filename,width=2048,height=2048,alpha=True);img.alpha_mode='CHANNEL_PACKED'
  img.colorspace_settings.name='sRGB' if suffix in ('D','E') else 'Non-Color'
  img.pixels.foreach_set(pixels.ravel());img.filepath_raw=OUT+'/'+filename;img.file_format='PNG';img.save();bpy.data.images.remove(img)
  img=bpy.data.images.load(OUT+'/'+filename,check_existing=False);img.alpha_mode='CHANNEL_PACKED'
  img.colorspace_settings.name='sRGB' if suffix in ('D','E') else 'Non-Color';images[suffix]=img
 mat=bpy.data.materials.get('FoodTruck_Vehicle') or bpy.data.materials.new('FoodTruck_Vehicle');mat.use_nodes=True;nodes=mat.node_tree.nodes;nodes.clear()
 output=nodes.new('ShaderNodeOutputMaterial');output.location=(400,0);bs=nodes.new('ShaderNodeBsdfPrincipled');bs.location=(120,0);bs.inputs['Roughness'].default_value=.65
 tex=nodes.new('ShaderNodeTexImage');tex.image=images['D'];tex.interpolation='Linear';tex.location=(-240,60)
 mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color']);mat.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface'])
 # Texture alpha is the game paint mask, never Blender transparency.
 for ob in obs:
  ob.data.materials.clear();ob.data.materials.append(mat)
  for p in ob.data.polygons:p.material_index=0
 with open(OUT+'/light_islands.json','w') as f:json.dump(lights,f,indent=2)
 return {'face_islands':len(entries),'used_atlas_cells':int(occupied.sum()),'light_faces':len(lights)}

def collision_export_save():
 old=bpy.data.objects.get('Truck_Collision')
 if old: bpy.data.objects.remove(old,do_unlink=True)
 points=[]
 # Actual body/wheel/hatch vertices, with mirror assemblies excluded.
 for name in ('Truck_Body','Truck_Hatch','Truck_Wheel_FL','Truck_Wheel_FR','Truck_Wheel_RL','Truck_Wheel_RR'):
  source=bpy.data.objects[name]
  for v in source.data.vertices:
   p=source.matrix_world @ v.co
   if p.z>2.901: continue  # Roof equipment is allowed outside the collision hull.
   if name=='Truck_Body' and abs(p.x)>1.145 and p.y<-1.70 and p.z>1.70: continue
   points.append(tuple(p))
 # Enclose the added front plate recess with one bumper plane instead of
 # giving the collision hull each millimetre-deep bezel and protector face.
 points.extend((x,-2.66,z) for x in (-1.09,1.09) for z in (.47,.66))
 bm=bmesh.new()
 for p in points: bm.verts.new(p)
 hull=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
 remove=set(hull.get('geom_interior',[]))|set(hull.get('geom_unused',[]))
 verts=[v for v in remove if isinstance(v,bmesh.types.BMVert)]
 if verts: bmesh.ops.delete(bm,geom=verts,context='VERTS')
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bmesh.ops.triangulate(bm,faces=list(bm.faces))
 me=bpy.data.meshes.new('Truck_Collision_Mesh'); bm.to_mesh(me); bm.free(); ob=bpy.data.objects.new('Truck_Collision',me); bpy.context.collection.objects.link(ob); ob.hide_render=True; ob.display_type='WIRE'; ob.hide_set(True)
 for o in list(bpy.data.objects): o.select_set(False)
 names=['Truck_Body','Truck_Wheel_FL','Truck_Wheel_FR','Truck_Wheel_RL','Truck_Wheel_RR','Truck_Hatch','Truck_Collision']
 ob.hide_set(False)
 for n in names: bpy.data.objects[n].select_set(True)
 bpy.context.view_layer.objects.active=bpy.data.objects['Truck_Body']
 bpy.ops.export_scene.fbx(filepath=OUT+'/FoodTruck.fbx',use_selection=True,object_types={'MESH'},global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',bake_space_transform=True,use_space_transform=True,mesh_smooth_type='OFF',use_triangles=True,use_tspace=True,bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
 ob.hide_set(True); ob.select_set(False)
 for img in bpy.data.images:
  if img.filepath.startswith(OUT): img.filepath='//'+os.path.basename(img.filepath)
 bpy.context.preferences.filepaths.save_version=0
 bpy.ops.wm.save_as_mainfile(filepath=OUT+'/FoodTruck.blend')
 counts={n:sum(len(p.vertices)-2 for p in bpy.data.objects[n].data.polygons) for n in names}
 with open(OUT+'/build_summary.json','w') as f: json.dump(counts,f,indent=2)
 return counts

stage=globals().get('TRUCK_STAGE','all')
if stage in ('geometry','all'):
 # Begin with an empty asset scene; retain the original non-exported scale reference.
 for ob in list(bpy.data.objects):
  if not ob.name.startswith('HumanRef'):bpy.data.objects.remove(ob,do_unlink=True)
 make_geometry(True);setup_render()
if stage in ('textures','all'):
 uv_info=texture_and_uv()
if stage in ('final','all'):
 result={'triangles':collision_export_save()}
else:
 result={'stage':stage,'objects':[o.name for o in bpy.data.objects if o.name.startswith('Truck_')]}

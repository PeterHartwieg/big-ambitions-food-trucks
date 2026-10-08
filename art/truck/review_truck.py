"""Multi-angle review contact sheets; uses the current running scene."""
import bpy, math, numpy as np, os
from mathutils import Vector
OUT='art/truck'
def review_sheet(filename, views, opened=True, size=(700,560)):
 sc=bpy.context.scene; cam=sc.camera; hatch=bpy.data.objects['Truck_Hatch']
 saved=(cam.location.copy(),cam.rotation_euler.copy(),hatch.rotation_euler.copy(),sc.render.resolution_x,sc.render.resolution_y,sc.cycles.samples)
 sc.render.resolution_x,sc.render.resolution_y=size; sc.cycles.samples=16
 hatch.rotation_euler[1]=math.radians(80) if opened else 0
 ground=Builder(); ground.box((-200,-200,-.055),(200,200,-.045),'plastic'); go=ground.object('ReviewGround')
 mat=bpy.data.materials.get('ReviewGround_Mat') or bpy.data.materials.new('ReviewGround_Mat'); mat.diffuse_color=(.16,.21,.25,1); go.data.materials.clear();go.data.materials.append(mat)
 panels=[]
 try:
  for i,loc in enumerate(views):
   cam.location=loc; cam.rotation_euler=(Vector((0,.25,1.4))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=7.6
   path=OUT+'/renders/'+filename.replace('.png',f'-view{i+1}.png');sc.render.filepath=path;bpy.ops.render.render(write_still=True)
   img=bpy.data.images.load(path,check_existing=False);panels.append(np.array(img.pixels[:],dtype=np.float32).reshape(size[1],size[0],4));bpy.data.images.remove(img)
  cols=2;rows=math.ceil(len(panels)/cols);pixels=np.ones((size[1]*rows,size[0]*cols,4),np.float32)*.1;pixels[:,:,3]=1
  for i,a in enumerate(panels):
   row=rows-1-i//cols;col=i%cols;pixels[row*size[1]:(row+1)*size[1],col*size[0]:(col+1)*size[0]]=a
  img=bpy.data.images.new('ReviewSheet',width=size[0]*cols,height=size[1]*rows,alpha=True);img.pixels.foreach_set(pixels.ravel());img.filepath_raw=OUT+'/renders/'+filename;img.file_format='PNG';img.save();bpy.data.images.remove(img)
 finally:
  bpy.data.objects.remove(go,do_unlink=True);cam.location,cam.rotation_euler,hatch.rotation_euler=saved[:3];sc.render.resolution_x,sc.render.resolution_y,sc.cycles.samples=saved[3:]
 return OUT+'/renders/'+filename

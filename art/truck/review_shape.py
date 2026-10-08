import bpy, numpy as np
from mathutils import Vector
TRUCK_STAGE='library'
exec(compile(open('art/truck/build_truck.py').read(),'build_truck.py','exec'))
def review_sheet(stem, views, opened=False):
 sc=bpy.context.scene;cam=sc.camera
 sc.cycles.samples=24;sc.render.resolution_x=850;sc.render.resolution_y=600
 tiles=[]
 for i,loc in enumerate(views):
  cam.location=loc;cam.rotation_euler=(Vector((0,.25,1.45))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=7.6
  name=stem+'-view'+str(i+1)+'.png'
  render_stage(name,opened=opened)
  im=bpy.data.images.load(OUT+'/renders/'+name,check_existing=False)
  pixels=np.array(im.pixels[:],dtype=np.float32).reshape(im.size[1],im.size[0],4);tiles.append(pixels);bpy.data.images.remove(im)
 pixels=np.concatenate(tiles,axis=1);im=bpy.data.images.new(stem,width=pixels.shape[1],height=pixels.shape[0],alpha=True)
 im.pixels.foreach_set(pixels.ravel());im.filepath_raw=OUT+'/renders/'+stem+'.png';im.file_format='PNG';im.save();bpy.data.images.remove(im)
 return {'sheet':stem,'views':len(views)}

"""Render the exact shipped FBX meshes and textures for Workshop artwork.

blender -b --factory-startup --python tools/release/render-models.py -- REPO OUTPUT
No source model, texture, or gameplay asset is modified. Output is promotional
studio rendering, not an in-game screenshot. Mounts match the Unity generators.
"""
import bpy
import hashlib
import json
import math
from pathlib import Path
import sys
from mathutils import Matrix, Vector

ROOT, OUT = map(Path, sys.argv[sys.argv.index('--') + 1:][:2])
OUT.mkdir(parents=True, exist_ok=True)
sources = {}


def source(path):
    path = ROOT / 'art' / path
    sources[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    return str(path)


def material(bc):
    mat = bpy.data.materials.new('Shipped surfaces')
    mat.use_nodes = True
    nt = mat.node_tree
    bs = nt.nodes.get('Principled BSDF')
    def texture(path, color):
        node = nt.nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(source(path), check_existing=True)
        node.image.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
        node.image.alpha_mode = 'CHANNEL_PACKED'
        return node
    base = texture(bc, True)
    nt.links.new(base.outputs['Color'], bs.inputs['Base Color'])
    bs.inputs['Roughness'].default_value = 0.5
    mask = bc.replace('_BC.png', '_M.png')
    if (ROOT / 'art' / mask).exists():
        m = texture(mask, True)
        sep = nt.nodes.new('ShaderNodeSeparateColor')
        nt.links.new(m.outputs['Color'], sep.inputs[0])
        nt.links.new(sep.outputs['Red'], bs.inputs['Metallic'])
        inv = nt.nodes.new('ShaderNodeMath')
        inv.operation = 'SUBTRACT'
        inv.inputs[0].default_value = 1
        nt.links.new(m.outputs['Alpha'], inv.inputs[1])
        nt.links.new(inv.outputs[0], bs.inputs['Roughness'])
    normal = bc.replace('_BC.png', '_N.png')
    if (ROOT / 'art' / normal).exists():
        n = texture(normal, False)
        nm = nt.nodes.new('ShaderNodeNormalMap')
        nt.links.new(n.outputs['Color'], nm.inputs['Color'])
        nt.links.new(nm.outputs['Normal'], bs.inputs['Normal'])
    return mat


def load(folder, model, hide=()):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=source(f'{folder}/{model}.fbx'))
    mat = material(f'{folder}/T_{model}_BC.png')
    obs = []
    for ob in set(bpy.data.objects) - before:
        if ob.type != 'MESH' or 'collision' in ob.name.lower() or ob.name in hide:
            bpy.data.objects.remove(ob, do_unlink=True)
            continue
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        obs.append(ob)
    return obs


def unity(x, y, z):
    return Vector((-x, -z, y))


def mounted(folder, model, position, turn, scale=1):
    obs = load(folder, model)
    for ob in obs:
        ob.matrix_world = Matrix.Translation(position) @ Matrix.Rotation(math.radians(turn), 4, 'Z') @ Matrix.Scale(scale, 4) @ ob.matrix_world
    return obs


def vehicle(kind):
    if kind == 'cart':
        obs = load('cart', 'FoodCart', ('Cart_Canopy_Furled',))
        obs += mounted('props/CashRegister', 'CashRegister', unity(.29,.95,.615), -90)
        pieces = [('HotDogRoller', unity(0,.963,.30)), ('DrinksCooler', unity(0,.963,-.30))]
    else:
        obs = load('truck', 'FoodTruck')
        hatch = bpy.data.objects['Truck_Hatch']
        hinge = hatch.matrix_world.translation.copy()
        hatch.matrix_world = Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(80),4,'Y') @ Matrix.Translation(-hinge) @ hatch.matrix_world
        obs += mounted('props/CashRegister','CashRegister',unity(.90,1.80,-.82),90,2.2)
        pieces = [(m,unity(-.62,1.75,z)) for m,z in zip(('Griddle','DumplingSteamer','CornGrill','DrinksCooler'),(-.875,-1.325,-.425,-1.775))]
    for name, pos in pieces:
        obs += mounted('equipment/'+name, name, pos, -90)
    return obs


def render(name, factory, direction, size):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc=bpy.context.scene
    obs=factory()
    bpy.context.view_layer.update()
    pts=[ob.matrix_world @ Vector(c) for ob in obs for c in ob.bound_box]
    low=Vector(tuple(min(p[i] for p in pts) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in pts) for i in range(3)))
    centre=(low+high)/2
    direction=Vector(direction).normalized()
    cam=bpy.data.objects.new('Studio camera',bpy.data.cameras.new('Studio camera'))
    sc.collection.objects.link(cam)
    cam.location=centre+direction*25
    cam.rotation_euler=(-direction).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'
    cam.data.clip_end=100
    sc.camera=cam
    rotation=cam.rotation_euler.to_matrix().transposed()
    proj=[rotation @ (p-centre) for p in pts]
    width=max(v.x for v in proj)-min(v.x for v in proj)
    height=max(v.y for v in proj)-min(v.y for v in proj)
    cam.data.ortho_scale=max(width,height*size[0]/size[1])*1.10
    # Centre the projected bounds, including the open hatch/canopy.
    offset=Vector(((max(v.x for v in proj)+min(v.x for v in proj))/2,(max(v.y for v in proj)+min(v.y for v in proj))/2,0))
    cam.location += cam.rotation_euler.to_matrix() @ offset
    world=bpy.data.worlds.new('Studio world'); world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.85,.9,1,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.7
    sc.world=world
    for label,energy,loc in [('Key',3.0,(-4,-6,8)),('Fill',1.2,(4,-2,5)),('Rim',1.5,(1,5,7))]:
        d=bpy.data.lights.new(label,'SUN'); d.energy=energy; d.angle=math.radians(25)
        ob=bpy.data.objects.new(label,d); sc.collection.objects.link(ob)
        ob.rotation_euler=(-Vector(loc)).to_track_quat('-Z','Y').to_euler()
    sc.render.engine='BLENDER_EEVEE'
    sc.render.resolution_x,sc.render.resolution_y=size
    sc.render.resolution_percentage=100
    sc.render.film_transparent=True
    sc.render.image_settings.file_format='PNG'; sc.render.image_settings.color_mode='RGBA'
    sc.view_settings.view_transform='AgX'; sc.view_settings.look='AgX - Medium High Contrast'
    sc.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
    print('WORKSHOP_RENDER',name,flush=True)


render('cart',lambda:vehicle('cart'),(-1,-1,.65),(1400,1400))
render('truck',lambda:vehicle('truck'),(-1,-1,.55),(1800,1200))
render('truck-hatch',lambda:vehicle('truck'),(-1,.15,.28),(1800,1100))
models=sorted({v['display'] for v in json.loads((ROOT/'data/economy.json').read_text())['equipment'].values() if 'display' in v})
for model in models:
    render(model,lambda m=model:load('equipment/'+m,m),(-1,-1,.75),(700,700))
for model in ('Dumplings','Tacos','ChoppedCheese','CornCob'):
    folder='products/'+model
    if (ROOT/'art'/folder/(model+'.fbx')).exists():
        render(model,lambda m=model,f=folder:load(f,m),(-1,-1,1),(600,600))
(OUT/'sources.json').write_text(json.dumps(sources,indent=2)+'\n')

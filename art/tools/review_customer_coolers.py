"""Check and render dedicated cooler placements against the actual FBX meshes.
Run: blender -b --factory-startup --python art/tools/review_customer_coolers.py
Writes only build/cooler-slot; never changes source scenes. Blender coordinates.
"""
from pathlib import Path
import json, math, runpy, sys
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'build/cooler-slot'
OUT.mkdir(parents=True, exist_ok=True)
sys.argv += ['--', str(ROOT / 'art'), str(OUT), str(ROOT / 'data/vehicle-diagrams.json'), str(ROOT / 'data/economy.json')]
helpers = runpy.run_path(str(ROOT / 'art/tools/render_diagrams.py'), run_name='cooler_review')
load, place, sun = (helpers[k] for k in ('import_meshes', 'place', 'sun'))
ART = ROOT / 'art'

def model(folder, name, position, turn, scale=1):
    obs = load(str(ART / folder / (name + '.fbx')), str(ART / folder / ('T_' + name + '_BC.png')),
               lambda ob: ob.name.endswith('_Body'))
    for ob in obs: place(ob, Vector(position), turn, scale)
    return obs[0]

def bounds(ob):
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return [[min(p[i] for p in pts), max(p[i] for p in pts)] for i in range(3)]

def tree(ob):
    verts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return BVHTree.FromPolygons(verts, [list(p.vertices) for p in ob.data.polygons])

results = []
for kind, side in [('cart', 1), ('cart', -1), ('truck', 1)]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    name = 'FoodCart' if kind == 'cart' else 'FoodTruck'
    vehicle = load(str(ART / kind / (name + '.fbx')), str(ART / kind / ('T_' + name + '_BC.png')),
                   lambda ob: not ob.name.endswith('_Collision') and ob.name != 'Cart_Canopy')
    body = next(ob for ob in vehicle if ob.name.endswith('_Body'))
    if kind == 'cart':
        cooler = model('equipment/CartDrinksCooler', 'CartDrinksCooler', (-side*.443, .32, .525), -side*90)
        equipment = [model('equipment/HotDogRoller', 'HotDogRoller', (0, -.30, .963), -side*90),
                     model('equipment/StorageCabinet', 'StorageCabinet', (0, .30, .963), -side*90)]
        register = model('props/CashRegister', 'CashRegister', (-.29, -.615, .95), -90)
        bpy.context.view_layer.update()
        b = bounds(cooler)
        assert b[0][0] >= -.48 and b[0][1] <= .48, b
        assert b[2][0] >= .52 and b[2][1] < .922, b # above fender, below worktop underside
        assert b[1][0] > -.7 and b[1][1] < .69, b # clear of push bar/handle brackets
        # The rear mounting lip may intersect the cart panel; the visible liner, shelves and drinks must not.
        hits = tree(body).overlap(tree(cooler))
        visible_hits = []
        for _, face in hits:
            xs = [(cooler.matrix_world @ cooler.data.vertices[i].co).x * -side
                  for i in cooler.data.polygons[face].vertices]
            if min(xs) > .418: visible_hits.append(face)
        assert not visible_hits, visible_hits
        target = Vector((0, 0, 1.02))
        eye = Vector((-side*3.2, -2.0, 2.0))
        label = 'cart-right' if side == 1 else 'cart-left'
    else:
        cooler = model('equipment/TruckDrinksCooler', 'TruckDrinksCooler', (-.875, 1.80, 1.80), -90)
        register = model('props/CashRegister', 'CashRegister', (-.90, .82, 1.80), 90, 2.2)
        equipment = [model('equipment/' + m, m, (.62, y, 1.75), -90)
                     for m, y in zip(['HotDogRoller', 'Griddle', 'BurgerStation', 'CoffeeStation'], [.425,.875,1.325,1.775])]
        bpy.context.view_layer.update()
        b = bounds(cooler)
        assert b[0][0] > -1.02 and b[0][1] < -.78, b # fully supported by the serving counter; clear of aisle
        assert b[1][0] > 1.55 and b[1][1] < 2.0, b # rear of window, clear of central server/register
        assert not tree(cooler).overlap(tree(register))
        cooler.location.z += .002 # ignore intentional counter contact only
        bpy.context.view_layer.update()
        assert not tree(body).overlap(tree(cooler)), 'truck body collision'
        hatch = next(ob for ob in vehicle if ob.name == 'Truck_Hatch')
        original = hatch.matrix_world.copy()
        hinge = original.translation.copy()
        for angle in range(0, 81, 10):
            hatch.matrix_world = Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(angle), 4, 'Y') @ Matrix.Translation(-hinge) @ original
            bpy.context.view_layer.update()
            assert not tree(hatch).overlap(tree(cooler)), ('hatch collision', angle, bounds(hatch), bounds(cooler))
        cooler.location.z -= .002
        target = Vector((-.2, 1.15, 1.85))
        eye = Vector((-6.0, 3.3, 3.1))
        label = 'truck-counter'
    for ob in equipment:
        assert not tree(ob).overlap(tree(cooler)), ob.name
    results.append({'view': label, 'blenderBounds': b, 'passed': True})
    world = bpy.data.worlds.new('ReviewWorld'); world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (.75,.78,.82,1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .8
    scene.world = world
    camera = bpy.data.objects.new('ReviewCamera', bpy.data.cameras.new('ReviewCamera'))
    scene.collection.objects.link(camera); camera.location = eye
    camera.rotation_euler = (target-eye).to_track_quat('-Z','Y').to_euler()
    camera.data.type = 'ORTHO'; camera.data.ortho_scale = 2.9 if kind == 'cart' else 3.8
    scene.camera = camera
    sun('Key', 2.8, Vector((side*.8, .4, -1)))
    sun('Fill', .7, Vector((-side*.8, -.5, -.3)), shadow=False)
    scene.render.engine = 'BLENDER_EEVEE'; scene.render.resolution_x = 1200; scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100; scene.render.film_transparent = True
    scene.view_settings.view_transform = 'AgX'; scene.view_settings.look = 'AgX - Punchy'
    scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'
    scene.render.filepath = str(OUT / (label+'.png'))
    bpy.ops.render.render(write_still=True)
    print('COOLER CLEAR', label, b)
(OUT / 'geometry-checks.json').write_text(json.dumps(results, indent=2)+'\n')

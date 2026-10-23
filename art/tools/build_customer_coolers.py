"""Build fitted cooler display variants from the existing DrinksCooler Blender source.

Run from the repository root with Blender --background --python this_file.
The original portable appliance is unchanged. The cart variant is its cabinet-front
insert (the cart body supplies the insulated cabinet); the truck variant is a
shallow counter fridge. Both keep the same door, cans, UVs and neutral artwork.
Surface maps are rebaked afterwards with apply_look.sh.
"""
import json
from pathlib import Path
import shutil
import bpy

ART = Path(__file__).resolve().parents[1]
SOURCE = ART / 'equipment/DrinksCooler'
for name, depth, height in [('CartDrinksCooler', .20, .70), ('TruckDrinksCooler', .45, 1.)]:
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'DrinksCooler.blend'))
    folder = ART / 'equipment' / name
    folder.mkdir(exist_ok=True)
    for ob in list(bpy.data.objects):
        if ob.name not in ('DrinksCooler_Body', 'DrinksCooler_Collision'):
            bpy.data.objects.remove(ob, do_unlink=True)
            continue
        ob.name = ob.name.replace('DrinksCooler', name)
        ob.data.name = ob.name + '_Mesh'
        # Bake dimensions into vertices, preserving metre units and identity transforms.
        for v in ob.data.vertices:
            v.co.y *= depth
            v.co.z *= height
        ob.data.update()
    for suffix in ('BaseColor', 'BC', 'M', 'N'):
        shutil.copy2(SOURCE / f'T_DrinksCooler_{suffix}.png', folder / f'T_{name}_{suffix}.png')
    # This is the same purchasable item; the build pipeline requires an icon for display variants too.
    shutil.copy2(SOURCE / 'Icon_DrinksCooler.png', folder / f'Icon_{name}.png')
    for img in bpy.data.images:
        if img.source == 'FILE' and 'T_DrinksCooler_' in img.filepath:
            img.filepath = str(folder / Path(img.filepath).name.replace('DrinksCooler', name))
    spec = json.loads((SOURCE / 'spec.json').read_text())
    spec['name'] = name
    spec['fbx'] = name + '.fbx'
    spec['textures'][0]['file'] = f'T_{name}_BaseColor.png'
    spec['objects'] = {k.replace('DrinksCooler', name): v for k, v in spec['objects'].items()}
    spec['envelope']['y'] = [v * depth for v in spec['envelope']['y']]
    spec['envelope']['z'] = [v * height for v in spec['envelope']['z']]
    spec['near_points'][0]['object'] = name + '_Body'
    (folder / 'spec.json').write_text(json.dumps(spec, indent=2) + '\n')
    if (SOURCE / 'look.json').exists():
        shutil.copy2(SOURCE / 'look.json', folder / 'look.json')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(folder / (name + '.blend')))
    bpy.ops.object.select_all(action='DESELECT')
    for ob in bpy.data.objects:
        ob.hide_set(False)
        ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(folder / (name + '.fbx')), use_selection=True,
        object_types={'MESH'}, axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        apply_scale_options='FBX_SCALE_ALL', add_leaf_bones=False, bake_anim=False)
    print('COOLER BUILT', name)

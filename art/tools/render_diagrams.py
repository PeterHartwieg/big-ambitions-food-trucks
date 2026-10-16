"""Render the BizMan vehicle page's vehicle drawings in layers: the vehicle with empty equipment places, and one
layer per place and display model, all from the same camera.

Usage:
  blender -b --factory-startup --python art/tools/render_diagrams.py -- \
      <art dir> <png dir> <json out> <economy.json> [check.png]

Per kind (truck, cart) it writes into <png dir>:
- VehicleDiagram_<Kind>.png: the vehicle, perspective, three-quarter from the side the pieces face, transparent
  background, lit softly from the front left like the item icons (render_icon.py). The truck stands with its hatch
  open, seen from the pavement a little above head height, so the cooking line shows through the window; the cart
  with its umbrella furled, seen from its right side and a little from the front.
- VehicleLayer_<Kind>_<place>_<Model>.png: only that display model on that place, with everything else in the
  scene as a holdout, so the window frame, the walls and the register cut it as in the full render. Cropped to its
  pixels; the JSON gives the crop's offset.
Then <json out> (data/vehicle-diagrams.json, schemaVersion 2) and, with a fifth argument, a check image with a few
places filled. The models are the `display` models of economy.json's equipment, placed as VehicleDisplays does:
scale 1, front turned to the hatch (truck) or the cart's right side. Positions are art/README.md's (Unity (x, y, z)
is Blender (-x, -z, y)).
"""
import json, math, os, sys
import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
ART, PNG_DIR, JSON_OUT, ECONOMY = args[0], args[1], args[2], args[3]
CHECK = args[4] if len(args) > 4 else None

PIECE = 0.40              # a piece's footprint (m): the fallback icon size
PIECE_HALF_HEIGHT = 0.25  # the place's hotspot above the counter (a typical piece is 0.40 to 0.60 m tall)
MARGIN = 0.03


def unity(x, y, z):
    return Vector((-x, -z, y))


KINDS = [
    {
        "kind": "truck", "label": "Truck", "size": (1200, 700),
        "fbx": "truck/FoodTruck.fbx", "tex": "truck/T_FoodTruck_BC.png",
        "hide": ["Truck_Collision"],
        # The hatch open: Unity localEulerAngles.z = +80 about its hinge, which is +80 degrees about Blender +Y.
        "hatch": ("Truck_Hatch", 80),
        # No cash register: in game it stands on the serving counter, in front of the middle places.
        # Camera: azimuth from the hatch side (-X) towards the rear (+Y), elevation, height of the eye, lens.
        "aim": unity(-0.2, 2.2, -1.10), "azimuth": -5, "elevation": 8, "lens": 85,
        # What must be in the picture: the window with the menu sign behind it, the awning and the roofline.
        "frame": [Vector((-1.1, y, z)) for y in (0.06, 2.68) for z in (1.70, 3.18)],
        # VehicleDisplays' Truck layout: filled from the middle out.
        "places": [("EquipmentLine2", unity(-0.62, 1.75, -0.875)), ("EquipmentLine3", unity(-0.62, 1.75, -1.325)),
                   ("EquipmentLine1", unity(-0.62, 1.75, -0.425)), ("EquipmentLine4", unity(-0.62, 1.75, -1.775))],
    },
    {
        "kind": "cart", "label": "Cart", "size": (900, 700),
        "fbx": "cart/FoodCart.fbx", "tex": "cart/T_FoodCart_BC.png",
        "hide": ["Cart_Collision", "Cart_Canopy"],
        # Cash register: Unity (0.29, 0.95, 0.615), +90 degrees about Y (Blender -90 about Z), scale 1.
        "register": (unity(0.29, 0.95, 0.615), -90, 1.0),
        # Azimuth from the cart's right side (-X) towards its front (-Y).
        "aim": unity(0, 1.0, 0.1), "azimuth": -30, "elevation": 22, "lens": 50,
        "frame": [Vector((x, y, z)) for x in (-0.5, 0.5) for y in (-0.77, 0.77) for z in (0.0, 2.3)],
        "places": [("EquipmentFront", unity(0, 0.963, 0.30)), ("EquipmentRear", unity(0, 0.963, -0.30))],
    },
]


def material(name, tex_path):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(tex_path)
    # The truck's base colour alpha is its paint mask, 0 on steel and the kitchen: keep the colour as stored.
    tex.image.alpha_mode = "CHANNEL_PACKED"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    mask_path, normal_path = tex_path.replace("_BC.png", "_M.png"), tex_path.replace("_BC.png", "_N.png")
    if os.path.exists(mask_path):
        m = nt.nodes.new("ShaderNodeTexImage")
        m.image = bpy.data.images.load(mask_path)
        m.image.colorspace_settings.name = "Non-Color"
        m.image.alpha_mode = "CHANNEL_PACKED"
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(m.outputs["Color"], sep.inputs[0])
        nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(m.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    if os.path.exists(normal_path):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(normal_path)
        n.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(n.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def import_meshes(fbx, tex, keep=lambda ob: True):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    mat = material(os.path.basename(tex), tex)
    meshes = []
    for ob in set(bpy.data.objects) - before:
        if ob.type != "MESH" or not keep(ob):
            bpy.data.objects.remove(ob, do_unlink=True)
            continue
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        meshes.append(ob)
    return meshes


def place(ob, pos, turn_deg, scale=1.0):
    """Put an imported model's origin at pos, turned about Blender Z, keeping the import's own axis fix."""
    if "base_rot" not in ob:
        ob["base_rot"] = [v for row in ob.matrix_world.to_3x3().normalized() for v in row]
    rot = Matrix([ob["base_rot"][0:3], ob["base_rot"][3:6], ob["base_rot"][6:9]])
    ob.matrix_world = (Matrix.Translation(pos) @ Matrix.Rotation(math.radians(turn_deg), 4, "Z")
                       @ Matrix.Scale(scale, 4) @ rot.to_4x4())


def sun(name, energy, direction, shadow=True):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = math.radians(20)
    data.use_shadow = shadow
    ob = bpy.data.objects.new(name, data)
    ob.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(ob)
    return ob


def displays():
    """Display model -> the game items that show it, from economy.json's equipment."""
    with open(ECONOMY) as f:
        equipment = json.load(f)["equipment"]
    out = {}
    for piece in equipment.values():
        model = piece.get("display") if isinstance(piece, dict) else None
        if model and os.path.exists(os.path.join(ART, "equipment", model, model + ".fbx")):
            out.setdefault(model, []).extend(piece.get("gameItems", []))
    return out


def pixels(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1].copy()
    bpy.data.images.remove(img)
    return px


def save(px, path):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels[:] = px[::-1].ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def render_kind(spec, models):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    vehicle = import_meshes(os.path.join(ART, spec["fbx"]), os.path.join(ART, spec["tex"]),
                            lambda ob: ob.name not in spec["hide"])
    if "hatch" in spec:
        name, angle = spec["hatch"]
        hatch = bpy.data.objects[name]
        hinge = hatch.matrix_world.translation.copy()
        hatch.matrix_world = (Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(angle), 4, "Y")
                              @ Matrix.Translation(-hinge) @ hatch.matrix_world)
    pos, turn, scale = spec.get("register", (None, 0, 1))
    for ob in [] if pos is None else import_meshes(os.path.join(ART, "props/CashRegister/CashRegister.fbx"),
                            os.path.join(ART, "props/CashRegister/T_CashRegister_BC.png"),
                            lambda ob: ob.name.endswith("_Body")):
        place(ob, pos, turn, scale)
        vehicle.append(ob)

    pieces = {}
    for model in models:
        obs = import_meshes(os.path.join(ART, "equipment", model, model + ".fbx"),
                            os.path.join(ART, "equipment", model, "T_" + model + "_BC.png"),
                            lambda ob: ob.name.endswith("_Body"))
        for ob in obs:
            ob.hide_render = True
        pieces[model] = obs

    world = bpy.data.worlds.new("DiagramWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.world = world

    # Camera: from the -X side, turned by the azimuth (+ towards +Y), looking down by the elevation.
    a, e = math.radians(spec["azimuth"]), math.radians(spec["elevation"])
    direction = Vector((-math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e)))
    cam = bpy.data.objects.new("DiagramCam", bpy.data.cameras.new("DiagramCam"))
    cam.data.lens = spec["lens"]
    cam.data.sensor_fit = "HORIZONTAL"
    cam.data.clip_end = 200
    scene.collection.objects.link(cam)
    scene.camera = cam
    w, h = spec["size"]
    scene.render.resolution_x, scene.render.resolution_y = w, h
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()

    def bounds():
        bpy.context.view_layer.update()
        vs = [world_to_camera_view(scene, cam, p) for p in spec["frame"]]
        return min(v.x for v in vs), max(v.x for v in vs), min(v.y for v in vs), max(v.y for v in vs)

    # Back off until the frame points fit with the margin, then shift the lens to centre them.
    lo, hi = 1.0, 60.0
    for _ in range(40):
        d = (lo + hi) / 2
        cam.location = spec["aim"] + direction * d
        cam.data.shift_x = cam.data.shift_y = 0
        x0, x1, y0, y1 = bounds()
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        fits = (x1 - x0) <= 1 - 2 * MARGIN and (y1 - y0) <= 1 - 2 * MARGIN
        lo, hi = (lo, d) if fits else (d, hi)
    cam.location = spec["aim"] + direction * hi
    x0, x1, y0, y1 = bounds()
    cam.data.shift_x = ((x0 + x1) / 2 - 0.5)
    cam.data.shift_y = ((y0 + y1) / 2 - 0.5) * h / w
    bpy.context.view_layer.update()

    right = cam.matrix_world.to_3x3() @ Vector((1, 0, 0))
    up = cam.matrix_world.to_3x3() @ Vector((0, 1, 0))
    fwd = -direction
    sun("Key", 2.6, fwd * 0.8 + right * 0.55 - up * 0.6)
    sun("Fill", 0.6, fwd * 0.5 - right * 0.7 - up * 0.3, shadow=False)
    sun("Rim", 0.6, -fwd * 0.8 - up * 0.4, shadow=False)

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Punchy"

    def shoot(path):
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    background = "VehicleDiagram_%s.png" % spec["label"]
    shoot(os.path.join(PNG_DIR, background))
    print("DIAGRAM", background)

    # Layers: the vehicle as a holdout, one piece shown.
    for ob in vehicle:
        ob.is_holdout = True
    places = []
    for name, spot in spec["places"]:
        hot = world_to_camera_view(scene, cam, spot + Vector((0, 0, PIECE_HALF_HEIGHT)))
        left = world_to_camera_view(scene, cam, spot + Vector((0, -PIECE / 2, PIECE_HALF_HEIGHT)))
        rightp = world_to_camera_view(scene, cam, spot + Vector((0, PIECE / 2, PIECE_HALF_HEIGHT)))
        size = abs(rightp.x - left.x) * w / h
        layers, files = {}, {}
        for model, obs in pieces.items():
            for ob in obs:
                place(ob, spot, -90)
                ob.hide_render = False
            file = "VehicleLayer_%s_%s_%s.png" % (spec["label"], name, model)
            path = os.path.join(PNG_DIR, file)
            shoot(path)
            for ob in obs:
                ob.hide_render = True
            px = pixels(path)
            ys, xs = np.nonzero(px[..., 3] > 0.004)
            if len(xs) == 0:
                os.remove(path)
                print("EMPTY", file)
                continue
            x0, x1, y0, y1 = int(xs.min()), int(xs.max()) + 1, int(ys.min()), int(ys.max()) + 1
            save(px[y0:y1, x0:x1], path)
            files[model] = {"file": file, "x": x0, "y": y0, "width": x1 - x0, "height": y1 - y0}
            for item in models[model]:
                layers[item] = files[model]
        places.append({"place": name, "x": round(hot.x, 4), "y": round(1 - hot.y, 4), "size": round(size, 4),
                       "depth": round(hot.z, 3), "layers": layers})
    order = [p["place"] for p in sorted(places, key=lambda p: -p["depth"])]
    for p in places:
        del p["depth"]
    return {"image": background, "width": w, "height": h,
            "iconSize": round(max(p["size"] for p in places), 4), "drawOrder": order, "places": places}


def main():
    models = displays()
    result = {
        "_notes": "Vehicle drawings on the BizMan vehicle page, rendered in layers by art/tools/render_diagrams.sh "
                  "(images in unity/FoodTrucks/UI). Per kind: the background image (the vehicle with empty "
                  "places) and its size in pixels; drawOrder, the places from back to front; places in the order "
                  "VehicleDisplays fills them. Per place: x/y, the place's hotspot (0 to 1 of the image, x right, "
                  "y down, 0.25 m above the counter), size, a fallback icon size as a fraction of the image height "
                  "(iconSize is the largest); layers, per game item (economy.json's equipment gameItems) the layer "
                  "of its display model: the file, cropped to its pixels, with its top-left corner x/y (y down) and "
                  "width/height in pixels of the full image. Draw the background, then each filled place's layer in "
                  "drawOrder. Keys starting with an underscore are notes.",
        "schemaVersion": 2,
    }
    for spec in KINDS:
        result[spec["kind"]] = render_kind(spec, models)
    with open(JSON_OUT, "w") as f:
        json.dump(result, f, indent=2)
        f.write("\n")
    if CHECK:
        check(result)


def check(result):
    """Each background with its places filled by a sample of models, composited, a dot on each hotspot."""
    samples = {"truck": ["Griddle", "BurgerStation", "HotDogRoller", "CoffeeStation"],
               "cart": ["HotDogRoller", "DrinksCooler"]}
    rows = []
    for kind, picks in samples.items():
        info = result[kind]
        px = pixels(os.path.join(PNG_DIR, info["image"]))
        px[..., :3] *= px[..., 3:]  # straight to premultiplied
        by_name = {p["place"]: p for p in info["places"]}
        fill = {p["place"]: picks[i] for i, p in enumerate(info["places"]) if i < len(picks)}
        for name in info["drawOrder"]:
            layer = next((l for l in by_name[name]["layers"].values() if l["file"].endswith("_" + fill.get(name, "") + ".png")), None)
            if not layer:
                continue
            lp = pixels(os.path.join(PNG_DIR, layer["file"]))
            y, x = layer["y"], layer["x"]
            dst = px[y:y + lp.shape[0], x:x + lp.shape[1]]
            a = lp[..., 3:]
            dst[..., :3] = lp[..., :3] * a + dst[..., :3] * (1 - a)
            dst[..., 3:] = a + dst[..., 3:] * (1 - a)
        h, w = px.shape[:2]
        rgb = px[..., :3] + (1 - px[..., 3:])  # premultiplied over white
        yy, xx = np.mgrid[0:h, 0:w]
        for p in info["places"]:
            rgb[np.hypot(xx - p["x"] * w, yy - p["y"] * h) < 5] = (1, 0, 0)
        rows.append(np.clip(rgb, 0, 1))
    W = max(r.shape[1] for r in rows)
    sheet = np.ones((sum(r.shape[0] for r in rows) + 20, W, 4), dtype=np.float32)
    y = 0
    for r in rows:
        sheet[y:y + r.shape[0], :r.shape[1], :3] = r
        y += r.shape[0] + 20
    save(sheet, CHECK)
    print("CHECK", CHECK)


main()

"""Render the BizMan vehicle page's equipment diagrams and the places' positions on them.

Usage:
  blender -b --factory-startup --python art/tools/render_diagrams.py -- <art dir> <png dir> <json out> [check.png]

Writes <png dir>/VehicleDiagram_Truck.png (1200 x 500) and VehicleDiagram_Cart.png (800 x 500): transparent,
orthographic, lit softly from the front left like the item icons (render_icon.py), no equipment on the places.
- Truck: seen from the hatch side (Blender -X, Unity +X), a little from above, with the food compartment's hatch
  side cut away above the floor (and the hatch hidden), so the cooking line on the back wall shows.
- Cart: seen from its right side (the side the pieces face while the cart is closed), a little from the front
  and above, umbrella furled, cash register in place.
Then projects each equipment place through the render camera into <json out> (x right, y down, 0..1), in the
order VehicleDisplays fills them. With a fourth argument it also writes a check image with the places circled.
Positions are art/README.md's (Unity (x, y, z) is Blender (-x, -z, y)).
"""
import json, math, os, sys
import bpy, bmesh
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
ART, PNG_DIR, JSON_OUT = args[0], args[1], args[2]
CHECK = args[3] if len(args) > 3 else None

PIECE = 0.40          # a piece's footprint along the line (m): the icon's suggested size
PIECE_HALF_HEIGHT = 0.22  # icon centre above the place (a typical piece is 0.40 to 0.50 m tall)
MARGIN = 0.04
METALLIC = 1.0


def unity(x, y, z):
    return Vector((-x, -z, y))


KINDS = [
    {
        "kind": "truck", "image": "VehicleDiagram_Truck.png", "size": (1200, 500),
        "fbx": "truck/FoodTruck.fbx", "tex": "truck/T_FoodTruck_BC.png",
        "hide": ["Truck_Collision", "Truck_Hatch"],
        "azimuth": 0, "elevation": 16,
        # VehicleDisplays' Truck layout: filled from the middle out.
        "places": [("EquipmentLine2", unity(-0.62, 1.75, -0.875)), ("EquipmentLine3", unity(-0.62, 1.75, -1.325)),
                   ("EquipmentLine1", unity(-0.62, 1.75, -0.425)), ("EquipmentLine4", unity(-0.62, 1.75, -1.775))],
    },
    {
        "kind": "cart", "image": "VehicleDiagram_Cart.png", "size": (800, 500),
        "fbx": "cart/FoodCart.fbx", "tex": "cart/T_FoodCart_BC.png",
        "hide": ["Cart_Collision", "Cart_Canopy"],
        "azimuth": 18, "elevation": 22,
        "places": [("EquipmentFront", unity(0, 0.963, 0.30)), ("EquipmentRear", unity(0, 0.963, -0.30))],
        # Cash register: Unity (0.29, 0.95, 0.615), +90 degrees about Y (Blender -90 about Z), scale 1.
        "register": (unity(0.29, 0.95, 0.615), -90, 1.0),
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
        # Steel at a third of its metallic: full metal reflects the dark inside of the cut truck and turns black.
        metal = nt.nodes.new("ShaderNodeMath")
        metal.operation = "MULTIPLY"
        metal.inputs[1].default_value = METALLIC
        nt.links.new(sep.outputs["Red"], metal.inputs[0])
        nt.links.new(metal.outputs[0], bsdf.inputs["Metallic"])
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


def import_meshes(fbx, tex, hide):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    meshes = []
    mat = material(os.path.basename(tex), tex)
    for ob in set(bpy.data.objects) - before:
        if ob.type != "MESH" or ob.name in hide:
            bpy.data.objects.remove(ob, do_unlink=True)
            continue
        ob.data.materials.clear()
        ob.data.materials.append(mat)
        meshes.append(ob)
    return meshes


def cut_truck(body):
    """Remove the food compartment's hatch side above the floor: aisle, serving counter, sink and side wall."""
    cut_x, floor_z, ceiling_z, bulkhead_y = -0.20, 0.97, 3.00, -0.95
    # The FBX import leaves a rotation on the object; cut in world coordinates.
    body.data.transform(body.matrix_world)
    body.matrix_world = Matrix.Identity(4)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for co, no in (((cut_x, 0, 0), (1, 0, 0)), ((0, 0, floor_z), (0, 0, 1)),
                   ((0, 0, ceiling_z), (0, 0, 1)), ((0, bulkhead_y, 0), (0, 1, 0))):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    doomed = [f for f in bm.faces
              if (c := f.calc_center_median()).x < cut_x and floor_z < c.z < ceiling_z and c.y > bulkhead_y]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bm.to_mesh(body.data)
    bm.free()


def sun(name, energy, direction, shadow=True):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = math.radians(20)
    data.use_shadow = shadow
    ob = bpy.data.objects.new(name, data)
    ob.rotation_euler = Vector(direction).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(ob)


def render(spec):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    meshes = import_meshes(os.path.join(ART, spec["fbx"]), os.path.join(ART, spec["tex"]), spec["hide"])
    if spec["kind"] == "truck":
        cut_truck(next(o for o in meshes if o.name == "Truck_Body"))
    if "register" in spec:
        pos, turn, scale = spec["register"]
        reg = import_meshes(os.path.join(ART, "props/CashRegister/CashRegister.fbx"),
                            os.path.join(ART, "props/CashRegister/T_CashRegister_BC.png"), [])
        for ob in reg:
            if not ob.name.endswith("_Body"):
                bpy.data.objects.remove(ob, do_unlink=True)
                continue
            ob.location = pos
            ob.rotation_euler = (0, 0, math.radians(turn))
            ob.scale = (scale,) * 3
            meshes.append(ob)
    meshes = [o for o in meshes if o.name in bpy.data.objects]

    world = bpy.data.worlds.new("DiagramWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (1, 1, 1, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.world = world

    # Camera: from the -X side, turned towards the front (-Y) by the azimuth, looking down by the elevation.
    a, e = math.radians(spec["azimuth"]), math.radians(spec["elevation"])
    direction = Vector((-math.cos(a) * math.cos(e), -math.sin(a) * math.cos(e), math.sin(e)))
    cam = bpy.data.objects.new("DiagramCam", bpy.data.cameras.new("DiagramCam"))
    cam.data.type = "ORTHO"
    scene.collection.objects.link(cam)
    scene.camera = cam
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    centre = sum(pts, Vector()) / len(pts)
    cam.location = centre + direction * 20
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_end = 100
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    local = [inv @ p for p in pts]
    xs, ys = [p.x for p in local], [p.y for p in local]
    cam.location = cam.matrix_world @ Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, 0))
    w, h = spec["size"]
    span_x, span_y = max(xs) - min(xs), max(ys) - min(ys)
    cam.data.sensor_fit = "HORIZONTAL"
    cam.data.ortho_scale = max(span_x, span_y * w / h) / (1 - 2 * MARGIN)

    # Lights relative to the view: key from the front left and above, soft fill from the right, a little rim.
    right = cam.matrix_world.to_3x3() @ Vector((1, 0, 0))
    up = cam.matrix_world.to_3x3() @ Vector((0, 1, 0))
    fwd = -direction
    sun("Key", 2.6, fwd * 0.8 + right * 0.55 - up * 0.6)
    sun("Fill", 0.6, fwd * 0.5 - right * 0.7 - up * 0.3, shadow=False)
    sun("Rim", 0.6, -fwd * 0.8 - up * 0.4, shadow=False)

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Punchy"
    out = os.path.join(PNG_DIR, spec["image"])
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)

    px_per_m = w / cam.data.ortho_scale
    places = []
    for name, p in spec["places"]:
        c = world_to_camera_view(scene, cam, p + Vector((0, 0, PIECE_HALF_HEIGHT)))
        b = world_to_camera_view(scene, cam, p)
        places.append({"place": name, "x": round(c.x, 4), "y": round(1 - c.y, 4),
                       "baseX": round(b.x, 4), "baseY": round(1 - b.y, 4)})
    print("DIAGRAM", out)
    return {"image": spec["image"], "width": w, "height": h,
            "iconSize": round(PIECE * px_per_m / h, 4), "places": places}


result = {
    "_notes": "Equipment diagrams on the BizMan vehicle page, rendered by art/tools/render_diagrams.sh. Per kind: "
              "the image (in unity/FoodTrucks/UI), its size in pixels, the suggested icon size as a fraction of "
              "the image height (a 0.40 m piece), and the places in the order VehicleDisplays fills them. x runs "
              "right and y down, 0 to 1 of the image; x/y is the icon centre (0.22 m above the place, about "
              "the middle of a piece), baseX/baseY the place itself on the counter. Keys starting with an "
              "underscore are notes.",
    "schemaVersion": 1,
}
for spec in KINDS:
    result[spec["kind"]] = render(spec)
with open(JSON_OUT, "w") as f:
    json.dump(result, f, indent=2)
    f.write("\n")

if CHECK:
    # Both images stacked on white, each place circled (icon size) with a dot on its base.
    rows = []
    for spec in KINDS:
        info = result[spec["kind"]]
        img = bpy.data.images.load(os.path.join(PNG_DIR, info["image"]))
        w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1]
        rgb = px[..., :3] * px[..., 3:] + (1 - px[..., 3:])
        yy, xx = np.mgrid[0:h, 0:w]
        r = info["iconSize"] * h / 2
        colours = [(1, 0, 0), (0, 0.6, 0), (0, 0.3, 1), (1, 0.5, 0)]
        for i, p in enumerate(info["places"]):
            cx, cy = p["x"] * w, p["y"] * h
            d = np.hypot(xx - cx, yy - cy)
            rgb[(d > r - 2) & (d < r + 1.5)] = colours[i]
            rgb[np.hypot(xx - p["baseX"] * w, yy - p["baseY"] * h) < 4] = colours[i]
        rows.append(rgb)
    W = max(r.shape[1] for r in rows)
    sheet = np.ones((sum(r.shape[0] for r in rows) + 20, W, 3), dtype=np.float32)
    y = 0
    for r_ in rows:
        sheet[y:y + r_.shape[0], :r_.shape[1]] = r_
        y += r_.shape[0] + 20
    H = sheet.shape[0]
    out = bpy.data.images.new("check", W, H, alpha=True)
    out.pixels[:] = np.concatenate([sheet[::-1], np.ones((H, W, 1), np.float32)], axis=2).ravel()
    out.filepath_raw = CHECK
    out.file_format = "PNG"
    out.save()
    print("CHECK", CHECK)

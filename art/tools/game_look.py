"""Give a built model the game's surface look, then re-export its FBX.

Usage (from the repo root, after the model's build script has saved its .blend):
  blender -b art/<kind>/<Name>/<Name>.blend --python art/tools/game_look.py -- art/<kind>/<Name>
  blender -b art/commissary/<Name>.blend --python art/tools/game_look.py -- art/commissary art/commissary/<Name>.spec.json
The second form is for folders that hold several models (the commissary): the spec names the model.

What it does (art/README.md, "Surface look"):
  1. Normals: edges sharper than 60 degrees stay hard, everything shallower (cylinder facets, the
     chamfers that build scripts already put on boxes) is smoothed, and a face-area weighted-normal pass
     keeps large flat faces flat. The game's own props are shaded this way.
  2. Bakes with Cycles on the model's own UVs: ambient occlusion, a tangent-space normal map of rounded
     edges (Bevel node, radius from the texel size) and an edge/noise pass in object space.
  3. Writes three textures next to the source base colour, at its size:
       T_<Name>_BC.png  base colour: graded into the game's value and saturation range, with baked
                        occlusion grime, object-space colour variation and lighter worn edges
       T_<Name>_M.png   HDRP mask map: R metallic, G ambient occlusion, B 0, A smoothness
       T_<Name>_N.png   tangent-space normal map (OpenGL, Y up, as Unity expects)
     The source T_<Name>_BaseColor.png (flat part colours from the build script) is left alone, so the
     step can run again. The mask's bytes are what the game stores: Unity imports it as sRGB, as the
     game does its own (art/README.md, "Surface look"), so G is decoded on sampling; R is 0 or 1 and A
     is never decoded. For the truck the sources are T_FoodTruck_D.png and T_FoodTruck_MaskMap.png.
  4. Re-exports the FBX with the export settings from the prompts and saves the .blend with a
     preview material that uses the new maps.

Materials per texel come from the flat source colour (see classify) or, for the truck, from its
source mask. A model folder can hold a look.json (or the spec a "look" entry) to override per colour, e.g.
  {"classes": [{"rgb": [0.55, 0.78, 0.86], "class": "glass"}], "kind": "food"}
"variants": ["a", "b"] also grades T_<Name>_a_BaseColor.png into T_<Name>_a_BC.png with the same bakes
and mask (the commissary sign's plates, one per commissary, on the same UVs).
"""
import bpy, json, math, os, sys
import numpy as np

ARGS = sys.argv[sys.argv.index("--") + 1:]
FOLDER = os.path.abspath(ARGS[0])
SPEC = json.load(open(ARGS[1] if len(ARGS) > 1 else os.path.join(FOLDER, "spec.json")))
NAME = SPEC["name"]
LOOK = SPEC.get("look") or (json.load(open(os.path.join(FOLDER, "look.json"))) if os.path.exists(os.path.join(FOLDER, "look.json")) else {})
VEHICLE = os.path.exists(os.path.join(FOLDER, f"T_{NAME}_D.png"))
KIND = LOOK.get("kind", "vehicle" if VEHICLE else "food" if "/products/" in FOLDER else "prop")
SMOOTH_ANGLE = math.radians(60.5)

# Smoothness (HDRP mask alpha) and metallic per surface class. Measured on the game's 1.0 props
# (art/README.md, "Surface look"): brushed steel 0.31-0.50 metallic 1, plastics and paint 0.30-0.65.
CLASSES = {
    "steel":  dict(metal=1.0, smooth=0.42),
    "dark":   dict(metal=0.0, smooth=0.30),   # black plastic, rubber, knobs
    "white":  dict(metal=0.0, smooth=0.45),   # enamel, white plastic
    "paint":  dict(metal=0.0, smooth=0.40),   # coloured paint and plastic
    "food":   dict(metal=0.0, smooth=0.35),
    "glass":  dict(metal=0.0, smooth=0.80),
    "fabric": dict(metal=0.0, smooth=0.20),
}


def log(*a):
    print("LOOK", *a, flush=True)


def visible_objects():
    obs = []
    for name, o in SPEC["objects"].items():
        if o.get("collision"):
            continue
        obs.append(bpy.data.objects[name])
    return obs


# ---------------------------------------------------------------- normals

def set_normals(ob):
    me = ob.data
    if me.has_custom_normals:
        with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=SMOOTH_ANGLE)
    mod = ob.modifiers.new("GameLookWeightedNormals", "WEIGHTED_NORMAL")
    mod.mode = "FACE_AREA"
    mod.weight = 50
    mod.keep_sharp = True
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_apply(modifier=mod.name)


# ---------------------------------------------------------------- baking

def uv_texel_size(obs, res):
    """Metres per texel: surface area over the UV area it gets."""
    area = uv_area = 0.0
    for ob in obs:
        me = ob.data
        uv = me.uv_layers[0].data
        me.calc_loop_triangles()
        for t in me.loop_triangles:
            area += t.area * ob.matrix_world.to_scale()[0] ** 2
            a, b, c = (uv[k].uv for k in t.loops)
            uv_area += abs((b - a).cross(c - a)) / 2
    return math.sqrt(area / max(uv_area, 1e-9)) / res


def bake_material(image):
    ma = bpy.data.materials.new("GameLook_Bake")
    ma.use_nodes = True
    nt = ma.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    target = nt.nodes.new("ShaderNodeTexImage")
    target.image = image
    nt.nodes.active = target
    return ma


def bake(obs, image, kind, setup, samples):
    sc = bpy.context.scene
    ma = bake_material(image)
    setup(ma.node_tree)
    saved = [list(ob.data.materials) for ob in obs]
    for ob in obs:
        ob.data.materials.clear()
        ob.data.materials.append(ma)
        ob.data.uv_layers.active_index = 0
    sc.cycles.samples = samples
    bpy.ops.object.select_all(action="DESELECT")
    for ob in obs:
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    kw = dict(type=kind, margin=6, margin_type="EXTEND", use_clear=True)
    if kind == "NORMAL":
        kw.update(normal_space="TANGENT")
    bpy.ops.object.bake(**kw)
    for ob, mats in zip(obs, saved):
        ob.data.materials.clear()
        for m in mats:
            ob.data.materials.append(m)
    bpy.data.materials.remove(ma)
    px = np.array(image.pixels[:], dtype=np.float32).reshape(image.size[1], image.size[0], 4)
    return px


def setup_bevel_normal(radius):
    def f(nt):
        out = nt.nodes["Material Output"]
        bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
        bev = nt.nodes.new("ShaderNodeBevel")
        bev.samples = 16
        bev.inputs["Radius"].default_value = radius
        nt.links.new(bev.outputs["Normal"], bsdf.inputs["Normal"])
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    return f


def setup_edge_noise(radius, size):
    """R: edge (rounded normal leaning away from the face), G: fine noise, B: coarse noise."""
    def f(nt):
        out = nt.nodes["Material Output"]
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        bev = nt.nodes.new("ShaderNodeBevel")
        bev.samples = 16
        bev.inputs["Radius"].default_value = radius
        dot = nt.nodes.new("ShaderNodeVectorMath")
        dot.operation = "DOT_PRODUCT"
        nt.links.new(bev.outputs["Normal"], dot.inputs[0])
        nt.links.new(geo.outputs["True Normal"], dot.inputs[1])
        edge = nt.nodes.new("ShaderNodeMath")
        edge.operation = "SUBTRACT"
        edge.inputs[0].default_value = 1.0
        nt.links.new(dot.outputs["Value"], edge.inputs[1])
        edge2 = nt.nodes.new("ShaderNodeMath")
        edge2.operation = "MULTIPLY"
        edge2.use_clamp = True
        edge2.inputs[1].default_value = 8.0
        nt.links.new(edge.outputs[0], edge2.inputs[0])
        tc = nt.nodes.new("ShaderNodeTexCoord")
        fine = nt.nodes.new("ShaderNodeTexNoise")
        fine.inputs["Scale"].default_value = 18.0 / size
        fine.inputs["Detail"].default_value = 6
        coarse = nt.nodes.new("ShaderNodeTexNoise")
        coarse.inputs["Scale"].default_value = 2.5 / size
        coarse.inputs["Detail"].default_value = 3
        for n in (fine, coarse):
            nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
        comb = nt.nodes.new("ShaderNodeCombineColor")
        nt.links.new(edge2.outputs[0], comb.inputs[0])
        nt.links.new(fine.outputs["Fac"], comb.inputs[1])
        nt.links.new(coarse.outputs["Fac"], comb.inputs[2])
        em = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(comb.outputs[0], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    return f


def setup_none(nt):
    out = nt.nodes["Material Output"]
    bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])


def blur(a, r=1):
    out = a.copy()
    for _ in range(r):
        out = (out + np.roll(out, 1, 0) + np.roll(out, -1, 0) + np.roll(out, 1, 1) + np.roll(out, -1, 1)) / 5
    return out


# ---------------------------------------------------------------- textures

def load_px(path):
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"   # raw values, no conversion
    px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
    size = tuple(img.size)
    bpy.data.images.remove(img)
    return px, size


def save_px(px, path, srgb):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), width=w, height=h, alpha=True)
    img.alpha_mode = "CHANNEL_PACKED"
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    img.pixels.foreach_set(np.clip(px, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def hsv(rgb):
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    s = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
    return s, mx


def classify(src):
    """Class index per texel from the flat source colour (sRGB values)."""
    names = list(CLASSES)
    rgb = src[..., :3]
    s, v = hsv(rgb)
    cls = np.full(rgb.shape[:2], names.index("food" if KIND == "food" else "paint"))
    cls[(s < 0.14) & (v > 0.92)] = names.index("white")
    cls[(s < 0.16) & (v >= 0.40) & (v <= 0.92)] = names.index("steel")
    if KIND == "food":   # greys in food are paper, foil or skewers; only clean light greys are metal
        cls[(s < 0.16) & (v >= 0.40) & (v <= 0.92) & (s > 0.08)] = names.index("food")
    cls[v < 0.22] = names.index("dark")
    for o in LOOK.get("classes", []):
        d = np.abs(rgb - np.array(o["rgb"], dtype=np.float32)).max(-1)
        cls[d < o.get("tolerance", 0.02)] = names.index(o["class"])
    return cls


def compose(src, ao, edge, fine, coarse, cls, src_mask=None):
    names = list(CLASSES)
    metal = np.zeros(cls.shape, np.float32)
    smooth = np.zeros(cls.shape, np.float32)
    for i, n in enumerate(names):
        metal[cls == i] = CLASSES[n]["metal"]
        smooth[cls == i] = CLASSES[n]["smooth"]
    if src_mask is not None:
        # The truck's own per-part mask: keep metallic and paint gloss, take chrome down to brushed steel.
        metal = (src_mask[..., 0] > 0.5).astype(np.float32)
        smooth = np.where(metal > 0, np.minimum(src_mask[..., 3], 0.50), np.minimum(src_mask[..., 3], 0.72))
        smooth = np.where(src_mask[..., 3] > 0.85, src_mask[..., 3], smooth)   # glass stays glossy
    rgb = src[..., :3].copy()
    s, v = hsv(rgb)
    grey = rgb.mean(-1, keepdims=True)
    is_metal = metal[..., None] > 0.5
    # Value and saturation into the game's range: paint a little darker and less saturated,
    # steel neutral (its colour is the reflection tint).
    sat_k = np.where(is_metal, 0.35, 0.80)
    rgb = grey + (rgb - grey) * sat_k
    # Steel: the game's steel albedo sits at 0.70-0.85 (sRGB) with metallic 1.
    rgb = np.where(is_metal, 0.52 + 0.40 * rgb, 0.03 + rgb * 0.86)
    # Grime and variation: occlusion darkens crevices, object-space noise breaks up flat fills.
    aon = np.clip(ao, 0, 1)[..., None]
    rgb *= 1.0 - 0.30 * (1.0 - aon)
    var = 1.0 + 0.07 * (coarse[..., None] - 0.5) + 0.05 * (fine[..., None] - 0.5)
    rgb *= var
    # Edges catch light and wear a little lighter.
    e = edge[..., None]
    rgb = rgb + e * 0.22 * (np.minimum(rgb * 1.25 + 0.04, 1.0) - rgb)
    bc = np.concatenate([np.clip(rgb, 0, 1), src[..., 3:4]], -1)
    sm = smooth * (0.88 + 0.24 * fine) - 0.15 * (1.0 - ao) - 0.06 * edge
    mask = np.stack([metal, np.clip(0.12 + 0.88 * ao, 0, 1), np.zeros_like(metal), np.clip(sm, 0.05, 0.95)], -1)
    return bc, mask


# ---------------------------------------------------------------- preview material and export

def preview_material(bc_path, m_path, n_path):
    ma = bpy.data.materials.get(f"{NAME}_GameLook") or bpy.data.materials.new(f"{NAME}_GameLook")
    ma.use_nodes = True
    nt = ma.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])

    def tex(path, colour):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(path, check_existing=False)
        n.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        n.image.alpha_mode = "CHANNEL_PACKED"
        return n
    # The mask decoded as sRGB, as Unity samples it (only G changes: R is 0 or 1, alpha is never decoded).
    bc, m, nm = tex(bc_path, True), tex(m_path, True), tex(n_path, False)
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(m.outputs["Color"], sep.inputs[0])
    # HDRP uses AO for indirect light only; a half-strength multiply stands in for it here.
    ao_mix = nt.nodes.new("ShaderNodeMix")
    ao_mix.data_type = "RGBA"
    ao_mix.blend_type = "MULTIPLY"
    ao_mix.inputs["Factor"].default_value = 0.5
    nt.links.new(bc.outputs["Color"], ao_mix.inputs["A"])
    nt.links.new(sep.outputs["Green"], ao_mix.inputs["B"])
    nt.links.new(ao_mix.outputs["Result"], bsdf.inputs["Base Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(m.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return ma


def export(obs):
    names = list(SPEC["objects"])
    bpy.ops.object.select_all(action="DESELECT")
    for n in names:
        ob = bpy.data.objects[n]
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(FOLDER, SPEC["fbx"]), use_selection=True, object_types={"MESH"},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_space_transform=True,
        mesh_smooth_type="OFF", use_triangles=True, use_tspace=True, bake_anim=False,
        add_leaf_bones=False, path_mode="RELATIVE", embed_textures=False)
    for n in names:
        if SPEC["objects"][n].get("collision"):
            bpy.data.objects[n].hide_set(True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)


# ---------------------------------------------------------------- main

def main():
    sc = bpy.context.scene
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    obs = visible_objects()
    for ob in sc.objects:   # only the model itself occludes and is baked
        if ob not in obs:
            ob.hide_render = True
    for ob in obs:
        ob.hide_render = False
        set_normals(ob)

    src_path = os.path.join(FOLDER, f"T_{NAME}_D.png" if VEHICLE else f"T_{NAME}_BaseColor.png")
    src, (w, h) = load_px(src_path)
    src_mask = load_px(os.path.join(FOLDER, f"T_{NAME}_MaskMap.png"))[0] if VEHICLE else None

    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("World")
    dims = max(max(ob.dimensions) for ob in obs)
    size = max(max((ob.matrix_world @ v.co).length for v in ob.data.vertices) for ob in obs) * 2
    texel = uv_texel_size(obs, w)
    radius = float(np.clip(1.8 * texel, 0.0025, 0.015)) * LOOK.get("bevel_scale", 1.0)
    sc.world.light_settings.distance = float(np.clip(0.25 * dims, 0.06, 0.6))
    log(f"{NAME}: kind {KIND}, {w}px, texel {texel*1000:.1f} mm, bevel radius {radius*1000:.1f} mm, AO distance {sc.world.light_settings.distance:.2f} m")

    img = bpy.data.images.new("GameLook_Target", width=w, height=h, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    ao = bake(obs, img, "AO", setup_none, 64)[..., 0]
    nrm = bake(obs, img, "NORMAL", setup_bevel_normal(radius), 16)
    en = bake(obs, img, "EMIT", setup_edge_noise(radius, size), 16)
    bpy.data.images.remove(img)
    ao = blur(ao, 1)
    edge, fine, coarse = blur(en[..., 0], 1), en[..., 1], en[..., 2]
    # Noise from Blender's Perlin sits around 0.5 with a narrow spread; stretch it to 0..1.
    fine = np.clip((fine - fine.mean()) * 4 + 0.5, 0, 1)
    coarse = np.clip((coarse - coarse.mean()) * 4 + 0.5, 0, 1)

    cls = classify(src)
    bc, mask = compose(src, ao, edge, fine, coarse, cls, src_mask)
    nrm[..., 3] = 1
    paths = {k: os.path.join(FOLDER, f"T_{NAME}_{k}.png") for k in ("BC", "M", "N")}
    save_px(bc, paths["BC"], True)
    save_px(mask, paths["M"], False)
    save_px(nrm, paths["N"], False)
    for v in LOOK.get("variants", []):
        vsrc = load_px(os.path.join(FOLDER, f"T_{NAME}_{v}_BaseColor.png"))[0]
        vbc, _ = compose(vsrc, ao, edge, fine, coarse, classify(vsrc), src_mask)
        save_px(vbc, os.path.join(FOLDER, f"T_{NAME}_{v}_BC.png"), True)
        log(f"{NAME}: variant {v}: wrote T_{NAME}_{v}_BC.png")
    counts = {n: round(float((cls == i).mean()), 3) for i, n in enumerate(CLASSES) if (cls == i).any()}
    log(f"{NAME}: classes {counts}, AO p10/p50 {np.percentile(ao, 10):.2f}/{np.percentile(ao, 50):.2f}")

    ma = preview_material(paths["BC"], paths["M"], paths["N"])
    for ob in obs:
        ob.data.materials.clear()
        ob.data.materials.append(ma)
        for p in ob.data.polygons:
            p.material_index = 0
    export(obs)
    log(f"{NAME}: wrote {', '.join(os.path.basename(p) for p in paths.values())} and {SPEC['fbx']}")


main()

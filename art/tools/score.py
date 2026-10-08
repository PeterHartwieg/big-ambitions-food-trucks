"""Check a model against its spec.json and write score.json next to it.

Usage:
  blender -b <model>.blend --python art/tools/score.py -- <spec.json>

Measures the saved .blend, then re-imports the exported .fbx into an empty
scene and measures it again. All positions are in Blender coordinates
(Z up, the model's front facing -Y, its left side on +X); see art/README.md
for the mapping to Unity. Exit code 0 when every check passes.
"""
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

spec_path = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
SPEC = json.load(open(spec_path))
FOLDER = os.path.dirname(spec_path)
POS_TOL = SPEC.get("position_tolerance_m", 0.005)
failures = []


def check(ok, label, detail=None):
    if not ok:
        failures.append(label if detail is None else f"{label}: {detail}")
    return bool(ok)


def r(v, n=4):
    return [round(float(c), n) for c in v]


def mesh_bm(ob):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(deps)
    me = ev.to_mesh()
    bm = bmesh.new()
    bm.from_mesh(me)
    ev.to_mesh_clear()
    bm.transform(ob.matrix_world)
    bm.normal_update()
    return bm


def tris(bm):
    return sum(len(f.verts) - 2 for f in bm.faces)


def bounds(bm):
    a = np.array([tuple(v.co) for v in bm.verts]) if bm.verts else np.zeros((1, 3))
    return Vector(a.min(0)), Vector(a.max(0))


def geometry(bm):
    non_manifold = [e for e in bm.edges if not e.is_manifold]
    out = {
        "non_manifold_edges": len(non_manifold),
        "wire_edges": sum(e.is_wire for e in bm.edges),
        "loose_verts": sum(not v.link_edges for v in bm.verts),
        "ngons": sum(len(f.verts) > 4 for f in bm.faces),
        "inconsistent_normal_edges": sum(e.is_manifold and not e.is_contiguous for e in bm.edges),
        "degenerate_faces": sum(f.calc_area() < 1e-9 for f in bm.faces),
    }
    # Closed islands must have a positive signed volume (normals pointing out).
    seen, inside_out = set(), 0
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        if all(e.is_manifold for g in isl for e in g.edges):
            vol = 0.0
            for g in isl:
                vs = [v.co for v in g.verts]
                for i in range(1, len(vs) - 1):
                    vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6.0
            inside_out += vol < 0
    out["closed_islands_inside_out"] = inside_out
    return out


def convexity(bm):
    worst = 0.0
    for f in bm.faces:
        n, p = f.normal, f.verts[0].co
        for v in bm.verts:
            worst = max(worst, (v.co - p).dot(n))
    return worst


def uv_triangles(ob, layer):
    me = ob.data
    if len(me.uv_layers) <= layer:
        return None
    uv = me.uv_layers[layer].data
    me.calc_loop_triangles()
    return np.array([[tuple(uv[i].uv) for i in lt.loops] for lt in me.loop_triangles], dtype=np.float64)


def rasterise(tri_sets, res=1024):
    """Count, per texel centre, how many UV triangles strictly cover it."""
    grid = np.zeros((res, res), dtype=np.int32)
    for t in tri_sets:
        for (a, b, c) in t * res:
            x0 = max(int(math.floor(min(a[0], b[0], c[0]))), 0)
            x1 = min(int(math.ceil(max(a[0], b[0], c[0]))), res - 1)
            y0 = max(int(math.floor(min(a[1], b[1], c[1]))), 0)
            y1 = min(int(math.ceil(max(a[1], b[1], c[1]))), res - 1)
            d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if x1 < x0 or y1 < y0 or abs(d) < 1e-12:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            l1 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
            l2 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
            l3 = 1 - l1 - l2
            eps = 1e-7
            grid[y0:y1 + 1, x0:x1 + 1] += (l1 > eps) & (l2 > eps) & (l3 > eps)
    return grid


def uv_report(obs, layer, strict):
    sets, outside = [], 0
    for ob in obs:
        t = uv_triangles(ob, layer)
        if t is None:
            check(False, f"UV layer {layer} missing on {ob.name}")
            continue
        flat = t.reshape(-1, 2)
        outside += int(np.sum(np.any((flat < -1e-4) | (flat > 1 + 1e-4), axis=1)))
        sets.append(t)
    if not sets:
        return {}
    g = rasterise(sets)
    covered = int(np.sum(g > 0))
    out = {"uv_coords_outside_0_1": outside, "fill": round(covered / g.size, 4),
           "overlap_fraction": round(int(np.sum(g > 1)) / max(covered, 1), 4)}
    if strict:
        check(outside == 0, f"UV{layer} coordinates outside 0-1", outside)
        check(out["overlap_fraction"] == 0, f"UV{layer} overlap", out["overlap_fraction"])
        check(out["fill"] >= SPEC.get("uv_min_fill", 0.4), f"UV{layer} fill below {SPEC.get('uv_min_fill', 0.4)}", out["fill"])
    return out


def find(name):
    return next((o for o in bpy.data.objects if o.type == "MESH" and (o.name == name or o.name.split(".")[0] == name)), None)


def scene_report(label, strict_textures):
    rep = {"label": label, "objects": sorted(o.name for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("HumanRef"))}
    objs = {n: find(n) for n in SPEC["objects"]}
    for n, o in objs.items():
        check(o is not None, f"[{label}] object {n} missing")
    extra = [n for n in rep["objects"] if n.split(".")[0] not in SPEC["objects"]]
    check(not extra, f"[{label}] unexpected mesh objects", extra)
    visible, total = [], 0
    rep["per_object"] = {}
    for n, cfg in SPEC["objects"].items():
        o = objs[n]
        if not o:
            continue
        bm = mesh_bm(o)
        mn, mx = bounds(bm)
        loc, rot, sca = o.matrix_world.decompose()
        info = {"triangles": tris(bm), "origin": r(loc), "bbox_min": r(mn), "bbox_max": r(mx),
                "rotation_deg": r([math.degrees(a) for a in rot.to_euler()], 3), "scale": r(sca), **geometry(bm)}
        # Blender's FBX importer adds a 90 degree axis rotation of its own, so transforms are checked on the .blend;
        # Unity imports these files with rotation 0 and scale 1 (art/README.md).
        if label == "blend":
            check(all(abs(a) < 1e-3 for a in info["rotation_deg"]) and all(abs(s - 1) < 1e-4 for s in sca), f"[{label}] {n} transforms not applied")
        check(info["triangles"] <= cfg["max_tris"], f"[{label}] {n} over {cfg['max_tris']} triangles", info["triangles"])
        for key in ("loose_verts", "wire_edges", "ngons", "inconsistent_normal_edges", "degenerate_faces", "closed_islands_inside_out"):
            check(info[key] == 0, f"[{label}] {n} {key}", info[key])
        if "origin" in cfg:
            check((loc - Vector(cfg["origin"])).length <= POS_TOL, f"[{label}] {n} origin {r(loc)} != {cfg['origin']}")
        if cfg.get("wheel"):
            centre, dims = (mn + mx) / 2, mx - mn
            axle = "XYZ"[int(np.argmin(dims))]
            info.update(hub_offset=round((loc - centre).length, 4), diameter=round(max(dims.y, dims.z), 4), width=round(dims.x, 4), axle=axle)
            check(info["hub_offset"] <= POS_TOL, f"[{label}] {n} origin not on the hub", info["hub_offset"])
            check(axle == "X", f"[{label}] {n} axle not along X", axle)
            check(abs(info["diameter"] - cfg["diameter"]) <= 0.01, f"[{label}] {n} diameter", info["diameter"])
            check(abs(info["width"] - cfg["width"]) <= 0.01, f"[{label}] {n} width", info["width"])
            check(mn.z >= -0.002, f"[{label}] {n} below the floor", round(mn.z, 4))
        if "hinge" in cfg:
            h = cfg["hinge"]
            ax = "XYZ".index(h["axis"])
            for i in range(3):
                if i != ax:
                    check(abs(loc[i] - h["line"][i]) <= POS_TOL, f"[{label}] {n} origin off the hinge line on {'XYZ'[i]}", r(loc))
            # Open the part about its hinge axis both ways and record which sign swings it towards "opens_towards".
            want = h["opens_towards"]  # e.g. "+z" or "-x"
            i, sgn = "xyz".index(want[1]), (1 if want[0] == "+" else -1)
            pts = [v.co - loc for v in bm.verts]
            moved = {}
            for s in (1, -1):
                m = Matrix.Rotation(math.radians(h["open_deg"]) * s, 3, h["axis"])
                moved[s] = float(np.mean([(loc + m @ p)[i] for p in pts]))
            info["opens_with_rotation_sign"] = max(moved, key=lambda s: moved[s] * sgn)
            info["open_centroid_" + want[1]] = {str(k): round(v, 3) for k, v in moved.items()}
        if cfg.get("collision"):
            worst = convexity(bm)
            info["max_vertex_outside_face_plane_m"] = round(worst, 5)
            check(worst <= 1e-3, f"[{label}] {n} not strictly convex", round(worst, 5))
            check(info["non_manifold_edges"] == 0, f"[{label}] {n} not closed", info["non_manifold_edges"])
            check(o.hide_render or label != "blend", f"[{label}] {n} should be hidden from renders")
        else:
            visible.append(o)
            total += info["triangles"]
        rep["per_object"][n] = info
        bm.free()
    rep["visible_triangles"] = total
    check(total <= SPEC["visible_budget"], f"[{label}] visible triangles over {SPEC['visible_budget']}", total)
    if visible:
        mn = Vector(np.min([rep["per_object"][o.name.split('.')[0]]["bbox_min"] for o in visible], 0))
        mx = Vector(np.max([rep["per_object"][o.name.split('.')[0]]["bbox_max"] for o in visible], 0))
        rep["visible_bbox_min"], rep["visible_bbox_max"], rep["visible_dims"] = r(mn), r(mx), r(mx - mn)
        for key, lim in SPEC.get("envelope", {}).items():
            lo, hi = lim
            i = "xyz".index(key)
            check(mn[i] >= lo - POS_TOL and mx[i] <= hi + POS_TOL, f"[{label}] visible extent on {key} {round(mn[i],3)}..{round(mx[i],3)} outside {lo}..{hi}")
        for key, (lo, hi) in SPEC.get("extent_at_least", {}).items():
            i = "xyz".index(key)
            check(mn[i] <= lo + 0.02 and mx[i] >= hi - 0.02, f"[{label}] visible extent on {key} {round(mn[i],3)}..{round(mx[i],3)} should reach {lo}..{hi}")
    # Points that must lie on a surface (grips, hinge, attachment points).
    rep["near_points"] = {}
    for p in SPEC.get("near_points", []):
        o = objs.get(p["object"])
        if not o:
            continue
        bm = mesh_bm(o)
        tree = BVHTree.FromBMesh(bm)
        hit = tree.find_nearest(Vector(p["point"]))
        dist = hit[3] if hit[0] is not None else 99
        rep["near_points"][p["label"]] = round(dist, 4)
        check(dist <= p["max_dist"], f"[{label}] {p['label']}: {p['object']} surface {round(dist,3)} m from {p['point']}")
        bm.free()
    # Volumes that must stay empty (cargo boxes, the pusher's legs).
    rep["clear_boxes"] = {}
    for c in SPEC.get("clear_boxes", []):
        lo, hi = Vector(c["min"]), Vector(c["max"])
        box = bmesh.new()
        bmesh.ops.create_cube(box, size=1.0)
        for v in box.verts:
            v.co = Vector(((lo.x + hi.x) / 2 + v.co.x * (hi.x - lo.x), (lo.y + hi.y) / 2 + v.co.y * (hi.y - lo.y), (lo.z + hi.z) / 2 + v.co.z * (hi.z - lo.z)))
        box_tree = BVHTree.FromBMesh(box)
        hits = 0
        for n in c["objects"]:
            o = objs.get(n)
            if not o:
                continue
            bm = mesh_bm(o)
            inside = sum(all(lo[i] < v.co[i] < hi[i] for i in range(3)) for v in bm.verts)
            hits += inside + len(BVHTree.FromBMesh(bm).overlap(box_tree))
            bm.free()
        box.free()
        rep["clear_boxes"][c["label"]] = hits
        check(hits == 0, f"[{label}] {c['label']} is not clear", hits)
    # Materials, UVs and textures.
    mats = sorted({m.name for o in visible for m in o.data.materials if m})
    rep["materials"] = mats
    check(len(mats) == 1, f"[{label}] expected one material on the visible objects", mats)
    for layer in range(SPEC.get("uv_layers", 1)):
        rep[f"uv{layer}"] = uv_report(visible, layer, strict=True)
    if strict_textures:
        rep["textures"] = {}
        for t in SPEC.get("textures", []):
            path = os.path.join(FOLDER, t["file"])
            ok = os.path.exists(path)
            check(ok, f"texture {t['file']} missing")
            if ok:
                img = bpy.data.images.load(path, check_existing=True)
                rep["textures"][t["file"]] = list(img.size)
                check(list(img.size) == [t["size"], t["size"]], f"texture {t['file']} size", list(img.size))
    return rep


report = {"spec": os.path.basename(spec_path), "blend": scene_report("blend", True)}
blend_mb = os.path.getsize(bpy.data.filepath) / 1e6
report["blend"]["blend_size_mb"] = round(blend_mb, 2)
check(blend_mb < 20, ".blend over 20 MB", round(blend_mb, 2))

fbx = os.path.join(FOLDER, SPEC["fbx"])
if os.path.exists(fbx):
    fbx_mb = os.path.getsize(fbx) / 1e6
    check(fbx_mb < 20, ".fbx over 20 MB", round(fbx_mb, 2))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    others = [o.name for o in bpy.data.objects if o.type != "MESH"]
    check(not others, "[fbx] non-mesh objects in the FBX", others)
    report["fbx"] = scene_report("fbx", False)
    report["fbx"]["fbx_size_mb"] = round(fbx_mb, 2)
    for n, info in report["blend"]["per_object"].items():
        f = report["fbx"]["per_object"].get(n)
        if f:
            check(f["triangles"] == info["triangles"], f"[fbx] {n} triangle count changed", (info["triangles"], f["triangles"]))
            check(max(abs(a - b) for a, b in zip(f["origin"], info["origin"])) <= 1e-3, f"[fbx] {n} origin moved", (info["origin"], f["origin"]))
else:
    check(False, f"{SPEC['fbx']} missing")

report["failures"] = failures
report["passed"] = not failures
out = os.path.join(FOLDER, "score.json")
with open(out, "w") as f:
    json.dump(report, f, indent=2)
print("SCORE", "PASS" if not failures else f"FAIL ({len(failures)})", out)
for line in failures:
    print("  -", line)
sys.exit(0 if not failures else 1)

"""Writes prompt.md and spec.json for each counter equipment piece and food product.

Run from the repo root: python3 art/tools/make_prop_prompts.py
Each prop gets its own folder under art/equipment/ or art/products/, built by one Sol run.
"""
import json
import os

# Equipment stands on a cart's counter or the truck's counter behind the hatch, front (customer side) towards -Y.
# One footprint for all, so any piece fits any slot: 0.40 m wide (X), 0.36 m deep (Y).
EQUIPMENT = {
    "CoffeeStation": ("Compact coffee station", 0.55,
        "A street-cart coffee setup: a compact stainless drip brewer or small espresso machine with a drip tray, "
        "two pump-action airpot urns beside it, and a short stack of paper cups. It replaces the game's shop-sized "
        "industrial coffee machine on the cart, so it must read as 'coffee' from the street."),
    "DrinksCooler": ("Countertop drinks cooler", 0.55,
        "A small upright glass-door drinks fridge: dark or white cabinet, a glass door with a frame and a vertical "
        "handle, and three shelves of cans and bottles in mixed colours seen through the glass (paint them as solid "
        "shapes behind the door frame; the material has no transparency). A light strip at the top, blank, no logo."),
    "CornGrill": ("Corn grill", 0.40,
        "A New York street-corn grill: a low rectangular charcoal grill on short legs with a grate, six to eight "
        "corn cobs in their husks or bare and charred lying across it, and a small tray of butter or toppings at one end."),
    "Griddle": ("Flat-top griddle", 0.40,
        "A bodega-style flat-top griddle: a thick steel plate with a front grease trough, splash guards on the back "
        "and sides, two control knobs on the front, a spatula resting on it, and a few patties or a pile of chopped "
        "meat and cheese plus two folded tortillas on the plate (it makes both chopped cheese sandwiches and tacos)."),
    "DumplingSteamer": ("Dumpling steamer", 0.55,
        "A stack of three round bamboo steamer baskets with a lid on a small gas burner or steamer pot, and a "
        "shallow tray in front holding a few dumplings and a pair of chopsticks."),
    "KabobGrill": ("Kabob grill", 0.40,
        "A long narrow charcoal trough grill on legs, as used by New York halal and kabob carts: a stainless trough "
        "with glowing coals (orange and dark grey blocks) and five or six metal skewers of meat and peppers lying "
        "across it, plus a small heat shield at the back."),
    "PopcornMachine": ("Popcorn machine", 0.60,
        "A classic red kettle popcorn machine: a red base cabinet, a glass cabinet on top with a red roof and four "
        "corner posts, a steel kettle hanging inside near the top, and the lower half of the glass cabinet heaped "
        "with popcorn. Glass panes are thin frames only (no glass faces); the popcorn shows through."),
    "CottonCandyMachine": ("Cotton candy machine", 0.50,
        "A cotton candy maker: a square base cabinet, a wide round steel bowl on top with a spinner head in the "
        "middle and a clear bubble guard rim, plus a holder at one side with three pink and blue cotton candies on "
        "paper sticks."),
    "SlushieMachine": ("Slushie machine", 0.60,
        "A two-bowl frozen drink dispenser: a steel base with two drip trays and taps at the front, and two tall "
        "rounded bowls on top, one filled bright blue and one bright red (paint the bowls as the coloured slush "
        "with a lighter rim; no transparency), with white lids."),
    "MerchRack": ("Souvenir and umbrella rack", 0.70,
        "A small countertop merchandise stand for a street vendor: a tiered stand with a few wrapped gift boxes in "
        "bright paper and ribbon, two snow globes and small generic city souvenirs (a tiny taxi, a mini skyline "
        "block), plus a cylinder holder at one side with four folded umbrellas in different colours standing in it. "
        "No text, flags or logos."),
    # Compact versions of the base game's own equipment, to sit on the cart beside the game's look. Each run gets
    # the game's item icon attached as a reference (kept outside the repo).
    "HotDogRoller": ("Compact hot dog roller grill", 0.40,
        "A compact version of the game's own hot dog grill (see the attached reference picture of the game's item): "
        "a brushed-steel roller grill with rows of shiny rollers carrying hot dogs, a sloped glass-free top, a front "
        "panel with a black inset strip (blank, no lettering) and two control knobs, a pull-out drip tray, short legs. "
        "Same proportions, colours and level of detail as the reference, only smaller."),
    "BurgerStation": ("Compact burger station", 0.45,
        "A compact burger station that combines the game's industrial grill and fryer (see the attached reference "
        "pictures): on the left two thirds a char-grill with dark ribbed grates, a steel front with three red-and-black "
        "knobs; on the right third a small deep fryer with one wire basket hanging over its oil well and a small "
        "control panel. Same brushed steel, colours and level of detail as the references."),
    "IceCreamCabinet": ("Compact ice cream cabinet", 0.50,
        "A compact version of the game's own ice cream counter (see the attached reference picture): a white-and-steel "
        "cabinet with horizontal steel stripes on the front, a curved glass hood with a red frame on top, and a row of "
        "four tubs of coloured ice cream (vanilla, strawberry, chocolate, mint) inside the well; paint the glass as a "
        "frame only so the tubs show. No lettering. Same colours and style as the reference, only smaller."),
    "PizzaOven": ("Compact pizza oven", 0.50,
        "A compact version of the game's own pizza oven (see the attached reference picture): a two-deck steel deck "
        "oven with dark glass doors and steel handles, a small control panel at the right of each deck, a flat steel "
        "top, standing on a short steel frame. Same proportions, colours and level of detail as the reference, scaled "
        "to sit on a cart."),
    "PastryCase": ("Compact pastry display case", 0.55,
        "A compact version of the game's own bakery showcase (see the attached reference picture): a steel base with "
        "small drawers, three sloped wooden shelves in a thin white frame with a wooden top, the shelves holding "
        "croissants, donuts and cupcakes in neat rows. Same materials, colours and style as the reference, only smaller."),
}

# Handheld products, like the game's own food items: sold over the counter and carried in wholesale boxes.
PRODUCTS = {
    "CornCob": ("Corn on the cob",
        "One grilled corn cob on a wooden stick, bare yellow kernels with dark grill marks, the lower end in a "
        "paper sleeve. Lying or standing, at most 0.24 m long."),
    "ChoppedCheese": ("Chopped cheese sandwich",
        "A New York chopped cheese: a hero roll about 0.20 m long, split and filled with chopped beef, melted cheese, "
        "lettuce and tomato, its lower half wrapped in foil or white deli paper."),
    "Tacos": ("Tacos",
        "Three soft tacos side by side in a paper boat tray, folded corn tortillas with meat, onion and cilantro "
        "showing on top, and a lime wedge."),
    "Dumplings": ("Dumplings",
        "Six pleated dumplings, half of them pan-fried golden on the bottom, in a small white paper or foam takeout "
        "tray, with a small sauce cup."),
    "Popcorn": ("Popcorn",
        "A striped paper popcorn bucket (red and white vertical stripes, no text), slightly tapered, overflowing "
        "with a heaped dome of white-yellow popped kernels. About 0.15 m tall."),
    "CottonCandy": ("Cotton candy",
        "A puffy pink cloud of cotton candy wound on a white paper cone or wooden stick, the cloud roughly "
        "egg-shaped and soft-looking (a few large lumpy lobes, not noise). At most 0.25 m tall with the stick."),
    "Kabob": ("Kabob",
        "One street-cart kabob: a wooden skewer with chunks of grilled chicken or lamb, pieces of red and green "
        "pepper and onion between them, grill marks on the meat, the skewer lying across a small paper tray. At "
        "most 0.24 m long."),
    "Slushie": ("Slushie",
        "A clear plastic cup of bright blue-red layered frozen slush (icy, slightly mounded top) with a domed lid "
        "and a straw. About 0.15 m tall, no text or logo on the cup."),
}

EXPORT = '''  ```python
  bpy.ops.export_scene.fbx(filepath=OUT + "/{name}.fbx", use_selection=True, object_types={{"MESH"}},
      global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
      axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_space_transform=True,
      mesh_smooth_type="OFF", use_triangles=True, use_tspace=True, bake_anim=False,
      add_leaf_bones=False, path_mode="RELATIVE", embed_textures=False)
  ```'''

PROMPT = '''# Task: model {article} {label_lower} for a Big Ambitions mod

You are driving a running Blender 5.2 instance through the `blender` MCP server (`execute_blender_code`, scene summaries, screenshots, viewport and thumbnail renders, API and manual search). Work on your own until the task is finished; nobody will answer questions during the run.

The asset belongs to a public Workshop mod for the PC game Big Ambitions (Unity 2022.3, HDRP, New York setting) that adds street food carts and trucks. {role} Game props are clean, simple low-poly shapes with flat colours, not photoreal sculpts. Neutral art: no text, logos, brand names or flags anywhere.

## What to build

{desc}

## Coordinates (read carefully)

Blender units are metres, Z up. **The front of the {kind}, the side that faces the customer, faces −Y.** Its left side is +X. This is what the FBX settings below turn into Unity's forward; it was measured in Unity. Do not build it facing +Y.

The origin (0, 0, 0) is on the floor (the surface it stands on), under the middle of its footprint. The game places it by this point.

## Size

- Everything visible inside x −{hx} to +{hx}, y −{hy} to +{hy}, z 0 to {h}. {size_note}
- It stands flat on z = 0: the underside covers the origin.
- Real proportions in metres; use `HumanRef_1.8m` to judge scale.

## Spec

- At most {body_tris} triangles for `{prefix}_Body`. Spend them on the silhouette and the parts that read from a few metres away, not on hidden faces.
- Rotation 0 and scale 1 applied on every object.
- Objects, with these exact names:
  - `{prefix}_Body`: the whole visible model, origin at (0, 0, 0).
  - `{prefix}_Collision`: a convex collision hull around it, at most {col_tris} triangles, built with `bmesh.ops.convex_hull` so it is strictly convex. Origin at (0, 0, 0). Hide it from renders.
- One material on `{prefix}_Body`, using one base-colour texture `T_{name}_BaseColor.png`, {tex} × {tex}, saved in the output folder. Flat colour blocks per part. UVs without overlaps, inside 0–1, and packed so that at least 40% of the texture is covered (check with the scorer).
- Clean geometry: no loose vertices or edges, no flipped normals, no n-gons, closed parts where the shape allows.
- Export `{name}.fbx` with exactly these settings, with only the two objects selected:

{export}

- Save the scene as `{name}.blend` in the output folder.

## Starting point

Blender has `art/start.blend` open: metric units and a wireframe box `HumanRef_1.8m` (a 1.8 m person for scale; leave it alone, it isn't rendered or exported). Don't open other files, and don't write outside the output folder.

## Checking your work

A scorer measures the result against this spec. Run it after exporting, read every failure, fix it, export again and rerun until it prints `SCORE PASS`. Start it from the running Blender with `execute_blender_code`, as a separate background Blender process, because a `blender -b` started from your shell sandbox crashes while probing the GPU:

```python
import subprocess
p = subprocess.run(["/Applications/Blender.app/Contents/MacOS/Blender", "-b", OUT + "/{name}.blend",
    "--python", REPO + "/art/tools/score.py",
    "--", OUT + "/spec.json"], stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=300)
result = {{"exit": p.returncode, "log": p.stdout[-4000:]}}
```

It writes `score.json` in the output folder. Do not edit the scorer or `spec.json`.

## Folders

- Repository: `{{REPO}}`
- Output folder: `{{OUT}}`

Put everything in the output folder. After each major step (blockout, detail, UVs and texture, final), save a viewport screenshot or render to `{{OUT}}/renders/` named `01-blockout.png`, `02-detail.png` and so on, and look at it before moving on. The last one, `04-final.png`, shows the finished model from the front at a slight angle from above, as a customer would see it. Save a small Python file with the build code you used as `{{OUT}}/build_{snake}.py` so the model can be regenerated.

## Finish

End with a short report: what you built, triangles per object, the final scorer result, and anything in the spec you could not meet and why.
'''


def snake(name):
    return "".join("_" + c.lower() if c.isupper() else c for c in name).lstrip("_")


def write(folder, name, prefix, label, desc, kind, hx, hy, h, body_tris, col_tris, tex, role, size_note):
    os.makedirs(os.path.join(folder, "renders"), exist_ok=True)
    article = "an" if label[0].lower() in "aeiou" else "a"
    prompt = PROMPT.format(article=article, label_lower=label.lower(), role=role, desc=desc, kind=kind,
                           hx=f"{hx:.2f}", hy=f"{hy:.2f}", h=f"{h:.2f}", size_note=size_note, prefix=prefix,
                           body_tris=f"{body_tris:,}", col_tris=col_tris, name=name, tex=tex,
                           export=EXPORT.format(name=name), snake=snake(name))
    with open(os.path.join(folder, "prompt.md"), "w") as f:
        f.write(prompt)
    spec = {
        "name": name,
        "fbx": f"{name}.fbx",
        "visible_budget": body_tris,
        "position_tolerance_m": 0.005,
        "uv_layers": 1,
        "uv_min_fill": 0.4,
        "textures": [{"file": f"T_{name}_BaseColor.png", "size": tex}],
        "objects": {
            f"{prefix}_Body": {"origin": [0, 0, 0], "max_tris": body_tris},
            f"{prefix}_Collision": {"origin": [0, 0, 0], "collision": True, "max_tris": col_tris},
        },
        "envelope": {"x": [-hx, hx], "y": [-hy, hy], "z": [0, h]},
        "near_points": [{"label": "stands on the floor at the origin", "object": f"{prefix}_Body",
                         "point": [0, 0, 0], "max_dist": 0.01}],
    }
    with open(os.path.join(folder, "spec.json"), "w") as f:
        json.dump(spec, f, indent=2)
        f.write("\n")


def main():
    for name, (label, h, desc) in EQUIPMENT.items():
        write(os.path.join("art", "equipment", name), name, name, label, desc, "equipment",
              0.20, 0.18, h, 1500, 60, 512,
              "It is a piece of equipment that stands on a food cart's counter (in one of two slots) or on the "
              "truck's counter behind its serving hatch, and the player can also buy it in a shop and carry it. "
              "Customers see it from the street, a few metres away, so it must say at a glance what the vendor sells.",
              "The cart's counter is small; stay inside this footprint so any piece fits any slot. Use most of it: "
              "the piece should look full-size for a cart, not like a toy.")
    for name, (label, desc) in PRODUCTS.items():
        write(os.path.join("art", "products", name), name, name, label, desc, "item",
              0.13, 0.13, 0.15, 600, 40, 256,
              "It is a food item the vendor sells over the counter; the game shows it in a customer's hand, on "
              "shelves and as the picture of a stock box, so it must read clearly at small size.",
              "A hand-held portion: about the size of a real one.")


if __name__ == "__main__":
    main()

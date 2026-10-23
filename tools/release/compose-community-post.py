"""Square announcement layout using unchanged model renders and the HART. SVG master.

Run with a Python environment containing fontTools and Pillow. All type is outlined in the
SVG, so the exported artwork does not depend on installed fonts.
"""
from pathlib import Path
import argparse
import base64
import hashlib
import json
from xml.sax.saxutils import quoteattr, escape

from PIL import Image
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont


parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument('--brand', type=Path)
args = parser.parse_args()
root = args.root.resolve()
brand = args.brand or root.parent / 'hart-group/brand'
build = root / 'build/workshop-0.10.0/community-post'
out = root / 'art/social/0.10.0'
build.mkdir(parents=True, exist_ok=True)
out.mkdir(parents=True, exist_ok=True)

archivo = TTFont(brand / 'fonts/Archivo-variable.ttf')
fonts = {
    400: instantiateVariableFont(archivo, {'wght': 400, 'wdth': 100}, inplace=False),
    800: instantiateVariableFont(archivo, {'wght': 800, 'wdth': 100}, inplace=False),
    'mono': TTFont(brand / 'fonts/IBMPlexMono-Medium.ttf'),
}
ink, green = '#15181A', '#00703A'


def lettering(label, x, y, size, weight=400, color=ink, tracking=0, anchor='start'):
    font = fonts[weight]
    glyphs, cmap = font.getGlyphSet(), font.getBestCmap()
    scale = size / font['head'].unitsPerEm
    advance = 0
    paths = []
    for char in label:
        name = cmap[ord(char)]
        pen = SVGPathPen(glyphs)
        glyphs[name].draw(pen)
        paths.append(f'<path transform="translate({advance:.4f} 0)" d="{pen.getCommands()}"/>')
        advance += font['hmtx'][name][0] + tracking / scale
    width = (advance - tracking / scale) * scale
    if anchor == 'end':
        x -= width
    elif anchor == 'middle':
        x -= width / 2
    return f'<g aria-label={quoteattr(label)} fill="{color}" transform="translate({x:.4f} {y}) scale({scale:.8f} {-scale:.8f})">{"".join(paths)}</g>'


def asset(file, x, y, w, h, mime='image/png'):
    data = base64.b64encode(file.read_bytes()).decode()
    return f'<image href="data:{mime};base64,{data}" x="{x}" y="{y}" width="{w}" height="{h}" preserveAspectRatio="xMidYMid meet"/>'


models = root / 'build/workshop-0.10.0/models'
logo = root / 'art/workshop/brand/hart-wordmark-on-light.svg'
parts = ['''<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1600" viewBox="0 0 1600 1600">
<title>HART. Street Food — community beta is live</title>
<desc>The mod's actual food cart and truck models on a clean pale background.</desc>
<defs>
  <linearGradient id="paper" x2="0" y2="1"><stop stop-color="#F7F5ED"/><stop offset="1" stop-color="#E9ECE6"/></linearGradient>
  <filter id="floor"><feGaussianBlur stdDeviation="22"/></filter>
</defs>
<rect width="1600" height="1600" fill="url(#paper)"/>''']
parts.append(asset(logo, 65, 24, 345, 345 * 200 / 545.62591, 'image/svg+xml'))
parts.append(lettering('A MOD FOR BIG AMBITIONS', 1504, 108, 26, 'mono', '#536259', tracking=.8, anchor='end'))
parts.append('<path d="M96 174H1504" stroke="#B8C4B9" stroke-width="2"/>')
parts.append(lettering('STREET FOOD', 800, 366, 174, 800, tracking=-3.0, anchor='middle'))
parts.append(lettering('From your first cart to a fleet.', 800, 443, 40, 400, '#536259', anchor='middle'))
parts.append('<ellipse cx="971" cy="1238" rx="408" ry="42" fill="#667866" opacity=".17" filter="url(#floor)"/>')
parts.append(asset(models / 'truck.png', 302, 470, 1272, 848))
parts.append('<ellipse cx="317" cy="1302" rx="193" ry="29" fill="#667866" opacity=".17" filter="url(#floor)"/>')
parts.append(asset(models / 'cart.png', 25, 752, 585, 585))
parts.append('<path d="M96 1386H1504" stroke="#B8C4B9" stroke-width="2"/>')
parts.append(lettering('COMMUNITY BETA IS LIVE', 800, 1470, 56, 800, green, tracking=.5, anchor='middle'))
parts.append(lettering('Available now on Steam Workshop', 800, 1530, 32, 400, '#536259', anchor='middle'))
parts.append('</svg>')
svg = build / 'hart-street-food-community-square.svg'
svg.write_text('\n'.join(parts))
sources = [logo, models / 'cart.png', models / 'truck.png']
record = {
    'title': 'HART. Street Food — community beta is live',
    'size': [1600, 1600],
    'workshop_url': 'https://steamcommunity.com/sharedfiles/filedetails/?id=3822355169',
    'source_assets': [{'path': str(p.relative_to(root)), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in sources],
    'note': 'Exact mod-model renders and original HART. logo. Models and logo are not redrawn. Type is outlined from the HART brand fonts.',
}
(out / 'community-post-sources.json').write_text(json.dumps(record, indent=2) + '\n')
print(svg)


def title_size(label, maximum=110):
    font = fonts[800]
    units = sum(font['hmtx'][font.getBestCmap()[ord(c)]][0] for c in label)
    return min(maximum, 1360 * font['head'].unitsPerEm / units)


def model_fit(name, x, y, w, h):
    """Place unchanged pixels by their visible bounds, excluding transparent margins."""
    path = models / (name + '.png')
    with Image.open(path) as picture:
        sw, sh = picture.size
        left, top, right, bottom = picture.getchannel('A').getbbox()
    scale = min(w / (right-left), h / (bottom-top))
    px = x + (w-(right-left)*scale)/2 - left*scale
    py = y + (h-(bottom-top)*scale)/2 - top*scale
    return asset(path, px, py, sw*scale, sh*scale)


def showcase_base(title, subtitle):
    return [f'''<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1600" viewBox="0 0 1600 1600">
<title>HART. Street Food — {escape(title)}</title>
<desc>Promotional artwork composed from the mod's original model renders.</desc>
<defs>
  <linearGradient id="paper" x2="0" y2="1"><stop stop-color="#F7F5ED"/><stop offset="1" stop-color="#E9ECE6"/></linearGradient>
  <filter id="floor"><feGaussianBlur stdDeviation="22"/></filter>
</defs>
<rect width="1600" height="1600" fill="url(#paper)"/>''',
        asset(logo, 65, 24, 345, 345*200/545.62591, 'image/svg+xml'),
        lettering('A MOD FOR BIG AMBITIONS', 1504, 108, 26, 'mono', '#536259', tracking=.8, anchor='end'),
        '<path d="M96 174H1504" stroke="#B8C4B9" stroke-width="2"/>',
        lettering(title, 800, 333, title_size(title), 800, anchor='middle'),
        lettering(subtitle, 800, 411, 37, 400, '#536259', anchor='middle'),
    ]


def showcase_footer():
    return [
        '<path d="M96 1386H1504" stroke="#B8C4B9" stroke-width="2"/>',
        lettering('COMMUNITY BETA IS LIVE', 800, 1470, 56, 800, green, tracking=.5, anchor='middle'),
        lettering('Available now on Steam Workshop', 800, 1530, 32, 400, '#536259', anchor='middle'),
        '</svg>',
    ]


showcase_sources = set()
showcase_files = []


def save_showcase(name, pieces, used_models):
    file = build / f'hart-street-food-{name}-square.svg'
    file.write_text('\n'.join(pieces + showcase_footer()))
    showcase_files.append(file.name)
    showcase_sources.update(models / (n+'.png') for n in used_models)
    print(file)


def quartet(name, title, subtitle, entries):
    pieces = showcase_base(title, subtitle)
    # No panel borders: the models and their names carry the composition.
    for i, (model, label) in enumerate(entries):
        x, y = 116 + (i%2)*736, 481 + (i//2)*465
        pieces.append(model_fit(model, x, y, 632, 354))
        pieces.append(lettering(label, x+316, y+404, 37, 800, anchor='middle'))
    save_showcase(name, pieces, [model for model, _ in entries])


quartet('02-new-foods', "WHAT'S ON THE MENU?", 'Four new favourites for your cart or truck.', [
    ('Dumplings', 'Dumplings'), ('Tacos', 'Tacos'),
    ('ChoppedCheese', 'Chopped cheese'), ('CornCob', 'Grilled corn'),
])
quartet('03-kitchen', 'BUILD YOUR KITCHEN.', 'Choose your equipment. Shape your menu.', [
    ('DumplingSteamer', 'Dumpling steamer'), ('Griddle', 'Griddle'),
    ('CornGrill', 'Corn grill'), ('PizzaOven', 'Pizza oven'),
])
quartet('04-treats', 'THE SWEETER SIDE.', 'Coffee, pastries, slushies and popcorn.', [
    ('CoffeeStation', 'Coffee station'), ('PastryCase', 'Pastry case'),
    ('SlushieMachine', 'Slushie machine'), ('PopcornMachine', 'Popcorn machine'),
])
pieces = showcase_base('YOUR SHOP ON WHEELS.', 'An open hatch. A fitted kitchen. Room to grow.')
pieces.append('<ellipse cx="800" cy="1227" rx="560" ry="30" fill="#667866" opacity=".15" filter="url(#floor)"/>')
pieces.append(model_fit('truck-hatch', 96, 482, 1408, 770))
pieces.append(lettering('4 EQUIPMENT SLOTS', 438, 1330, 29, 'mono', green, anchor='middle'))
pieces.append(lettering('UP TO 2 CREW', 1162, 1330, 29, 'mono', green, anchor='middle'))
save_showcase('05-truck', pieces, ['truck-hatch'])
showcase_record = {
    'size': [1600, 1600],
    'files': showcase_files,
    'source_assets': [
        {'path': str(p.relative_to(root)), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
        for p in [logo, *sorted(showcase_sources)]
    ],
    'note': record['note'],
}
(out / 'showcase-sources.json').write_text(json.dumps(showcase_record, indent=2)+'\n')

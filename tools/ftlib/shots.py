import base64
import html
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def fetch_shot(source, directory, name=None):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    filename = name or re.split(r'[/\\]', source)[-1]
    filename = re.sub(r'[^a-zA-Z0-9_.-]', '_', filename)
    target = directory / filename
    if Path(source).is_file():
        if Path(source).resolve() != target.resolve():
            shutil.copyfile(source, target)
    else:
        host = os.environ.get('BA_GAME_HOST')
        if not host:
            raise ValueError(f'Screenshot is on the game PC at {source}: set BA_GAME_HOST to its ssh host')
        result = subprocess.run(['scp', '--', f'{host}:{source}', str(target)],
                                capture_output=True, text=True)
        if result.returncode:
            raise ValueError(f'Could not fetch screenshot {source}: check BA_GAME_HOST and ssh access ({result.stderr.strip()})')
    if not target.read_bytes().startswith(b'\x89PNG\r\n\x1a\n'):
        raise ValueError(f'Invalid PNG at {target}: take the screenshot again')
    return target.resolve()


def contact_sheet(directory):
    directory = Path(directory)
    annotations = {}
    report = directory / 'report.json'
    if report.exists():
        for step in json.loads(report.read_text()).get('steps', []):
            if step.get('shot'):
                annotations[Path(step['shot']).name] = f"Step {step['step']}: {step.get('note', '')}"
    tiles = []
    for path in sorted(directory.glob('*.png')):
        encoded = base64.b64encode(path.read_bytes()).decode()
        tiles.append(f'<figure><a href="data:image/png;base64,{encoded}"><img src="data:image/png;base64,{encoded}" alt="{html.escape(path.name, quote=True)}"></a><figcaption>{html.escape(path.name)}<p>{html.escape(annotations.get(path.name, ""))}</p></figcaption></figure>')
    page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Food Trucks shots</title><style>
:root{color-scheme:light dark}body{font:16px system-ui;margin:24px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:16px}figure{margin:0;border:1px solid #8885;border-radius:8px;overflow:hidden}img{width:100%;display:block}figcaption{padding:12px;overflow-wrap:anywhere}p{margin:8px 0 0;opacity:.75}
</style><h1>Food Trucks shots</h1><main>''' + ''.join(tiles) + '</main></html>'
    destination = directory / 'index.html'
    destination.write_text(page)
    return destination.resolve()

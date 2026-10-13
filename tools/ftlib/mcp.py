import base64
import json
import math
from pathlib import Path
import re
import sys

from .bridge import Bridge, BridgeError
from .logcheck import check_log, format_report
from .paths import lookup
from .shots import ROOT, fetch_shot


def tool(name, description, properties=None, required=()):
    return {'name': name, 'description': description,
            'inputSchema': {'type': 'object', 'properties': properties or {},
                            'required': list(required), 'additionalProperties': False}}


STRING = {'type': 'string'}
NUMBER = {'type': 'number', 'minimum': 0}
TOOLS = [
    tool('ft_health', 'Read bridge session, version, scene, clock and load completion (load.status pending/done/failed). Answers during pending calls and after restarts; restarted/reconnect_required report a changed session. Health does not reconnect or allow mutations. Use ft_reconnect to begin a fresh session.'),
    tool('ft_reconnect', 'Explicitly pin the current bridge session and discard old operation ids and event cursors. Use after ft_health reports a restart or a call detects one. Changing calls are refused locally until reconnect; every pinned call also sends expect_session so the bridge rejects a stale request before running it. Resume with events since=1 and fresh scenario values.'),
    tool('ft_state', 'Read game state, optionally at a dotted path or list filter. Includes load progress, clock targets/arrival and keep_needs, player activity/clock holder/needs (energy, hunger, happiness), available hires, bay readiness and today/yesterday transactions.', {'path': STRING}),
    tool('ft_call', 'Run a command as built in docs/test-bridge.md. new_game is unavailable: load an FT save made by hand. FT saves must have a character of their own. timeout_s is clamped to 1..600 seconds. Unpause before vehicle entry and other walks, including their waits. Vehicle open/exit/restock/rent_bay require driving or pushing it; enter and back_to_hatch reply before walking finishes, so poll player.activity and in_vehicle/at_hatch. Check vehicle/crew result.done; vehicle.why is null or a refused commissary choice. Save returns name, path and bytes only after verified serialization/compression and file output. Use commissary order, commissary bay_candidates (site, radius up to 80; legal truck spots with walk and rejection reasons, reads only) and crew assign/unassign with state.employees ids. load from a non-FT city is refused; wait for load.status done. Replies carry op ids; main-thread timeouts poll that op for up to 30s without repeating the command. Use ft_op to follow longer work. Open/exit need unpaused time. Time speed is >0 to 60; run_until accepts +N days, then stops exactly on the target and pauses at speed 1. run_until keeps the player energy and hunger topped up (keep_needs, default true for run_until, false for speed alone) so a long run never ends in a collapse; state.player.needs shows them. Close clock-holding screens before unpausing.',
         {'cmd': STRING, 'args': {'type': 'object'}, 'timeout_s': {'type':'number','description':'Clamped to 1..600 seconds.'}}, ('cmd',)),
    tool('ft_events', 'Read events numbered since and later (inclusive; default 1). Use next as the next cursor; dropped counts lost events. Replies carry a session; a changed session or lower next means a bridge restart and fails the read. Start a fresh scenario run because old events and remembered values are void. Optionally filter by regex.',
         {'since': {'type': 'integer', 'minimum': 0}, 'grep': STRING}),
    tool('ft_op', 'Query a recorded operation without repeating it. Returns session, id, cmd and status pending/done/failed, with result or error when finished. A session change fails recovery; never retry an old mutation after a restart. The bridge keeps 500 operations; query replies have no new op id.',
         {'id': {'type':'integer','minimum':1}}, ('id',)),
    tool('ft_wait_event', 'Wait for a new event matching a regex.', {'regex': STRING, 'timeout_s': NUMBER}, ('regex',)),
    tool('ft_ui_list', 'List visible buttons, texts and all visible dropdown options, even when the dropdown is closed. Entries include kind and ids from this list; changed labels refuse stale id clicks. Visibility does not check clipping or overlapping windows; refresh after changes.'),
    tool('ft_ui_click', 'Requires a loaded FT city with its own character. Native save/load/delete/character controls and pause-menu confirmations refuse; use ft_call save/load. Click a unique, case-insensitive button or option text, or an id from the last ui list. Selecting a list-picker option also needs its confirm button. Duplicate employee options need the correct row id.',
         {'text': STRING, 'id': {'type': 'integer'}}),
    tool('ft_shoot', 'In the city, render 8 frames into a separate camera and fetch the PNG. Either pose and target (a named pose; the bridge pulls the camera in front of walls or picks another side, and says so in pose_note), or a free camera: x/y/z (world metres) with look_at {x,y,z} or yaw (degrees clockwise from +z) and pitch (degrees down), and fov (5 to 120, default 60). The reply has camera (where it stood), pose_note, and uncovered/outlined: the buildings whose view-blocking fade (for the game camera) or hover outline the bridge lifted for its picture. Name: 1 to 80 letters/digits/._-. Width 16 to 3840, height 16 to 2160. timeout_s defaults to 20. Move near distant targets so their street is loaded.',
         {'name': STRING, 'pose': STRING, 'target': STRING, 'width': {'type': 'integer', 'minimum': 16, 'maximum':3840},
          'height': {'type': 'integer', 'minimum': 16, 'maximum':2160}, **{key: {'type': 'number'} for key in ('x', 'y', 'z', 'yaw', 'pitch')},
          'fov': {'type': 'number', 'minimum': 5, 'maximum': 120},
          'look_at': {'type': 'object', 'description': 'The point a free camera looks at: {"x", "y", "z"} in world metres.'},
          'timeout_s': {'type': 'number', 'description': 'Clamped to 1..600 seconds; default 20.'},
          'compare': {'type': 'boolean', 'description': "Also the as-is picture with the game's fades and outlines left on (asis_path, fetched as local_asis_path)."},
          'frames': {'type': 'integer', 'minimum': 8, 'maximum': 240, 'description': 'Frames rendered before the picture is read; default 8.'},
          'game_view': {'type': 'boolean', 'description': "Also the game's own frame as the player sees it (game_path, fetched as local_game_path)."}}, ('name',)),
    tool('ft_check_log', 'Check a local Player.log. Row rules count only when rows are selected.',
         {'path': STRING, 'version': STRING, 'rows': STRING}, ('path',)),
]


def validate_args(schema, args):
    if not isinstance(args, dict):
        raise ValueError('Tool arguments must be an object: send a JSON object')
    if set(args) - schema['properties'].keys():
        raise ValueError('Unknown tool argument: use the fields in tools/list')
    for key in schema['required']:
        if key not in args:
            raise ValueError(f'Missing {key}: supply it as shown in tools/list')
    for key, val in args.items():
        field = schema['properties'][key]
        expected = field['type']
        valid = {'string': isinstance(val, str), 'object': isinstance(val, dict),
                 'integer': isinstance(val, int) and not isinstance(val, bool),
                 'number': isinstance(val, (int, float)) and not isinstance(val, bool),
                 'boolean': isinstance(val, bool)}[expected]
        if not valid or (expected == 'number' and isinstance(val,float) and not math.isfinite(val)) or ('minimum' in field and val < field['minimum']) or ('maximum' in field and val > field['maximum']):
            raise ValueError(f'Invalid {key}: use a {expected} within the limits in tools/list')


def call_tool(name, args, bridge):
    spec = next((t for t in TOOLS if t['name'] == name), None)
    if spec is None:
        raise ValueError(f'Unknown tool {name}: call tools/list for available tools')
    validate_args(spec['inputSchema'], args)
    image = None
    if name == 'ft_health':
        result = bridge.health()
    elif name == 'ft_reconnect':
        result = bridge.reconnect()
    elif name == 'ft_state':
        reply = bridge.call('state')
        result = lookup(reply['result'], args.get('path', ''))
    elif name == 'ft_call':
        result = bridge.call(args['cmd'], args.get('args', {}), args.get('timeout_s', 10))
    elif name == 'ft_op':
        result = bridge.op(args['id'])
    elif name == 'ft_events':
        result = bridge.events(args.get('since', 0))
        if args.get('grep'):
            regex = re.compile(args['grep'])
            result['events'] = [e for e in result['events'] if regex.search(e['text'])]
    elif name == 'ft_wait_event':
        cursor = bridge.events()['next']
        result = bridge.wait_event(args['regex'], args.get('timeout_s', 30), cursor)
    elif name == 'ft_ui_list':
        result = bridge.call('ui', {'op': 'list'})
    elif name == 'ft_ui_click':
        if ('text' in args) == ('id' in args):
            raise ValueError('Click needs one text or id: read ft_ui_list and choose one')
        result = bridge.call('ui', {'op': 'click', **args})
    elif name == 'ft_shoot':
        if not ({'pose', 'target'} <= args.keys() or {'x', 'y', 'z'} <= args.keys()):
            raise ValueError('Shot needs pose and target, or explicit camera coordinates: use the contract poses')
        if 'look_at' in args and ('pose' in args or 'yaw' in args or 'pitch' in args):
            raise ValueError('look_at goes with a free camera (x/y/z) instead of yaw and pitch: drop pose, yaw and pitch')
        if 'look_at' in args and not all(isinstance(args['look_at'].get(k), (int, float)) and not isinstance(args['look_at'].get(k), bool) for k in 'xyz'):
            raise ValueError('look_at needs numbers x, y and z: send {"x": …, "y": …, "z": …}')
        shot = {k: v for k, v in args.items() if k != 'timeout_s'}
        result = bridge.call('shoot', shot, args.get('timeout_s', 20))
        local = fetch_shot(result['result']['path'], ROOT / 'build/shots')
        result['local_path'] = str(local)
        for key in ('asis_path', 'game_path'):
            if result['result'].get(key):
                result['local_' + key] = str(fetch_shot(result['result'][key], ROOT / 'build/shots'))
        image = {'type': 'image', 'mimeType': 'image/png', 'data': base64.b64encode(local.read_bytes()).decode()}
    elif name == 'ft_check_log':
        report = check_log(args['path'], args.get('version'), args.get('rows'))
        return {'content': [{'type': 'text', 'text': format_report(report)}],
                'structuredContent': report, 'isError': not report['ok']}
    content = [{'type': 'text', 'text': json.dumps(result, ensure_ascii=False)}]
    if image:
        content.append(image)
    return {'content': content, 'structuredContent': result if isinstance(result, dict) else {'value': result}, 'isError': False}


def respond(request, bridge):
    if not isinstance(request, dict) or request.get('jsonrpc') != '2.0' or not isinstance(request.get('method'), str):
        return {'jsonrpc': '2.0', 'id': request.get('id') if isinstance(request, dict) else None,
                'error': {'code': -32600, 'message': 'Invalid request: send a JSON-RPC 2.0 object'}}
    if 'id' not in request:
        return None
    response = {'jsonrpc': '2.0', 'id': request['id']}
    params = request.get('params', {})
    method = request['method']
    if not isinstance(params, dict):
        response['error'] = {'code': -32602, 'message': 'Invalid params: send an object'}
        return response
    if method == 'initialize':
        response['result'] = {'protocolVersion': '2025-06-18', 'capabilities': {'tools': {}},
                              'serverInfo': {'name': 'foodtrucks-game', 'version': '1.0.0'}}
    elif method in ('ping', 'notifications/initialized'):
        response['result'] = {}
    elif method == 'tools/list':
        response['result'] = {'tools': TOOLS}
    elif method == 'tools/call':
        try:
            response['result'] = call_tool(params['name'], params.get('arguments', {}), bridge)
        except (BridgeError, ValueError, OSError, KeyError, TypeError, re.error) as exc:
            message = str(exc).replace('\n', ' ')
            print(message, file=sys.stderr, flush=True)
            response['result'] = {'content': [{'type': 'text', 'text': message}], 'isError': True}
            if isinstance(exc, BridgeError) and exc.reply:
                response['result']['structuredContent'] = exc.reply
    else:
        response['error'] = {'code': -32601, 'message': f'Unknown method {method}: use initialize, tools/list, tools/call or ping'}
    return response


def main():
    bridge = Bridge()
    for line in sys.stdin:
        try:
            request = json.loads(line)
            response = respond(request, bridge)
        except ValueError:
            response = {'jsonrpc': '2.0', 'id': None, 'error': {'code': -32700, 'message': 'Invalid JSON: send one JSON-RPC object per line'}}
        if response is not None:
            print(json.dumps(response, ensure_ascii=False), flush=True)

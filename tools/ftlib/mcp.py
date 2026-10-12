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
    tool('ft_health', 'Read bridge version, scene and clock.'),
    tool('ft_state', 'Read game state, optionally at a dotted path or list filter.', {'path': STRING}),
    tool('ft_call', 'Run a command from docs/test-bridge.md on an FT test save.',
         {'cmd': STRING, 'args': {'type': 'object'}, 'timeout_s': NUMBER}, ('cmd',)),
    tool('ft_events', 'Read events after a sequence cursor, optionally matching a regex.',
         {'since': {'type': 'integer', 'minimum': 0}, 'grep': STRING}),
    tool('ft_wait_event', 'Wait for a new event matching a regex.', {'regex': STRING, 'timeout_s': NUMBER}, ('regex',)),
    tool('ft_ui_list', 'List visible buttons, dropdown options and texts.'),
    tool('ft_ui_click', 'Click one uniquely matching button text or one button id.',
         {'text': STRING, 'id': {'type': 'integer'}}),
    tool('ft_shoot', 'Take a PNG with a pose and target, or explicit camera coordinates. Fetch it and return the image.',
         {'name': STRING, 'pose': STRING, 'target': STRING, 'width': {'type': 'integer', 'minimum': 1},
          'height': {'type': 'integer', 'minimum': 1}, **{key: {'type': 'number'} for key in ('x', 'y', 'z', 'yaw', 'pitch', 'fov')}}, ('name',)),
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
                 'number': isinstance(val, (int, float)) and not isinstance(val, bool)}[expected]
        if not valid or (expected == 'number' and not math.isfinite(val)) or ('minimum' in field and val < field['minimum']):
            raise ValueError(f'Invalid {key}: use a {expected} within the limits in tools/list')


def call_tool(name, args, bridge):
    spec = next((t for t in TOOLS if t['name'] == name), None)
    if spec is None:
        raise ValueError(f'Unknown tool {name}: call tools/list for available tools')
    validate_args(spec['inputSchema'], args)
    image = None
    if name == 'ft_health':
        result = bridge.health()
    elif name == 'ft_state':
        reply = bridge.call('state')
        result = lookup(reply['result'], args.get('path', ''))
    elif name == 'ft_call':
        result = bridge.call(args['cmd'], args.get('args', {}), args.get('timeout_s', 10))
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
        if not ({'pose', 'target'} <= args.keys() or {'x', 'y', 'z', 'yaw', 'pitch', 'fov'} <= args.keys()):
            raise ValueError('Shot needs pose and target, or explicit camera coordinates: use the contract poses')
        result = bridge.call('shoot', args)
        local = fetch_shot(result['result']['path'], ROOT / 'build/shots')
        result['local_path'] = str(local)
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

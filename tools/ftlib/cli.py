import argparse
import json
import re
import sys
import time

from .bridge import Bridge, BridgeError
from .mouse import Mouse, parse_point
from .paths import lookup, parse_args
from .shots import fetch_shot


def readable(value, indent=0):
    if isinstance(value, dict):
        return '\n'.join(' ' * indent + str(k) + ': ' + (readable(v, indent + 2) if not isinstance(v, (dict, list)) else '\n' + readable(v, indent + 2)) for k, v in value.items())
    if isinstance(value, list):
        return '\n'.join(' ' * indent + '- ' + readable(v, indent + 2).lstrip() for v in value) or ' ' * indent + '(none)'
    return json.dumps(value, ensure_ascii=False) if not isinstance(value, str) else value


# Options of `ft` itself (before the subcommand) that take a value.
TOP_VALUE_OPTIONS = set()
# Options of `ft call` that take a value, so their value is never read as a key=value argument.
CALL_VALUE_OPTIONS = {'--args', '--timeout', '--json'}


def call_flags(argv):
    """For the `call` subcommand only: a --json not followed by a JSON object means "print the raw reply" (as --raw),
    wherever it stands, and the output flags move after the key=value arguments so argparse never splits them."""
    # The subcommand is the first word that isn't one of ft's own options.
    at = next((i for i, token in enumerate(argv) if not token.startswith('-')), None)
    if at is None or argv[at] != 'call':
        return argv
    head, rest = argv[:at + 1], argv[at + 1:]
    words, options = [], []
    i = 0
    while i < len(rest):
        token = rest[i]
        if token == '--json' and not (i + 1 < len(rest) and rest[i + 1].lstrip().startswith('{')):
            options.append('--raw')
        elif token in CALL_VALUE_OPTIONS and i + 1 < len(rest):
            options += [token, rest[i + 1]]
            i += 1
        elif token.startswith('-') and token != '-':
            options.append(token)
        else:
            words.append(token)
        i += 1
    return head + words + options


def point(text, option):
    try:
        values = [float(v) for v in text.split(',')]
    except ValueError:
        values = []
    if len(values) != 3:
        raise ValueError(f'{option} takes X,Y,Z in metres, for example 393.2,1.7,217.6')
    return dict(zip('xyz', values))


def shoot_args(args):
    """The shoot call's arguments: a named pose (--pose and --target) or a free camera (--at, then --look-at or
    --yaw/--pitch), with --fov, --width and --height for either."""
    named = args.pose is not None or args.target is not None
    if named and args.at is not None:
        raise ValueError('shoot takes --pose and --target, or --at for a free camera, not both')
    call = {'name': args.name}
    if named:
        if args.pose is None or args.target is None:
            raise ValueError('a named shot needs both --pose and --target')
        if args.look_at is not None or args.yaw is not None or args.pitch is not None:
            raise ValueError('--look-at, --yaw and --pitch go with --at (a free camera), not with --pose')
        call.update(pose=args.pose, target=args.target)
    elif args.at is not None:
        call.update(point(args.at, '--at'))
        if args.look_at is not None:
            if args.yaw is not None or args.pitch is not None:
                raise ValueError('a free camera takes --look-at or --yaw/--pitch, not both')
            call['look_at'] = point(args.look_at, '--look-at')
        else:
            if args.yaw is not None: call['yaw'] = args.yaw
            if args.pitch is not None: call['pitch'] = args.pitch
    else:
        raise ValueError('shoot needs --pose and --target, or --at X,Y,Z (with --look-at or --yaw/--pitch)')
    for key in ('fov', 'width', 'height', 'frames'):
        if getattr(args, key) is not None:
            call[key] = getattr(args, key)
    for key in ('compare', 'game_view'):
        if getattr(args, key):
            call[key] = True
    return call


def mouse_command(mouse, args):
    """`ft mouse OP ...`: the rig virtual mouse, with the same checks as the MCP tools."""
    op = args.mouse_op
    if op == 'status': return mouse.status(args.trace_id)
    if op == 'move': return mouse.move(parse_point(args.to), args.frames)
    if op == 'down': return mouse.down(parse_point(args.at) if args.at else None, args.button)
    if op == 'up': return mouse.up(args.button, args.settle_frames)
    if op == 'scroll': return mouse.scroll(args.x, args.y, parse_point(args.at) if args.at else None)
    if op == 'drag':
        return mouse.drag(parse_point(args.start), parse_point(args.end), args.button, args.steps, args.duration,
                          args.settle_frames, args.escape_after_release)
    return mouse.release()


def add_mouse(sub):
    p = sub.add_parser('mouse', help='The rig virtual mouse (agent test player only). Points: X,Y Unity screen pixels '
                                     '(bottom-left origin) or a ui-geometry target (ui3.4, ui3.4@U,V)')
    ops = p.add_subparsers(dest='mouse_op', required=True)
    q = ops.add_parser('status', help='Backends, position and buttons; --trace reads a gesture trace')
    q.add_argument('--trace', dest='trace_id')
    q = ops.add_parser('move', help='Hover to a point (starts an interaction; end it with mouse release)')
    q.add_argument('to')
    q.add_argument('--frames', type=int)
    q = ops.add_parser('down', help='Press a button (at a point when no interaction is active)')
    q.add_argument('at', nargs='?')
    q.add_argument('--button', type=int)
    q = ops.add_parser('up', help='Release a held button')
    q.add_argument('--button', type=int)
    q.add_argument('--settle-frames', dest='settle_frames', type=int)
    q = ops.add_parser('scroll', help='One-frame wheel pulse')
    q.add_argument('--x', type=float)
    q.add_argument('--y', type=float)
    q.add_argument('--at')
    q = ops.add_parser('drag', help='Self-contained press, move, release and cleanup')
    q.add_argument('start')
    q.add_argument('end')
    q.add_argument('--button', type=int)
    q.add_argument('--steps', type=int)
    q.add_argument('--duration', type=float, help='duration_s, 0 to 10 (default 0.4)')
    q.add_argument('--settle-frames', dest='settle_frames', type=int)
    q.add_argument('--escape-after-release', action='store_true',
                   help="Run ui escape in the frame the Schedule handle's native release returns")
    ops.add_parser('release', help="End this owner's interaction (idempotent)")
    for q in ops.choices.values():
        q.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    p = sub.add_parser('ui-geometry', help='Screen geometry (bottom-left pixels) and targets of UI components')
    p.add_argument('component', nargs='?')
    p.add_argument('--path-suffix', dest='path_suffix')
    p.add_argument('--max', type=int)
    p.add_argument('--target')
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)


def main():
    parser = argparse.ArgumentParser(description='Read and drive the Food Trucks test bridge')
    parser.add_argument('--json', action='store_true', dest='raw')
    sub = parser.add_subparsers(dest='command', required=True)
    for name in ('health', 'state', 'ui'):
        p = sub.add_parser(name)
        p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
        if name == 'state':
            p.add_argument('path', nargs='?')
    p = sub.add_parser('call')
    p.add_argument('cmd')
    p.add_argument('pairs', nargs='*')
    # --json anywhere on the line prints the raw JSON reply; --json '{...}' (a JSON object) is the call's arguments, as
    # --args '{...}' is.
    p.add_argument('--json', dest='json_option', metavar='OBJECT')
    p.add_argument('--args', dest='arguments', metavar='OBJECT', help='The call arguments as a JSON object')
    p.add_argument('--raw', action='store_true', dest='call_raw', help='Print the raw reply (as --json does)')
    p.add_argument('--timeout', type=float, default=10)
    p = sub.add_parser('op', help='Query an earlier operation without repeating it')
    p.add_argument('id', type=int)
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    p = sub.add_parser('events')
    p.add_argument('--since', type=int, default=0)
    p.add_argument('--follow', action='store_true')
    p.add_argument('--grep')
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    p = sub.add_parser('wait-event')
    p.add_argument('regex')
    p.add_argument('--timeout', type=float, default=30)
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    p = sub.add_parser('shoot', help='A named pose round a target, or a free camera (--at with --look-at or --yaw/--pitch)')
    p.add_argument('name')
    p.add_argument('--pose')
    p.add_argument('--target')
    p.add_argument('--at', metavar='X,Y,Z', help='Free camera: where it stands (world metres)')
    p.add_argument('--look-at', dest='look_at', metavar='X,Y,Z', help='Free camera: the point it looks at')
    p.add_argument('--yaw', type=float, help='Free camera: degrees clockwise from +z (instead of --look-at)')
    p.add_argument('--pitch', type=float, help='Free camera: degrees down (instead of --look-at)')
    p.add_argument('--fov', type=float, help='Field of view in degrees, 5 to 120 (default 60)')
    p.add_argument('--width', type=int)
    p.add_argument('--height', type=int)
    p.add_argument('--timeout', type=float, default=20, help='Seconds to wait for the picture (timeout_s, 1 to 600)')
    p.add_argument('--compare', action='store_true', help='Also the as-is picture, with the game\'s fades and outlines left on (<name>-asis.png)')
    p.add_argument('--game-view', dest='game_view', action='store_true', help='Also the game\'s own frame (<name>-game.png)')
    p.add_argument('--frames', type=int, help='Frames rendered before the picture is read, 8 to 240 (default 8)')
    p.add_argument('--fetch', help='Copy the PNG here (needs BA_GAME_HOST when the game runs on another PC)')
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    p = sub.add_parser('click')
    p.add_argument('text')
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    add_mouse(sub)
    args = parser.parse_args(call_flags(sys.argv[1:]))
    bridge = Bridge()
    try:
        if args.command == 'health':
            reply = bridge.health()
        elif args.command == 'state':
            reply = bridge.call('state')
            if not args.raw:
                reply = lookup(reply['result'], args.path)
        elif args.command == 'call':
            if args.json_option is not None:
                if args.arguments is not None:
                    raise ValueError('Give the call arguments once: --args or --json with an object')
                args.arguments = args.json_option
            reply = bridge.call(args.cmd, parse_args(args.pairs, args.arguments), args.timeout)
            args.raw = args.raw or args.call_raw
        elif args.command == 'op':
            reply = bridge.op(args.id)
        elif args.command == 'wait-event':
            cursor = bridge.events()['next']
            reply = bridge.wait_event(args.regex, args.timeout, cursor)
        elif args.command == 'mouse':
            reply = mouse_command(Mouse(bridge), args)
        elif args.command == 'ui-geometry':
            reply = Mouse(bridge).geometry(args.component, args.path_suffix, args.max, args.target)
        elif args.command in ('ui', 'click'):
            reply = bridge.call('ui', {'op': 'list'} if args.command == 'ui' else {'op': 'click', 'text': args.text})
        elif args.command == 'shoot':
            reply = bridge.call('shoot', shoot_args(args), args.timeout)
            if args.fetch:
                local = str(fetch_shot(reply['result']['path'], args.fetch))
                # In the result too, where --json readers look for the shot (live session 5), and at the top as before.
                reply['result']['local_path'] = local
                reply['local_path'] = local
                for key in ('asis_path', 'game_path'):
                    if reply['result'].get(key):
                        reply['result']['local_' + key] = str(fetch_shot(reply['result'][key], args.fetch))
        else:
            pattern = re.compile(args.grep) if args.grep else None
            while True:
                try:
                    reply = bridge.events(args.since)
                except BridgeError as exc:
                    if not args.follow or not exc.restart: raise
                    print(f'{exc}; following the new session from event 1',file=sys.stderr,flush=True)
                    bridge.reset_session()
                    args.since = 1
                    continue
                if reply['dropped']:
                    print(f"Lost {reply['dropped']} events: restart with a fresh cursor", file=sys.stderr)
                if pattern:
                    reply['events'] = [e for e in reply['events'] if pattern.search(e['text'])]
                if args.raw:
                    print(json.dumps(reply, ensure_ascii=False), flush=True)
                else:
                    for event in reply['events']:
                        print(f"{event['seq']} day {event['day']} {event['time']} [{event['kind']}] {event['text']}", flush=True)
                if not args.follow:
                    return 0
                args.since = reply['next']
                time.sleep(.2)
        if args.raw:
            print(json.dumps(reply, ensure_ascii=False, indent=2))
        else:
            if isinstance(reply, dict) and 'result' in reply:
                reply = reply['result'] | ({'op':reply['op']} if 'op' in reply else {}) | ({'local_path': reply['local_path']} if 'local_path' in reply else {})
            print(readable(reply))
        return 0
    except BridgeError as exc:
        if (args.raw or getattr(args,'call_raw',False)) and exc.reply:
            print(json.dumps(exc.reply, ensure_ascii=False))
        detail=f' (op {exc.op}; tools/ft op {exc.op})' if exc.op is not None else ''
        print(str(exc).replace('\n', ' ') + detail, file=sys.stderr)
        return 1
    except (ValueError, OSError, re.error) as exc:
        print(f'Cannot run command: {exc}; check tools/ft --help and the command arguments'.replace('\n', ' '), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        return 0

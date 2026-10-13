import argparse
import json
import re
import sys
import time

from .bridge import Bridge, BridgeError
from .paths import lookup, parse_args
from .shots import fetch_shot


def readable(value, indent=0):
    if isinstance(value, dict):
        return '\n'.join(' ' * indent + str(k) + ': ' + (readable(v, indent + 2) if not isinstance(v, (dict, list)) else '\n' + readable(v, indent + 2)) for k, v in value.items())
    if isinstance(value, list):
        return '\n'.join(' ' * indent + '- ' + readable(v, indent + 2).lstrip() for v in value) or ' ' * indent + '(none)'
    return json.dumps(value, ensure_ascii=False) if not isinstance(value, str) else value


def call_flags(argv):
    """After `call`, a --json not followed by a JSON object means "print the raw reply" (as --raw), wherever it stands."""
    if 'call' not in argv:
        return argv
    at = argv.index('call')
    out = argv[:at + 1]
    rest = argv[at + 1:]
    i = 0
    while i < len(rest):
        token = rest[i]
        if token == '--json' and not (i + 1 < len(rest) and rest[i + 1].lstrip().startswith('{')):
            out.append('--raw')
        else:
            out.append(token)
        i += 1
    return out


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
    p = sub.add_parser('shoot')
    p.add_argument('name')
    p.add_argument('--pose', required=True)
    p.add_argument('--target', required=True)
    p.add_argument('--fetch')
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
    p = sub.add_parser('click')
    p.add_argument('text')
    p.add_argument('--json', action='store_true', dest='raw', default=argparse.SUPPRESS)
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
        elif args.command in ('ui', 'click'):
            reply = bridge.call('ui', {'op': 'list'} if args.command == 'ui' else {'op': 'click', 'text': args.text})
        elif args.command == 'shoot':
            reply = bridge.call('shoot', {'name': args.name, 'pose': args.pose, 'target': args.target})
            if args.fetch:
                reply['local_path'] = str(fetch_shot(reply['result']['path'], args.fetch))
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

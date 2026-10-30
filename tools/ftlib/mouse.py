"""The rig virtual mouse (docs/test-bridge.md, "Rig virtual mouse"): argument rules shared by ft, ft-mcp and the mock
bridge, and a small client. The rules match big-ambitions-agent-rig rig-input/Shared/MouseArgs.cs; the parity tests compare them
(tools/tests/test_rig_mouse.py, with BA_AGENT_RIG or the sibling checkout)."""
import math

PROTOCOL = 1
OPS = ('status', 'move', 'down', 'up', 'scroll', 'drag', 'release')
FIELDS = {
    'status': ('trace_id',),
    'move': ('to', 'frames'),
    'down': ('button', 'at'),
    'up': ('button', 'settle_frames'),
    'scroll': ('x', 'y', 'at'),
    'drag': ('from', 'to', 'button', 'steps', 'duration_s', 'settle_frames', 'after_release'),
    'release': (),
}
GEOMETRY_FIELDS = ('component', 'path_suffix', 'max', 'target')
# Whole-number arguments and their inclusive bounds; then finite-number arguments.
INTEGERS = {'frames': (1, 240), 'steps': (1, 240), 'settle_frames': (0, 120), 'button': (0, 2), 'max': (1, 50)}
NUMBERS = {'duration_s': (0, 10), 'x': (-100, 100), 'y': (-100, 100)}
DEFAULTS = {'button': 0, 'frames': 1, 'steps': 12, 'duration_s': 0.4, 'settle_frames': 2, 'max': 20}
AFTER_RELEASE = {'op': 'ui_escape', 'phase': 'after_native_release'}
HOLD_TIMEOUT_S = 30
ERROR_CODES = ('rig_mouse_unavailable', 'mouse_busy', 'mouse_invalid', 'mouse_protected', 'stale_ui_target', 'mouse_timeout',
               'mouse_cancelled', 'mouse_no_native_release', 'mouse_after_release_failed', 'mouse_unknown_trace')
# Added by the client and the lock layer; never part of an operation's own fields.
TRANSPORT = ('op', 'timeout_s')


class MouseArgsError(ValueError):
    pass


def _number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _check_fields(args, allowed, where):
    extra = set(args) - set(allowed) - set(TRANSPORT)
    if extra:
        raise MouseArgsError(f'mouse_invalid: unsupported field {sorted(extra)[0]!r} for {where}')


def _integer(args, key):
    value = args[key]
    low, high = INTEGERS[key]
    if not (_number(value) and float(value).is_integer() and low <= value <= high):
        raise MouseArgsError(f'mouse_invalid: {key} must be a whole number from {low} to {high}')


def _finite(args, key):
    value = args[key]
    low, high = NUMBERS[key]
    if not (_number(value) and low <= value <= high):
        raise MouseArgsError(f'mouse_invalid: {key} must be a finite number from {low} to {high}')


def point(value, name):
    """A point: {"x", "y"} Unity screen pixels (bottom-left origin) or {"target", "uv"?} from ui geometry."""
    if not isinstance(value, dict):
        raise MouseArgsError(f'mouse_invalid: {name} must be an object: {{"x":…,"y":…}} or {{"target":…}}')
    if 'target' in value:
        _check_fields(value, ('target', 'uv'), name)
        if not isinstance(value['target'], str) or not value['target']:
            raise MouseArgsError(f'mouse_invalid: {name}.target must be a target string from ui geometry')
        if 'uv' in value:
            uv = value['uv']
            if not isinstance(uv, dict) or set(uv) != {'x', 'y'} or not all(_number(uv[k]) and 0 <= uv[k] <= 1 for k in 'xy'):
                raise MouseArgsError(f'mouse_invalid: {name}.uv must be {{"x":0..1,"y":0..1}}')
        return value
    if set(value) - {'x', 'y'} or not all(k in value for k in 'xy'):
        raise MouseArgsError(f'mouse_invalid: {name} needs exactly "x" and "y" (or "target")')
    if not all(_number(value[k]) and value[k] >= 0 for k in 'xy'):
        raise MouseArgsError(f'mouse_invalid: {name} x and y must be finite screen pixels, 0 or more')
    return value


def mouse_args(op, args=None):
    """The checked `mouse` arguments for one operation. The bridge also checks screen bounds and targets."""
    args = dict(args or {})
    if op not in OPS:
        raise MouseArgsError(f"mouse_invalid: unknown mouse op {op!r}: {', '.join(OPS)}")
    args.pop('op', None)
    _check_fields(args, FIELDS[op], op)
    for key in args:
        if key in INTEGERS: _integer(args, key)
        elif key in NUMBERS: _finite(args, key)
    for key in ('to', 'from', 'at'):
        if key in args: point(args[key], key)
    if op == 'status' and 'trace_id' in args and not isinstance(args['trace_id'], str):
        raise MouseArgsError('mouse_invalid: trace_id must be a string')
    if op == 'move' and 'to' not in args:
        raise MouseArgsError('mouse_invalid: move needs "to"')
    if op == 'drag' and not {'from', 'to'} <= args.keys():
        raise MouseArgsError('mouse_invalid: drag needs "from" and "to"')
    if op == 'scroll' and not args.get('x') and not args.get('y'):
        raise MouseArgsError('mouse_invalid: scroll needs a nonzero x or y')
    if 'after_release' in args and args['after_release'] is not None and args['after_release'] != AFTER_RELEASE:
        raise MouseArgsError('mouse_invalid: after_release supports only {"op":"ui_escape","phase":"after_native_release"}')
    return {'op': op, **args}


def geometry_args(args=None):
    args = dict(args or {})
    args.pop('op', None)
    _check_fields(args, GEOMETRY_FIELDS, 'ui geometry')
    if 'max' in args: _integer(args, 'max')
    for key in ('component', 'path_suffix', 'target'):
        if key in args and (not isinstance(args[key], str) or not args[key]):
            raise MouseArgsError(f'mouse_invalid: {key} must be a nonempty string')
    if 'component' not in args and 'target' not in args:
        raise MouseArgsError('mouse_invalid: ui geometry needs "component" (e.g. WorkShiftSliderHandle) or "target"')
    return {'op': 'geometry', **args}


def timeout_for(op, args):
    """Seconds to wait for a reply: a gesture spends real frames (30 FPS in background mode) and its duration."""
    if op != 'drag':
        return 10
    frames = args.get('steps', DEFAULTS['steps']) + args.get('settle_frames', DEFAULTS['settle_frames']) + 8
    return min(600, max(10, args.get('duration_s', DEFAULTS['duration_s']) + frames / 15 + 5))


def parse_point(text):
    """CLI points: X,Y in Unity screen pixels, or a ui geometry target (ui3.4, or ui3.4@U,V for a local UV point)."""
    target, at, uv = text.partition('@')
    try:
        if text.startswith('ui') or at:
            result = {'target': target}
            if uv:
                u, v = (float(n) for n in uv.split(','))
                result['uv'] = {'x': u, 'y': v}
            return point(result, 'point')
        x, y = (float(n) for n in text.split(','))
    except ValueError as exc:
        if isinstance(exc, MouseArgsError): raise
        raise MouseArgsError('mouse_invalid: a point is X,Y in Unity screen pixels or a ui geometry target (ui3.4 or ui3.4@U,V)') from exc
    return point({'x': x, 'y': y}, 'point')


class Mouse:
    """`mouse` and `ui geometry` through an ftlib Bridge, with the same checks as ft and ft-mcp."""

    def __init__(self, bridge):
        self.bridge = bridge

    def call(self, op, **args):
        checked = mouse_args(op, {k: v for k, v in args.items() if v is not None})
        timeout = checked.pop('timeout_s', None) or timeout_for(op, checked)
        return self.bridge.call('mouse', checked, timeout)

    def status(self, trace_id=None): return self.call('status', trace_id=trace_id)
    def move(self, to, frames=None): return self.call('move', to=to, frames=frames)
    def down(self, at=None, button=None): return self.call('down', at=at, button=button)
    def up(self, button=None, settle_frames=None): return self.call('up', button=button, settle_frames=settle_frames)
    def scroll(self, x=None, y=None, at=None): return self.call('scroll', x=x, y=y, at=at)
    def release(self): return self.call('release')

    def drag(self, start, end, button=None, steps=None, duration_s=None, settle_frames=None, escape_after_release=False):
        return self.call('drag', **{'from': start}, to=end, button=button, steps=steps, duration_s=duration_s,
                         settle_frames=settle_frames, after_release=dict(AFTER_RELEASE) if escape_after_release else None)

    def geometry(self, component=None, path_suffix=None, max=None, target=None):
        args = geometry_args({k: v for k, v in dict(component=component, path_suffix=path_suffix, max=max, target=target).items() if v is not None})
        return self.bridge.call('ui', args)

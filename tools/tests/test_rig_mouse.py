"""The rig virtual mouse (docs/test-bridge.md, "Rig virtual mouse"): client rules against the rig source, MCP schemas,
the mock's protocol/timeline model and client transport. Native acceptance is a live rig check, never these tests."""
import json
from pathlib import Path
import re
import runpy
import sys
import tempfile
import unittest
from unittest.mock import patch
import os

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from ftlib import mcp, mouse
from ftlib.bridge import Bridge, BridgeError
from ftlib import cli

NS = runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_rig_mouse_test')
# The rig assembly lives in big-ambitions-agent-rig; the parity tests read it from BA_AGENT_RIG or the sibling checkout
# and are skipped without one (the mod never needs the rig to build or ship).
RIG = Path(os.environ.get('BA_AGENT_RIG') or TOOLS.parents[1] / 'big-ambitions-agent-rig') / 'rig-input'
HAVE_RIG = (RIG / 'Shared/MouseArgs.cs').is_file()
NO_RIG = f'needs the big-ambitions-agent-rig checkout ({RIG}); set BA_AGENT_RIG'


def _rig(name):
    return (RIG / name).read_text(encoding='utf-8') if HAVE_RIG else ''


SOURCE = '\n'.join(p.read_text(encoding='utf-8') for p in sorted((RIG / 'Shared').glob('*.cs')) + sorted((RIG / 'Runtime').glob('*.cs')))
ARGS_CS = _rig('Shared/MouseArgs.cs')
SERVICE_CS = _rig('Runtime/Service.cs')
ESCAPE = {'op': 'ui_escape', 'phase': 'after_native_release'}


@unittest.skipUnless(HAVE_RIG, NO_RIG)
class SourceParityTests(unittest.TestCase):
    """ftlib.mouse (used by ft, ft-mcp and the mock) states the same protocol as the rig assembly."""

    def const(self, name):
        return float(re.search(rf'\b{name}\s*=\s*([\d.]+)', ARGS_CS).group(1))

    def test_ops_and_fields_match(self):
        ops = re.search(r'Ops = \{([^}]*)\}', ARGS_CS).group(1)
        self.assertEqual(tuple(re.findall(r'"(\w+)"', ops)), mouse.OPS)
        for op, fields in mouse.FIELDS.items():
            row = re.search(rf'\["{op}"\] = new(?:\[\] \{{([^}}]*)\}}| string\[0\])', ARGS_CS)
            self.assertIsNotNone(row, op)
            self.assertEqual(tuple(re.findall(r'"(\w+)"', row.group(1) or '')), fields, op)
        geometry = re.search(r'GeometryFields = \{([^}]*)\}', ARGS_CS).group(1)
        self.assertEqual(tuple(re.findall(r'"(\w+)"', geometry)), mouse.GEOMETRY_FIELDS)

    def test_bounds_and_defaults_match(self):
        self.assertEqual(self.const('MaxSteps'), mouse.INTEGERS['steps'][1])
        self.assertEqual(self.const('MaxFrames'), mouse.INTEGERS['frames'][1])
        self.assertEqual(self.const('MaxSettle'), mouse.INTEGERS['settle_frames'][1])
        self.assertEqual(self.const('MaxDuration'), mouse.NUMBERS['duration_s'][1])
        self.assertEqual(self.const('MaxScroll'), mouse.NUMBERS['x'][1])
        self.assertEqual(self.const('HoldTimeoutSeconds'), mouse.HOLD_TIMEOUT_S)
        self.assertEqual(self.const('Protocol'), mouse.PROTOCOL)
        defaults = {
            'steps': r'Int\(args, "steps", (\d+)', 'settle_frames': r'Int\(args, "settle_frames", (\d+)',
            'frames': r'Int\(args, "frames", (\d+)', 'duration_s': r'Number\(args, "duration_s", ([\d.]+)',
        }
        for key, pattern in defaults.items():
            for found in re.findall(pattern, SERVICE_CS):
                self.assertEqual(float(found), mouse.DEFAULTS[key], key)
        self.assertIn(f'Int(args, "max", {mouse.DEFAULTS["max"]}, 1, {mouse.INTEGERS["max"][1]})', (RIG / 'Runtime/UiGeometry.cs').read_text())
        self.assertIn(f'"{ESCAPE["op"]}"', ARGS_CS)
        self.assertIn(f'"{ESCAPE["phase"]}"', ARGS_CS)

    def test_every_error_code_is_used(self):
        used = set(re.findall(r'"((?:rig_)?mouse_[a-z_]+|stale_ui_target)"', SOURCE))
        self.assertEqual(used, set(mouse.ERROR_CODES))

    def test_rig_assembly_gates_on_the_isolated_player(self):
        # Gating: the service refuses to start without both flags, an agent port and the isolated product.
        util = _rig('Runtime/Util.cs')
        for needle in ('"-agent-tests"', '"-foodtrucks-test"', 'Agent1 = 18330', 'AgentMac = 28330', '"Big Ambitions Agent Tests"', 'IsReady'):
            self.assertIn(needle, util)
        for project in RIG.glob('*.csproj'):
            text = project.read_text()
            self.assertNotIn('FoodTrucks', re.sub(r'<!--.*?-->', '', text, flags=re.S).replace('FoodTrucks.Core', ''))


class ModSideTests(unittest.TestCase):
    """What stays in Food Trucks: the lock gate, the docs and the rule that the rig assembly never enters the mod."""

    def test_every_error_code_is_documented(self):
        docs = (TOOLS.parent / 'docs/test-bridge.md').read_text(encoding='utf-8')
        for code in mouse.ERROR_CODES:
            self.assertIn(f'`{code}`', docs)

    def test_lock_gate_reads_status_and_geometry_only(self):
        gate = (TOOLS / 'bridge-locks/ControlGate.cs').read_text()
        self.assertIn('if (cmd == "mouse") return op == "status";', gate)
        self.assertIn('op == "geometry"', gate)

    def test_rig_assembly_stays_out_of_the_mod(self):
        mod = TOOLS.parent / 'unity/FoodTrucks'
        for path in mod.rglob('*'):
            if path.suffix in ('.cs', '.asmdef', '.json', '.csproj'):
                self.assertNotIn('AgentRigInput', path.read_text(encoding='utf-8', errors='ignore'), path)
        self.assertNotIn('rig-input', (TOOLS / 'build-mod.sh').read_text())
        self.assertNotIn('AgentRigInput', (TOOLS / 'build-mod.sh').read_text())
        # Its source lives in big-ambitions-agent-rig (old ignored bin/obj leftovers do not count).
        left = [p for p in (TOOLS / 'rig-input').rglob('*') if p.suffix in ('.cs', '.csproj', '.py') and not {'bin', 'obj'} & set(p.parts)]
        self.assertEqual(left, [])


class SchemaTests(unittest.TestCase):
    def tool(self, name):
        return next(t for t in mcp.TOOLS if t['name'] == name)['inputSchema']

    def test_every_mouse_tool_mirrors_the_shared_fields(self):
        for op in mouse.OPS:
            schema = self.tool('ft_mouse_' + op)
            expected = set(mouse.FIELDS[op]) | ({'timeout_s'} if op not in ('status', 'release') else set())
            self.assertEqual(set(schema['properties']), expected, op)
            self.assertFalse(schema['additionalProperties'])
            for key, (low, high) in {**mouse.INTEGERS, **mouse.NUMBERS}.items():
                if key in schema['properties']:
                    self.assertEqual((schema['properties'][key]['minimum'], schema['properties'][key]['maximum']), (low, high), key)
        self.assertEqual(set(self.tool('ft_mouse_drag')['required']), {'from', 'to'})
        self.assertEqual(set(self.tool('ft_ui_geometry')['properties']), set(mouse.GEOMETRY_FIELDS))

    def test_tools_validate_before_any_request_and_keep_credentials_outside_args(self):
        class Recorder:
            def __init__(self): self.calls = []
            def call(self, cmd, args=None, timeout_s=10): self.calls.append((cmd, args, timeout_s)); return {'ok': True, 'result': {}}
        bridge = Recorder()
        for name, args in [('ft_mouse_drag', {'from': {'x': 1, 'y': 1}, 'to': {'x': 2, 'y': 1}, 'steps': True}),
                           ('ft_mouse_drag', {'from': {'x': 1, 'y': 1}, 'to': {'x': 2, 'y': 1}, 'after_release': {'op': 'quit'}}),
                           ('ft_mouse_move', {'to': {'x': float('nan'), 'y': 1}}),
                           ('ft_mouse_up', {'button': 3}), ('ft_mouse_scroll', {}),
                           ('ft_mouse_status', {'lock_token': 'x'}), ('ft_ui_geometry', {})]:
            with self.subTest(name=name, args=args), self.assertRaises(ValueError):
                mcp.call_tool(name, args, bridge)
        self.assertEqual(bridge.calls, [])
        mcp.call_tool('ft_mouse_drag', {'from': {'target': 'ui2.1'}, 'to': {'x': 940, 'y': 500}, 'settle_frames': 0, 'after_release': ESCAPE}, bridge)
        cmd, args, timeout = bridge.calls[-1]
        self.assertEqual((cmd, args['op'], args['after_release']), ('mouse', 'drag', ESCAPE))
        self.assertNotIn('lock_token', args)
        self.assertGreaterEqual(timeout, 10)
        mcp.call_tool('ft_ui_geometry', {'component': 'WorkShiftSliderHandle'}, bridge)
        self.assertEqual(bridge.calls[-1][:2], ('ui', {'op': 'geometry', 'component': 'WorkShiftSliderHandle'}))
        # The generic ft_call still reaches the same command unchanged.
        mcp.call_tool('ft_call', {'cmd': 'mouse', 'args': {'op': 'status'}}, bridge)
        self.assertEqual(bridge.calls[-1][:2], ('mouse', {'op': 'status'}))


class MockMouseTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.mock = NS['Mock'](self.folder.name)
        self.mock.rig_mouse = 'ready'

    def call(self, args):
        return self.mock.call('mouse', args)

    def refused(self, code, cmd, args):
        with self.assertRaises(ValueError) as exc:
            self.mock.call(cmd, args)
        self.assertTrue(str(exc.exception).startswith(code + ':'), str(exc.exception))

    def handle(self, side='right'):
        g = self.mock.call('ui', {'op': 'geometry', 'component': 'WorkShiftSliderHandle', 'path_suffix': side.title() + 'Handle'})
        self.assertEqual(g['screen'], {'width': 1920, 'height': 1080, 'origin': 'bottom-left'})
        return g['elements'][0]

    def test_normal_bridge_has_no_mouse_and_unavailable_rig_says_why(self):
        self.mock.rig_mouse = None
        with self.assertRaisesRegex(ValueError, "Unknown command 'mouse'"):
            self.call({'op': 'status'})
        self.assertNotIn('rig_mouse', self.mock.health())
        self.mock.rig_mouse = 'unavailable'
        status = self.call({'op': 'status'})
        self.assertFalse(status['available'])
        self.assertEqual(self.mock.health()['rig_mouse']['status'], 'unavailable')
        self.refused('rig_mouse_unavailable', 'mouse', {'op': 'drag', 'from': {'x': 1, 'y': 1}, 'to': {'x': 2, 'y': 2}})

    def test_drag_is_one_sample_per_frame_with_whole_frame_edges(self):
        h = self.handle()
        self.assertEqual(h['schedule']['handle'], 'right')
        to = {'x': h['center']['x'] + h['schedule']['pixels_per_hour']['x'], 'y': h['center']['y']}
        r = self.call({'op': 'drag', 'from': {'target': h['target']}, 'to': to})
        self.assertFalse(r['active'])
        self.assertEqual(r['buttons'], [False] * 3)
        self.assertTrue(r['down_frame'] < r['last_move_frame'] < r['up_frame'] < r['completed_frame'])
        self.assertEqual(r['completed_frame'], r['up_frame'] + 2)
        self.assertTrue(r['native_release_observed'])
        self.assertTrue(r['mock'])
        trace = self.call({'op': 'status', 'trace_id': r['trace_id']})['trace']
        frames = [e['frame'] for e in trace]
        self.assertEqual(frames, sorted(set(frames)))
        self.assertEqual([e['phase'] for e in trace][:3], ['prepare', 'hover', 'down'])
        self.assertEqual(sum(1 for e in trace if e['down']), 1)
        self.assertEqual(sum(1 for e in trace if e['up']), 1)
        hold = next(e for e in trace if e['phase'] == 'hold')
        self.assertEqual((hold['x'], hold['buttons'][0]), (to['x'], True))

    def test_raw_points_have_no_release_observer_and_after_release_needs_one(self):
        r = self.call({'op': 'drag', 'from': {'x': 10, 'y': 10}, 'to': {'x': 30, 'y': 10}, 'steps': 2, 'duration_s': 0})
        self.assertIsNone(r['native_release_observed'])
        self.assertIsNone(r['native_release_frame'])
        self.refused('mouse_invalid', 'mouse', {'op': 'drag', 'from': {'x': 10, 'y': 10}, 'to': {'x': 30, 'y': 10}, 'after_release': ESCAPE})

    def test_release_then_close_happens_in_the_native_release_frame(self):
        self.mock.dialog = 'phone'
        h = self.handle()
        r = self.call({'op': 'drag', 'from': {'target': h['target']}, 'to': {'x': 940, 'y': 500}, 'settle_frames': 0, 'after_release': ESCAPE})
        self.assertEqual(r['after_release']['frame'], r['native_release_frame'])
        self.assertTrue(r['after_release']['same_frame'])
        self.assertEqual(r['after_release']['result'], {'closed': 'phone'})
        self.assertEqual(r['completed_frame'], r['up_frame'])
        self.assertEqual(self.mock.dialog, 'closed')

    def test_down_and_up_on_different_calls_and_frames_and_busy_rules(self):
        down = self.call({'op': 'down', 'at': {'x': 100, 'y': 100}})
        self.assertTrue(down['active'])
        self.assertEqual(down['buttons'], [True, False, False])
        self.refused('mouse_busy', 'mouse', {'op': 'drag', 'from': {'x': 1, 'y': 1}, 'to': {'x': 2, 'y': 2}})
        self.refused('mouse_invalid', 'mouse', {'op': 'down', 'button': 0})
        moved = self.call({'op': 'move', 'to': {'x': 140, 'y': 100}, 'frames': 4})
        self.assertEqual(moved['buttons'][0], True)
        up = self.call({'op': 'up'})
        self.assertGreater(up['up_frame'], down['down_frame'] + 4)
        self.assertFalse(up['active'])
        self.refused('mouse_invalid', 'mouse', {'op': 'up'})

    def test_stale_covered_and_protected_targets_are_refused_before_input(self):
        h = self.handle()
        self.mock.mouse.relayout()
        self.refused('stale_ui_target', 'mouse', {'op': 'drag', 'from': {'target': h['target']}, 'to': {'x': 2, 'y': 2}})
        h = self.handle()
        self.mock.mouse.covered = True
        self.refused('stale_ui_target', 'mouse', {'op': 'drag', 'from': {'target': h['target']}, 'to': {'x': 2, 'y': 2}})
        self.mock.mouse.covered = False
        self.mock.pause_menu_open = True
        self.refused('mouse_protected', 'mouse', {'op': 'down', 'at': {'x': 5, 'y': 5}})
        self.mock.pause_menu_open = False
        self.assertIsNone(self.mock.mouse.interaction)
        self.refused('mouse_invalid', 'mouse', {'op': 'drag', 'from': {'x': 1920, 'y': 1}, 'to': {'x': 2, 'y': 2}})
        self.refused('mouse_invalid', 'ui', {'op': 'geometry', 'component': 'NoSuchThing'})

    def test_invalid_json_numbers_and_fields(self):
        for args in [{'op': 'drag', 'from': {'x': 1, 'y': 1}, 'to': {'x': 2, 'y': 2}, 'steps': True},
                     {'op': 'drag', 'from': {'x': 1, 'y': 1}, 'to': {'x': 2, 'y': 2}, 'duration_s': float('inf')},
                     {'op': 'move', 'to': {'x': 1, 'y': 1}, 'frames': 0},
                     {'op': 'scroll', 'at': {'x': 1, 'y': 1}, 'y': 101},
                     {'op': 'release', 'force': True},
                     {'op': 'hover'}]:
            with self.subTest(args=args):
                self.refused('mouse_invalid', 'mouse', args)

    def test_ft_save_required_but_release_always_works(self):
        self.call({'op': 'down', 'at': {'x': 100, 'y': 100}})
        self.mock.state['save'] = 'Peter city'
        with self.assertRaisesRegex(ValueError, "doesn't start with FT"):
            self.call({'op': 'move', 'to': {'x': 1, 'y': 1}})
        released = self.call({'op': 'release'})
        self.assertTrue(released['released'])
        self.assertFalse(released['active'])
        self.assertEqual(self.call({'op': 'release'})['released'], False)

    def test_scene_change_and_stop_cancel_and_clear_traces(self):
        r = self.call({'op': 'down', 'at': {'x': 100, 'y': 100}})
        self.mock.restore('FT mock')
        self.assertIsNone(self.mock.mouse.interaction)
        self.assertEqual(self.call({'op': 'status', 'trace_id': r['trace_id']})['end_reason'], 'the scene changed')
        self.mock.session = 'new-session'
        self.refused('mouse_unknown_trace', 'mouse', {'op': 'status', 'trace_id': r['trace_id']})
        self.call({'op': 'down', 'at': {'x': 100, 'y': 100}})
        self.mock.unload()
        self.assertIsNone(self.mock.mouse.interaction)


class ClientTests(unittest.TestCase):
    def test_mouse_client_sends_checked_args_with_outer_credentials(self):
        with patch.dict(os.environ, {'BA_LOCK_TOKEN': 'a' * 64}, clear=True):
            bridge = Bridge('http://127.0.0.1:18330')
            sent = []
            def request(path, body=None, *args, **kwargs):
                sent.append((path, body))
                if path == '/health': bridge.session = 's1'
                return {'ok': True, 'session': 's1', 'op': 7, 'result': {'active': False}}
            bridge._request = request
            mouse.Mouse(bridge).drag({'target': 'ui1.2'}, {'x': 940, 'y': 500}, settle_frames=0, escape_after_release=True)
            body = sent[-1][1]
            self.assertEqual(body['cmd'], 'mouse')
            self.assertEqual(body['lock_token'], 'a' * 64)
            self.assertEqual(body['expect_session'], 's1')
            self.assertNotIn('lock_token', body['args'])
            self.assertEqual(body['args']['after_release'], ESCAPE)
            self.assertNotIn('steps', body['args'])
            with self.assertRaises(mouse.MouseArgsError):
                mouse.Mouse(bridge).drag({'x': -1, 'y': 0}, {'x': 1, 'y': 1})
            self.assertEqual(len([s for s in sent if s[0] == '/call']), 1)

    def test_timed_out_gesture_is_recovered_by_operation_not_replayed(self):
        bridge = Bridge('http://127.0.0.1:18330')
        bridge.session = 's1'
        calls = []
        def request(path, body=None, *args, **kwargs):
            calls.append(body['cmd'])
            if body['cmd'] == 'mouse':
                raise BridgeError('timed out on the main thread; the command goes on', remote=True,
                                  reply={'ok': False, 'session': 's1', 'op': 9, 'error': 'timed out on the main thread; the command goes on'})
            return {'ok': True, 'session': 's1', 'result': {'id': 9, 'session': 's1', 'status': 'done', 'result': {'completed': True}}}
        bridge._request = request
        reply = mouse.Mouse(bridge).drag({'x': 1, 'y': 1}, {'x': 2, 'y': 1})
        self.assertEqual(calls, ['mouse', 'op'])
        self.assertTrue(reply['result']['completed'])

    def test_old_rig_gives_a_clear_capability_error(self):
        bridge = Bridge('http://127.0.0.1:18330')
        bridge.session = 's1'
        bridge._request = lambda path, body=None, *a, **k: (_ for _ in ()).throw(
            BridgeError("Unknown command 'mouse'. Commands: state, ...", remote=True, reply={'ok': False, 'session': 's1', 'op': 3}))
        with self.assertRaisesRegex(BridgeError, "Unknown command 'mouse'"):
            mouse.Mouse(bridge).status()

    def test_cli_points_and_commands(self):
        self.assertEqual(mouse.parse_point('900,500.5'), {'x': 900.0, 'y': 500.5})
        self.assertEqual(mouse.parse_point('ui3.4@1,0'), {'target': 'ui3.4', 'uv': {'x': 1.0, 'y': 0.0}})
        with self.assertRaises(mouse.MouseArgsError):
            mouse.parse_point('ui3.4@2,0')
        with self.assertRaises(mouse.MouseArgsError):
            mouse.parse_point('left')
        class Fake:
            def __getattr__(self, name):
                return lambda *a: (name, a)
        args = type('A', (), dict(mouse_op='drag', start='ui1.1', end='940,500', button=None, steps=4, duration=0.0,
                                  settle_frames=0, escape_after_release=True))
        self.assertEqual(cli.mouse_command(Fake(), args),
                         ('drag', ({'target': 'ui1.1'}, {'x': 940.0, 'y': 500.0}, None, 4, 0.0, 0, True)))


if __name__ == '__main__':
    unittest.main()

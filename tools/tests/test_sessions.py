import copy
from contextlib import redirect_stderr, redirect_stdout
import io
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch
import zlib

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from ftlib import cli
from ftlib.bridge import BridgeError
from ftlib.scenario import Runner
from offline_transport import OfflineSession


class SessionTests(unittest.TestCase):
    def setUp(self):
        self.transport = OfflineSession().__enter__()
        self.addCleanup(self.transport.__exit__, None, None, None)
        self.mock = self.transport.mock
        self.bridge = self.transport.bridge

    def restart_past(self, cursor):
        previous = self.mock.session
        self.mock.unload()
        self.mock.start_bridge()
        for n in range(cursor + 2):
            self.mock.event(f'after restart {n}')
        self.assertNotEqual(previous, self.mock.session)
        self.assertGreater(self.mock.events(0)['next'], cursor)

    def test_session_on_all_replies_and_operation_statuses(self):
        session = self.bridge.health()['session']
        self.assertTrue(session)
        self.assertEqual(self.bridge.session, session)
        self.assertEqual(self.bridge.events()['session'], session)
        reply = self.bridge.call('state')
        self.assertEqual(reply['session'], session)
        status = self.bridge.op(reply['op'])
        self.assertEqual(status['session'], session)
        self.assertEqual(status['result']['session'], session)
        self.assertEqual(status['result']['status'], 'done')
        self.bridge.reset_session()
        with self.assertRaises(BridgeError) as error:
            self.bridge.call('unknown')
        self.assertEqual(error.exception.reply['session'], session)
        self.assertEqual(self.bridge.session, session)
        status = self.bridge.op(error.exception.op)['result']
        self.assertEqual((status['session'], status['status']), (session, 'failed'))
        # Bad request bodies and endpoints also carry the current session.
        with self.assertRaises(BridgeError) as error:
            self.bridge._request('/call', {})
        self.assertEqual(error.exception.reply['session'], session)
        with self.assertRaises(BridgeError) as error:
            self.bridge._request('/missing')
        self.assertEqual(error.exception.reply['session'], session)

    def test_fast_restart_detected_from_every_endpoint_and_failure(self):
        for endpoint in ('health', 'events', 'call', 'error', 'recover'):
            with self.subTest(endpoint=endpoint):
                self.bridge.reset_session()
                original = self.bridge.call('state')
                cursor = self.bridge.events()['next']
                old_session = self.bridge.session
                self.restart_past(cursor)
                with self.assertRaises(BridgeError) as error:
                    if endpoint == 'health': self.bridge.health()
                    elif endpoint == 'events': self.bridge.events(cursor)
                    elif endpoint == 'call': self.bridge.call('state')
                    elif endpoint == 'error': self.bridge.call('unknown')
                    else: self.bridge.recover(original['op'])
                self.assertTrue(error.exception.restart)
                self.assertIn('events and remembered values from before are void', str(error.exception))
                self.assertEqual(self.bridge.session, old_session)

    def test_operation_status_must_match_outer_session(self):
        reply = self.bridge.call('state')
        self.mock.operations[reply['op']]['session'] = 'another bridge'
        with self.assertRaises(BridgeError) as error:
            self.bridge.op(reply['op'])
        self.assertTrue(error.exception.restart)

    def test_timeout_recovery_never_crosses_restart_or_retries_mutation(self):
        self.mock.operation_delays['debug'] = 10
        original = self.bridge.op
        def restarted_status(identifier):
            self.restart_past(self.mock.events(0)['next'])
            return original(identifier)
        with patch.object(self.bridge, 'op', side_effect=restarted_status), patch.object(self.mock, 'call', wraps=self.mock.call) as calls:
            with self.assertRaises(BridgeError) as error:
                self.bridge.call('debug', {'action': 'spawn truck'}, timeout_s=1)
            self.assertTrue(error.exception.restart)
            self.assertEqual(sum(c.args[0] == 'debug' for c in calls.call_args_list), 1)
            self.assertEqual(len(self.mock.state['vehicles']), 1)

    def test_scenario_invalidates_values_and_stops_even_soft_keep_going(self):
        original = self.mock.call
        runner = Runner(self.bridge)
        def restarting_call(cmd, args):
            if cmd == 'console':
                self.restart_past(runner.cursor)
                return {'output': ['restarted']}
            return original(cmd, args)
        document = {'name': 'restart-past-cursor', 'checks': 'unit', 'steps': [
            {'remember': 'money', 'from': 'state', 'path': 'money'},
            {'call': 'console', 'args': {'line': 'restart'}, 'soft': True, 'expect_ok': False},
            {'call': 'money', 'args': {'set': 1}}]}
        with patch.object(self.mock, 'call', side_effect=restarting_call), tempfile.TemporaryDirectory() as folder:
            report, _ = runner.run(document, folder, keep_going=True)
        self.assertFalse(report['ok'])
        self.assertEqual(len(report['steps']), 2)
        self.assertIn('events and remembered values from before are void', ' '.join(report['summary']))
        self.assertEqual(runner.variables, {})
        self.assertIsNone(runner.last)
        self.assertNotEqual(report['session'], self.mock.session)
        self.assertEqual(report['events'], 0)
        self.assertNotEqual(self.mock.state['money'], 1)
        # An explicit new run discards old context and accepts the new session.
        with tempfile.TemporaryDirectory() as folder:
            report, _ = runner.run({'name': 'fresh', 'checks': 'unit', 'steps': [{'note': 'fresh run'}]}, folder)
        self.assertTrue(report['ok'], report['summary'])
        self.assertEqual(report['session'], self.mock.session)

    def test_scenario_restart_between_health_and_initial_events(self):
        original = self.bridge.health
        def health_then_restart():
            reply = original()
            self.restart_past(self.mock.events(0)['next'])
            return reply
        with patch.object(self.bridge, 'health', side_effect=health_then_restart), tempfile.TemporaryDirectory() as folder:
            report, _ = Runner(self.bridge).run({'name': 'startup-restart', 'checks': 'unit', 'steps': [{'call': 'money', 'args': {'set': 1}}]}, folder)
        self.assertFalse(report['ok'])
        self.assertIn('Bridge restarted', report['summary'][0])
        self.assertNotEqual(self.mock.state['money'], 1)

    def test_follow_announces_fast_restart_and_reads_from_one(self):
        old_session = self.mock.session
        old_cursor = self.mock.events(0)['next']
        sleeps = []
        def tick(seconds):
            sleeps.append(seconds)
            if len(sleeps) == 1: self.restart_past(old_cursor)
            else: raise KeyboardInterrupt
        output, errors = io.StringIO(), io.StringIO()
        with patch.object(cli, 'Bridge', return_value=self.bridge), patch.object(sys, 'argv', ['ft', 'events', '--follow', '--json']), patch.object(cli.time, 'sleep', side_effect=tick), redirect_stdout(output), redirect_stderr(errors):
            self.assertEqual(cli.main(), 0)
        batches = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual(len(batches), 2)
        self.assertEqual(batches[0]['session'], old_session)
        self.assertEqual(batches[1]['session'], self.mock.session)
        self.assertEqual([e['seq'] for e in batches[1]['events']], list(range(1, old_cursor + 3)))
        self.assertIn('Bridge restarted', errors.getvalue())
        self.assertIn('following the new session from event 1', errors.getvalue())
        self.assertEqual(self.bridge.session, self.mock.session)

    def test_serialization_failure_is_not_hidden_by_successful_compression(self):
        saved = self.bridge.call('save', {'name': 'FT partial'})['result']
        snapshot = copy.deepcopy(self.mock.saves['FT partial'])
        self.mock.save_failure = 'serialization_then_compression'
        self.mock.state['money'] = 7
        with self.assertRaisesRegex(BridgeError, 'failed while writing the city') as error:
            self.bridge.call('save', {'name': 'FT partial'})
        path = Path(saved['path'])
        self.assertGreater(path.stat().st_size, 0)
        self.assertEqual(zlib.decompress(path.read_bytes()), b'{"incomplete":')
        self.assertEqual(self.mock.saves['FT partial'], snapshot)
        self.assertFalse(self.mock.save_in_progress)
        self.assertEqual(self.bridge.op(error.exception.op)['result']['status'], 'failed')
        self.assertTrue(any(e['text'].startswith('SaveGame serialization failed:') for e in self.mock.events(0)['events']))

    def test_admitted_before_stop_never_executes_after_restart(self):
        old_session = self.mock.session
        original_start = threading.Thread.start
        workers = []
        def stop_before_worker_start(thread):
            workers.append(thread)
            self.mock.unload()
            self.mock.start_bridge()
            return original_start(thread)
        # Admission has created the pending op, but its worker is not enqueued yet.
        with patch.object(threading.Thread, 'start', new=stop_before_worker_start), patch.object(self.mock, 'call', wraps=self.mock.call) as calls:
            reply = self.mock.operation('debug', {'action': 'spawn truck'})
            for thread in workers: thread.join(1)
            self.assertEqual(calls.call_count, 0)
        self.assertFalse(reply['ok'])
        self.assertEqual(reply['error'], 'the bridge stopped')
        self.assertEqual(reply['session'], old_session)
        self.assertNotEqual(old_session, self.mock.session)
        self.assertEqual(self.mock.state['vehicles'], [])
        self.assertEqual(self.mock.operation_waiters, {})
        self.assertEqual(self.bridge.call('state')['session'], self.mock.session)


if __name__ == '__main__':
    unittest.main()

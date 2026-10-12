from contextlib import redirect_stderr
import io
import json
import os
from pathlib import Path
import selectors
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from ftlib.bridge import BridgeError
from ftlib.mcp import respond
from offline_transport import OfflineSession


# The control file restarts only the test double, never a real game.
MCP_BOOT = '''
import json, runpy, sys
from pathlib import Path
sys.path.insert(0, sys.argv[1])
sys.path.insert(0, sys.argv[1] + '/tests')
from offline_transport import OfflineSession
import ftlib.bridge
with OfflineSession() as transport:
    trigger, audit = Path(sys.argv[2]), Path(sys.argv[3])
    original_open = transport.bridge.http.open
    def open_request(request, timeout=None):
        with audit.open('a') as log:
            log.write(json.dumps({'url':request.full_url,'body':json.loads(request.data) if request.data else None}) + '\\n')
        if trigger.exists():
            trigger.unlink()
            cursor = transport.mock.events(0)['next']
            transport.mock.unload()
            transport.mock.start_bridge()
            for n in range(cursor + 2): transport.mock.event('new session event ' + str(n))
        return original_open(request, timeout)
    transport.bridge.http.open = open_request
    ftlib.bridge.Bridge = lambda: transport.bridge
    entry = sys.argv[1] + '/ft-mcp'
    sys.argv = [entry]
    runpy.run_path(entry, run_name='__main__')
'''


class ReconnectTests(unittest.TestCase):
    def setUp(self):
        self.transport = OfflineSession().__enter__()
        self.addCleanup(self.transport.__exit__, None, None, None)
        self.bridge, self.mock = self.transport.bridge, self.transport.mock

    def restart(self):
        self.mock.unload()
        self.mock.start_bridge()

    def test_pinned_session_sent_as_top_level_field_on_every_call(self):
        with patch.object(self.bridge.http, 'open', wraps=self.bridge.http.open) as requests:
            first = self.bridge.call('state')
            self.assertNotIn('expect_session', json.loads(requests.call_args.args[0].data))
            for cmd, args in [('money', {'set':123}), ('op', {'id':first['op']}), ('ui', {'op':'list'})]:
                self.bridge.call(cmd, args)
                body = json.loads(requests.call_args.args[0].data)
                self.assertEqual(body['expect_session'], first['session'])
                self.assertNotIn('expect_session', body['args'])
            self.bridge.reset_session()
            pinned = self.bridge.health()['session']
            self.bridge.call('state')
            self.assertEqual(json.loads(requests.call_args.args[0].data)['expect_session'], pinned)

    def test_mock_mismatch_refuses_before_admission_or_execution(self):
        old = self.mock.session
        self.restart()
        next_op = self.mock.next_op
        with patch.object(self.mock, 'call', wraps=self.mock.call) as calls:
            for cmd, args in [('debug', {'action':'spawn truck'}), ('money', {'set':1}), ('op', {'id':1}), ('quit', {})]:
                reply = self.mock.operation(cmd, args, old)
                self.assertEqual(reply, {'ok':False, 'session':self.mock.session,
                                        'error':f'the bridge restarted (session {self.mock.session}); reconnect first.'})
            self.assertEqual(calls.call_count, 0)
        self.assertEqual(self.mock.next_op, next_op)
        self.assertEqual(self.mock.operations, {})
        self.assertEqual(self.mock.state['vehicles'], [])
        self.assertTrue(self.mock.operation('state', {}, self.mock.session)['ok'])

    def test_discovered_restart_blocks_calls_locally_and_health_stays_available(self):
        first = self.bridge.call('state')
        cursor = self.bridge.events()['next']
        self.restart()
        with patch.object(self.mock, 'call', wraps=self.mock.call) as calls:
            with self.assertRaises(BridgeError) as error:
                self.bridge.call('debug', {'action':'spawn truck'})
            self.assertTrue(error.exception.restart)
            self.assertEqual(error.exception.reply['error'], f'the bridge restarted (session {self.mock.session}); reconnect first.')
            self.assertEqual(calls.call_count, 0)
        with patch.object(self.bridge.http, 'open', wraps=self.bridge.http.open) as requests:
            for cmd in ('money', 'debug', 'state', 'op', 'save', 'quit'):
                with self.assertRaises(BridgeError) as error: self.bridge.call(cmd, {'id':first['op']})
                self.assertTrue(error.exception.restart)
            self.assertEqual(requests.call_count, 0)
        for _ in range(2):
            health = self.bridge.health()
            self.assertTrue(health['ok'])
            self.assertTrue(health['restarted'])
            self.assertTrue(health['reconnect_required'])
            self.assertEqual(health['session'], self.mock.session)
            self.assertEqual(health['previous_session'], first['session'])
            self.assertEqual(self.bridge.session, first['session'])
        fresh = self.bridge.reconnect()
        self.assertFalse(fresh['reconnect_required'])
        self.assertEqual(fresh['session'], self.mock.session)
        self.assertEqual(fresh['events_since'], 1)
        self.assertEqual(self.bridge.operation_ids, set())
        self.assertEqual(self.bridge.event_cursors, set())
        with patch.object(self.bridge.http, 'open', wraps=self.bridge.http.open) as requests:
            with self.assertRaisesRegex(BridgeError, 'from before reconnect'): self.bridge.op(first['op'])
            with self.assertRaisesRegex(BridgeError, 'from before reconnect'): self.bridge.events(cursor)
            self.assertEqual(requests.call_count, 0)
        self.assertTrue(self.bridge.call('debug', {'action':'spawn truck'})['ok'])
        self.assertEqual(len(self.mock.state['vehicles']), 1)
        self.assertTrue(self.bridge.events(1)['ok'])

    def test_health_can_discover_restart_without_implicitly_reconnecting(self):
        self.bridge.health()
        self.restart()
        with redirect_stderr(io.StringIO()):
            result = respond({'jsonrpc':'2.0', 'id':1, 'method':'tools/call', 'params':{'name':'ft_health'}}, self.bridge)['result']
        self.assertFalse(result['isError'])
        self.assertTrue(result['structuredContent']['reconnect_required'])
        with patch.object(self.bridge.http, 'open') as requests:
            with self.assertRaises(BridgeError): self.bridge.call('money', {'set':1})
            requests.assert_not_called()

    def test_failed_reconnect_keeps_calls_blocked_and_old_pin(self):
        old = self.bridge.health()['session']
        self.restart()
        self.bridge.health()
        with patch.object(self.bridge.http, 'open', side_effect=urllib.error.URLError('offline')):
            with self.assertRaises(BridgeError): self.bridge.reconnect()
        self.assertEqual(self.bridge.session, old)
        self.assertTrue(self.bridge.reconnect_required)
        with patch.object(self.bridge.http, 'open') as requests:
            with self.assertRaises(BridgeError): self.bridge.call('debug', {'action':'spawn truck'})
            requests.assert_not_called()


class PersistentMCPTests(unittest.TestCase):
    def test_same_stdio_server_refuses_restart_then_reconnects(self):
        with tempfile.TemporaryDirectory() as folder:
            trigger, audit = Path(folder)/'restart', Path(folder)/'requests.jsonl'
            env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1')
            env.pop('BA_GAME_HOST', None)
            process = subprocess.Popen([sys.executable, '-c', MCP_BOOT, str(TOOLS), str(trigger), str(audit)],
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, env=env)
            identifier = 0
            def request(method, params=None):
                nonlocal identifier
                identifier += 1
                process.stdin.write(json.dumps({'jsonrpc':'2.0', 'id':identifier, 'method':method, 'params':params or {}})+'\n')
                process.stdin.flush()
                with selectors.DefaultSelector() as selector:
                    selector.register(process.stdout, selectors.EVENT_READ)
                    self.assertTrue(selector.select(10), 'MCP did not reply')
                line = process.stdout.readline()
                self.assertTrue(line, 'MCP exited before replying')
                reply = json.loads(line)
                self.assertEqual(reply['id'], identifier)
                return reply['result']
            def tool(name, args=None): return request('tools/call', {'name':name, 'arguments':args or {}})
            def requests(): return [json.loads(line) for line in audit.read_text().splitlines()]
            try:
                request('initialize')
                names = {t['name'] for t in request('tools/list')['tools']}
                self.assertIn('ft_reconnect', names)
                first = tool('ft_call', {'cmd':'money', 'args':{'set':1234}})
                self.assertFalse(first['isError'])
                old = first['structuredContent']
                cursor = tool('ft_events')['structuredContent']['next']
                trigger.write_text('restart the test double')
                refused = tool('ft_call', {'cmd':'debug', 'args':{'action':'spawn truck'}})
                self.assertTrue(refused['isError'])
                refusal = refused['structuredContent']
                new_session = refusal['session']
                self.assertNotEqual(old['session'], new_session)
                self.assertEqual(refusal['error'], f'the bridge restarted (session {new_session}); reconnect first.')
                self.assertNotIn('op', refusal)
                self.assertEqual(requests()[-1]['body']['expect_session'], old['session'])
                count = len(requests())
                self.assertTrue(tool('ft_call', {'cmd':'money', 'args':{'set':1}})['isError'])
                self.assertEqual(len(requests()), count, 'blocked mutation reached the bridge')
                for _ in range(2):
                    health = tool('ft_health')
                    self.assertFalse(health['isError'])
                    self.assertTrue(health['structuredContent']['reconnect_required'])
                    self.assertEqual(health['structuredContent']['session'], new_session)
                fresh = tool('ft_reconnect')
                self.assertFalse(fresh['isError'])
                self.assertEqual(fresh['structuredContent']['session'], new_session)
                self.assertFalse(fresh['structuredContent']['reconnect_required'])
                count = len(requests())
                self.assertTrue(tool('ft_op', {'id':old['op']})['isError'])
                self.assertTrue(tool('ft_events', {'since':cursor})['isError'])
                self.assertEqual(len(requests()), count, 'old context reached the bridge')
                state = tool('ft_state')['structuredContent']
                self.assertEqual(state['money'], 1234)
                self.assertEqual(state['vehicles'], [], 'refused mutation actually ran')
                success = tool('ft_call', {'cmd':'debug', 'args':{'action':'spawn truck'}})
                self.assertFalse(success['isError'])
                self.assertEqual(success['structuredContent']['session'], new_session)
                self.assertEqual(requests()[-1]['body']['expect_session'], new_session)
                self.assertEqual(len(tool('ft_state')['structuredContent']['vehicles']), 1)
                events = tool('ft_events', {'since':1})
                self.assertFalse(events['isError'])
                self.assertEqual(events['structuredContent']['events'][0]['seq'], 1)
                self.assertGreater(events['structuredContent']['next'], cursor)
            finally:
                process.stdin.close()
                try: process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=10)
                process.stdout.close()
                process.stderr.close()


if __name__ == '__main__':
    unittest.main()

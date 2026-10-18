import json
import math
import os
import re
import time
import urllib.error
import urllib.request

from . import locks
from .paths import lookup, meets, PathError

RESTART_MESSAGE = 'Bridge restarted: events and remembered values from before are void; use ft_reconnect in MCP or start a fresh scenario run'


class BridgeError(RuntimeError):
    def __init__(self, message, remote=False, reply=None, restart=False):
        super().__init__(message)
        self.remote = remote
        self.reply = reply
        self.op = (reply or {}).get('op')
        self.restart = restart


def check_session(expected, reply):
    session = reply.get('session')
    if not isinstance(session, str) or not session:
        raise BridgeError('Bridge reply has no valid session: update the bridge before running checks')
    if expected is not None and session != expected:
        raise BridgeError(RESTART_MESSAGE, reply=reply, restart=True)
    return session


class Bridge:
    def __init__(self, url=None):
        self.url = (url or os.environ.get('BA_BRIDGE_URL', 'http://127.0.0.1:8330')).rstrip('/')
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        self.lock_owner = os.environ.get('BA_LOCK_OWNER')
        self.session = None
        self.reconnect_required = False
        self.operation_ids = set()
        self.event_cursors = set()
        self.stale_operations = set()
        self.stale_cursors = set()

    def discard_context(self):
        self.stale_operations.update(self.operation_ids)
        self.stale_cursors.update(self.event_cursors)
        self.operation_ids.clear()
        self.event_cursors.clear()

    def reset_session(self):
        self.discard_context()
        self.session = None
        self.reconnect_required = False

    def observe(self, reply, allow_restart=False):
        try:
            self.session = check_session(self.session, reply)
        except BridgeError as exc:
            if not exc.restart: raise
            self.reconnect_required = True
            self.discard_context()
            if not allow_restart: raise

    def reconnect(self):
        reply = self._request('/health', allow_restart=True)
        self.discard_context()
        self.session = check_session(None, reply)
        self.reconnect_required = False
        return {**reply, 'reconnected':True, 'reconnect_required':False, 'events_since':1}

    def _request(self, path, body=None, timeout_s=10, allow_restart=False):
        if self.reconnect_required and path != '/health':
            raise BridgeError(RESTART_MESSAGE, restart=True)
        request = urllib.request.Request(self.url + path,
                                         data=None if body is None else json.dumps(body, allow_nan=False).encode(),
                                         headers={'Content-Type': 'application/json'})
        try:
            with self.http.open(request, timeout=timeout_s) as response:
                reply = json.load(response)
        except urllib.error.HTTPError as exc:
            try:
                with exc:
                    reply = json.load(exc)
            except (ValueError, OSError):
                raise BridgeError(f'Bridge HTTP {exc.code}: inspect the game log and tools/ft health') from exc
        except (urllib.error.URLError, OSError, TimeoutError) as exc:
            if body is not None:
                raise BridgeError(f'Lost call reply at {self.url}: its outcome is unknown; check the game log before repeating a changing command (no operation id received)') from exc
            hint = ('start the local Mac fallback with tools/agent-tests/mac-agent start in the Driven Rides checkout; no tunnel is needed'
                    if locks.instance_for_url(self.url) == 'agentmac'
                    else 'start the game with tools/run-remote.sh start, then tools/run-remote.sh tunnel')
            raise BridgeError(f'No bridge at {self.url}: {hint}') from exc
        except ValueError as exc:
            raise BridgeError(f'Invalid JSON from {self.url}: check the tunnel points to the test bridge') from exc
        if not isinstance(reply, dict) or not isinstance(reply.get('ok'), bool):
            raise BridgeError('Invalid bridge reply: check the bridge version with tools/ft health')
        self.observe(reply, allow_restart)
        if path == '/call' and not self.reconnect_required and reply.get('op') is not None:
            self.operation_ids.add(reply['op'])
        if not reply['ok']:
            raise BridgeError(reply.get('error', 'Bridge call failed: inspect the game log'), remote=True, reply=reply)
        return reply

    def health(self):
        reply = self._request('/health', allow_restart=True)
        if self.reconnect_required:
            return {**reply, 'restarted':True, 'reconnect_required':True,
                    'previous_session':self.session, 'message':RESTART_MESSAGE}
        return reply

    def op(self, identifier):
        return self.call('op', {'id': identifier})

    def recover(self, identifier, timeout_s=30):
        deadline = time.monotonic() + timeout_s
        while True:
            try:
                status = self.op(identifier)['result']
            except BridgeError as exc:
                if exc.restart: raise
                if exc.remote and str(exc) == 'the bridge stopped':
                    raise BridgeError(str(exc), remote=True, reply={**(exc.reply or {'ok':False,'error':str(exc)}), 'op':identifier}) from exc
                raise BridgeError(f'Cannot recover operation {identifier}: {exc}; query tools/ft op {identifier} before repeating the command', reply={'ok':False,'session':self.session,'op':identifier,'error':str(exc)}) from exc
            if status['status'] == 'done':
                return {'ok':True, 'session':self.session, 'op':identifier, 'result':status['result']}
            if status['status'] == 'failed':
                reply = {'ok':False, 'session':self.session, 'op':identifier, 'error':status['error']}
                raise BridgeError(status['error'], remote=True, reply=reply)
            if status['status'] != 'pending':
                raise BridgeError(f'Unknown status for operation {identifier}: inspect tools/ft op {identifier}')
            if time.monotonic() >= deadline:
                reply = {'ok':False,'session':self.session,'op':identifier,'error':f'Operation {identifier} is still pending: use tools/ft op {identifier}; do not repeat the command'}
                raise BridgeError(reply['error'], reply=reply)
            time.sleep(min(.1, max(0, deadline-time.monotonic())))

    def call(self, cmd, args=None, timeout_s=10, recovery_s=30):
        if self.reconnect_required:
            raise BridgeError(RESTART_MESSAGE, restart=True)
        identifier = (args or {}).get('id')
        if cmd == 'op' and isinstance(identifier, (int,float)) and identifier in self.stale_operations and identifier not in self.operation_ids:
            raise BridgeError('Operation id is from before reconnect: discard it and use an id from the current session')
        arguments = dict(args or {})
        arguments.setdefault('timeout_s', timeout_s)
        limit = arguments['timeout_s']
        if isinstance(limit, bool) or not isinstance(limit, (int, float)) or (isinstance(limit, float) and not math.isfinite(limit)):
            raise BridgeError('Invalid timeout_s: use a finite number of seconds (clamped to 1..600)')
        limit = min(600, max(1, limit))
        arguments['timeout_s'] = limit
        instance = locks.instance_for_url(self.url)
        owner = arguments.get('owner') or self.lock_owner
        if cmd == 'lock' and arguments.get('op') == 'acquire':
            arguments['owner'] = owner
            if owner: self.lock_owner = owner
        credential = locks.token(instance, create=cmd == 'lock' and arguments.get('op') == 'acquire', owner=owner)
        # Old mock/read-only callers retain their transport behavior. A real owner pins health first.
        if credential and self.session is None: self.health()
        body = {'cmd':cmd, 'args':arguments}
        if credential: body['lock_token'] = credential
        if self.session is not None: body['expect_session'] = self.session
        try:
            reply = self._request('/call', body, limit + 2)
        except BridgeError as exc:
            if cmd != 'op' and exc.remote and exc.op is not None and str(exc).startswith('timed out on the main thread;'):
                reply = self.recover(exc.op, recovery_s)
            else:
                raise
        if not isinstance(reply.get('result'), dict):
            raise BridgeError('Invalid command result: inspect the game log and bridge version')
        if cmd == 'op':
            self.observe(reply['result'])
            self.operation_ids.add(reply['result']['id'])
        return reply

    def events(self, since=0):
        if self.reconnect_required:
            raise BridgeError(RESTART_MESSAGE, restart=True)
        if isinstance(since, bool) or not isinstance(since, int) or since < 0:
            raise BridgeError('Invalid event cursor: use a nonnegative sequence number from events.next')
        if since > 1 and since in self.stale_cursors and since not in self.event_cursors:
            raise BridgeError('Event cursor is from before reconnect: discard it and read events from since=1')
        reply = self._request(f'/events?since={since}')
        if reply['next'] < since:
            self.reconnect_required = True
            self.discard_context()
            raise BridgeError(f"Bridge restarted: events.next {reply['next']} is below cursor {since}; use a fresh cursor and rerun the scenario", restart=True)
        self.event_cursors.add(reply['next'])
        return reply

    def wait_event(self, regex, timeout_s=30, since=0):
        pattern = re.compile(regex)
        deadline = time.monotonic() + timeout_s
        while True:
            batch = self.events(since)
            if batch['dropped']:
                raise BridgeError(f"Lost {batch['dropped']} events: rerun the check with a fresh event cursor")
            for event in batch['events']:
                if pattern.search(event['text']):
                    return event
            since = batch['next']
            if time.monotonic() >= deadline:
                raise BridgeError(f'No event matching {regex!r} within {timeout_s:g}s: inspect tools/ft events')
            time.sleep(min(.1, max(0, deadline - time.monotonic())))

    def wait_state(self, path, condition, timeout_s=30, poll_s=.1):
        deadline = time.monotonic() + timeout_s
        while True:
            state = self.call('state')['result']
            try:
                actual = lookup(state, path)
                present = True
            except PathError as exc:
                actual = str(exc)
                present = False
            if present and meets(actual, condition):
                return actual
            if time.monotonic() >= deadline:
                raise BridgeError(f'State {path} stayed {actual!r}: expected {condition!r}; inspect tools/ft state')
            time.sleep(min(poll_s, max(0, deadline - time.monotonic())))


_default = None


def client():
    global _default
    if _default is None:
        _default = Bridge()
    return _default


def health():
    return client().health()


def reconnect():
    return client().reconnect()


def call(cmd, args=None, timeout_s=10):
    return client().call(cmd, args, timeout_s)


def events(since=0):
    return client().events(since)


def op(identifier):
    return client().op(identifier)


def wait_event(regex, timeout_s=30, since=0):
    return client().wait_event(regex, timeout_s, since)


def wait_state(path, condition, timeout_s=30, poll_s=.1):
    return client().wait_state(path, condition, timeout_s, poll_s)

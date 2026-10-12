import json
import math
import os
import re
import time
import urllib.error
import urllib.request

from .paths import lookup, meets, PathError


class BridgeError(RuntimeError):
    def __init__(self, message, remote=False, reply=None):
        super().__init__(message)
        self.remote = remote
        self.reply = reply
        self.op = (reply or {}).get('op')


class Bridge:
    def __init__(self, url=None):
        self.url = (url or os.environ.get('BA_BRIDGE_URL', 'http://127.0.0.1:8322')).rstrip('/')
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def _request(self, path, body=None, timeout_s=10):
        request = urllib.request.Request(self.url + path,
                                         data=None if body is None else json.dumps(body, allow_nan=False).encode(),
                                         headers={'Content-Type': 'application/json'})
        try:
            with self.http.open(request, timeout=timeout_s) as response:
                reply = json.load(response)
        except urllib.error.HTTPError as exc:
            try:
                reply = json.load(exc)
            except (ValueError, OSError):
                raise BridgeError(f'Bridge HTTP {exc.code}: inspect the game log and tools/ft health') from exc
        except (urllib.error.URLError, OSError, TimeoutError) as exc:
            if body is not None:
                raise BridgeError(f'Lost call reply at {self.url}: its outcome is unknown; check the game log before repeating a changing command (no operation id received)') from exc
            raise BridgeError(f'No bridge at {self.url}: start the game with tools/run-remote.sh start, then tools/run-remote.sh tunnel') from exc
        except ValueError as exc:
            raise BridgeError(f'Invalid JSON from {self.url}: check the tunnel points to the test bridge') from exc
        if not isinstance(reply, dict) or not isinstance(reply.get('ok'), bool):
            raise BridgeError('Invalid bridge reply: check the bridge version with tools/ft health')
        if not reply['ok']:
            raise BridgeError(reply.get('error', 'Bridge call failed: inspect the game log'), remote=True, reply=reply)
        return reply

    def health(self):
        return self._request('/health')

    def op(self, identifier):
        return self.call('op', {'id': identifier})

    def recover(self, identifier, timeout_s=30):
        deadline = time.monotonic() + timeout_s
        while True:
            try:
                status = self.op(identifier)['result']
            except BridgeError as exc:
                raise BridgeError(f'Cannot recover operation {identifier}: {exc}; query tools/ft op {identifier} before repeating the command', reply={'ok':False,'op':identifier,'error':str(exc)}) from exc
            if status['status'] == 'done':
                return {'ok':True, 'op':identifier, 'result':status['result']}
            if status['status'] == 'failed':
                reply = {'ok':False, 'op':identifier, 'error':status['error']}
                raise BridgeError(status['error'], remote=True, reply=reply)
            if status['status'] != 'pending':
                raise BridgeError(f'Unknown status for operation {identifier}: inspect tools/ft op {identifier}')
            if time.monotonic() >= deadline:
                reply = {'ok':False,'op':identifier,'error':f'Operation {identifier} is still pending: use tools/ft op {identifier}; do not repeat the command'}
                raise BridgeError(reply['error'], reply=reply)
            time.sleep(min(.1, max(0, deadline-time.monotonic())))

    def call(self, cmd, args=None, timeout_s=10, recovery_s=30):
        arguments = dict(args or {})
        arguments.setdefault('timeout_s', timeout_s)
        limit = arguments['timeout_s']
        if isinstance(limit, bool) or not isinstance(limit, (int, float)) or (isinstance(limit, float) and not math.isfinite(limit)):
            raise BridgeError('Invalid timeout_s: use a finite number of seconds (clamped to 1..600)')
        limit = min(600, max(1, limit))
        arguments['timeout_s'] = limit
        try:
            reply = self._request('/call', {'cmd': cmd, 'args': arguments}, limit + 2)
        except BridgeError as exc:
            if cmd != 'op' and exc.remote and exc.op is not None and str(exc).startswith('timed out on the main thread;'):
                reply = self.recover(exc.op, recovery_s)
            else:
                raise
        if not isinstance(reply.get('result'), dict):
            raise BridgeError('Invalid command result: inspect the game log and bridge version')
        return reply

    def events(self, since=0):
        if isinstance(since, bool) or not isinstance(since, int) or since < 0:
            raise BridgeError('Invalid event cursor: use a nonnegative sequence number from events.next')
        reply = self._request(f'/events?since={since}')
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

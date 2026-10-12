"""Exercise HTTP handlers without sockets when FT_TEST_TRANSPORT=inprocess is set."""
import io
import json
from pathlib import Path
import runpy
from types import SimpleNamespace
import tempfile
import threading
from urllib.parse import urlsplit
import urllib.error

from ftlib.bridge import Bridge

NAMESPACE = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'mock-bridge'), run_name='mock_test')


class Stream(io.BytesIO):
    def close(self):
        pass


class Connection:
    def __init__(self, request):
        self.input = Stream(request)
        self.output = Stream()

    def makefile(self, mode, *args):
        return self.input if 'r' in mode else self.output

    def sendall(self, data):
        self.output.write(data)


class MemoryHTTP:
    def __init__(self, mock):
        self.server = SimpleNamespace(mock=mock, shutdown=mock.stop.set)

    def open(self, request, timeout=None):
        if self.server.mock.stop.is_set():
            raise urllib.error.URLError('Mock stopped')
        url = urlsplit(request.full_url)
        assert url.hostname == '127.0.0.1'
        body = request.data or b''
        method = 'POST' if request.data is not None else 'GET'
        path = url.path + ('?' + url.query if url.query else '')
        wire = f'{method} {path} HTTP/1.0\r\nHost: 127.0.0.1\r\nContent-Length: {len(body)}\r\n\r\n'.encode() + body
        connection = Connection(wire)
        NAMESPACE['Handler'](connection, ('127.0.0.1', 1), self.server)
        reply = connection.output.getvalue()
        headers, payload = reply.split(b'\r\n\r\n', 1)
        status=int(headers.split(b'\r\n',1)[0].split()[1])
        if status!=200: raise urllib.error.HTTPError(request.full_url,status,'Mock HTTP error',{},io.BytesIO(payload))
        return io.BytesIO(payload)


class OfflineSession:
    def __enter__(self):
        self.folder = tempfile.TemporaryDirectory(prefix='ft-offline-')
        self.mock = NAMESPACE['Mock'](self.folder.name, clock_rate=100)
        self.url = 'http://127.0.0.1:1'
        self.bridge = Bridge(self.url)
        self.bridge.transport = 'inprocess test double'
        self.bridge.http = MemoryHTTP(self.mock)
        self.thread = threading.Thread(target=self.mock.run_clock, daemon=True)
        self.thread.start()
        return self

    def __exit__(self, *args):
        self.mock.unload()
        self.mock.stop.set()
        self.thread.join(timeout=10)
        self.folder.cleanup()


# Subprocesses still exchange MCP messages through real stdio pipes.
BOOT = '''
import os, runpy, sys
sys.path.insert(0, sys.argv[1])
sys.path.insert(0, sys.argv[1] + '/tests')
from offline_transport import OfflineSession
import ftlib.bridge
with OfflineSession() as session:
    ftlib.bridge.Bridge = lambda *args, **kwargs: session.bridge
    entry = sys.argv[2]
    sys.argv = sys.argv[2:]
    runpy.run_path(entry, run_name='__main__')
'''

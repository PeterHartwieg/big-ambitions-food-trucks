import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from ftlib import locks
from ftlib.bridge import Bridge

class LockClientTests(unittest.TestCase):
    def test_explicit_owner_and_private_per_instance_tokens(self):
        with tempfile.TemporaryDirectory() as root, patch.dict(os.environ, {'BA_LOCK_TOKEN_DIR':root}, clear=True):
            self.assertIsNone(locks.token('agent1'))
            with self.assertRaises(RuntimeError): locks.token('agent1',create=True)
            a=locks.token('agent1',create=True,owner='a')
            self.assertEqual(a,locks.token('agent1',owner='a'))
            self.assertNotEqual(a,locks.token('normal',create=True,owner='a'))
            self.assertNotEqual(a,locks.token('agent1',create=True,owner='b'))
            for path in Path(root).glob('*.json'): self.assertEqual(path.stat().st_mode & 0o777,0o600)
    def test_client_attaches_token_and_pins_before_mutating(self):
        with patch.dict(os.environ,{'BA_LOCK_TOKEN':'a'*64},clear=True):
            b=Bridge('http://127.0.0.1:18330'); requests=[]
            def request(path,body=None,*args,**kw):
                requests.append((path,body))
                if path=='/health': b.session='first'
                return {'ok':True,'session':'first','result':{}}
            b._request=request
            b.call('time',{'pause':True})
            self.assertEqual(requests[0][0],'/health')
            self.assertEqual(requests[1][1]['lock_token'],'a'*64)
            self.assertEqual(requests[1][1]['expect_session'],'first')
    def test_token_is_sent_over_stdin_not_process_arguments(self):
        with patch('ftlib.locks.subprocess.run') as run:
            run.return_value.returncode=0;run.return_value.stdout='{}'
            locks.remote('fixed script',{'token':'secret-capability'})
            self.assertNotIn('secret-capability',str(run.call_args.args))
            self.assertIn('secret-capability',run.call_args.kwargs['input'])
    def test_endpoint_mapping(self):
        self.assertEqual(locks.instance_for_url('http://127.0.0.1:8330'),'normal')
        self.assertEqual(locks.instance_for_url('http://127.0.0.1:18330'),'agent1')

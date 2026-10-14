import json
from pathlib import Path
import sys
import unittest
import urllib.error
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from offline_transport import OfflineSession


class BrowserMutationGuardTests(unittest.TestCase):
    def setUp(self):
        self.session = OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__, None, None, None)

    def request(self, headers):
        return urllib.request.Request(self.session.url + '/call',
            data=json.dumps({'cmd':'debug','args':{'action':'spawn truck'}}).encode(), headers=headers)

    def test_browser_origins_never_queue_mutations(self):
        for header in ({'Origin':'https://example.com'}, {'Origin':'null'},
                       {'Sec-Fetch-Site':'cross-site'}, {'Origin':'http://127.0.0.1:8330'}):
            with self.subTest(header=header):
                request = self.request({'Content-Type':'application/json', **header})
                with self.assertRaises(urllib.error.HTTPError) as raised:
                    self.session.bridge.http.open(request)
                self.assertEqual(raised.exception.code, 403)
                raised.exception.close()
        self.assertEqual(self.session.mock.state['vehicles'], [])
        self.assertEqual(self.session.mock.next_op, 1)

    def test_browser_simple_media_types_never_queue_mutations(self):
        for media in (None, 'text/plain', 'application/x-www-form-urlencoded', 'multipart/form-data; boundary=x'):
            with self.subTest(media=media):
                with self.assertRaises(urllib.error.HTTPError) as raised:
                    self.session.bridge.http.open(self.request({} if media is None else {'Content-Type':media}))
                self.assertEqual(raised.exception.code, 415)
                raised.exception.close()
        self.assertEqual(self.session.mock.state['vehicles'], [])
        self.assertEqual(self.session.mock.next_op, 1)

    def test_native_json_clients_still_work(self):
        with self.session.bridge.http.open(self.request({'Content-Type':'Application/JSON; charset=utf-8'})) as response:
            self.assertTrue(json.load(response)['ok'])
        self.assertEqual(len(self.session.mock.state['vehicles']), 1)
        self.assertTrue(self.session.bridge.call('state')['ok'])


if __name__ == '__main__': unittest.main()

"""Grid survey and atomic placement protocol, through the normal bridge/client guards."""
import copy
import runpy
from pathlib import Path
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
from offline_transport import OfflineSession
from ftlib.bridge import BridgeError

PLAY = runpy.run_path(str(TOOLS / 'play'), run_name='floorplan_test')['main']


class FloorPlanTests(unittest.TestCase):
    def setUp(self):
        self.session = OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__, None, None, None)
        self.mock, self.bridge = self.session.mock, self.session.bridge
        self.mock.state['save'] = 'FT playtest floorplan'
        self.mock.state['clock']['paused'] = True
        self.flat = 'ba:street_secondavenue#8'
        self.mock.life.update(inside=self.flat, homes=[self.flat])
        self.mock.state['player'].update(x=0., y=0., z=0.)
        self.mock.life['hands'] = {'key': 'ba:itemname_closedcardboardbox', 'item': 'Closed cardboard box', 'contents': [
            {'key': 'ba:itemname_bedindividualmodern', 'item': 'Bed (Individual Modern)', 'amount': 1, 'furniture': True,
             'food': False, 'paid': True, 'price': 987654.32}]}
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.stop = Path(self.folder.name) / 'STOP'
        self.lines = []

    def call(self, op, **args):
        return self.bridge.call(op, args)['result']

    def play(self, *words):
        self.lines.clear()
        return PLAY(list(words), bridge=self.bridge, out=self.lines.append, stop_file=str(self.stop),
                    memory=str(Path(self.folder.name) / 'memory'))

    def test_survey_is_read_only_and_exposes_geometry_and_every_target_without_economics(self):
        self.mock.life['beds'][self.flat] = [(1., 1., 90), (3., 1., 0)]
        before = copy.deepcopy((self.mock.life, self.mock.state))
        plan = self.call('floorplan')
        self.assertEqual(before, (self.mock.life, self.mock.state))
        self.assertEqual(plan['square_size'], .5)
        self.assertEqual(plan['player']['square'], 'B2')
        self.assertEqual({c['kind'] for c in plan['cells']}, {'floor', 'wall', 'opening', 'blocked', 'void'})
        self.assertEqual(len(plan['items']), 2)
        self.assertEqual(plan['items'][0]['anchor']['square'], 'D4')
        self.assertEqual(plan['items'][0]['use'][0]['square'], 'E5')
        self.assertTrue(plan['items'][0]['reachable'])
        self.assertEqual(self.play('floorplan'), 0, self.lines)
        rendered = '\n'.join(self.lines)
        self.assertIn('Legend:', rendered)
        self.assertIn('D4', rendered)
        self.assertIn('D3', plan['items'][0]['squares'])
        self.assertNotIn('D6', plan['items'][0]['squares'])
        self.assertIn('use E5 (reachable)', rendered)
        self.assertNotRegex(rendered, r'\$|987654|price|balance')
        self.assertEqual(len(plan['map']), plan['rows'])
        self.assertTrue(all(len(row) == len(plan['columns']) for row in plan['map']))

    def test_checks_fit_blocker_and_unreachable_and_preserve_cargo_items_and_money(self):
        before = copy.deepcopy((self.mock.life, self.mock.state))
        result = self.call('place', square='D4', rotation=100, check=True)
        self.assertTrue(result['fits']); self.assertFalse(result['placed']); self.assertTrue(result['restored'])
        self.assertEqual((result['x'], result['z'], result['rotation'], result['square']), (1., 1., 90., 'D4'))
        self.assertEqual(before, (self.mock.life, self.mock.state))
        self.mock.life['floorplan_blocked'] = True
        self.assertEqual(self.play('place', 'D4', '--check'), 0, self.lines)
        self.assertIn('blocked', self.lines[0]); self.assertIn('  Blocked by: Mock pillar.', self.lines)
        self.assertEqual(self.mock.life['hands'], before[0]['hands'])
        self.mock.life.update(floorplan_blocked=False, floorplan_unreachable=True)
        self.assertEqual(self.play('place', 'D4', '--check'), 0, self.lines)
        self.assertIn('fits', self.lines[0]); self.assertIn('  Reachable from you: no.', self.lines)
        self.assertEqual(self.mock.life['hands'], before[0]['hands'])

    def test_place_move_check_and_raw_coordinates_share_protocol(self):
        self.assertEqual(self.play('place', 'd4', '--rotation', '90'), 0, self.lines)
        self.assertIn('placed at D4', self.lines[0]); self.assertIsNone(self.mock.life['hands'])
        before = copy.deepcopy(self.mock.life['beds'])
        preview = self.call('place', square='F5', move='bed', check=True)
        self.assertTrue(preview['moved']); self.assertTrue(preview['restored'])
        self.assertEqual(before, self.mock.life['beds'])
        self.assertEqual(self.play('place', 'F5', '--move', 'bed'), 0, self.lines)
        self.assertIn('placed at F5', self.lines[0])
        result = self.call('place', x=-.25, z=.25, move='bed', check=True)
        self.assertEqual((result['x'], result['z'], result['square']), (0., .5, 'B3'))
        self.assertTrue(result['restored'])

    def test_wall_blocked_void_bad_labels_and_unpaid_do_not_consume_furniture(self):
        original = copy.deepcopy(self.mock.life['hands'])
        for square in ('A1', 'L9', 'M10'):
            with self.subTest(square=square):
                r = self.call('place', square=square, check=True)
                self.assertFalse(r['fits']); self.assertTrue(r['restored'])
                self.assertEqual(original, self.mock.life['hands'])
        for args in ({'square': 'A0'}, {'square': 'ZZ999'}, {'square': 'A1', 'x': 0, 'z': 0}, {'square': 'D4', 'check': 'true'}):
            with self.subTest(args=args), self.assertRaises(BridgeError): self.call('place', **args)
        self.mock.life['hands']['contents'][0]['paid'] = False
        with self.assertRaisesRegex(BridgeError, "haven't paid"): self.call('place', square='D4', check=True)

    def test_upper_floor_does_not_show_or_move_lower_floor_furniture(self):
        self.call('place', square='D4')
        self.assertEqual(len(self.call('floorplan')['items']), 1)
        self.mock.state['player']['y'] = 3.2
        self.assertEqual(self.call('floorplan')['items'], [])
        with self.assertRaisesRegex(BridgeError, 'No furniture'):
            self.call('place', square='D4', move='bed', check=True)
        self.mock.state['player']['y'] = 0.
        self.assertEqual(len(self.call('floorplan')['items']), 1)

    def test_current_floor_and_save_building_character_and_stop_guards(self):
        self.mock.state['player']['y'] = 3.2
        self.assertEqual(self.call('floorplan')['floor'], 2)
        self.mock.life['inside'] = None
        with self.assertRaisesRegex(BridgeError, 'inside'): self.call('floorplan')
        self.mock.life['inside'] = self.flat
        self.mock.state['save'] = 'Series'
        with self.assertRaisesRegex(BridgeError, "doesn't start with FT"): self.call('floorplan')
        self.mock.state['save'] = 'FT playtest floorplan'
        self.stop.write_text('review')
        self.assertEqual(self.play('place', 'D4', '--check'), 2)
        self.assertIn('referee stopped', self.lines[0])
        self.assertEqual(self.play('floorplan'), 0)
        self.mock.saves['Series'] = {'character': self.mock.character}
        with self.assertRaisesRegex(BridgeError, 'character also has saves'): self.call('floorplan')


if __name__ == '__main__':
    unittest.main()

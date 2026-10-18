"""Protocol and blind-player coverage. Mock rates are deliberately fake; Core covers real orders."""
import copy
from pathlib import Path
import runpy
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'tests'))
from offline_transport import NAMESPACE

PLAY = runpy.run_path(str(TOOLS / 'play'), run_name='serving_test')


class ServingWorkTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.mock = NAMESPACE['Mock'](self.folder.name, clock_rate=0)
        self.addCleanup(self.mock.unload)
        self.mock.state['save'] = 'FT playtest work'
        self.mock.state['licence']['until_day'] = 60

    def window(self, kind='cart', units=50):
        v = self.mock.spawn(kind)
        v['pieces'] = [{'item': 'hotdoggrill', 'units': units, 'cap': 500}]
        v['stock'] = [{'item': 'Hotdog', 'key': 'ba:itemname_hotdog', 'amount': units}]
        self.mock.operate(v, 'open')
        if kind == 'truck':
            self.mock.walks.clear()
            self.mock.at_hatch(v, 'opened')
        return v

    def test_native_controls_require_this_working_window(self):
        v = self.mock.spawn('cart')
        with self.assertRaisesRegex(ValueError, 'serving window'):
            self.mock.operate(v, 'fast_forward')
        v = self.window()
        other = self.mock.spawn('cart')
        with self.assertRaisesRegex(ValueError, 'serving window'):
            self.mock.operate(other, 'skip_work', {'minutes': 60})
        for duration in (None, 0, 4, 721, 5.5, float('nan')):
            with self.subTest(duration=duration), self.assertRaisesRegex(ValueError, 'whole number'):
                self.mock.operate(v, 'skip_work', {'minutes': duration})
        self.assertFalse(self.mock.time_skip)

    def test_fast_forward_runs_clock_sales_and_needs_then_resets_on_leaving(self):
        v = self.window('truck')
        self.assertTrue(self.mock.operate(v, 'fast_forward')['done'])
        self.assertGreater(self.mock.state['clock']['speed'], 1)
        self.assertTrue(self.mock.read_state()['player']['work']['fast_forward'])
        for _ in range(20): self.mock.tick()
        self.assertEqual(v['stock'][0]['amount'], 48)
        self.assertLess(self.mock.state['player']['needs']['energy'], 100)
        self.assertEqual(self.mock.state['money'], 20000)
        self.mock.operate(v, 'fast_forward', {'enabled': False})
        self.assertEqual(self.mock.state['clock']['speed'], 1)
        self.mock.operate(v, 'fast_forward')
        self.mock.operate(v, 'step_out')
        self.assertIsNone(self.mock.read_state()['player']['work'])
        self.assertEqual(self.mock.state['clock']['speed'], 1)

    def test_skip_has_same_sales_and_needs_as_live_minutes_and_leaves_no_work_spender(self):
        v = self.window()
        before = copy.deepcopy(v['stock'])
        self.mock.operate(v, 'skip_work', {'minutes': 30})
        self.assertTrue(self.mock.time_skip)
        for _ in range(30): self.mock.tick()
        self.assertFalse(self.mock.time_skip)
        self.assertEqual(v['stock'][0]['amount'], before[0]['amount'] - 3)
        self.assertEqual(self.mock.takings[(v['id'], 1)], 3)
        self.assertEqual(self.mock.state['money'], 20000)
        self.assertAlmostEqual(self.mock.state['player']['needs']['energy'], 100 - 30 * .056)
        self.assertAlmostEqual(self.mock.state['player']['needs']['hunger'], 100 - 30 * .084)
        self.assertEqual(self.mock.serving(v), 'nobody')
        self.mock.operate(v, 'work_window')
        self.assertEqual(self.mock.serving(v), 'player')
        self.mock.operate(v, 'stop_work')
        self.assertEqual(self.mock.serving(v), 'nobody')

    def test_sell_out_closure_and_energy_stop_work_skips_early(self):
        for reason in ('stock', 'close', 'energy', 'licence'):
            with self.subTest(reason=reason):
                self.mock.reset('FT playtest work')
                self.mock.state['licence']['until_day'] = 60
                v = self.window(units=1 if reason == 'stock' else 50)
                self.mock.operate(v, 'skip_work', {'minutes': 720})
                if reason == 'close': self.mock.operate(v, 'close')
                if reason == 'energy': self.mock.state['player']['needs']['energy'] = .01
                if reason == 'licence': self.mock.state['licence']['until_day'] = 0
                for _ in range(20):
                    if not self.mock.time_skip: break
                    self.mock.tick()
                self.assertFalse(self.mock.time_skip)
                self.assertIsNone(self.mock.work)
                self.assertLess(self.mock.absolute, 420 + 720)
                self.assertGreaterEqual(v['stock'][0]['amount'], 0)
                if reason == 'stock': self.assertEqual(self.mock.takings[(v['id'], 1)], 1)

    def test_work_hud_is_read_only_and_native_slider_drives_skip(self):
        v = self.window()
        work = self.mock.read_state()['player']['work']
        self.assertTrue(work['stock_read_only'])
        cargo = copy.deepcopy(v['stock'])
        with self.assertRaisesRegex(ValueError, 'vehicle enter first'):
            self.mock.operate(v, 'transfer', {'direction': 'return', 'item': 'Hotdog'})
        self.assertEqual(cargo, v['stock'])
        self.mock.last_ui = self.mock.buttons()
        slider = next(b for b in self.mock.last_ui if b['kind'] == 'slider')
        self.mock.call('ui', {'op': 'set', 'id': slider['id'], 'value': 15})
        self.mock.click({'text': 'Skip time'})
        self.assertEqual(self.mock.work_goal, 435)
        for _ in range(15): self.mock.tick()
        self.assertFalse(self.mock.time_skip)

    def test_blind_play_uses_only_native_vehicle_controls_and_waits_for_skip(self):
        v = self.window()
        mock = self.mock
        class Bridge:
            def health(self): return mock.health()
            def call(self, cmd, args, timeout_s): return {'result': mock.call(cmd, args)}
            def events(self, since): return {'next': mock.seq}
        lines = []
        play = PLAY['Play'](Bridge(), out=lines.append, stop_file='', memory=str(Path(self.folder.name) / 'memory'))
        play.vehicle(v['id'], 'fast_forward', 'on')
        play.vehicle(v['id'], 'fast_forward', 'off')
        called = []
        play.wait_for_skip = lambda cursor: called.append(cursor)
        play.vehicle(v['id'], 'skip_work', '30')
        self.assertEqual(self.mock.work_goal, 450)
        self.assertEqual(len(called), 1)
        self.assertTrue(any('skip_work: done' in line for line in lines))
        with self.assertRaises(PLAY['Refused']): play.vehicle(v['id'], 'skip_work', '721')


if __name__ == '__main__': unittest.main()

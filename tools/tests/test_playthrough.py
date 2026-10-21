import copy
import csv
import json
import math
from pathlib import Path
import runpy
import sys
import tempfile
import time
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'tests'))
from ftlib import playthrough as pt
from offline_transport import OfflineSession, NAMESPACE

PLAN = TOOLS.parent / 'tests/game/playthrough/phase-a.json'
MAIN = runpy.run_path(str(TOOLS / 'playthrough'), run_name='playthrough_test')['main']
PLAY = runpy.run_path(str(TOOLS / 'play'), run_name='play_test')['main']
HOTDOG, TACOS = 'ba:itemname_hotdog', 'foodtrucks:itemname_tacos'


def txn(kind, amount, day, clock='08:00', balance=0):
    return {'type': kind, 'label': kind, 'amount': amount, 'balance': balance, 'day': day, 'time': clock}


def vehicle(stock, site='hells-kitchen', in_bay=True, x=0., z=0., open_=False, week=()):
    return {'id': 'truck-1', 'kind': 'truck', 'x': x, 'z': z, 'open': open_, 'stock': dict(stock), 'pieces': ['hotdoggrill'],
            'site': site, 'in_bay': in_bay, 'bay_site': site if in_bay else None, 'crew': ['a'], 'week': list(week), 'problem': None, 'legal': True}


def summary(day, sold=None, **lines):
    base = {k: 0.0 for k in ('sales', 'catering', 'stock_cost', 'wages', 'parking', 'permit', 'commissary_rent', 'overnight_fines', 'profit')}
    base.update(lines)
    return {'day': day, 'today': day, 'in_summary': False, 'fees_paid_at': [],
            'fleet': {'revenue': base['sales'], 'stock_cost': base['stock_cost'], 'licence': 0.0, 'event_places': 0.0, 'delivery_fees': 0.0, 'profit': 0.0},
            'vehicles': [{'id': 'truck-1', 'kind': 'truck', **base, 'sold': [{'item': k, 'units': n} for k, n in (sold or {}).items()],
                          'stops_served': 1, 'minutes_open': 60, 'stock_now': []}]}


def snap(day, clock, money, stock_site, stock_vehicle, summaries, transactions=(), orders=(), weekday='tuesday', **v):
    return {'label': f'day {day} {clock}', 'save': 'FT playtest', 'day': day, 'time': clock, 'weekday': weekday, 'money': money,
            'vehicles': [vehicle(stock_vehicle, **v)], 'stock': {'hells-kitchen': dict(stock_site)}, 'orders': list(orders),
            'spaces': {'truck-1': 'hells-kitchen'}, 'transactions': list(transactions),
            'summaries': {str(s['day']): s for s in summaries}, 'events': []}


class CheckTests(unittest.TestCase):
    def test_new_transactions_count_each_booking_once_even_when_two_are_equal(self):
        sale = txn(pt.SALES_TYPES[0], 13.0, 2, '10:00', 100)
        prev = snap(2, '05:00', 0, {}, {}, [], [txn(pt.RENT_TYPE, -41.1, 1, '00:00'), sale])
        cur = snap(3, '05:00', 0, {}, {}, [], [sale, sale, txn(pt.WAGE_TYPE, -110.0, 2, '00:00')])
        fresh = pt.new_transactions(prev, cur)
        self.assertEqual([t['type'] for t in fresh], [pt.SALES_TYPES[0], pt.WAGE_TYPE])
        with self.assertRaises(pt.Violation): pt.new_transactions(prev, snap(5, '05:00', 0, {}, {}, []))

    def test_money_reconciles_to_the_cent_and_stops_when_it_does_not(self):
        prev = snap(2, '05:00', 1_000_00, {}, {}, [])
        booked = [txn(pt.SALES_TYPES[1], 26.0, 2, '00:00', 1026), txn(pt.RENT_TYPE, -41.1, 3, '00:00', 984)]
        pt.check_money(prev, snap(3, '05:00', 1_000_00 + 2600 - 4110, {}, {}, [], booked))
        with self.assertRaises(pt.Violation) as caught:
            pt.check_money(prev, snap(3, '05:00', 1_000_00 + 2600 - 4110 + 100, {}, {}, [], booked))
        self.assertEqual(caught.exception.check, 'money')
        self.assertIn('off by $1.00', caught.exception.message)

    def test_a_days_books_line_up_with_the_game_line_by_line(self):
        day = summary(2, sales=26.0, wages=110.0, commissary_rent=41.1)
        books = [txn(pt.SALES_TYPES[1], 26.0, 2), txn(pt.WAGE_TYPE, -110.0, 2), txn(pt.RENT_TYPE, -41.1, 2),
                 txn(pt.ORDER_TYPE, -1500.0, 2), txn(pt.PURCHASE_TYPE, -15000.0, 2), txn(pt.WAGE_TYPE, -99.0, 3)]
        pt.check_day_books(2, books, day, 1500_00, 15000_00)
        self.assertEqual(pt.check_day_books(2, books, day, 1500_00, 15000_00), [])
        # The game's other spending isn't ours: more wages or vehicles than the crews' and the runner's is a note.
        notes = pt.check_day_books(2, books, summary(2, sales=26.0, wages=100.0, commissary_rent=41.1), 1500_00, 15000_00)
        self.assertEqual(len(notes), 1)
        self.assertIn('wages', notes[0])
        self.assertEqual(len(pt.check_day_books(2, books + [txn('ba:transaction_rent', -900.0, 2)], day, 1500_00, 0)), 1)
        # Less than ours, or any of our own lines off, stops the run.
        with self.assertRaises(pt.Violation) as caught:
            pt.check_day_books(2, books, summary(2, sales=26.0, wages=120.0, commissary_rent=41.1), 1500_00, 15000_00)
        self.assertIn('wages', caught.exception.message)
        with self.assertRaises(pt.Violation):
            pt.check_day_books(2, books, summary(2, sales=27.0, wages=110.0, commissary_rent=41.1), 1500_00, 15000_00)

    def test_stock_is_conserved_per_food_with_deliveries_and_sales(self):
        prev = snap(2, '05:00', 0, {HOTDOG: 920}, {HOTDOG: 40}, [summary(1), summary(2, {HOTDOG: 0})])
        cur = snap(3, '05:00', 0, {HOTDOG: 880 + 500}, {HOTDOG: 50}, [summary(2, {HOTDOG: 30}), summary(3, {})])
        delivered = [{'n': 1, 'site': 'hells-kitchen', 'item': HOTDOG, 'units': 500}]
        self.assertEqual(pt.check_stock(prev, cur, delivered), {'truck-1': {HOTDOG: 30}})
        with self.assertRaises(pt.Violation) as caught:
            pt.check_stock(prev, cur, [])
        self.assertIn('(+500)', caught.exception.message)
        # A vehicle that ends with less than it started less what it sold lost stock, even if the totals agree.
        lost = snap(3, '05:00', 0, {HOTDOG: 920 + 15}, {HOTDOG: 5}, [summary(2, {HOTDOG: 20}), summary(3, {})])
        with self.assertRaises(pt.Violation) as caught:
            pt.check_stock(prev, lost, [])
        self.assertIn('vanished', caught.exception.message)

    def test_sales_before_and_after_the_snapshot_hours_are_split_by_the_sold_counts(self):
        # 3 sold on day 2 before 05:00 belong to the window before; 4 more after 00:00 on day 3 to this one.
        prev = snap(2, '05:00', 0, {}, {HOTDOG: 40}, [summary(1), summary(2, {HOTDOG: 3})])
        cur = snap(3, '05:00', 0, {}, {HOTDOG: 40 - 10 - 4}, [summary(2, {HOTDOG: 13}), summary(3, {HOTDOG: 4})])
        self.assertEqual(pt.sold_between(prev, cur, 'truck-1'), {HOTDOG: 14})
        pt.check_stock(prev, cur, [])

    def test_moves_want_each_vehicle_in_its_bay_or_at_an_overnight_stop(self):
        pt.check_moves(snap(3, '05:00', 0, {}, {}, []))
        away = snap(3, '05:00', 0, {}, {}, [], in_bay=False, x=100., z=50.)
        with self.assertRaises(pt.Violation) as caught: pt.check_moves(away)
        self.assertIn('not in its bay', caught.exception.message)
        night = [{'day': 'monday', 'stops': [{'open': '20:00', 'close': '02:00', 'x': 101., 'z': 50.}]}]
        pt.check_moves(snap(3, '05:00', 0, {}, {}, [], in_bay=False, x=100., z=50., week=night))
        with self.assertRaises(pt.Violation):
            pt.check_moves(snap(3, '05:00', 0, {}, {}, [], open_=True))
        # The referee leaves the player's own vehicle alone, and a vehicle without a crewed route.
        mine = snap(3, '05:00', 0, {}, {}, [], in_bay=False, x=100., z=50., week=[{'day': 'monday', 'stops': [{'open': '11:00', 'close': '14:00', 'x': 0, 'z': 0}]}])
        mine['player_with'] = 'truck-1'
        self.assertEqual(pt.check_moves(mine, crewed_only=True), [])
        no_route = snap(3, '05:00', 0, {}, {}, [], in_bay=False, x=100., z=50.)
        pt.check_moves(no_route, crewed_only=True)
        problem = snap(3, '05:00', 0, {}, {}, [])
        problem['vehicles'][0]['problem'] = 'overlap'
        self.assertEqual(len(pt.check_moves(problem)), 1)

    def test_error_lines_stop_the_run(self):
        pt.check_events([{'kind': 'log', 'text': 'Street food sales paid in: …'}])
        with self.assertRaises(pt.Violation) as caught:
            pt.check_events([{'kind': 'log', 'day': 3, 'time': '10:00', 'text': '[Mod:Food Trucks] ERROR: Route: boom'}])
        self.assertEqual(caught.exception.check, 'log')

    def test_ledger_is_written_in_dollars_and_read_back(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'ledger.csv'
            prev = snap(2, '05:00', 1_000_00, {}, {HOTDOG: 40}, [])
            cur = snap(3, '05:00', 1_020_50, {}, {HOTDOG: 38}, [])
            rows = pt.ledger_rows(2, summary(2, {HOTDOG: 2}, sales=26.0, stock_cost=3.0), prev, cur, [], [],
                                  [txn(pt.SALES_TYPES[1], 26.0, 2), txn(pt.RENT_TYPE, -5.5, 2)])
            pt.append_ledger(path, rows)
            back = pt.read_ledger(path)
            self.assertEqual([r['vehicle'] for r in back], ['truck-1', 'fleet'])
            self.assertEqual((back[0]['revenue'], back[0]['money_after'], back[0]['game_day_total']), ('26.00', '1020.50', '20.50'))
            self.assertEqual(back[0]['sold'], f'{HOTDOG}:2')
            with path.open() as f: self.assertEqual(next(csv.reader(f)), list(pt.LEDGER_FIELDS))

    def test_balance_gaps_beyond_a_quarter_are_flagged_not_failed(self):
        ledger = [{'day': '2', 'vehicle': 'truck-1', 'revenue': '100.00'}, {'day': '3', 'vehicle': 'truck-1', 'revenue': '100.00'},
                  {'day': '1', 'vehicle': 'truck-1', 'revenue': '0.00'}, {'day': '2', 'vehicle': 'fleet', 'revenue': '200.00'}]
        expected = {'vehicles': [{'id': 'truck-1', 'kind': 'truck', 'days': {d: {'revenue': 90.0} for d in pt.WEEK}}]}
        row = pt.compare_balance(ledger, expected, from_day=2)[0]
        self.assertEqual((row['actual'], row['expected'], row['days'], row['outside']), (200_00, 180_00, 2, False))
        expected['vehicles'][0]['days']['tuesday'] = {'revenue': 20.0}
        self.assertTrue(pt.compare_balance(ledger, expected, from_day=2)[0]['outside'])


class OfflineCheckTests(unittest.TestCase):
    """check over a run folder: every window and check on its own, gaps reported as not checkable."""

    def folder(self, snaps, orders=()):
        folder = Path(tempfile.mkdtemp())
        self.addCleanup(lambda: __import__('shutil').rmtree(folder))
        (folder / 'run.json').write_text(json.dumps({'mode': 'watch', 'plan': {'vehicles': []}, 'vehicles': {}, 'purchases': [], 'orders': list(orders)}))
        (folder / 'snapshots.jsonl').write_text(''.join(json.dumps(s) + '\n' for s in snaps))
        return folder

    def test_orders_count_only_in_the_window_they_arrive_and_every_check_runs(self):
        a = snap(1, '08:00', 1000_00, {}, {}, [summary(1, {})])
        b = snap(2, '05:00', 1000_00, {}, {}, [summary(1, {}), summary(2, {})])
        c = snap(3, '05:00', 1000_00 - 750_00, {HOTDOG: 500}, {}, [summary(2, {}), summary(3, {})], [txn(pt.ORDER_TYPE, -750.0, 2)])
        d = snap(4, '05:00', 1000_00 - 750_00 + 1, {HOTDOG: 500}, {}, [summary(3, {}), summary(4, {})], [txn(pt.ORDER_TYPE, -750.0, 2)])
        d['stock'] = None
        for s in (a, b, c, d): s['label'] = f"day {s['day']} 05:00"; s['vehicles'] = []
        # Order 1 (500 hot dogs) arrives day 2; order 2 (an equipment order) and order 3 (burgers, due day 9) don't count before.
        orders = [{'n': 1, 'site': 'hells-kitchen', 'item': HOTDOG, 'units': 500, 'total': 750.0, 'due_day': 2, 'goods': 750_00},
                  {'n': 3, 'site': 'hells-kitchen', 'item': 'ba:itemname_burger', 'units': 1500, 'total': 900.0, 'due_day': 9, 'goods': 900_00}]
        results, _ = pt.check_folder(self.folder([a, b, c, d], orders))
        status = {(r['window'], r['check']): r['status'] for r in results}
        self.assertEqual(status[('day 1 05:00 to day 2 05:00', 'stock')], 'pass')
        self.assertEqual(status[('day 2 05:00 to day 3 05:00', 'stock')], 'pass')
        self.assertEqual(status[('day 2 05:00 to day 3 05:00', 'books')], 'pass')
        # The last window: money off by a cent too many fails, stock is not checkable, and the others still run.
        self.assertEqual(status[('day 3 05:00 to day 4 05:00', 'stock')], 'not checkable')
        self.assertEqual(status[('day 3 05:00 to day 4 05:00', 'money')], 'fail')
        self.assertEqual(status[('day 3 05:00 to day 4 05:00', 'moves')], 'pass')
        self.assertEqual(len(results), 3 * len(pt.CHECKS))

    def test_the_games_own_errors_are_notes_and_ours_fail(self):
        game = {'kind': 'unity_error', 'day': 3, 'time': '11:30', 'text': 'NullReferenceException\nUI.Components.ReorderableList.InitializeItems ()'}
        self.assertEqual(len(pt.check_events([game, game])), 1)
        with self.assertRaises(pt.Violation):
            pt.check_events([{**game, 'text': 'NullReferenceException\nFoodTrucks.CartSelling.Tick ()'}])


class PlaythroughRunTests(unittest.TestCase):
    """Setup and three days against the mock bridge, which models money (every charge a transaction), stock (deliveries,
    the crews' morning loads and sales) and the crews' routes, so the checks have something to catch."""

    def setUp(self):
        self.session = OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__, None, None, None)
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.lines = []

    def main(self, *argv):
        return MAIN(['--dir', self.folder.name, *argv], bridge=self.session.bridge, out=self.lines.append)

    def test_refuses_a_save_not_named_ft_playtest(self):
        self.assertEqual(self.main('setup', str(PLAN), '--run', 'x'), 2)
        self.assertIn("starts with 'FT playtest'", self.lines[-1] if 'STOPPED' in self.lines[-1] else self.lines[-2])
        self.assertEqual(self.session.mock.state['vehicles'], [])

    def test_setup_and_three_days_pass_every_check(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.main('setup', str(PLAN), '--run', 'a'), 0, self.lines)
        run = Path(self.folder.name) / 'a'
        meta = json.loads((run / 'run.json').read_text())
        self.assertEqual(set(meta['vehicles']), {'cart'})
        self.assertEqual([p['cents'] for p in meta['purchases']], [1200_00])
        state = self.session.mock.read_state()
        types = {t['type'] for t in state['transactions_today']}
        self.assertIn('ba:transaction_vehiclebought', types)
        self.assertEqual(self.main('run', '--run', 'a', '--days', '3', '--reload-every', '2'), 0, self.lines)
        self.assertTrue(any("reloaded 'FT playtest d3'" in line for line in self.lines))
        ledger = pt.read_ledger(run / 'ledger.csv')
        self.assertEqual(sorted({int(r['day']) for r in ledger}), [1, 2, 3, 4])
        self.assertEqual(len(ledger), 4 * 2)
        for r in ledger:
            if r['vehicle'] != 'fleet' and int(r['day']) >= 2:
                self.assertGreater(float(r['revenue']), 0)
                self.assertEqual(r['stops_served'], '3')
        self.assertEqual(len(meta['plan']['changes']), 2)
        self.assertEqual(len(json.loads((run / 'run.json').read_text())['changes_done']), 2)
        self.assertEqual(self.main('check', '--run', 'a'), 0, self.lines)
        self.assertEqual(self.main('report', '--run', 'a', '--balance', 'off'), 0)
        report = (run / 'report.md').read_text()
        self.assertIn('Every check passed', report)
        self.assertIn('| Day |', report)
        saves = [n for n in self.session.mock.saves if n.startswith('FT playtest d')]
        self.assertEqual(sorted(saves), ['FT playtest d1', 'FT playtest d2', 'FT playtest d3', 'FT playtest d4', 'FT playtest d5'])

    def test_growth_buys_the_truck_once_money_less_the_reserve_covers_it(self):
        plan = json.loads(PLAN.read_text())
        plan['growth'].update(enabled=True, reserve=1000)
        path = Path(self.folder.name) / 'grow.json'
        path.write_text(json.dumps(plan))
        with self.session.mock.lock:
            self.session.mock.state['money'] = 40000.0
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.main('setup', str(path), '--run', 'g'), 0, self.lines)
        self.assertEqual(self.main('run', '--run', 'g', '--days', '3', '--reload-every', '0'), 0, self.lines)
        meta = json.loads((Path(self.folder.name) / 'g' / 'run.json').read_text())
        self.assertEqual(meta['grown']['day'], 3)
        self.assertTrue(meta['grown']['ready'])
        self.assertEqual(sorted(p['kind'] for p in meta['purchases']), ['cart', 'truck'])
        truck = meta['vehicles']['truck']
        rows = [r for r in pt.read_ledger(Path(self.folder.name) / 'g' / 'ledger.csv') if r['vehicle'] == truck]
        self.assertGreater(float(next(r for r in rows if r['day'] == '4')['revenue']), 0)
        self.assertEqual(self.main('report', '--run', 'g', '--balance', 'off'), 0)
        self.assertIn('growth: truck', (Path(self.folder.name) / 'g' / 'report.md').read_text())

    def test_stock_that_vanishes_stops_the_run_with_the_food_and_place(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.main('setup', str(PLAN), '--run', 'b'), 0, self.lines)
        with self.session.mock.lock:
            line = next(s for s in self.session.mock.state['commissary']['stock'] if s['key'] == HOTDOG)
            line['amount'] -= 7
        self.assertEqual(self.main('run', '--run', 'b', '--days', '2'), 2)
        stopped = json.loads((Path(self.folder.name) / 'b' / 'run.json').read_text())['stopped']
        self.assertEqual(stopped['check'], 'stock')
        self.assertIn(HOTDOG, stopped['message'])
        self.assertIn('-7', stopped['message'])
        # A stopped run doesn't go on.
        self.assertEqual(self.main('run', '--run', 'b', '--days', '1'), 2)

    def test_money_that_moves_without_a_transaction_stops_the_run(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.main('setup', str(PLAN), '--run', 'c'), 0, self.lines)
        with self.session.mock.lock:
            self.session.mock.state['money'] += 5
        self.assertEqual(self.main('run', '--run', 'c', '--days', '1'), 2)
        stopped = json.loads((Path(self.folder.name) / 'c' / 'run.json').read_text())['stopped']
        self.assertEqual(stopped['check'], 'money')
        self.assertIn('off by $5.00', stopped['message'])


class BridgeOpsParityTests(unittest.TestCase):
    """The new bridge ops in the mock, against the game side's source for their keys and messages."""

    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.mock = NAMESPACE['Mock'](self.folder.name)
        self.source = ''.join((TOOLS.parent / 'unity/FoodTrucks/Scripts' / name).read_text()
                              for name in ('TestBridgePlaythrough.cs', 'TestBridgeWorld.cs', 'TestBridgeState.cs'))

    def refused(self, cmd, args, text):
        with self.assertRaises(ValueError) as exc:
            self.mock.call(cmd, args)
        self.assertEqual(str(exc.exception), text)

    def test_cart_open_places_the_player_opposite_customers_and_allows_walking_away(self):
        v = self.mock.spawn('cart')
        v.update(x=20., z=30., pieces=[{'item': 'hotdoggrill', 'units': 40, 'cap': 40}],
                 stock=[{'item': 'Hotdog', 'key': HOTDOG, 'amount': 40}])
        self.mock.state['licence']['until_day'] = 60
        player = self.mock.state['player']
        for heading in (0., 45., 90., 180., 270.):
            with self.subTest(heading=heading):
                v['heading'] = heading
                player.update(x=20., z=29., heading=heading)
                self.assertTrue(self.mock.operate(v, 'open')['done'])
                angle = math.radians(heading)
                self.assertAlmostEqual(player['x'], v['x']-.85*math.cos(angle))
                self.assertAlmostEqual(player['z'], v['z']+.85*math.sin(angle))
                self.assertAlmostEqual(player['heading'], (heading+90)%360)
                self.assertIsNone(player['in_vehicle'])
                self.assertIsNone(player['at_hatch'])
                self.assertEqual(self.mock.serving(v), 'player')
                # An open cart keeps its existing distance rule and never pulls a walker back.
                player.update(x=40., z=30.)
                self.assertEqual(self.mock.serving(v), 'nobody')
                self.mock.operate(v, 'close')
                self.assertEqual((player['x'], player['z']), (40., 30.))

    def test_kerbside_cart_tries_safe_ends_then_leaves_the_player_where_they_stand(self):
        v = self.mock.spawn('cart')
        v.update(x=20., z=30., pieces=[{'item': 'hotdoggrill', 'units': 40, 'cap': 40}],
                 stock=[{'item': 'Hotdog', 'key': HOTDOG, 'amount': 40}])
        self.mock.state['licence']['until_day'] = 60
        player = self.mock.state['player']
        for side in (-1, 1):
            for heading in (0., 45., 90., 180., 270.):
                for available in ('front', 'rear', 'none'):
                    with self.subTest(side=side, heading=heading, available=available):
                        angle = math.radians(heading)
                        outward = (side*math.cos(angle), -side*math.sin(angle))
                        forward = (math.sin(angle), math.cos(angle))
                        tested = []

                        def safe(vehicle, point):
                            dx, dz = point[0]-vehicle['x'], point[2]-vehicle['z']
                            tested.append((dx, dz))
                            pavement = dx*outward[0]+dz*outward[1] >= -.001
                            # Fixture: front end cut off by a fence when only the rear is reachable.
                            reachable = available == 'front' or (available == 'rear' and dx*forward[0]+dz*forward[1] < 0)
                            return pavement and reachable

                        self.mock.cart_serving_safe = safe
                        v.update(heading=heading, hatch_side='left' if side < 0 else 'right')
                        player.update(x=20., y=v['y'], z=29., heading=15.)
                        before = {k: player[k] for k in ('x', 'y', 'z', 'heading')}
                        self.assertTrue(self.mock.operate(v, 'open')['done'])
                        self.assertEqual(len(tested), 2 if available == 'front' else 3)
                        if available == 'none':
                            self.assertEqual({k: player[k] for k in before}, before)
                            expected_log = 'player left where they stand'
                        else:
                            direction = 1 if available == 'front' else -1
                            self.assertAlmostEqual(player['x'], v['x']+direction*1.07*forward[0])
                            self.assertAlmostEqual(player['z'], v['z']+direction*1.07*forward[1])
                            dx, dz = player['x']-v['x'], player['z']-v['z']
                            self.assertGreaterEqual(dx*outward[0]+dz*outward[1], -.001)
                            look_x, look_z = v['x']+outward[0]*1.05, v['z']+outward[1]*1.05
                            self.assertAlmostEqual(player['heading'], math.degrees(math.atan2(look_x-player['x'], look_z-player['z'])) % 360)
                            expected_log = f'at the {available} end beside the window'
                        self.assertTrue(any(expected_log in e['text'] for e in self.mock.events(0)['events']))
                        self.mock.operate(v, 'close')

    def test_vehicle_buy_charges_the_dealers_price_as_the_games_purchase(self):
        money = self.mock.state['money']
        reply = self.mock.call('vehicle', {'op': 'buy', 'kind': 'truck'})
        self.assertEqual(set(reply), {'done', 'id', 'kind', 'price', 'money', 'notices', 'bay', 'state'})
        self.assertEqual((reply['done'], reply['kind'], reply['price']), (True, 'truck', 15000.0))
        self.assertEqual(self.mock.state['money'], money - 15000)
        t = self.mock.read_state()['transactions_today'][-1]
        self.assertEqual((t['type'], t['amount']), ('ba:transaction_vehiclebought', -15000.0))
        self.assertIn('"ba:transaction_vehiclebought"', self.source)
        self.assertEqual(self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})['price'], 1200.0)
        self.mock.state['money'] = 100.0
        text = 'Not enough money for the truck: it costs $15,000.00 and the player has $100.00.'
        self.refused('vehicle', {'op': 'buy', 'kind': 'truck'}, text)
        self.assertIn('Not enough money for the {kind}: it costs {CartSelling.Money(cents)} and the player has', self.source)
        self.refused('vehicle', {'op': 'buy', 'kind': 'van'}, "Unknown kind 'van': truck or cart.")
        self.assertIn("Unknown kind '{kind}': truck or cart.", self.source)
        for key in ('done', 'id', 'kind', 'price', 'money', 'notices', 'bay', 'state'):
            self.assertIn(f'["{key}"]', self.source)

    def test_route_remove_stop_removes_one_stop_of_a_day(self):
        v = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['friday']})
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_secondavenue#4', 'days': ['friday']})
        reply = self.mock.call('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 1, 'day': 'friday'})
        self.assertEqual(set(reply), {'done', 'day', 'stop', 'place', 'open', 'close', 'notices', 'state'})
        self.assertEqual((reply['done'], reply['day'], reply['stop'], reply['place'], reply['open'], reply['close']),
                         (True, 'friday', 1, 'ba:street_thirdstreet#6', '11:00', '14:00'))
        friday = reply['state']['route']['week'][4]['stops']
        self.assertEqual([(s['open'], s['close']) for s in friday], [('14:00', '17:00')])
        self.refused('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 2, 'day': 'friday'}, f"{v['id']} has 1 stop on Friday; no stop 2.")
        self.refused('route', {'op': 'remove_stop', 'vehicle': v['id'], 'day': 'friday'}, 'Missing "stop": the stop\'s number on that day\'s route, from 1.')
        for text in ('{id} has {todays.Count} stop{(todays.Count == 1 ? "" : "s")} on {day}; no stop {number}.',
                     'Missing \\"stop\\": the stop\'s number on that day\'s route, from 1.', 'RouteStops.Remove(vehicle, index)'):
            self.assertIn(text, self.source)
        for key in ('done', 'day', 'stop', 'place', 'open', 'close', 'notices', 'state'):
            self.assertIn(f'["{key}"]', self.source)

    def test_bay_takes_vehicles_nose_to_tail_in_its_lane_and_refuses_when_full(self):
        # Playtest a14: a cart put beside the truck stood half in the driving lane and read "parked legally, in the bay".
        truck, cart = self.mock.spawn('truck'), self.mock.spawn('cart')
        self.mock.place_vehicle(truck, 'hells-kitchen')
        self.mock.place_vehicle(cart, 'hells-kitchen')
        self.assertEqual(cart['x'], truck['x'])
        self.assertGreaterEqual(abs(cart['z'] - truck['z']), 5.8 / 2 + 1.5 / 2 + 0.5)
        # legal is the pavement rule Open checks (a parking lane isn't a pitch); the lane reading is its own field.
        self.assertFalse(cart['legal'])
        self.assertEqual(cart['legal_reason'], 'pavement rule: inside a parking lane; in the bay at hells-kitchen; clear of the driving lanes')
        self.assertIs(cart['clear_of_lanes'], True)
        self.assertIsNone(truck['clear_of_lanes'])
        for text in ('$"; in the bay at {bay.Id}"', '"; clear of the driving lanes"', '"; part of it stands in a driving lane"',
                     '"; the driving lanes weren\'t read"', '["clear_of_lanes"] = clearOfLanes', 'legal = rule.Legal;'):
            self.assertIn(text, self.source)
        for _ in range(2): self.mock.place_vehicle(self.mock.spawn('truck'), 'hells-kitchen')
        extra = self.mock.spawn('truck')
        with self.assertRaises(ValueError) as full:
            self.mock.place_vehicle(extra, 'hells-kitchen')
        self.assertRegex(str(full.exception), r'^No free place in the bay of hells-kitchen for the truck truck-5: its lane is full \(.*\)\. It stays where it is\.$')
        self.assertIn('No free place in the bay of {siteId} for the {kind} {id}: its lane is full ({what}). It stays where it is.', self.source)
        self.assertIn("You didn't set off.", self.source)

    def test_a_cart_bought_at_a_commissary_goes_into_its_bay_lane(self):
        # Within 25 m beyond the bay's radius, as the bridge's BuyNearBayMetres.
        self.assertIn('private const float BuyNearBayMetres = 25f;', self.source)
        bay_x, bay_z = NAMESPACE['bay_xy']('hells-kitchen')
        self.mock.state['money'] = 100000.0
        self.mock.state['player'].update(x=bay_x + 15 + 25 + 1, z=bay_z)
        self.assertIsNone(self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})['bay'])
        self.mock.state['player'].update(x=bay_x + 15 + 24, z=bay_z)
        truck = self.mock.call('vehicle', {'op': 'buy', 'kind': 'truck'})
        cart = self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        self.assertEqual((truck['bay']['site'], truck['bay']['parked'], cart['bay']['parked']), ('hells-kitchen', True, True))
        self.assertTrue(cart['state']['commissary']['in_bay'])
        self.assertNotEqual(cart['bay']['z'], truck['bay']['z'])
        self.assertEqual((cart['bay']['in_bay'], cart['bay']['clear_of_lanes']), (True, True))
        for key in ('site', 'parked', 'why', 'fallback', 'in_bay', 'bay_why', 'clear_of_lanes'):
            self.assertIn(f'["{key}"]', self.source)
        # A full lane: bought all the same (a dealer delivers anyway), by the kerb nearby, and the reply says so.
        for _ in range(2): self.mock.call('vehicle', {'op': 'buy', 'kind': 'truck'})
        self.fill_bay()
        kerb = self.mock.call('vehicle', {'op': 'buy', 'kind': 'truck'})
        self.assertTrue(kerb['done'])
        self.assertEqual((kerb['bay']['parked'], kerb['bay']['fallback']), (False, 'kerb'))
        self.assertIn(f"It stands at x {kerb['bay']['x']:.1f}, z {kerb['bay']['z']:.1f}, by the kerb nearby.", kerb['bay']['why'])
        self.assertIn('", by the kerb nearby."', self.source)
        # No kerb space either: refused before anything is spawned or charged.
        self.mock.kerb_free = False
        money, count = self.mock.state['money'], len(self.mock.state['vehicles'])
        with self.assertRaises(ValueError) as full:
            self.mock.call('vehicle', {'op': 'buy', 'kind': 'truck'})
        self.assertRegex(str(full.exception), r'^No safe place for a new truck: the lane of the bay of hells-kitchen is full \(.*\) and no kerb space is free within 60 m\. Nothing was bought\.$')
        self.assertIn('No safe place for a new {kind}: the lane of the bay of {site.Id} is full ({what}) and no kerb space is free within {KerbMetres:F0} m. Nothing was bought.', self.source)
        self.assertEqual((self.mock.state['money'], len(self.mock.state['vehicles'])), (money, count))

    def test_the_kerb_fallback_clears_lane_cars_as_its_search_ignores_them(self):
        # Review round 3: FreeParkingNear doesn't count the lane's parked cars, so placing there with clear false could
        # refuse and leave a paid vehicle in the road.
        fallback = self.source[self.source.index('foreach (var space in spaces)'):]
        self.assertIn('TeleportVehicle(vehicle.id, space.Position, space.Rotation.eulerAngles.y, true);', fallback[:400])

    def test_the_buy_fallback_takes_the_kerb_space_the_preflight_found_first(self):
        # Review round 4: searched again from the spawn, the only free space (58 m behind the player) was out of reach.
        self.assertIn('if (HireRoutes.FreeParkingForNew(PlayerHelper.GetPosition(), KerbMetres) is { } space) kerb = (space.Item1, space.Item2);', self.source)
        self.assertIn('bay = IntoNearBay(bought, kind, nearBay, kerbSpace);', self.source)
        fallback = self.source[self.source.index('var spaces = new List<(Vector3 Position, Quaternion Rotation)>();'):]
        self.assertLess(fallback.index('if (kerb != null) spaces.Add(kerb.Value);'), fallback.index('HireRoutes.FreeParkingNear(Spawned(vehicle)'))

    def test_a_refused_action_keeps_the_crew_s_hold_on_its_stop(self):
        v = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['monday']})
        v.update(open=True, serving='crew', queue={'length': 0, 'places': []})
        key = self.mock.stop_key(self.mock.plans[v['id']][0])
        self.mock.serving_stop[v['id']] = key
        self.mock.bay_pending[v['id']] = 'hells-kitchen'
        with self.assertRaises(ValueError):
            self.mock.call('vehicle', {'id': v['id'], 'op': 'open'})
        self.assertEqual((self.mock.serving_stop.get(v['id']), self.mock.bay_pending.get(v['id'])), (key, 'hells-kitchen'))

    def test_a_cart_bought_at_a_bay_with_unreadable_lanes_goes_to_the_kerb(self):
        self.mock.call('teleport', {'who': 'player', 'place': 'commissary:hells-kitchen'})
        self.mock.traffic_lanes = False
        reply = self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        self.assertEqual((reply['bay']['parked'], reply['bay']['fallback']), (False, 'kerb'))
        self.assertIn("the driving lanes couldn't be read to check it stands clear of them", reply['bay']['why'])
        self.assertIsNone(reply['state']['clear_of_lanes'])
        self.assertIsNone(reply['state']['commissary'])
        self.mock.kerb_free = False
        with self.assertRaises(ValueError) as refused:
            self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        self.assertEqual(str(refused.exception), "No safe place for a new cart: the bay of hells-kitchen can't take it (the driving lanes can't be read to check a cart "
                                                 "stands clear of them) and no kerb space is free within 60 m. Nothing was bought.")
        self.assertIn("No safe place for a new {kind}: the bay of {site.Id} can't take it (the driving lanes can't be read to check a cart stands clear of them) "
                      "and no kerb space is free within {KerbMetres:F0} m. Nothing was bought.", self.source)
        # In a bay with the lanes readable again, the reply's clearance is the state's.
        self.mock.traffic_lanes = True
        self.mock.kerb_free = True
        ok = self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        self.assertEqual(ok['bay']['clear_of_lanes'], ok['state']['clear_of_lanes'])

    def test_two_buys_at_a_full_bay_get_their_own_kerb_spaces(self):
        self.mock.state['money'] = 100000.0
        for _ in range(3): self.mock.place_vehicle(self.mock.spawn('truck'), 'hells-kitchen')
        self.fill_bay()
        self.mock.call('teleport', {'who': 'player', 'place': 'commissary:hells-kitchen'})
        first = self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        second = self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        self.assertEqual((first['bay']['fallback'], second['bay']['fallback']), ('kerb', 'kerb'))
        self.assertNotEqual((first['bay']['x'], first['bay']['z']), (second['bay']['x'], second['bay']['z']))
        self.assertEqual((second['state']['legal'], second['state']['legal_reason']), (False, 'pavement rule: inside a parking lane; clear of the driving lanes'))

    def test_teleporting_a_cart_to_a_bay_without_readable_lanes_is_refused(self):
        cart = self.mock.spawn('cart')
        cart.update(x=10., z=20.)
        self.mock.traffic_lanes = False
        with self.assertRaises(ValueError) as refused:
            self.mock.call('teleport', {'who': cart['id'], 'place': 'bay:hells-kitchen'})
        self.assertEqual(str(refused.exception), f"No place in the bay of hells-kitchen put the cart {cart['id']} in the bay (a cart within the radius, but the driving "
                                                 "lanes couldn't be read to check it stands clear of them). It is back where it stood (x 10.0, z 20.0).")
        for text in ("but the driving lanes couldn't be read to check it stands clear of them", 'It is back where it stood (x {now.x:F1}, z {now.z:F1}).'):
            self.assertIn(text, self.source)
        state = next(v for v in self.mock.read_state()['vehicles'] if v['id'] == cart['id'])
        self.assertEqual((state['x'], state['z'], state['clear_of_lanes']), (10., 20., None))
        self.assertTrue(state['legal_reason'].endswith("; the driving lanes weren't read"))

    def test_a_pending_return_gives_way_to_the_next_day_s_stop(self):
        cart = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': cart['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['tuesday']})
        cart.update(crew=['Mock crew'], pieces=[{'item': 'hotdoggrill', 'units': 40, 'cap': 40}],
                    stock=[{'item': 'Hotdog', 'key': 'ba:itemname_hotdog', 'amount': 40}])
        cart['route'] = self.mock.plan_route(cart, [{'employee': 'Mock crew', 'id': 'c1', 'week_hours': 8, 'wants_min': None, 'wants_max': None,
                                                     'shifts': [{'day': 'tuesday', 'start': 10, 'end': 18, 'station': 'serving1'}]}])
        # Monday night: no place in its bay, so its return waits; Tuesday it opens at its stop.
        self.mock.bay_pending[cart['id']] = 'hells-kitchen'
        for _ in range(3): self.mock.place_vehicle(self.mock.spawn('truck'), 'hells-kitchen')
        self.fill_bay()
        stop = self.mock.plans[cart['id']][0]
        self.mock.plan_hour(2, stop['open'])
        self.assertTrue(cart['open'])
        self.assertNotIn(cart['id'], self.mock.bay_pending)
        # A place frees up while it serves: nothing moves it off its stop.
        self.mock.state['vehicles'][1].update(x=0., z=0., commissary=None)
        self.mock.plan_hour(2, stop['open'] + 1)
        self.assertEqual((cart['x'], cart['z'], cart['open']), (stop['x'], stop['z'], True))
        # A player action cancels one too.
        self.mock.bay_pending[cart['id']] = 'hells-kitchen'
        self.mock.call('teleport', {'who': self.mock.spawn('truck')['id'], 'x': 1, 'z': 1})
        self.assertIn(cart['id'], self.mock.bay_pending)
        self.mock.call('vehicle', {'id': cart['id'], 'op': 'close'})
        self.assertNotIn(cart['id'], self.mock.bay_pending)

    def test_removing_a_stop_leaves_a_window_the_player_reopened(self):
        v = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['monday']})
        v.update(open=True, serving='crew', queue={'length': 0, 'places': []}, pieces=[{'item': 'hotdoggrill', 'units': 40, 'cap': 40}],
                 stock=[{'item': 'Hotdog', 'key': 'ba:itemname_hotdog', 'amount': 40}])
        self.mock.serving_stop[v['id']] = self.mock.stop_key(self.mock.plans[v['id']][0])
        # The player closes the crew's window and opens one of their own: the crew no longer holds the stop.
        self.mock.call('vehicle', {'id': v['id'], 'op': 'close'})
        self.assertNotIn(v['id'], self.mock.serving_stop)
        self.mock.state['licence']['until_day'] = 60
        self.mock.call('vehicle', {'id': v['id'], 'op': 'open'})
        self.assertTrue(v['open'])
        reply = self.mock.call('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 1, 'day': 'monday'})
        self.assertTrue(reply['state']['open'])

    def assert_no_footprints_overlap(self):
        vehicles = [v for v in self.mock.state['vehicles'] if (v['x'], v['z']) != (0., 0.)]
        for i, a in enumerate(vehicles):
            for b in vehicles[i + 1:]:
                self.assertFalse(self.mock.overlaps(self.mock.footprint(a), self.mock.footprint(b)),
                                 f"{a['id']} at ({a['x']}, {a['z']}, {a['heading']}) overlaps {b['id']} at ({b['x']}, {b['z']}, {b['heading']})")

    def test_footprints_are_measured_at_their_heading(self):
        # A 5.8 x 2.5 m truck facing +x and a cart 3 m along x from it overlap; along z, 3 m apart they don't touch.
        truck, cart = self.mock.spawn('truck'), self.mock.spawn('cart')
        truck.update(x=100., z=100., heading=90.)
        cart.update(x=103., z=100., heading=90.)
        self.assertTrue(self.mock.overlaps(self.mock.footprint(truck), self.mock.footprint(cart)))
        cart.update(x=100., z=103.)
        self.assertFalse(self.mock.overlaps(self.mock.footprint(truck), self.mock.footprint(cart)))
        # Perpendicular: the truck turned to face +z (1.25 m to each side) and a cart facing +x (0.75 m nose and tail):
        # 1.9 m apart they overlap, 2.0 m apart they touch, which keeps clear.
        truck.update(heading=0.)
        cart.update(x=101.9, z=100., heading=90.)
        self.assertTrue(self.mock.overlaps(self.mock.footprint(truck), self.mock.footprint(cart)))
        cart.update(x=102.)
        self.assertFalse(self.mock.overlaps(self.mock.footprint(truck), self.mock.footprint(cart)))

    def test_kerb_deliveries_and_drives_to_a_full_bay_never_overlap(self):
        self.mock.state['money'] = 100000.0
        for _ in range(3): self.mock.place_vehicle(self.mock.spawn('truck'), 'hells-kitchen')
        self.fill_bay()
        self.mock.call('teleport', {'who': 'player', 'place': 'commissary:hells-kitchen'})
        for kind in ('cart', 'cart', 'truck'):
            self.assertEqual(self.mock.call('vehicle', {'op': 'buy', 'kind': kind})['bay']['fallback'], 'kerb')
        truck = self.mock.spawn('truck')
        self.mock.state['player']['in_vehicle'] = truck['id']
        self.mock.state['clock']['paused'] = False
        self.mock.call('drive', {'id': truck['id'], 'place': 'bay:hells-kitchen'})
        self.assertEqual(self.mock.call('drive', {'op': 'status'})['park']['where'], 'kerb')
        self.assert_no_footprints_overlap()

    def test_a_bay_place_counts_a_vehicle_moved_there_by_coordinates(self):
        truck, cart = self.mock.spawn('truck'), self.mock.spawn('cart')
        x, z = NAMESPACE['bay_xy']('hells-kitchen')
        self.mock.call('teleport', {'who': truck['id'], 'x': x, 'z': z})
        self.assertIsNone(truck['commissary'])
        self.mock.call('teleport', {'who': cart['id'], 'place': 'bay:hells-kitchen'})
        self.assert_no_footprints_overlap()
        self.assertGreaterEqual(abs(cart['z'] - truck['z']), 2.9 + 0.75 + 0.5 - 1e-9)

    def test_refusals_answering_done_false_keep_a_pending_return(self):
        v = self.mock.spawn('truck')
        self.mock.bay_pending[v['id']] = 'hells-kitchen'
        # Open without equipment: a notice and done false.
        self.assertFalse(self.mock.call('vehicle', {'id': v['id'], 'op': 'open'})['done'])
        self.assertEqual(self.mock.bay_pending.get(v['id']), 'hells-kitchen')
        # A drive refused before it starts.
        self.mock.state['clock']['paused'] = False
        with self.assertRaises(ValueError):
            self.mock.call('drive', {'id': v['id'], 'place': 'bay:hells-kitchen'})
        self.assertEqual(self.mock.bay_pending.get(v['id']), 'hells-kitchen')

    def test_a_cart_sent_to_a_bay_facing_across_it_is_checked_at_that_heading(self):
        # Collisions at the final heading (review round 6); a cart has no wheel test, so it parks.
        x, z = NAMESPACE['bay_xy']('hells-kitchen')
        beside = self.mock.spawn('truck')
        beside.update(x=x + 1.6, z=z, heading=0.)
        cart = self.mock.spawn('cart')
        self.mock.call('teleport', {'who': cart['id'], 'place': 'bay:hells-kitchen', 'heading': 90})
        self.assertEqual(cart['heading'], 90.)
        self.assertNotEqual(cart['z'], z)
        self.assert_no_footprints_overlap()

    def test_a_truck_across_the_bay_lane_fails_the_wheel_test_and_stays_put(self):
        truck = self.mock.spawn('truck')
        truck.update(x=10., z=20., heading=45.)
        with self.assertRaises(ValueError) as refused:
            self.mock.call('teleport', {'who': truck['id'], 'place': 'bay:hells-kitchen', 'heading': 90})
        self.assertEqual(str(refused.exception), f"No place in the bay of hells-kitchen put the truck {truck['id']} in the bay (not legally parked "
                                                 "(the game's wheel test)). It is back where it stood (x 10.0, z 20.0).")
        self.assertEqual((truck['x'], truck['z'], truck['heading'], truck['commissary']), (10., 20., 45., None))
        # 20 degrees off the lane still parks; so does facing back along it.
        for heading in (20, 180):
            self.mock.call('teleport', {'who': truck['id'], 'place': 'bay:hells-kitchen', 'heading': heading})
            self.assertTrue(truck['commissary']['in_bay'])

    def test_an_outdoor_hand_truck_counts_against_a_bay_place(self):
        x, z = NAMESPACE['bay_xy']('hells-kitchen')
        self.mock.life['handtrucks'].append({'id': 'handtruck-1', 'name': 'Hand truck', 'kind': 'handtruck', 'home': None, 'inside': None,
                                             'x': x, 'y': 0., 'z': z, 'heading': 0., 'contents': [], 'capacity': 4})
        cart = self.mock.spawn('cart')
        self.mock.call('teleport', {'who': cart['id'], 'place': 'bay:hells-kitchen'})
        self.assertFalse(self.mock.overlaps(self.mock.footprint(cart), self.mock.footprint(self.mock.life['handtrucks'][0])))
        self.assertGreaterEqual(abs(cart['z'] - z), 0.45 + 0.75 + 0.5 - 1e-9)
        # One indoors doesn't stand in the street.
        self.mock.life['handtrucks'][0]['inside'] = 'ba:street_secondavenue#8'
        other = self.mock.spawn('cart')
        self.mock.call('teleport', {'who': other['id'], 'x': 0, 'z': 0})
        cart.update(x=0., z=0., commissary=None)
        self.mock.call('teleport', {'who': other['id'], 'place': 'bay:hells-kitchen'})
        self.assertEqual((other['x'], other['z']), (x, z))

    def test_helping_at_the_crew_s_window_keeps_its_stop(self):
        v = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['monday']})
        v.update(open=True, serving='crew', queue={'length': 0, 'places': []}, x=50., z=50.)
        key = self.mock.stop_key(self.mock.plans[v['id']][0])
        self.mock.serving_stop[v['id']] = key
        self.mock.state['player'].update(x=50., z=51., in_vehicle=None)
        self.mock.call('vehicle', {'id': v['id'], 'op': 'work_window'})
        self.assertEqual(self.mock.serving_stop.get(v['id']), key)
        # Removing that stop closes the crew's window, the player helping there or not (RouteStops.Remove).
        reply = self.mock.call('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 1, 'day': 'monday'})
        self.assertFalse(reply['state']['open'])

    def crew_window(self):
        """A cart its crew opened at Monday's stop at 12:00, its one crew member on shift 10-14."""
        v = self.mock.spawn('cart')
        self.mock.call('debug', {'action': 'hire'})
        employee = self.mock.read_state()['employees'][0]['id']
        self.mock.call('crew', {'op': 'assign', 'vehicle': v['id'], 'employee_id': employee})
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['monday']})
        self.mock.call('crew', {'op': 'schedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 10, 'end': 14})
        self.mock.state['clock'].update(day=1, time='12:00')
        v.update(open=True, serving='crew', queue={'length': 0, 'places': []})
        self.mock.serving_stop[v['id']] = self.mock.stop_key(self.mock.plans[v['id']][0])
        return v, employee

    def test_a_schedule_edit_ends_the_crew_s_service_only_when_nobody_is_on_shift(self):
        # Core's HireRunner.Step closes a crew window with nobody on shift on its next minute; the mock does it at once.
        self.assertIn('return want != null && want.StopIndex == vehicle.HireStop && Covered(vehicle, now, now)',
                      (TOOLS.parent / 'Core/Fleet/HireRunner.cs').read_text())
        v, employee = self.crew_window()
        self.mock.call('crew', {'op': 'schedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 17, 'end': 22})
        self.assertTrue(v['open'])
        self.mock.call('crew', {'op': 'unschedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 10})
        self.assertEqual((v['open'], v['serving']), (False, 'nobody'))
        v, employee = self.crew_window()
        self.mock.call('crew', {'op': 'schedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 15, 'end': 20, 'replace': True})
        self.assertFalse(v['open'])
        v, employee = self.crew_window()
        self.mock.call('crew', {'op': 'schedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 11, 'end': 16, 'replace': True})
        self.assertTrue(v['open'])

    def test_a_crew_window_closes_through_a_gap_in_its_shifts(self):
        # An 11-20 stop with shifts 10-14 and 17-22: closed from 14:00, as HireRunner.Step closes it, with no sales. The
        # mock doesn't reopen it at 17:00 (the game's runner may, after its own waits; review round 8).
        v, employee = self.crew_window()
        v.update(open=False, serving='nobody', queue=None, pieces=[{'item': 'hotdoggrill', 'units': 40, 'cap': 40}],
                 stock=[{'item': 'Hotdog', 'key': 'ba:itemname_hotdog', 'amount': 40}])
        self.mock.serving_stop.pop(v['id'], None)
        stop = self.mock.plans[v['id']][0]
        stop.update(open=11 * 60, close=20 * 60)
        self.mock.call('crew', {'op': 'schedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 17, 'end': 22})
        self.mock.state['licence']['until_day'] = 60
        self.mock.absolute = 11 * 60 - 1
        seen = {}
        while self.mock.absolute < 18 * 60:
            self.mock.tick()
            seen[self.mock.absolute] = (v['open'], v['stock'][0]['amount'])
        self.assertTrue(seen[13 * 60 + 59][0])
        self.assertLess(seen[13 * 60 + 59][1], 40)
        self.assertEqual([seen[m][0] for m in (14 * 60, 15 * 60, 17 * 60, 18 * 60)], [False] * 4)
        self.assertEqual(seen[14 * 60][1], seen[18 * 60][1])

    def test_an_unassigned_hire_s_shifts_go_with_them(self):
        v, employee = self.crew_window()
        self.mock.call('crew', {'op': 'unassign', 'vehicle': v['id'], 'employee_id': employee})
        self.assertEqual(v['route']['shifts'], [])
        self.assertFalse(v['open'])
        self.assertIn('WeekPlan.DropCrew(vehicle.Schedule, employeeId);', (TOOLS.parent / 'unity/FoodTrucks/Scripts/HireCrew.cs').read_text())

    def test_closing_a_crew_window_ends_the_player_s_work_there(self):
        for close in ('remove_stop', 'coverage'):
            v, employee = self.crew_window()
            self.mock.state['player'].update(x=v['x'], z=v['z'] + 1, in_vehicle=None)
            self.mock.call('vehicle', {'id': v['id'], 'op': 'work_window'})
            self.mock.call('vehicle', {'id': v['id'], 'op': 'fast_forward', 'enabled': True})
            self.assertIsNotNone(self.mock.work)
            if close == 'remove_stop':
                self.mock.call('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 1, 'day': 'monday'})
            else:
                self.mock.call('crew', {'op': 'unschedule', 'vehicle': v['id'], 'employee_id': employee, 'day': 'monday', 'start': 10})
            self.assertFalse(v['open'], close)
            self.assertIsNone(self.mock.work, close)
            self.assertIsNone(self.mock.state['player']['at_hatch'], close)
            self.assertEqual(self.mock.state['clock']['speed'], 1, close)
            labels = [b['text'] for b in self.mock.call('ui', {'op': 'list'})['buttons']]
            self.assertNotIn('Work duration', labels, close)
            with self.assertRaises(ValueError):
                self.mock.call('vehicle', {'id': v['id'], 'op': 'skip_work', 'minutes': 30})

    def test_buying_at_each_commissary_s_door_puts_the_vehicle_in_its_own_bay(self):
        self.mock.state['money'] = 100000.0
        for site in NAMESPACE['SITES']:
            self.mock.call('teleport', {'who': 'player', 'place': f'commissary:{site}'})
            reply = self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
            self.assertEqual((reply['bay']['site'], reply['bay']['parked']), (site, True), site)
            self.assertEqual(reply['state']['commissary']['bay_site'], site)

    def test_a_full_lane_clear_goes_through_the_checked_placement(self):
        # Review round 2: clearing the lane's cars at a full bay's middle once teleported there unchecked.
        self.assertIn('var spaced = InTheWay(controller, middle, rotation, vans, BayGapMetres);', self.source)
        self.assertIn('return PlaceInBay(vehicle, site, new[] { middle }, rotation, clear, vans);', self.source)

    def test_walking_a_cart_to_a_bay_without_readable_lanes_is_refused(self):
        cart = self.mock.spawn('cart')
        self.mock.state['player'].update(in_vehicle=cart['id'], x=0., z=0.)
        self.mock.state['clock']['paused'] = False
        self.mock.traffic_lanes = False
        with self.assertRaises(ValueError) as refused:
            self.mock.call('walk', {'place': 'bay:hells-kitchen'})
        self.assertRegex(str(refused.exception), r"^No pavement beside the free place in the bay of hells-kitchen at x .*, z .* to stand on while you park the cart\. You didn't set off\.$")
        self.assertIn('if (lanes.Unavailable) return null;', self.source)
        self.assertIn("No pavement beside the free place in the bay of {baySite} at x {slot.x:F1}, z {slot.z:F1} to stand on while you park the cart. You didn't set off.", self.source)
        self.assertEqual((self.mock.state['player']['x'], self.mock.state['player']['in_vehicle']), (0., cart['id']))

    def test_a_pending_bay_return_never_moves_a_vehicle_the_player_took(self):
        for _ in range(3): self.mock.place_vehicle(self.mock.spawn('truck'), 'hells-kitchen')
        self.fill_bay()
        cart = self.mock.spawn('cart')
        cart.update(x=50., z=50., commissary=None)
        self.mock.bay_pending[cart['id']] = 'hells-kitchen'
        self.mock.state['player'].update(x=50., z=50.)
        self.mock.operate(cart, 'enter')
        self.assertNotIn(cart['id'], self.mock.bay_pending)
        # Even one still pending is left alone while the player holds it.
        self.mock.bay_pending[cart['id']] = 'hells-kitchen'
        self.mock.state['vehicles'][0].update(x=0., z=0., commissary=None)
        self.mock.plan_hour(1, 600)
        self.assertEqual((cart['x'], cart['z'], cart['commissary']), (50., 50., None))
        self.assertNotIn(cart['id'], self.mock.bay_pending)

    def test_the_open_stop_and_pending_returns_survive_a_reload(self):
        v = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['monday']})
        v.update(open=True, serving='crew', queue={'length': 0, 'places': []})
        self.mock.serving_stop[v['id']] = self.mock.stop_key(self.mock.plans[v['id']][0])
        self.mock.store('FT reload')
        self.mock.restore('FT reload', emit=False)
        reply = self.mock.call('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 1, 'day': 'monday'})
        self.assertEqual((reply['state']['open'], reply['state']['serving']), (False, 'nobody'))
        # A return pending in this session doesn't follow into a save stored without it.
        self.mock.store('FT before')
        other = self.mock.vehicle(v['id'])
        self.mock.bay_pending[other['id']] = 'hells-kitchen'
        self.mock.restore('FT before', emit=False)
        self.assertEqual(self.mock.bay_pending, {})
        loaded = self.mock.vehicle(v['id'])
        before = (loaded['x'], loaded['z'])
        self.mock.plan_hour(1, 600)
        self.assertEqual((loaded['x'], loaded['z']), before)
        # One stored pending comes back with the save.
        self.mock.bay_pending[loaded['id']] = 'hells-kitchen'
        self.mock.store('FT pending')
        self.mock.bay_pending.clear()
        self.mock.restore('FT pending', emit=False)
        self.assertEqual(self.mock.bay_pending, {loaded['id']: 'hells-kitchen'})

    def fill_bay(self, site='hells-kitchen'):
        """Carts into the lane's last gaps until it is full."""
        while True:
            cart = self.mock.spawn('cart')
            try: self.mock.place_vehicle(cart, site)
            except ValueError:
                self.mock.state['vehicles'].remove(cart)
                return

    def test_teleport_to_a_bay_says_whether_it_is_in_the_bay(self):
        cart = self.mock.spawn('cart')
        reply = self.mock.call('teleport', {'who': cart['id'], 'place': 'bay:hells-kitchen'})
        self.assertEqual((reply['in_bay'], reply['bay_why'], reply['clear_of_lanes']), (True, 'a cart within the radius', True))

    def test_removing_the_stop_being_served_closes_the_vehicle_first(self):
        v = self.mock.spawn('cart')
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'days': ['monday']})
        stop = self.mock.plans[v['id']][0]
        v.update(open=True, serving='crew', queue={'length': 0, 'places': []})
        self.mock.serving_stop[v['id']] = self.mock.stop_key(stop)
        reply = self.mock.call('route', {'op': 'remove_stop', 'vehicle': v['id'], 'stop': 1, 'day': 'monday'})
        self.assertTrue(reply['done'])
        self.assertEqual((reply['state']['open'], reply['state']['serving']), (False, 'nobody'))
        self.assertIn('if (vehicle.IsOpen && vehicle.HireStop == index) CartSelling.CloseByCrew(vehicle, HireCrew.Now());',
                      (TOOLS.parent / 'unity/FoodTrucks/Scripts/RouteStops.cs').read_text())

    def test_a_vehicle_back_to_a_full_bay_retries_each_minute_until_parked(self):
        trucks = [self.mock.spawn('truck') for _ in range(3)]
        for t in trucks: self.mock.place_vehicle(t, 'hells-kitchen')
        self.fill_bay()
        cart = self.mock.spawn('cart')
        cart.update(x=0., z=0., commissary=None)
        self.mock.bay_pending[cart['id']] = 'hells-kitchen'
        self.mock.plan_hour(1, 600)
        self.assertIsNone(cart['commissary'])
        self.assertIn(cart['id'], self.mock.bay_pending)
        # A place frees up: the next minute it is parked, and no longer waits.
        trucks[0].update(x=0., z=0., commissary=None)
        self.mock.plan_hour(1, 601)
        self.assertTrue(cart['commissary']['in_bay'])
        self.assertNotIn(cart['id'], self.mock.bay_pending)

    def test_day_summary_shape_and_guard(self):
        v = self.mock.spawn('truck')
        reply = self.mock.call('day_summary', {})
        self.assertEqual(set(reply), {'day', 'today', 'in_summary', 'fleet', 'fees_paid_at', 'vehicles'})
        self.assertEqual(set(reply['fleet']), {'revenue', 'stock_cost', 'licence', 'event_places', 'delivery_fees', 'profit'})
        row = reply['vehicles'][0]
        self.assertEqual(row['id'], v['id'])
        keys = {'id', 'kind', 'sales', 'catering', 'stock_cost', 'wages', 'parking', 'permit', 'commissary_rent', 'overnight_fines', 'marketing',
                'profit', 'sold', 'stops_served', 'minutes_open', 'stock_now'}
        self.assertEqual(set(row), keys)
        for key in keys | set(reply) | set(reply['fleet']):
            self.assertIn(f'["{key}"]', self.source)
        self.refused('day_summary', {'day': 5}, 'day is a whole day number from 1 to today (1).')
        self.assertIn('day is a whole day number from 1 to today ({today}).', self.source)

    def test_route_add_stop_by_address_and_paste_day(self):
        v = self.mock.spawn('cart')
        reply = self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6'})
        self.assertTrue(reply['done'])
        self.assertEqual(set(reply), {'done', 'address', 'x', 'z', 'heading', 'distance', 'days', 'notices', 'state'})
        self.assertEqual([d['day'] for d in reply['days']], list(pt.WEEK))
        again = self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_secondavenue#4', 'days': ['monday']})
        week = again['state']['route']['week']
        self.assertEqual([(s['open'], s['close']) for s in week[0]['stops']], [('11:00', '14:00'), ('14:00', '17:00')])
        self.assertEqual(len(week[1]['stops']), 1)
        pasted = self.mock.call('route', {'op': 'paste_day', 'vehicle': v['id'], 'from': 'monday', 'to': ['tuesday']})
        self.assertEqual(len(pasted['state']['route']['week'][1]['stops']), 2)
        self.refused('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'Third Street 6'},
                     '"address" is a building\'s address as the game writes it, ba:street_<name>#<number>; got \'Third Street 6\'.')
        self.refused('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#999'},
                     'no building at ba:street_thirdstreet#999 in this city')
        self.refused('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6', 'open': '11:00'},
                     'Give both "open" and "close", or neither for the default hours.')
        self.refused('route', {'op': 'paste_day', 'vehicle': v['id'], 'from': 'someday', 'to': 'all'}, "Unknown day 'someday': monday to sunday.")
        self.refused('route', {'op': 'move', 'vehicle': v['id']}, "Unknown route op 'move': add_stop, remove_stop, paste_day.")
        for text in ('Give both \\"open\\" and \\"close\\", or neither for the default hours.', "Unknown route op '{op}': add_stop, remove_stop, paste_day.",
                     "Unknown day '{name}': monday to sunday.", 'no building at {address} in this city'):
            self.assertIn(text, self.source)
        for key in ('done', 'address', 'x', 'z', 'heading', 'distance', 'days', 'added', 'stops', 'problem'):
            self.assertIn(f'["{key}"]', self.source)
        self.mock.state['save'] = 'Non-FT mock'
        with self.assertRaises(ValueError): self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6'})

    def test_a_route_to_do_names_the_days_with_nobody_on_shift(self):
        v = self.mock.spawn('cart')
        self.mock.call('debug', {'action': 'hire'})
        self.mock.call('crew', {'op': 'assign', 'vehicle': v['id'], 'employee_id': self.mock.read_state()['employees'][0]['id']})
        self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6'})
        todo = next(x for x in self.mock.read_state()['vehicles'] if x['id'] == v['id'])['todos'][0]
        self.assertEqual((todo['kind'], todo['text']), ('route_problem', 'Route: nobody on shift Monday to Sunday'))
        self.mock.call('crew', {'op': 'auto_schedule', 'vehicle': v['id']})
        employee = self.mock.read_state()['vehicles'][0]['route']['shifts'][0]['id']
        for day in ('saturday', 'sunday'):
            self.mock.call('crew', {'op': 'schedule', 'vehicle': v['id'], 'employee_id': employee, 'day': day, 'start': 10, 'end': 10})
        todo = next(x for x in self.mock.read_state()['vehicles'] if x['id'] == v['id'])['todos'][0]
        self.assertEqual(todo['text'], 'Route: nobody on shift Saturday and Sunday')
        self.assertEqual(NAMESPACE['week_days'](['friday', 'monday', 'wednesday']), 'Monday, Wednesday and Friday')
        self.assertIn('["text"] = t.Text', self.source)
        self.assertIn('data["cause"] = RouteCauseText(task.itemInstanceId)', (TOOLS.parent / 'unity/FoodTrucks/Scripts/VehicleTodos.cs').read_text())

    def test_the_players_life_ops_match_the_game_side(self):
        life = (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeLife.cs').read_text()
        world = (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeWorld.cs').read_text()
        ui = (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeUi.cs').read_text()
        listed = self.mock.call('use', {'op': 'list'})
        self.assertEqual(set(listed), {'radius', 'inside', 'things'})
        for key in set(listed) | {'id', 'kind', 'name', 'click', 'x', 'z', 'distance', 'address', 'business', 'for_rent', 'yours', 'sells', 'sells_key'}:
            self.assertIn(f'["{key}"]', life)
        self.mock.state['clock']['paused'] = False
        clicked = self.mock.call('use', {'op': 'click', 'id': 1})
        self.assertEqual(set(clicked), {'clicked', 'kind', 'click', 'handled', 'why', 'placing_cancelled', 'still_walking', 'still_entering', 'inside', 'inside_before', 'open', 'notices', 'x', 'z'})
        for key in clicked:
            self.assertIn(f'["{key}"]', life)
        rode = self.mock.call('taxi', {'address': 'ba:street_thirdstreet#6'})
        self.assertEqual(set(rode), {'done', 'address', 'fare', 'minutes', 'money', 'notices', 'x', 'z'})
        for key in rode:
            self.assertIn(f'["{key}"]', life)
        texts = ['No taxi is waiting for you: click a taxi in the street first (use list shows the ones near you; use click gets in), then say where to.',
                 "Your hands are empty: food you buy comes in a bag to eat from.", "You can place furniture only inside a building you rent.",
                 "You aren't holding a box of furniture (shops pack what you buy in a box).", "You're not inside a building.",
                 "The game is paused: the walk over takes real time. Set a speed first."]
        self.refused('taxi', {'address': 'ba:street_thirdstreet#6'}, texts[0])
        self.refused('eat', {}, texts[1])
        self.refused('use', {'op': 'right_click', 'id': 1}, "Unknown use op 'right_click': list or click.")
        self.assertIn("Unknown use op '{op}': list or click.", life)
        self.mock.dialog = 'agency'
        self.refused('call', {'contact': 'the_city_of_new_york'}, "You're in a call or a conversation already: finish it first (its own buttons, or ui escape).")
        self.assertIn("You're in a call or a conversation already: finish it first (its own buttons, or ui escape).", life)
        self.mock.dialog = 'closed'
        self.refused('place', {'x': 1, 'z': 1}, texts[2])
        self.refused('walk', {'place': 'exit'}, texts[4])
        self.mock.state['clock']['paused'] = True
        self.mock.call('use', {'op': 'list'})
        self.refused('use', {'op': 'click', 'id': 1}, texts[5])
        for text in texts:
            self.assertIn(text, life + world)
        self.assertEqual({c['id'] for c in self.mock.call('call', {'op': 'list'})['contacts']}, {'the_city_of_new_york'})
        self.assertIn('the_city_of_new_york', (TOOLS.parent / 'unity/FoodTrucks/Scripts/CityLicensing.cs').read_text())
        fleet = (TOOLS.parent / 'unity/FoodTrucks/Scripts/FleetRecruiting.cs').read_text()
        patches = (TOOLS.parent / 'unity/FoodTrucks/Scripts/Patches.cs').read_text()
        rules = (TOOLS.parent / 'tests/game/logchecks/0.9.10.txt').read_text()
        for method in ('NoBusinesses', 'OnRecruitmentSettingsSet', 'OnEnable', 'SelectBusiness', 'SetUpContract'):
            self.assertIn(f'"{method}"', patches)
            self.assertIn(f'.{method}', rules)
        for line in ('Fleet recruiting: the agency offers a campaign for the street food fleet (no building business).',
                     'Fleet recruiting: campaign for '):
            self.assertIn(line, fleet)
        self.assertIn('FleetRecruiting.EndForUninstall()', (TOOLS.parent / 'unity/FoodTrucks/Scripts/Uninstall.cs').read_text())
        for key in ('contact', 'name', 'calling', 'open', 'contacts', 'category', 'unread', 'app', 'item', 'done', 'hunger', 'energy', 'placed', 'why', 'rotation'):
            self.assertIn(f'["{key}"]', life)
        for key in ('name', 'amount', 'shows_time'):
            self.assertIn(f'["{key}"]', (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeCommands.cs').read_text())
        for key in ('value', 'min', 'max', 'whole_numbers', 'on'):
            self.assertIn(f'["{key}"]', ui)
        self.assertIn('case "set":', ui)
        state = self.mock.read_state()['player']
        for key in ('hands', 'inside', 'homes', 'riding'):
            self.assertIn(key, state)
            self.assertIn(f'["{key}"]', self.source)
        self.assertIn('["warnings"]', (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeCommands.cs').read_text())
        self.mock.state['save'] = 'Non-FT mock'
        for cmd, args in (('taxi', {'address': 'ba:street_thirdstreet#6'}), ('eat', {}), ('place', {'x': 1, 'z': 1}), ('phone', {'app': 'contacts'}),
                          ('call', {'contact': 'the_city_of_new_york'}), ('use', {'op': 'click', 'id': 1})):
            with self.assertRaises(ValueError): self.mock.call(cmd, args)

    def test_transactions_carry_the_balance_after_them(self):
        self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        t = self.mock.read_state()['transactions_today'][-1]
        self.assertEqual(t['balance'], 20000 - 1200)
        self.assertIn('["balance"] = Math.Round(t.balance, 2)', self.source)


if __name__ == '__main__':
    unittest.main()


class BlindPlayTests(unittest.TestCase):
    """tools/play, the tester's only controls, and the referee watching it (playthrough watch)."""

    def setUp(self):
        self.session = OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__, None, None, None)
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.stop = str(Path(self.folder.name) / 'STOP')
        self.lines = []

    def play(self, *argv):
        self.lines.clear()
        return PLAY(list(argv), bridge=self.session.bridge, out=self.lines.append, stop_file=self.stop,
                    memory=str(Path(self.folder.name) / 'memory.json'))

    def ride(self, address):
        """Into the taxi waiting in the street, then to the address."""
        self.session.bridge.call('time', {'speed': 1})
        self.assertEqual(self.play('use'), 0, self.lines)
        taxi = next(line for line in self.lines if 'Taxi' in line).split(']')[0][1:]
        self.assertEqual(self.play('use', taxi), 0, self.lines)
        self.assertEqual(self.play('go', address), 0, self.lines)
        self.assertIn('The taxi set you down', self.lines[0])

    def ui_click(self, text):
        self.assertEqual(self.play('ui', 'click', text), 0, self.lines)

    def thing(self, name):
        self.assertEqual(self.play('use'), 0, self.lines)
        return next(line for line in self.lines if name in line).split(']')[0][1:]

    def licence_by_phone(self):
        self.assertEqual(self.play('call', 'The City of New York'), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        buy = next(line for line in self.lines if 'Buy a licence' in line).split('] ', 1)[1]
        self.ui_click(buy)
        self.ui_click('Vehicle permits')
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        self.ui_click(next(line for line in self.lines if 'Buy the permit' in line).split('] ', 1)[1])
        self.ui_click('Hang up')

    def hire_candidate(self):
        # The mock has no recruitment campaign: a candidate waits on the phone, as one the game's channels sent.
        with self.session.mock.lock:
            self.session.mock.life['candidates'].append({'id': 'candidate-1', 'name': 'Mock candidate', 'skill': 55., 'satisfaction': 100., 'wage': 11.})
        self.assertEqual(self.play('phone', 'employees'), 0, self.lines)
        self.ui_click('Hire Mock candidate')
        self.ui_click('Close')
        return self.session.mock.read_state()['employees'][0]['id']

    def test_the_manual_lists_the_commands_and_nothing_else_is_there(self):
        import contextlib, io
        text = io.StringIO()
        with contextlib.redirect_stdout(text), self.assertRaises(SystemExit):
            PLAY(['--help'])
        for command in ('look', 'places', 'go ADDRESS', 'walk X Z', 'map filters', 'buy truck|cart', 'order SITE', 'crew assign', 'route add', 'time until',
                        'use [--radius M]', 'eat [ITEM]', 'place X Z', 'phone APP', 'call list', 'ui set ID VALUE', 'How the city works',
                        'drive TRUCK PLACE', 'bay:SITE', 'stop:N'):
            self.assertIn(command, text.getvalue())
        # Hints, not numbers: no price, fee or duration in the manual's city section.
        hints = text.getvalue().split('How the city works', 1)[1].split('If the referee', 1)[0]
        self.assertNotRegex(hints, r'\$|\d')
        import argparse
        names = set(runpy.run_path(str(TOOLS / 'play'), run_name='play_test')['parser']()._subparsers._group_actions[0].choices)
        self.assertEqual(names, {'look', 'notices', 'places', 'go', 'walk', 'map', 'screen', 'ui', 'time', 'buy', 'vehicle', 'order',
                                 'crew', 'price', 'route', 'save', 'load', 'use', 'eat', 'place', 'floorplan', 'drop', 'pickup', 'handtruck', 'phone', 'call', 'drive'})
        # No shortcuts: no money, teleport, spawn, fixture or debug op is reachable, and the old licence and hire are gone.
        for argv in (['money', 'set', '1000000'], ['licence'], ['hire'], ['teleport', 'x'], ['debug', 'hire']):
            with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                PLAY(argv)
        source = (TOOLS / 'play').read_text()
        for cmd in ("'money'", "'teleport'", "'debug'", "'fixture'", "'logistics'", "'console'"):
            self.assertNotIn(f"self.call({cmd}", source)

    def test_price_sets_and_clears_a_food_s_price_and_look_shows_it(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        cart = self.session.mock.state['vehicles'][0]
        cart['stock'] = [{'item': 'Hotdog', 'key': 'ba:itemname_hotdog', 'amount': 40}]
        self.assertEqual(self.play('price', cart['id'], 'Hotdog', '14.50'), 0, self.lines)
        self.assertIn('Hotdog: $14.50.', self.lines)
        self.assertEqual(self.play('look'), 0, self.lines)
        self.assertTrue(any('40 Hotdog (price $14.50)' in line for line in self.lines), self.lines)
        self.assertEqual(self.play('price', cart['id'], 'ba:itemname_hotdog', 'auto'), 0, self.lines)
        self.assertIn('Hotdog: automatic price.', self.lines)

    def test_crew_shifts_are_added_replaced_and_removed_and_a_route_stop_removed(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        cart = self.session.mock.state['vehicles'][0]['id']
        self.session.bridge.call('debug', {'action': 'hire'})
        employee = self.session.bridge.call('state')['result']['employees'][0]['id']
        self.assertEqual(self.play('crew', 'assign', cart, employee), 0, self.lines)
        self.assertEqual(self.play('crew', 'schedule', cart, employee, 'thursday', '8', '16'), 0, self.lines)
        self.assertEqual(self.play('crew', 'schedule', cart, employee, 'thursday', '17', '23'), 0, self.lines)
        self.assertEqual(self.play('crew', 'schedule', cart, employee, 'thursday', '15', '18'), 1, self.lines)
        self.assertIn('already works 08-16, 17-23 on Thursday', self.lines[0])
        self.assertEqual(self.play('crew', 'schedule', cart, employee, 'thursday', '10', '14', '--replace'), 0, self.lines)
        self.assertEqual(self.play('crew', 'unschedule', cart, employee, 'thursday', '10'), 0, self.lines)
        self.assertEqual(self.play('crew', 'assign', cart, employee, '--replace'), 2, self.lines)
        self.assertEqual(self.play('route', 'add', cart, 'ba:street_thirdstreet#6', '--days', 'friday'), 0, self.lines)
        self.assertEqual(self.play('route', 'add', cart, 'ba:street_secondavenue#4', '--days', 'friday'), 0, self.lines)
        self.assertEqual(self.play('route', 'remove', cart, '1', '--day', 'friday'), 0, self.lines)
        self.assertEqual(self.lines, ['Removed stop 1 (ba:street_thirdstreet#6, 11:00 to 14:00) from friday.'])
        self.assertEqual(self.play('route', 'remove', cart, '3', '--day', 'friday'), 1, self.lines)

    def test_look_shows_why_a_cart_isnt_legal_and_its_lane_reading(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        with self.session.mock.lock:
            cart = self.session.mock.state['vehicles'][0]
            cart.update(legal=False, legal_reason='pavement rule: inside a parking lane; part of it stands in a driving lane', clear_of_lanes=False)
        self.assertEqual(self.play('look'), 0, self.lines)
        line = next(l for l in self.lines if l.startswith(f"Cart {cart['id']}:"))
        self.assertIn('not parked legally (pavement rule: inside a parking lane; part of it stands in a driving lane) at x', line)
        self.assertIn('  Part of it stands in a driving lane.', self.lines)
        with self.session.mock.lock: cart.update(legal=True, legal_reason='pavement rule: legal', clear_of_lanes=None)
        self.assertEqual(self.play('look'), 0, self.lines)
        self.assertIn("  Whether it stands clear of the driving lanes couldn't be read.", self.lines)
        self.assertFalse(any('(pavement rule' in l for l in self.lines), self.lines)

    def test_walking_pushes_a_held_cart_into_the_bay_and_keeps_the_clock_stopped(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.session.mock.state['player'].update(x=380., z=210.)
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        cart = self.session.mock.state['vehicles'][0]['id']
        self.session.bridge.call('time', {'speed': 1})
        self.assertEqual(self.play('vehicle', cart, 'enter'), 0, self.lines)
        # Entering walks to the cart first.
        for _ in range(50):
            if self.session.bridge.call('state')['result']['player']['in_vehicle'] == cart: break
            time.sleep(.05)
        self.session.bridge.call('time', {'pause': True})
        self.assertEqual(self.play('walk', '--to', 'bay:hells-kitchen'), 0, self.lines)
        self.assertIn(f'pushing {cart}', self.lines[0])
        # Parked in the bay's lane and let go of (playtest a14: pushed up beside the truck it stood in the road).
        self.assertTrue(any(f"You parked {cart} in the bay's lane" in line for line in self.lines), self.lines)
        v = self.session.mock.state['vehicles'][0]
        self.assertTrue(v['commissary']['in_bay'])
        self.assertIsNone(self.session.mock.state['player']['in_vehicle'])
        self.assertTrue(self.session.mock.state['clock']['paused'])
        # On foot, a walk to a point; a truck can't be walked.
        self.session.bridge.call('time', {'speed': 1})
        self.assertEqual(self.play('walk', '400', '230'), 0, self.lines)
        self.assertIn('You walked', self.lines[0])
        self.assertEqual(self.play('walk', '400'), 2)
        self.assertEqual(self.play('buy', 'truck'), 0, self.lines)
        truck = self.session.mock.state['vehicles'][1]['id']
        self.session.bridge.call('time', {'speed': 1})
        self.assertEqual(self.play('vehicle', truck, 'enter'), 0, self.lines)
        for _ in range(50):
            if self.session.bridge.call('state')['result']['player']['in_vehicle'] == truck: break
            time.sleep(.05)
        self.assertEqual(self.play('walk', '410', '230'), 1)
        self.assertIn('get out first', self.lines[0])

    def enter(self, vehicle):
        self.session.bridge.call('time', {'speed': 1})
        self.assertEqual(self.play('vehicle', vehicle, 'enter'), 0, self.lines)
        for _ in range(50):
            if self.session.bridge.call('state')['result']['player']['in_vehicle'] == vehicle: break
            time.sleep(.05)
        self.session.bridge.call('time', {'pause': True})

    def test_driving_a_truck_to_its_bay_to_a_building_and_to_a_stop(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.session.mock.state['player'].update(x=0., z=0.)
        self.assertEqual(self.play('buy', 'truck'), 0, self.lines)
        truck = self.session.mock.state['vehicles'][0]['id']
        # Only from the driver's seat, and a cart is pushed.
        self.assertEqual(self.play('drive', truck, 'bay:hells-kitchen'), 1, self.lines)
        self.assertIn(f'get in first (vehicle {truck} enter)', self.lines[0])
        self.enter(truck)
        self.assertEqual(self.play('drive', truck, 'bay:hells-kitchen', '--speed', '4'), 2, self.lines)
        # To the bay: out of the space, the ride, into the bay; the clock is put back as it was (paused).
        self.assertEqual(self.play('drive', truck, 'bay:hells-kitchen'), 0, self.lines)
        self.assertIn('pulled', self.lines[0])
        self.assertTrue(any(line.startswith('The drive: Complete: arrived') for line in self.lines), self.lines)
        self.assertIn('Parked in the bay.', self.lines)
        self.assertTrue(any('parked legally, in the bay at hells-kitchen' in line for line in self.lines), self.lines)
        self.assertTrue(self.session.mock.state['clock']['paused'])
        self.assertTrue(self.session.mock.state['vehicles'][0]['commissary']['in_bay'])
        # To a building: by the kerb, the hatch to the pavement.
        address = runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_city')['CITY'][3]['key']
        self.assertEqual(self.play('drive', truck, 'address:' + address), 0, self.lines)
        self.assertIn('Parked by the kerb.', self.lines)
        self.assertTrue(any('hatch to the pavement' in line for line in self.lines), self.lines)
        # To a stop of today's route; a stop that isn't there is the game's refusal.
        self.assertEqual(self.play('drive', truck, 'stop:1'), 1, self.lines)
        self.assertIn("has 0 stops on today's route (Monday); no stop 1", self.lines[0])
        self.session.bridge.call('route', {'op': 'add_stop', 'vehicle': truck, 'address': 'ba:street_secondavenue#8', 'days': ['monday']})
        self.assertEqual(self.play('drive', truck, 'stop:1'), 0, self.lines)
        self.assertIn('Parked by the kerb.', self.lines)
        # A stop in the truck's own commissary's bay is parked in the bay, not at the kerb nearby.
        self.session.mock.state['commissary']['spaces'].append({'site': 'hells-kitchen', 'vehicle': truck})
        bay_x, bay_z = runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_city')['bay_xy']('hells-kitchen')
        self.session.mock.plans[truck].append({'day': 'monday', 'address': 'bay', 'x': bay_x + 1.8, 'z': bay_z + 2.4, 'open': 1200, 'close': 1260})
        self.assertEqual(self.play('drive', truck, 'stop:2'), 0, self.lines)
        self.assertIn('Parked in the bay.', self.lines)
        # Self Driving refuses: the truck goes back where it stood, and the tool says so.
        before = dict(self.session.mock.state['vehicles'][0])
        self.session.mock.ride_refusal = 'no route to the destination'
        self.assertEqual(self.play('drive', truck, 'address:' + address), 0, self.lines)
        self.assertTrue(any('The drive failed: Self Driving refused the ride from 3 places on the lane: no route to the destination; the truck is back where it stood' in line
                            for line in self.lines), self.lines)
        after = self.session.mock.state['vehicles'][0]
        self.assertEqual((after['x'], after['z']), (before['x'], before['z']))
        # Without the self-driving mod there is no drive.
        self.session.mock.self_driving = False
        self.assertEqual(self.play('drive', truck, 'bay:hells-kitchen'), 1, self.lines)
        self.assertIn("HART. Self Driving isn't loaded", self.lines[0])

    def test_a_walk_from_a_stopped_clock_puts_back_its_speed(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.session.bridge.call('time', {'pause': True, 'speed': 4})
        self.assertEqual(self.play('walk', '400', '230'), 0, self.lines)
        clock = self.session.bridge.call('state')['result']['clock']
        self.assertEqual((clock['paused'], clock['speed']), (True, 4))
        # Refused by the game after the clock was started: put back all the same.
        self.assertEqual(self.play('walk', '--to', 'vehicle:no-such-cart'), 1, self.lines)
        clock = self.session.bridge.call('state')['result']['clock']
        self.assertEqual((clock['paused'], clock['speed']), (True, 4))

    def test_the_map_lists_its_filters_and_the_buildings_one_lights(self):
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('map', 'filters'), 0, self.lines)
        self.assertTrue(any(line.startswith('commissaries:') for line in self.lines), self.lines)
        # The Street food filters follow Core's rule: a business decides before its building (a liquor store in a
        # shop draws the night crowd, a recruitment agency in a special building draws offices).
        city = {b['key']: b for b in runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_city')['CITY']}
        for address, filter_ in (('ba:street_fifthavenue#44', 'nightlife'), ('ba:street_broadwaystreet#5', 'offices')):
            self.session.mock.state['player'].update(x=city[address]['x'], z=city[address]['z'])
            first = self.session.bridge.call('map', {'filter': filter_, 'limit': 5})['result']['buildings']
            self.assertIn(address, [x['address'] for x in first], filter_)
        self.assertEqual(self.play('map', 'commissaries', '--limit', '3'), 0, self.lines)
        self.assertIn('buildings lit by commissaries', self.lines[0])
        self.assertEqual(len(self.lines), 4)
        self.assertTrue(all('commissary' in line for line in self.lines[1:]), self.lines)

    def test_a_day_of_living_food_a_home_a_bed_and_sleep(self):
        city = {b['key']: b for b in runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_city')['CITY']}
        burger = next(k for k, b in city.items() if b.get('businessType') == 'ba:businesstype_fastfoodrestaurant')
        furniture = next(k for k, b in city.items() if b.get('businessType') == 'ba:businesstype_furniturestore')
        flat = next(k for k, b in city.items() if b.get('buildingType') == 'ba:buildingtype_residential' and not b.get('businessType'))
        self.session.mock.state['player'].update(x=city[burger]['x'], z=city[burger]['z'])
        self.session.bridge.call('save', {'name': 'FT playtest'})
        # Time runs the needs down: nothing tops them up.
        self.assertEqual(self.play('time', 'until', '+0', '23:00'), 0, self.lines)
        needs = self.session.mock.state['player']['needs']
        self.assertLess(needs['hunger'], 25)
        self.assertFalse(any('needs topped up' in e['text'] for e in self.session.mock.ring))
        self.assertEqual(self.play('look'), 0, self.lines)
        self.assertTrue(any('you are hungry' in line for line in self.lines), self.lines)
        self.assertTrue(any(line == 'Home: none.' for line in self.lines), self.lines)
        self.assertIn('  happiness: No home -20%', self.lines)
        # A burger: in through the door, order at the till, eat from the bag.
        self.session.bridge.call('time', {'speed': 1})
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == burger)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertIn(f"You're now inside {burger}.", self.lines)
        self.assertEqual(self.play('use', self.thing('Cash register')), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        self.ui_click(next(line for line in self.lines if 'Burger' in line).split('] ', 1)[1].split(' (option)')[0])
        money = self.session.mock.state['money']
        self.ui_click('Place order')
        self.assertEqual(self.session.mock.state['money'], money - 8)
        hunger = needs['hunger']
        self.assertEqual(self.play('eat'), 0, self.lines)
        self.assertGreater(needs['hunger'], hunger)
        self.assertEqual(self.play('eat'), 1, self.lines)
        self.assertIn('hands are empty', self.lines[0])
        self.assertEqual(self.play('walk', '--to', 'exit'), 0, self.lines)
        self.assertIn('You walked out of', self.lines[0])
        # A flat: its entrance offers it for rent.
        self.ride(flat)
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == flat)
        self.assertTrue(door['for_rent'])
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.ui_click('Rent building')
        self.assertEqual(self.session.mock.read_state()['player']['homes'], [flat])
        self.assertEqual(self.play('look'), 0, self.lines)
        self.assertIn('  happiness: First apartment +10%, 7 days left', self.lines)
        self.assertFalse(any('No home' in line for line in self.lines), self.lines)
        # A bed: bought in a shop, carried home in its box, set down on the floor.
        self.ride(furniture)
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == furniture)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertEqual(self.play('use', self.thing('King size bed')), 0, self.lines)
        self.assertEqual(self.play('place', '1', '1'), 1, self.lines)
        self.assertEqual(self.play('use', self.thing('Cash register')), 0, self.lines)
        self.ui_click('Pay')
        self.assertEqual(self.play('walk', '--to', 'exit'), 0, self.lines)
        self.ride(flat)
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == flat)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertEqual(self.play('place', '2', '3', '--rotation', '100'), 0, self.lines)
        self.assertIn('turned 90 degrees', self.lines[0])
        # Sleep: a click on the bed, the hours on the slider, Start; the time skip passes before the tool answers.
        needs['energy'] = 20.
        self.assertEqual(self.play('use', self.thing('Click to sleep')), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        slider = next(line for line in self.lines if '(slider' in line).split(']')[0][1:]
        # A text field (the bank's loan amount): ui type fills it; a slider refuses it.
        self.assertNotEqual(self.play('ui', 'type', slider, '20000'), 0, self.lines)
        self.session.mock.last_ui.append({'id': 99, 'text': 'Amount', 'kind': 'text field', 'path': 'Loan/Amount', 'value': ''})
        self.assertEqual(self.play('ui', 'type', '99', '20000'), 0, self.lines)
        self.assertIn('Amount: "20000".', self.lines[0])
        self.session.mock.last_ui.pop()
        self.assertEqual(self.play('ui', 'set', slider, '120'), 0, self.lines)
        day_time = self.session.mock.state['clock']['time']
        self.ui_click('Start sleeping')
        self.assertGreater(needs['energy'], 50)
        self.assertNotEqual(self.session.mock.state['clock']['time'], day_time)

    def test_the_licence_needs_the_course_and_a_call_to_the_city(self):
        city = {b['key']: b for b in runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_city')['CITY']}
        school = next(k for k, b in city.items() if b.get('businessType') == 'ba:businesstype_school')
        self.session.mock.state['player'].update(x=city[school]['x'], z=city[school]['z'])
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        # Without the course the City turns the licence down.
        self.assertEqual(self.play('call', 'list'), 0, self.lines)
        self.assertIn('The City of New York (the_city_of_new_york)', self.lines)
        self.assertEqual(self.play('call', 'the_city_of_new_york'), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        self.ui_click(next(line for line in self.lines if 'Buy a licence' in line).split('] ', 1)[1])
        self.assertIsNone(self.session.mock.state['licence']['until_day'])
        # The course: the school's Market Demands door, Food Safety, all its hours on the slider, Start.
        self.session.bridge.call('time', {'speed': 1})
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == school)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertEqual(self.play('use', self.thing('Market Demands')), 0, self.lines)
        self.ui_click('Food Safety Course')
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        slider = next(line for line in self.lines if '(slider' in line).split(']')[0][1:]
        self.assertEqual(self.play('ui', 'set', slider, '6'), 0, self.lines)
        money = self.session.mock.state['money']
        self.ui_click('Start')
        self.assertTrue(any(line.startswith('Time passed') for line in self.lines) or self.session.mock.state['licence']['course_done'], self.lines)
        self.assertTrue(self.session.mock.state['licence']['course_done'])
        self.assertEqual(self.session.mock.state['money'], money - 150)
        self.licence_by_phone()
        lic = self.session.mock.state['licence']
        self.assertTrue(lic['until_day'])
        self.assertEqual(lic['permits'], 1)

    def test_the_agency_recruits_for_the_fleet_after_the_hr_course(self):
        city = {b['key']: b for b in runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='mock_city')['CITY']}
        school = next(k for k, b in city.items() if b.get('businessType') == 'ba:businesstype_school')
        agency = next(k for k, b in city.items() if b.get('businessType') == 'ba:businesstype_recruitmentagency')
        self.session.mock.state['player'].update(x=city[agency]['x'], z=city[agency]['z'])
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.session.bridge.call('time', {'speed': 1})
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == agency)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        # Before the HR course the agency turns you away, as the game's does.
        self.assertEqual(self.play('use', self.thing('Receptionist')), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        self.assertTrue(any('Basic HR course' in line for line in self.lines), self.lines)
        self.ui_click('Hang up')
        self.assertEqual(self.play('walk', '--to', 'exit'), 0, self.lines)
        # The course at the school's Basic HR door.
        self.ride(school)
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == school)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertEqual(self.play('use', self.thing('Basic HR')), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        slider = next(line for line in self.lines if '(slider' in line).split(']')[0][1:]
        self.assertEqual(self.play('ui', 'set', slider, '4'), 0, self.lines)
        self.ui_click('Start')
        self.assertIn('basic_hr', self.session.mock.life['diplomas'])
        self.assertEqual(self.play('walk', '--to', 'exit'), 0, self.lines)
        # Still no business and no vehicle: the agency says so.
        self.ride(agency)
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == agency)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertEqual(self.play('use', self.thing('Receptionist')), 0, self.lines)
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        self.assertTrue(any('business of your own' in line for line in self.lines), self.lines)
        self.ui_click('Hang up')
        # With a cart, the fleet is in the form: business, skill, part or full time, the deadline, then Confirm.
        self.assertEqual(self.play('walk', '--to', 'exit'), 0, self.lines)
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        door = next(t for t in self.session.bridge.call('use', {})['result']['things'] if t.get('address') == agency)
        self.assertEqual(self.play('use', str(door['id'])), 0, self.lines)
        self.assertEqual(self.play('use', self.thing('Receptionist')), 0, self.lines)
        self.ui_click('Street food fleet')
        self.ui_click('Confirm')
        self.assertEqual(self.session.mock.dialog, 'agency_form')
        self.ui_click('Customer Service')
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        self.assertTrue(any('Part time (toggle, off)' in line for line in self.lines), self.lines)
        self.ui_click('Part time')
        self.assertEqual(self.play('ui', 'list'), 0, self.lines)
        deadline = next(line for line in self.lines if 'Deadline (slider' in line).split(']')[0][1:]
        self.assertEqual(self.play('ui', 'set', deadline, '2'), 0, self.lines)
        money = self.session.mock.state['money']
        self.ui_click('Confirm')
        self.assertEqual(self.session.mock.state['money'], money - 300)
        t = self.session.mock.read_state()['transactions_today'][-1]
        self.assertEqual(t['type'], 'ba:transaction_recruitmentcampaign')
        # The candidate turns up over the next days, unassigned; hired on the phone, it can join the cart's crew.
        self.assertEqual(self.play('walk', '--to', 'exit'), 0, self.lines)
        self.assertEqual(self.play('time', 'until', '+1', '12:00'), 0, self.lines)
        self.assertTrue(any('Candidate received' in e['text'] for e in self.session.mock.ring))
        candidate = self.session.mock.life['candidates'][0]['name']
        self.assertEqual(self.play('phone', 'employees'), 0, self.lines)
        self.ui_click('Hire ' + candidate)
        self.ui_click('Close')
        employee = self.session.mock.read_state()['employees'][0]['id']
        cart = self.session.mock.state['vehicles'][0]['id']
        self.assertEqual(self.play('crew', 'assign', cart, employee), 0, self.lines)

    def test_a_taxi_charges_its_fare_and_takes_its_time(self):
        self.session.mock.state['player'].update(x=1440., z=420.)
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('go', 'ba:street_thirdstreet#6'), 1, self.lines)
        self.assertIn('No taxi is waiting', self.lines[0])
        money, minute = self.session.mock.state['money'], self.session.mock.absolute
        self.ride('ba:street_thirdstreet#6')
        self.assertLess(self.session.mock.state['money'], money)
        self.assertGreater(self.session.mock.absolute, minute)
        t = self.session.mock.read_state()['transactions_today'][-1]
        self.assertEqual(t['type'], 'ba:transaction_taxiride')

    def test_controls_refuse_outside_a_playtest_save_and_past_the_speed_cap(self):
        self.assertEqual(self.play('buy', 'cart'), 2)
        self.assertIn("starts with 'FT playtest'", self.lines[0])
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('time', 'speed', '60'), 2)
        self.assertEqual(self.play('save', 'FT mine'), 2)
        self.assertEqual(self.play('load', 'FT mock'), 2)
        self.assertEqual(self.play('vehicle', 'cart-1', 'teleport'), 2)

    def test_a_blind_week_start_with_the_referee_watching(self):
        # The fresh character's home is in Hell's Kitchen.
        self.session.mock.state['player'].update(x=1440., z=420.)
        self.session.bridge.call('save', {'name': 'FT playtest'})
        self.assertEqual(self.play('places', '--radius', '500'), 0)
        self.assertFalse(any('demand' in line or 'appetite' in line for line in self.lines))
        commissary = next(line for line in self.lines if 'commissary' in line)
        address = commissary.split()[2]
        self.ride(address)
        self.assertEqual(self.play('buy', 'cart'), 0, self.lines)
        cart = self.session.mock.state['vehicles'][0]['id']
        self.assertEqual(self.play('vehicle', cart, 'rent_bay'), 0, self.lines)
        with self.session.mock.lock:
            self.session.mock.state['licence'].update(course_done=True)
        self.licence_by_phone()
        employee = self.hire_candidate()
        self.assertEqual(self.play('crew', 'assign', cart, employee), 0, self.lines)
        self.assertEqual(self.play('order', 'hells-kitchen', 'ba:itemname_hotdoggrill', '1', 'Hotdog', '1'), 0, self.lines)
        self.assertEqual(self.play('route', 'add', cart, 'ba:street_thirdstreet#6'), 0, self.lines)
        self.assertEqual(self.play('look'), 0)
        self.assertTrue(any('On order' in line for line in self.lines))

        referee_bridge = pt_bridge(self.session)
        referee = pt.Run(referee_bridge, Path(self.folder.name) / 'w', log=lambda *_: None)
        import threading
        result = {}
        watcher = threading.Thread(target=lambda: result.update(code=referee.watch(self.stop, poll_s=.05, polls=400)))
        watcher.start()
        self.assertEqual(self.play('time', 'until', '+1', '08:05'), 0, self.lines)
        self.assertEqual(self.play('vehicle', cart, 'enter'), 0, self.lines)  # the walk there waits for the clock
        self.assertEqual(self.play('time', 'speed', '1'), 0)
        import time as clock
        deadline = clock.time() + 5
        while self.session.mock.state['player']['in_vehicle'] != cart and clock.time() < deadline: clock.sleep(.02)
        for argv in (('vehicle', cart, 'take', 'ba:itemname_hotdoggrill'), ('vehicle', cart, 'restock'), ('vehicle', cart, 'exit'),
                     ('time', 'pause'), ('crew', 'auto', cart), ('time', 'until', '+1', '06:00')):
            self.assertEqual(self.play(*argv), 0, (argv, self.lines))
        clock.sleep(.5)
        self.assertFalse(Path(self.stop).exists())
        # Stock that vanishes behind the player's back: the referee stops the game and says why.
        with self.session.mock.lock:
            line = next(s for s in self.session.mock.state['commissary']['stock'] if s['key'] == HOTDOG)
            line['amount'] -= 3
        self.assertEqual(self.play('time', 'until', '+1', '06:00'), 0)
        watcher.join(30)
        self.assertEqual(result.get('code'), 2)
        reason = Path(self.stop).read_text()
        self.assertIn('stock', reason)
        self.assertTrue(self.session.mock.state['clock']['paused'])
        self.assertEqual(self.play('time', 'speed', '5'), 2)
        self.assertIn('referee stopped', self.lines[0])
        ledger = pt.read_ledger(Path(self.folder.name) / 'w' / 'ledger.csv')
        self.assertTrue(ledger)


def pt_bridge(session):
    """A second client on the same mock, as the referee runs its own beside the tester's."""
    from ftlib.bridge import Bridge
    from offline_transport import MemoryHTTP
    bridge = Bridge(session.url)
    bridge.http = MemoryHTTP(session.mock)
    return bridge

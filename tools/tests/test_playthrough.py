import copy
import csv
import json
from pathlib import Path
import runpy
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'tests'))
from ftlib import playthrough as pt
from offline_transport import OfflineSession, NAMESPACE

PLAN = TOOLS.parent / 'tests/game/playthrough/phase-a.json'
MAIN = runpy.run_path(str(TOOLS / 'playthrough'), run_name='playthrough_test')['main']
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
        with self.assertRaises(pt.Violation) as caught:
            pt.check_day_books(2, books, summary(2, sales=26.0, wages=100.0, commissary_rent=41.1), 1500_00, 15000_00)
        self.assertIn('wages', caught.exception.message)
        with self.assertRaises(pt.Violation):
            pt.check_day_books(2, books, day, 1500_00, 0)

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

    def test_error_lines_stop_the_run(self):
        pt.check_events([{'kind': 'log', 'text': 'Crew sales paid in: …'}])
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
        self.assertEqual(set(meta['vehicles']), {'truck', 'cart'})
        self.assertEqual(sorted(p['cents'] for p in meta['purchases']), [1200_00, 15000_00])
        state = self.session.mock.read_state()
        types = {t['type'] for t in state['transactions_today']}
        self.assertIn('ba:transaction_vehiclebought', types)
        self.assertEqual(self.main('run', '--run', 'a', '--days', '3', '--reload-every', '2'), 0, self.lines)
        self.assertTrue(any("reloaded 'FT playtest d3'" in line for line in self.lines))
        ledger = pt.read_ledger(run / 'ledger.csv')
        self.assertEqual(sorted({int(r['day']) for r in ledger}), [1, 2, 3, 4])
        self.assertEqual(len(ledger), 4 * 3)
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

    def test_vehicle_buy_charges_the_dealers_price_as_the_games_purchase(self):
        money = self.mock.state['money']
        reply = self.mock.call('vehicle', {'op': 'buy', 'kind': 'truck'})
        self.assertEqual(set(reply), {'done', 'id', 'kind', 'price', 'money', 'notices', 'state'})
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
        for key in ('done', 'id', 'kind', 'price', 'money', 'notices', 'state'):
            self.assertIn(f'["{key}"]', self.source)

    def test_day_summary_shape_and_guard(self):
        v = self.mock.spawn('truck')
        reply = self.mock.call('day_summary', {})
        self.assertEqual(set(reply), {'day', 'today', 'in_summary', 'fleet', 'fees_paid_at', 'vehicles'})
        self.assertEqual(set(reply['fleet']), {'revenue', 'stock_cost', 'licence', 'event_places', 'delivery_fees', 'profit'})
        row = reply['vehicles'][0]
        self.assertEqual(row['id'], v['id'])
        keys = {'id', 'kind', 'sales', 'catering', 'stock_cost', 'wages', 'parking', 'permit', 'commissary_rent', 'overnight_fines',
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
        self.refused('route', {'op': 'move', 'vehicle': v['id']}, "Unknown route op 'move': add_stop, paste_day.")
        for text in ('Give both \\"open\\" and \\"close\\", or neither for the default hours.', "Unknown route op '{op}': add_stop, paste_day.",
                     "Unknown day '{name}': monday to sunday.", 'no building at {address} in this city'):
            self.assertIn(text, self.source)
        for key in ('done', 'address', 'x', 'z', 'heading', 'distance', 'days', 'added', 'stops', 'problem'):
            self.assertIn(f'["{key}"]', self.source)
        self.mock.state['save'] = 'Non-FT mock'
        with self.assertRaises(ValueError): self.mock.call('route', {'op': 'add_stop', 'vehicle': v['id'], 'address': 'ba:street_thirdstreet#6'})

    def test_transactions_carry_the_balance_after_them(self):
        self.mock.call('vehicle', {'op': 'buy', 'kind': 'cart'})
        t = self.mock.read_state()['transactions_today'][-1]
        self.assertEqual(t['balance'], 20000 - 1200)
        self.assertIn('["balance"] = Math.Round(t.balance, 2)', self.source)


if __name__ == '__main__':
    unittest.main()

"""The AI playthrough runner (docs/ai-playtest-plan.md): Phase A's setup from a plan file, the day loop, the daily
ledger and the checks that stop a run at the first broken rule.

The checks are pure functions over snapshots (one per stop of the clock), so `check` can run them again offline from
a run folder. Money is reconciled in whole cents against the game's own transactions (`state.transactions_today`),
stock per food at each commissary and on each vehicle, moves from where each vehicle stands at 05:00."""
from collections import Counter
import csv
import json
import math
import os
from pathlib import Path
import re
import subprocess
import time

from .shots import ROOT

ECONOMY = json.loads((ROOT / 'data/economy.json').read_text())
FOODS = set(ECONOMY['products'])
WEEK = ('monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday')
SAVE_PREFIX = 'FT playtest'
# The dealer's prices, as the bridge's vehicle buy charges them (the vehicle type assets).
PRICES = {kind: float(re.search(r'^\s*price: ([0-9.]+)', (ROOT / f'unity/FoodTrucks/Vehicles/Food{kind.title()}.asset').read_text(), re.M).group(1))
          for kind in ('truck', 'cart')}
SALES_TYPES = ('foodtrucks:transaction_sale', 'foodtrucks:transaction_crew_sales', 'foodtrucks:transaction_catering')
WAGE_TYPE = 'ba:transaction_unassignedwage'
RENT_TYPE = 'foodtrucks:transaction_commissary_rent'
ORDER_TYPE = 'foodtrucks:transaction_commissary_order'
FINE_TYPE = 'foodtrucks:transaction_overnight_fine'
PURCHASE_TYPE = 'ba:transaction_vehiclebought'
# The game keeps money and transaction amounts as 32-bit floats: each booking can round by a fraction of a cent at a
# balance of tens of thousands. A cent of slack, plus a fifth of a cent per transaction booked in between.
SLACK_CENTS = 1
SLACK_PER_TRANSACTION = .2
BALANCE_BAND = .25
# Money columns of the ledger, written in dollars with cents.
MONEY_FIELDS = ('revenue', 'stock_cost', 'wages', 'commissary_rent', 'delivery_fees', 'fines', 'purchases', 'money_before', 'money_after',
                'game_day_total', 'game_other')
LEDGER_FIELDS = ('day', 'vehicle', 'kind', 'stops_served', 'minutes_open', 'sold', 'revenue', 'stock_cost', 'wages',
                 'commissary_rent', 'delivery_fees', 'fines', 'purchases', 'stock_start', 'stock_end', 'loaded', 'deliveries',
                 'money_before', 'money_after', 'game_day_total', 'game_other')


class Violation(RuntimeError):
    """A broken rule: the run stops here."""
    def __init__(self, check, message, day=None, details=None):
        super().__init__(f'{check}: {message}')
        self.check, self.message, self.day, self.details = check, message, day, details or {}

    def as_dict(self):
        return {'check': self.check, 'day': self.day, 'message': self.message, 'details': self.details}


def cents(value):
    return int(round(float(value) * 100))


def money_text(c):
    return f"{'-' if c < 0 else ''}${abs(c) / 100:,.2f}"


def slack(count):
    return SLACK_CENTS + math.ceil(SLACK_PER_TRANSACTION * count)


# ---- snapshots ----

def snapshot(state, summaries, events=(), label=''):
    """The parts of `state` the checks use, with the day summaries for today and yesterday (day_summary replies)."""
    clock = state.get('clock') or {}
    vehicles = []
    for v in state.get('vehicles') or []:
        if 'error' in v and 'kind' not in v:
            raise Violation('state', f"the bridge couldn't read vehicle {v.get('id')}: {v['error']}")
        commissary = v.get('commissary') or {}
        vehicles.append({
            'id': v['id'], 'kind': v['kind'], 'x': v.get('x'), 'z': v.get('z'), 'open': bool(v.get('open')),
            'stock': {s['key']: s['amount'] for s in v.get('stock') or [] if s.get('key') in FOODS and s.get('amount')},
            'pieces': [p['item'] for p in v.get('pieces') or []],
            'site': commissary.get('site'), 'in_bay': bool(commissary.get('in_bay')), 'bay_site': commissary.get('bay_site'),
            'crew': list(v.get('crew') or []),
            'week': ((v.get('route') or {}).get('week')) or [],
            'problem': (v.get('route') or {}).get('problem'),
            'legal': v.get('legal'),
        })
    commissary = state.get('commissary') or {}
    stock = {}
    for line in commissary.get('stock') or []:
        if line.get('key') in FOODS and line.get('amount'):
            site = stock.setdefault(line['site'], {})
            site[line['key']] = site.get(line['key'], 0) + line['amount']
    return {
        'label': label, 'save': state.get('save'), 'day': clock.get('day'), 'time': clock.get('time'),
        'player_with': ((state.get('player') or {}).get('in_vehicle') or (state.get('player') or {}).get('at_hatch')),
        'weekday': (clock.get('weekday') or '').lower(), 'money': cents(state.get('money') or 0),
        'vehicles': vehicles, 'stock': stock,
        'orders': [{k: o.get(k) for k in ('n', 'site', 'item', 'units', 'total', 'due_day')} for o in commissary.get('orders') or []],
        'spaces': {s['vehicle']: s['site'] for s in commissary.get('spaces') or []},
        'transactions': [{k: t.get(k) for k in ('type', 'label', 'amount', 'balance', 'day', 'time')} for t in state.get('transactions_today') or []],
        'summaries': {str(s['day']): s for s in summaries},
        'events': list(events),
    }


def transaction_key(t):
    return (t['day'], t['time'], t['type'], cents(t['amount']), t.get('balance'), t.get('label'))


def new_transactions(prev, cur):
    """Transactions in `cur` not already in `prev`. Both hold the game's transactions of their today and yesterday; a
    transaction of a day both cover counts once (as a multiset: two equal ones are two)."""
    if cur['day'] - prev['day'] > 1:
        raise Violation('money', f"the snapshots are {cur['day'] - prev['day']} days apart: state only lists today's and yesterday's transactions, "
                                 'so a day in between can\'t be reconciled', cur['day'])
    seen = Counter(transaction_key(t) for t in prev['transactions'] if t['day'] >= cur['day'] - 1)
    fresh = []
    for t in cur['transactions']:
        key = transaction_key(t)
        if seen[key]:
            seen[key] -= 1
        else:
            fresh.append(t)
    return fresh


# ---- checks ----

def check_money(prev, cur):
    """The money change between two snapshots is the sum of the game's transactions booked in between."""
    fresh = new_transactions(prev, cur)
    booked = sum(cents(t['amount']) for t in fresh)
    change = cur['money'] - prev['money']
    if abs(change - booked) > slack(len(fresh)):
        by_type = Counter()
        for t in fresh: by_type[t['type']] += cents(t['amount'])
        raise Violation('money', f"money changed by {money_text(change)} from day {prev['day']} {prev['time']} to day {cur['day']} {cur['time']}, "
                                 f"but the game booked {money_text(booked)} in {len(fresh)} transactions "
                                 f"(off by {money_text(change - booked)})", cur['day'],
                        {'by_type': {k: v / 100 for k, v in by_type.items()}})
    return fresh


OUR_TYPES = SALES_TYPES + (WAGE_TYPE, RENT_TYPE, ORDER_TYPE, FINE_TYPE, PURCHASE_TYPE)


def check_day_books(day, transactions, summary, delivered_goods, purchases):
    """One finished day: the mod's own lines (day_summary) against the game's transactions of those types booked into
    that day. The game's other spending (the apartment, taxes, other hires' wages, other vehicles) isn't ours: wages and
    vehicle purchases may be above ours, which is returned as a note; below ours, or any other line off, stops the run.
    `delivered_goods`: cents of goods in the orders delivered that day; `purchases`: cents of vehicles the runner bought."""
    notes = []
    of_day = [t for t in transactions if t['day'] == day]
    def booked(types):
        rows = [t for t in of_day if t['type'] in types]
        return -sum(cents(t['amount']) for t in rows) if types != SALES_TYPES else sum(cents(t['amount']) for t in rows), len(rows)
    vehicles = summary['vehicles']
    lines = [
        ('sales', SALES_TYPES, sum(cents(v['sales']) + cents(v['catering']) for v in vehicles), False),
        ('wages', (WAGE_TYPE,), sum(cents(v['wages']) for v in vehicles), True),
        ('commissary rent', (RENT_TYPE,), sum(cents(v['commissary_rent']) for v in vehicles), False),
        ('overnight fines', (FINE_TYPE,), sum(cents(v['overnight_fines']) for v in vehicles), False),
        ('commissary orders', (ORDER_TYPE,), delivered_goods + cents(summary['fleet']['delivery_fees']), False),
        ('vehicle purchases', (PURCHASE_TYPE,), purchases, True),
    ]
    for name, types, expected, others in lines:
        got, count = booked(types)
        if others and got > expected + slack(count):
            notes.append(f"day {day}: the game booked {money_text(got)} of {name}, {money_text(got - expected)} more than the crews' and "
                         "the runner's (another hire or vehicle of the player's)")
        elif abs(got - expected) > slack(count):
            raise Violation('books', f"day {day}: the game booked {money_text(got)} of {name} in {count} transactions, "
                                     f"the mod's ledger and the runner's records say {money_text(expected)}", day)
    return notes


def sold_between(prev, cur, vehicle_id):
    """Units of each food a vehicle sold between two snapshots, from the day summaries' sold counts (calendar days)."""
    def units(snap, day):
        summary = snap['summaries'].get(str(day))
        if summary is None: return None
        v = next((x for x in summary['vehicles'] if x['id'] == vehicle_id), None)
        if v is None: return {}
        if v.get('sold') is None: return None
        return {s['item']: s['units'] for s in v['sold']}
    total = Counter()
    for day in range(prev['day'], cur['day'] + 1):
        now = units(cur, day)
        if now is None:
            raise Violation('stock', f"day {day}'s sold counts for {vehicle_id} aren't in the day {cur['day']} snapshot", cur['day'])
        total.update(now)
    before = units(prev, prev['day'])
    if before is None:
        raise Violation('stock', f"day {prev['day']}'s sold counts for {vehicle_id} aren't in the day {prev['day']} snapshot", prev['day'])
    total.subtract(before)
    return {k: n for k, n in total.items() if n}


def delivered_between(prev, cur, placed, refused):
    """Orders delivered between two snapshots: those waiting at `prev` or placed since (`placed`), gone at `cur`, and not
    refused. Returns [(order)], each with site, item, units, total."""
    waiting = {o['n']: o for o in prev['orders']}
    for o in placed: waiting.setdefault(o['n'], o)
    still = {o['n'] for o in cur['orders']}
    return [o for n, o in sorted(waiting.items()) if n not in still and n not in refused]


def check_stock(prev, cur, delivered):
    """Per food: what each commissary and its vehicles held, plus deliveries, less sales, is what they hold now; and no
    vehicle has less than it started with less what it sold (crews only load, never give back)."""
    groups = {}
    sold_by = {}
    for v in cur['vehicles']:
        before = next((x for x in prev['vehicles'] if x['id'] == v['id']), None)
        start = before['stock'] if before else {}
        sold = sold_between(prev, cur, v['id']) if before else {}
        sold_by[v['id']] = sold
        for food in set(start) | set(v['stock']) | set(sold):
            loaded = v['stock'].get(food, 0) - start.get(food, 0) + sold.get(food, 0)
            if loaded < 0:
                raise Violation('stock', f"{v['kind']} {v['id']}: {food} went from {start.get(food, 0)} to {v['stock'].get(food, 0)} "
                                         f"with {sold.get(food, 0)} sold, so {-loaded} vanished", cur['day'])
        site = cur['spaces'].get(v['id'])
        groups.setdefault(site, []).append((start, v['stock'], sold))
    sites = set(prev['stock']) | set(cur['stock']) | {o['site'] for o in delivered}
    for site in sites | set(groups):
        foods = set(prev['stock'].get(site, {})) | set(cur['stock'].get(site, {})) | {o['item'] for o in delivered if o['site'] == site}
        for start, end, sold in groups.get(site, []): foods |= set(start) | set(end) | set(sold)
        for food in foods & FOODS:
            before = prev['stock'].get(site, {}).get(food, 0) + sum(g[0].get(food, 0) for g in groups.get(site, []))
            after = cur['stock'].get(site, {}).get(food, 0) + sum(g[1].get(food, 0) for g in groups.get(site, []))
            came = sum(o['units'] for o in delivered if o['site'] == site and o['item'] == food)
            went = sum(g[2].get(food, 0) for g in groups.get(site, []))
            if before + came - went != after:
                where = f'commissary {site} and its vehicles' if site else 'the vehicles without a commissary space'
                raise Violation('stock', f"{food} at {where}: {before} held + {came} delivered - {went} sold = {before + came - went}, "
                                         f"but {after} are there now ({after - (before + came - went):+d})", cur['day'])
    return sold_by


def check_moves(snap, crewed_only=False):
    """At 05:00 each vehicle stands in its own commissary's bay, or at a stop of yesterday's route that runs past
    midnight, and none is open otherwise. A route Core finds a problem with is returned as a note."""
    yesterday = WEEK[(WEEK.index(snap['weekday']) - 1) % 7] if snap['weekday'] in WEEK else None
    notes = [f"day {snap['day']}: {v['kind']} {v['id']}: Core's route check says {v['problem']}" for v in snap['vehicles'] if v['problem']]
    for v in snap['vehicles']:
        if crewed_only and (not v['crew'] or not any(d['stops'] for d in v['week']) or snap.get('player_with') == v['id']):
            continue  # the player's own vehicle goes where the player takes it
        site = snap['spaces'].get(v['id'])
        if site and v['in_bay'] and v['bay_site'] == site and not v['open']:
            continue
        overnight = [s for d in v['week'] if d['day'] == yesterday for s in d['stops'] if _minute(s['close']) < _minute(s['open'])]
        if any(v['x'] is not None and math.hypot(v['x'] - s['x'], v['z'] - s['z']) <= 30 for s in overnight):
            continue
        where = f"at x {v['x']}, z {v['z']}" if v['x'] is not None else 'off the map'
        raise Violation('moves', f"{v['kind']} {v['id']} is {where}{', open' if v['open'] else ''} at day {snap['day']} {snap['time']}, "
                                 f"not in its bay at {site or 'its commissary (it has no space)'} or at an overnight stop", snap['day'])
    return notes


def check_events(events):
    for e in events:
        if e.get('kind') in ('error', 'unity_error') or 'ERROR: ' in e.get('text', ''):
            raise Violation('log', f"day {e.get('day')} {e.get('time')}: {e['text']}", e.get('day'), {'event': e})


def _minute(text):
    h, m = text.split(':')
    return int(h) * 60 + int(m)


def refused_orders(events):
    out = set()
    for e in events:
        m = re.search(r'Commissary delivery: order (\d+), .* not delivered', e.get('text', ''))
        if m: out.add(int(m.group(1)))
    return out


# ---- the ledger ----

def ledger_rows(day, summary, prev, cur, delivered, purchases, transactions):
    """Rows for one finished day: one per vehicle and one for the fleet's own lines. Stock start and end are the vehicle's
    at the two snapshots around the day (05:00 to 05:00 in the day loop)."""
    rows = []
    game_total = sum(cents(t['amount']) for t in transactions if t['day'] == day)
    # Everything the game booked that day that isn't one of the mod's lines, a crew wage or a vehicle bought.
    other = sum(cents(t['amount']) for t in transactions if t['day'] == day and t['type'] not in OUR_TYPES)
    for v in summary['vehicles']:
        start = next((x['stock'] for x in prev['vehicles'] if x['id'] == v['id']), {})
        end = next((x['stock'] for x in cur['vehicles'] if x['id'] == v['id']), {})
        sold = {s['item']: s['units'] for s in v['sold'] or []}
        loaded = {f: end.get(f, 0) - start.get(f, 0) + sold.get(f, 0) for f in set(start) | set(end) | set(sold)}
        rows.append({
            'day': day, 'vehicle': v['id'], 'kind': v['kind'], 'stops_served': v['stops_served'], 'minutes_open': v['minutes_open'],
            'sold': _items(sold), 'revenue': cents(v['sales']) + cents(v['catering']), 'stock_cost': cents(v['stock_cost']),
            'wages': cents(v['wages']), 'commissary_rent': cents(v['commissary_rent']), 'delivery_fees': 0,
            'fines': cents(v['overnight_fines']), 'purchases': sum(p['cents'] for p in purchases if p['id'] == v['id'] and p['day'] == day),
            'stock_start': _items(start), 'stock_end': _items(end), 'loaded': _items({k: n for k, n in loaded.items() if n}),
            'deliveries': '', 'money_before': prev['money'], 'money_after': cur['money'], 'game_day_total': game_total, 'game_other': other,
        })
    rows.append({
        'day': day, 'vehicle': 'fleet', 'kind': '', 'stops_served': '', 'minutes_open': '', 'sold': '',
        'revenue': cents(summary['fleet']['revenue']), 'stock_cost': cents(summary['fleet']['stock_cost']), 'wages': '', 'commissary_rent': '',
        'delivery_fees': cents(summary['fleet']['delivery_fees']), 'fines': '', 'purchases': sum(p['cents'] for p in purchases if p['day'] == day),
        'stock_start': '', 'stock_end': '', 'loaded': '',
        'deliveries': _items(Counter({f"{o['site']}/{o['item']}": o['units'] for o in delivered})),
        'money_before': prev['money'], 'money_after': cur['money'], 'game_day_total': game_total, 'game_other': other,
    })
    return rows


def _items(counts):
    return ';'.join(f'{k}:{v}' for k, v in sorted(counts.items()))


def append_ledger(path, rows):
    path = Path(path)
    new = not path.exists()
    with path.open('a', newline='') as f:
        writer = csv.DictWriter(f, LEDGER_FIELDS)
        if new: writer.writeheader()
        for row in rows:
            writer.writerow({k: (f'{v / 100:.2f}' if k in MONEY_FIELDS and isinstance(v, int) else v) for k, v in row.items()})


def read_ledger(path):
    path = Path(path)
    if not path.exists(): return []
    with path.open(newline='') as f:
        return list(csv.DictReader(f))


# ---- the balance comparison ----

def balance_plan(snap, plan):
    """What tools/balance prices: each vehicle's foods and the stops of its week as the game holds them now."""
    names = {v['id']: v.get('name') for v in plan.get('vehicles', []) if v.get('id')}
    out = []
    for v in snap['vehicles']:
        menu = sorted(set(v['stock']) | {f for pv in plan.get('vehicles', []) if pv.get('id') == v['id'] for f in pv.get('foods', [])})
        out.append({'id': v['id'], 'name': names.get(v['id']), 'kind': v['kind'], 'menu': menu, 'servers': len(v['crew']),
                    'week': [{'day': d['day'], 'stops': [{'x': s['x'], 'z': s['z'], 'open': _minute(s['open']),
                                                         'close': _minute(s['close']) + (1440 if _minute(s['close']) <= _minute(s['open']) else 0)}
                                                        for s in d['stops']]} for d in v['week']]})
    return {'vehicles': out}


def run_balance(plan_file, out_file):
    """Runs the balance calculator's playthrough report. Returns its JSON, or raises with what went wrong."""
    command = ['dotnet', 'run', '--project', str(ROOT / 'tools/balance'), '-c', 'Release', '--',
               '--reports', 'playthrough', '--plan', str(plan_file), '--json-out', str(out_file)]
    result = subprocess.run(command, capture_output=True, text=True, cwd=ROOT / 'tools/balance')
    if result.returncode:
        raise RuntimeError(f'tools/balance failed ({result.returncode}): {result.stderr.strip()[-500:]}')
    return json.loads(Path(out_file).read_text())


def compare_balance(ledger, expected, from_day=1):
    """Per vehicle: revenue over the days run (from `from_day`, the first day the crews had shifts) against the
    calculator's for the same weekdays. Outside ±25% is a finding, never a failure."""
    rows = []
    actual = Counter()
    weekdays = {}
    for r in ledger:
        if r['vehicle'] == 'fleet' or int(r['day']) < from_day: continue
        actual[r['vehicle']] += cents(r['revenue'])
        weekdays.setdefault(r['vehicle'], []).append(int(r['day']))
    for v in expected.get('vehicles', []):
        days = weekdays.get(v['id'], [])
        want = sum(cents(v['days'][WEEK[(d - 1) % 7]]['revenue']) for d in days if WEEK[(d - 1) % 7] in v['days'])
        got = actual.get(v['id'], 0)
        gap = None if want == 0 else (got - want) / want
        rows.append({'vehicle': v['id'], 'kind': v['kind'], 'days': len(days), 'actual': got, 'expected': want, 'gap': gap,
                     'outside': gap is None or abs(gap) > BALANCE_BAND})
    return rows


# ---- the run against the bridge ----

class Run:
    """One playthrough's folder (build/playtest/<run>) and its bridge session."""

    def __init__(self, bridge, folder, speed=20, day_timeout_s=900, log=print, reload_every=7):
        self.bridge = bridge
        self.reload_every = reload_every
        self.folder = Path(folder)
        self.speed = speed
        self.day_timeout_s = day_timeout_s
        self.log = log
        self.meta_path = self.folder / 'run.json'
        self.meta = json.loads(self.meta_path.read_text()) if self.meta_path.exists() else None

    # -- files --
    def save_meta(self):
        self.folder.mkdir(parents=True, exist_ok=True)
        self.meta_path.write_text(json.dumps(self.meta, indent=2))

    def snapshots(self):
        path = self.folder / 'snapshots.jsonl'
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def add_snapshot(self, snap):
        with (self.folder / 'snapshots.jsonl').open('a') as f:
            f.write(json.dumps(snap) + '\n')

    def record_violation(self, violation):
        self.meta.setdefault('findings', []).append(violation.as_dict())
        self.meta['stopped'] = violation.as_dict()
        self.save_meta()

    # -- bridge helpers --
    def call(self, cmd, args=None, timeout_s=30):
        return self.bridge.call(cmd, args or {}, timeout_s)['result']

    def state(self):
        return self.call('state')

    def guard(self):
        health = self.bridge.health()
        save = health.get('save') or ''
        if health.get('scene') != 'city' or not save.startswith(SAVE_PREFIX):
            raise Violation('guard', f"the loaded save is '{save}' ({health.get('scene')}); the playthrough runs only in a save whose name "
                                     f"starts with '{SAVE_PREFIX}' (a fresh character's, made by hand)")
        return health

    def events(self):
        batch = self.bridge.events(self.meta.get('events_next', 1))
        if batch.get('dropped'):
            raise Violation('log', f"{batch['dropped']} bridge events were lost before the runner read them")
        self.meta['events_next'] = batch['next']
        with (self.folder / 'events.jsonl').open('a') as f:
            for e in batch['events']: f.write(json.dumps(e) + '\n')
        return batch['events']

    def take(self, label):
        state = self.state()
        day = state['clock']['day']
        summaries = [self.call('day_summary', {'day': d}) for d in (day - 1, day) if d >= 1]
        events = self.events()
        snap = snapshot(state, summaries, events, label)
        self.add_snapshot(snap)
        return snap

    def done(self, reply, what):
        if not reply.get('done'):
            raise Violation('refused', f"{what} was refused: {reply.get('why') or '; '.join(reply.get('notices') or []) or 'no reason given'}")
        return reply

    def wait(self, path, condition, timeout_s=60):
        return self.bridge.wait_state(path, condition, timeout_s, poll_s=.2)

    def run_clock_to(self, day, clock):
        self.call('time', {'run_until': {'day': day, 'time': clock}, 'speed': self.speed})
        self.wait('clock.arrived_at', {'equals': f'day {day} {clock}'}, self.day_timeout_s)

    # -- setup --
    def setup(self, plan):
        try:
            return self._setup(plan)
        except Violation as violation:
            if self.meta is not None and self.meta.get('stopped') is None: self.record_violation(violation)
            raise

    def _setup(self, plan):
        if self.meta is not None:
            raise Violation('guard', f'{self.folder} already holds a run; pick another --run name')
        health = self.guard()
        self.meta = {'plan': plan, 'save': health['save'], 'vehicles': {}, 'purchases': [], 'orders': [], 'events_next': 1,
                     'checked_days': [], 'findings': [], 'notes': [], 'stopped': None, 'started': time.strftime('%Y-%m-%d %H:%M:%S')}
        self.folder.mkdir(parents=True, exist_ok=True)
        self.save_meta()
        self.call('time', {'pause': True})
        state = self.state()
        if state['vehicles']:
            raise Violation('guard', f"the save already has {len(state['vehicles'])} food vehicles; Phase A starts from a fresh character")
        if plan.get('starting_money') is not None:
            # Optional: a starting balance set before the books start (no transaction). Phase A keeps the character's own.
            self.call('money', {'set': plan['starting_money']})
        before = self.take('setup start')
        self.log(f'Setup on day {before["day"]} {before["time"]}: {len(plan["vehicles"])} vehicles at {plan["commissary"]}, money {money_text(before["money"])}.')
        due = self.establish(plan['vehicles'], plan['orders'], before['day'], licence=plan.get('licence', 'debug') == 'debug')
        self.save_game()
        # The order arrives at 08:00 the next morning: fit the equipment and fill up in the bay, then plan the shifts.
        self.run_clock_to(due, '08:05')
        self.ready(plan['vehicles'])
        after = self.take('setup end')
        self.meta['first_run_day'] = after['day']
        self.check_window(before, after)
        self.save_game()
        self.log(f'Setup done on day {after["day"]} {after["time"]}: money {money_text(after["money"])}.')
        return after

    def establish(self, vehicles, orders, day, licence=False):
        """Buys the vehicles, rents their spaces, hires their crews, orders the stock and adds the stops. Returns the
        day the order arrives."""
        site = self.meta['plan']['commissary']
        self.call('teleport', {'who': 'player', 'place': f'commissary:{site}'})
        for v in vehicles:
            reply = self.done(self.call('vehicle', {'op': 'buy', 'kind': v['kind']}), f"buying the {v['name']} ({v['kind']})")
            self.meta['vehicles'][v['name']] = reply['id']
            self.meta['purchases'].append({'id': reply['id'], 'kind': v['kind'], 'cents': cents(reply['price']), 'day': day})
            self.log(f"  bought the {v['name']}: {reply['id']} for {money_text(cents(reply['price']))}")
            self.save_meta()
        for v in vehicles:
            vid = self.meta['vehicles'][v['name']]
            self.call('teleport', {'who': vid, 'place': f'bay:{site}', 'clear': True})
            self.done(self.call('vehicle', {'id': vid, 'op': 'rent_bay'}), f"renting a space at {site} for the {v['name']}")
        if licence:
            # The debug action grants the vendor licence and a permit for every food vehicle, free of charge (no transaction).
            self.call('debug', {'action': 'licence'})
        for v in vehicles:
            vid = self.meta['vehicles'][v['name']]
            for _ in range(v.get('crew', 1)):
                self.call('debug', {'action': 'hire'})
                free = self.state()['employees']
                if not free: raise Violation('refused', "the debug hire left no unassigned hire to put on the crew")
                self.done(self.call('crew', {'op': 'assign', 'vehicle': vid, 'employee_id': free[0]['id']}), f"putting {free[0]['name']} on the {v['name']}")
        placed = []
        batch, boxes = [], 0
        for line in list(orders) + [None]:
            if line is None or boxes + line['boxes'] > 10:
                if batch:
                    reply = self.call('commissary', {'op': 'order', 'site': site, 'items': batch})
                    for o in reply['orders']:
                        placed.append({'n': o['n'], 'site': o['site'], 'item': o['item'], 'units': o['units'],
                                       'goods': cents(o['goods']), 'due_day': o['due_day']})
                batch, boxes = [], 0
                if line is None: break
            batch.append({'item': line['item'], 'boxes': line['boxes']})
            boxes += line['boxes']
        self.meta['orders'].extend(placed)
        self.save_meta()
        for v in vehicles:
            vid = self.meta['vehicles'][v['name']]
            for address in v['stops']:
                self.done(self.call('route', {'op': 'add_stop', 'vehicle': vid, 'address': address, 'days': 'all'}),
                          f"adding the stop by {address} to the {v['name']}'s week")
        return max((o['due_day'] for o in placed), default=day + 1)

    def ready(self, vehicles):
        """After the delivery: fit and fill each vehicle in the bay, then the crews' auto schedule."""
        for v in vehicles:
            self.fit(v, self.meta['vehicles'][v['name']])
        for v in vehicles:
            vid = self.meta['vehicles'][v['name']]
            self.done(self.call('crew', {'op': 'auto_schedule', 'vehicle': vid}), f"the auto schedule for the {v['name']}")
        self.call('time', {'pause': True})

    def grow(self, cur):
        """The plan's growth: once money less the reserve covers the next vehicle's price, buy it with its crew, stock and
        stops, as a player would. Returns True when it did."""
        growth = self.meta['plan'].get('growth') or {}
        if not growth.get('enabled') or self.meta.get('grown') is not None: return False
        vehicles = growth['vehicles']
        price = sum(cents(PRICES[v['kind']]) for v in vehicles)
        if cur['money'] - cents(growth.get('reserve', 0)) < price: return False
        self.log(f"  growth on day {cur['day']}: money {money_text(cur['money'])} covers {money_text(price)} with "
                 f"{money_text(cents(growth.get('reserve', 0)))} kept; buying {', '.join(v['name'] for v in vehicles)}.")
        due = self.establish(vehicles, growth.get('orders', []), cur['day'])
        self.meta['grown'] = {'day': cur['day'], 'due': due}
        self.save_meta()
        return True

    def fit(self, v, vid):
        """In the bay: the player gets in, takes each piece of equipment from the commissary's stock, fills up and parks."""
        self.call('teleport', {'who': 'player', 'place': f'vehicle:{vid}'})
        self.call('time', {'speed': 1})
        self.done(self.call('vehicle', {'id': vid, 'op': 'enter'}), f"getting into the {v['name']}")
        self.wait('player.in_vehicle', {'equals': vid}, 60)
        # The panel's takes work paused; only the Park button needs the clock running.
        self.call('time', {'pause': True})
        for item in v['equipment']:
            self.done(self.call('vehicle', {'id': vid, 'op': 'transfer', 'direction': 'take', 'item': item, 'units': 1}),
                      f"fitting {item} on the {v['name']}")
        self.done(self.call('vehicle', {'id': vid, 'op': 'restock'}), f"filling up the {v['name']}")
        self.call('time', {'speed': 1})
        self.done(self.call('vehicle', {'id': vid, 'op': 'exit'}), f"parking the {v['name']}")
        self.call('time', {'pause': True})

    def save_game(self, day=None):
        day = day or self.state()['clock']['day']
        name = f'{SAVE_PREFIX} d{day}'
        self.call('save', {'name': name}, 120)
        self.meta.setdefault('saves', []).append(name)
        self.save_meta()
        return name

    # -- the day loop --
    def run_days(self, days):
        try:
            return self._run_days(days)
        except Violation as violation:
            if self.meta is not None and self.meta.get('stopped') is None and violation.check != 'guard': self.record_violation(violation)
            raise

    def _run_days(self, days):
        if self.meta is None: raise Violation('guard', f'no run in {self.folder}: run setup first')
        if self.meta.get('stopped'): raise Violation('guard', f"this run stopped at {self.meta['stopped']['check']}: {self.meta['stopped']['message']}")
        self.guard()
        prev = self.snapshots()[-1]
        for _ in range(days):
            day = prev['day'] + (1 if prev['time'] >= '05:00' else 0)
            self.run_clock_to(day, '05:00')
            cur = self.take(f'day {day} 05:00')
            self.check_window(prev, cur, moves=True)
            name = self.save_game(day)
            self.log(f"Day {day} 05:00: money {money_text(cur['money'])}, saved '{name}'.")
            if self.reload_every and (day - 1) % self.reload_every == 0:
                cur = self.reload(name, cur)
            self.apply_changes(day)
            grown = self.meta.get('grown')
            if grown and grown['due'] == day and not grown.get('ready'):
                self.run_clock_to(day, '08:05')
                self.ready(self.meta['plan']['growth']['vehicles'])
                after = self.take(f'day {day} grown')
                self.check_window(cur, after)
                grown['ready'] = True
                self.save_meta()
                cur = after
            elif self.grow(cur):
                after = self.take(f'day {day} buying')
                self.check_window(cur, after)
                cur = after
            prev = cur
        return prev

    def reload(self, name, before):
        """The weekly round trip: load the save just written and check it brought back the same money, stock and places."""
        self.call('load', {'name': name}, 60)
        self.wait('load.status', {'not_equals': 'pending'}, 600)
        state = self.state()
        if (state.get('load') or {}).get('status') != 'done':
            raise Violation('save', f"loading '{name}' failed: {(state.get('load') or {}).get('error')}", before['day'])
        self.call('time', {'pause': True})
        after = self.take(f"reloaded '{name}'")
        for key in ('money', 'stock', 'spaces'):
            if after[key] != before[key]:
                raise Violation('save', f"'{name}' loaded with a different {key}: {before[key]} before, {after[key]} after", before['day'])
        for v in before['vehicles']:
            back = next((x for x in after['vehicles'] if x['id'] == v['id']), None)
            if back is None or back['stock'] != v['stock'] or back['pieces'] != v['pieces'] or back['crew'] != v['crew']:
                raise Violation('save', f"'{name}' loaded {v['kind']} {v['id']} differently: {v} before, {back} after", before['day'])
        self.check_window(before, after)
        self.log(f"  reloaded '{name}': money, stock, crews and pieces round-trip.")
        return after

    def apply_changes(self, day):
        for change in self.meta['plan'].get('changes', []):
            if change.get('day') != day: continue
            vid = self.meta['vehicles'][change['vehicle']]
            if change['op'] == 'paste_day':
                self.done(self.call('route', {'op': 'paste_day', 'vehicle': vid, 'from': change['from'], 'to': change['to']}), change.get('why', 'a route change'))
            elif change['op'] == 'shift':
                state = self.state()
                v = next(x for x in state['vehicles'] if x['id'] == vid)
                employee = (v['route'] or {}).get('shifts', [])[change['crew']]
                self.done(self.call('crew', {'op': 'schedule', 'vehicle': vid, 'employee_id': employee['id'], 'day': change['weekday'],
                                             'start': change['start'], 'end': change['end']}), change.get('why', 'a shift change'))
            else:
                raise Violation('plan', f"unknown change op '{change['op']}'")
            self.meta.setdefault('changes_done', []).append({**change, 'vehicle_id': vid})
            self.save_meta()
            self.log(f"  change on day {day}: {change.get('why', change['op'])}.")

    # -- checks over a window --
    def check_window(self, prev, cur, moves=False, crewed_only=False):
        try:
            check_events(cur['events'])
            check_money(prev, cur)
            self.meta.setdefault('delivered', [])
            self.meta.setdefault('refused', [])
            new_refusals = refused_orders(cur['events']) - set(self.meta['refused'])
            if new_refusals:
                self.note(f"day {cur['day']}: commissary orders {', '.join(map(str, sorted(new_refusals)))} weren't delivered: the payment was "
                          'refused (see events.jsonl)')
            refused = set(self.meta['refused']) | new_refusals
            settled = {d['n'] for d in self.meta['delivered']} | refused
            placed = [o for o in self.meta['orders'] if o['n'] not in settled]
            delivered = delivered_between(prev, cur, placed, refused)
            check_stock(prev, cur, delivered)
            if moves:
                for text in check_moves(cur, crewed_only): self.note(text)
            self.meta['refused'] = sorted(refused)
            for o in delivered:
                # Orders arrive at 08:00 on their due day and are paid then.
                self.meta['delivered'].append({**o, 'day': o['due_day']})
            for day in range(prev['day'], cur['day']):
                if day in self.meta['checked_days'] or str(day) not in cur['summaries']: continue
                summary = cur['summaries'][str(day)]
                goods = sum(self.goods(o) for o in self.meta['delivered'] if o['day'] == day)
                bought = sum(p['cents'] for p in self.meta['purchases'] if p['day'] == day)
                for text in check_day_books(day, cur['transactions'], summary, goods, bought): self.note(text)
                day_start = self.snapshot_at(day, '05:00') or prev
                append_ledger(self.folder / 'ledger.csv', ledger_rows(day, summary, day_start, cur,
                                                                     [o for o in self.meta['delivered'] if o['day'] == day],
                                                                     self.meta['purchases'], cur['transactions']))
                self.meta['checked_days'].append(day)
            self.save_meta()
        except Violation as violation:
            self.record_violation(violation)
            raise

    # -- the referee: watch a game someone else plays --
    def watch(self, stop_file, poll_s=2.0, polls=None):
        """Takes a snapshot after each in-game 05:00 and runs the checks; changes nothing. On a hard failure it pauses the
        game and writes the reason to `stop_file`. Returns 0 when `polls` ran out, 2 after a stop."""
        if self.meta is None:
            health = self.guard()
            self.meta = {'mode': 'watch', 'plan': {'vehicles': [], 'changes': []}, 'save': health['save'], 'vehicles': {}, 'purchases': [],
                         'orders': [], 'events_next': 1, 'checked_days': [], 'findings': [], 'notes': [], 'stopped': None,
                         'started': time.strftime('%Y-%m-%d %H:%M:%S')}
            self.folder.mkdir(parents=True, exist_ok=True)
            self.save_meta()
        if self.meta.get('stopped'):
            raise Violation('guard', f"this run stopped at {self.meta['stopped']['check']}: {self.meta['stopped']['message']}")
        snaps = self.snapshots()
        prev = snaps[-1] if snaps else self.take('watch start')
        self.learn(prev)
        if not snaps: self.meta['first_run_day'] = prev['day'] + 1
        self.save_meta()
        count = 0
        while polls is None or count < polls:
            count += 1
            if os.path.exists(stop_file): return 2
            health = self.bridge.health()
            if health.get('scene') == 'city' and health.get('day') and (health['day'], health['time']) >= (prev['day'] + 1, '05:00'):
                if not (health.get('save') or '').startswith(SAVE_PREFIX):
                    self.note(f"day {health['day']}: the save is now '{health.get('save')}'; the referee only checks FT playtest saves")
                else:
                    cur = self.take(f"day {health['day']} 05:00")
                    self.learn(cur)
                    try:
                        if cur['day'] - prev['day'] > 1:
                            self.note(f"day {cur['day']}: {cur['day'] - prev['day']} days passed between two looks (a sleep or a load?); "
                                      'money and stock not reconciled for them')
                            check_events(cur['events'])
                        else:
                            self.check_window(prev, cur, moves=True, crewed_only=True)
                    except Violation as violation:
                        if self.meta.get('stopped') is None: self.record_violation(violation)
                        try: self.call('time', {'pause': True})
                        except Exception: pass
                        Path(stop_file).write_text(f"day {cur['day']} {cur['time']}, {violation.check}: {violation.message}\n")
                        self.log(f'STOP: {violation.check}: {violation.message}')
                        return 2
                    self.log(f"Day {cur['day']} {cur['time']}: money {money_text(cur['money'])}; checks pass.")
                    prev = cur
            time.sleep(poll_s)
        return 0

    def learn(self, snap):
        """What the player did since the last look, as the referee can see it: new orders and vehicles bought."""
        known = {o['n'] for o in self.meta['orders']}
        for o in snap['orders']:
            if o['n'] not in known:
                self.meta['orders'].append({**o, 'goods': cents(o.get('total') or 0)})
        owned = {p['id'] for p in self.meta['purchases']}
        bought = [t for t in snap['transactions'] if t['type'] == PURCHASE_TYPE]
        for v in snap['vehicles']:
            if v['id'] in owned: continue
            paid = next((t for t in bought if cents(t['amount']) == -cents(PRICES[v['kind']])), None)
            if paid is not None:
                bought.remove(paid)
                self.meta['purchases'].append({'id': v['id'], 'kind': v['kind'], 'cents': cents(PRICES[v['kind']]), 'day': paid['day']})
            self.meta['vehicles'].setdefault(v['id'], v['id'])

    def note(self, text):
        # Something odd that doesn't stop the run: in run.json's notes and the report.
        self.meta.setdefault('notes', []).append(text)
        self.log(f'  note: {text}')

    def goods(self, order):
        placed = next((o for o in self.meta['orders'] if o['n'] == order['n']), None)
        return placed['goods'] if placed else cents(order.get('total') or 0)

    def snapshot_at(self, day, clock):
        return next((s for s in self.snapshots() if s['day'] == day and s['time'] == clock), None)


def check_folder(folder, log_path=None, version=None):
    """`check`: the checks again over a run folder's snapshots, offline, and the log rules on a Player.log."""
    folder = Path(folder)
    meta = json.loads((folder / 'run.json').read_text())
    snaps = [json.loads(line) for line in (folder / 'snapshots.jsonl').read_text().splitlines()]
    results = []
    settled = set()
    for prev, cur in zip(snaps, snaps[1:]):
        try:
            check_events(cur['events'])
            check_money(prev, cur)
            settled |= refused_orders(cur['events'])
            delivered = delivered_between(prev, cur, [o for o in meta.get('orders', []) if o['n'] not in settled], settled)
            settled |= {o['n'] for o in delivered}
            check_stock(prev, cur, delivered)
            if cur['label'].endswith('05:00'): check_moves(cur)
            results.append({'window': f"{prev['label']} to {cur['label']}", 'ok': True})
        except Violation as violation:
            results.append({'window': f"{prev['label']} to {cur['label']}", 'ok': False, **violation.as_dict()})
            break
    log = None
    if log_path:
        from .logcheck import check_log
        log = check_log(log_path, version, None)
    return results, log


def write_report(folder, balance_rows=None, balance_note=None):
    folder = Path(folder)
    meta = json.loads((folder / 'run.json').read_text())
    ledger = read_ledger(folder / 'ledger.csv')
    snaps = [json.loads(line) for line in (folder / 'snapshots.jsonl').read_text().splitlines()] if (folder / 'snapshots.jsonl').exists() else []
    names = {vid: name for name, vid in meta.get('vehicles', {}).items()}
    out = [f"# Playthrough {folder.name}", '']
    plan = meta.get('plan', {})
    out.append(f"Plan `{plan.get('name')}` in `{meta.get('save')}`, started {meta.get('started')}. "
               f"{len(meta.get('checked_days', []))} days checked; saves: {', '.join(meta.get('saves', [])) or 'none'}.")
    out.append('')
    stopped = meta.get('stopped')
    out.append('## Result')
    out.append('')
    out.append(f"Stopped on day {stopped['day']} at the **{stopped['check']}** check: {stopped['message']}" if stopped else
               'Every check passed on every day run.')
    out.append('')
    out.append('## What was played')
    out.append('')
    for v in plan.get('vehicles', []):
        out.append(f"- {v['name']} ({v['kind']}, `{meta['vehicles'].get(v['name'])}`): crew {v.get('crew')}, foods {', '.join(v['foods'])}, "
                   f"stops by {', '.join(v['stops'])}.")
    grown = meta.get('grown')
    for v in ((plan.get('growth') or {}).get('vehicles', []) if grown else []):
        out.append(f"- Day {grown['day']}, growth: {v['name']} ({v['kind']}, `{meta['vehicles'].get(v['name'])}`): crew {v.get('crew')}, "
                   f"foods {', '.join(v['foods'])}, stops by {', '.join(v['stops'])}.")
    for c in meta.get('changes_done', []):
        out.append(f"- Day {c['day']}: {c.get('why', c['op'])}.")
    out.append('')
    out.append('## Daily profit per vehicle')
    out.append('')
    days = sorted({int(r['day']) for r in ledger})
    vehicles = sorted({r['vehicle'] for r in ledger if r['vehicle'] != 'fleet'})
    if days:
        out.append('| Day | ' + ' | '.join(names.get(v, v) for v in vehicles) + ' | Fleet lines | Money at the end |')
        out.append('|---|' + '---|' * (len(vehicles) + 2))
        for d in days:
            rows = {r['vehicle']: r for r in ledger if int(r['day']) == d}
            def profit(r):
                return cents(r['revenue']) - sum(cents(r[k] or 0) for k in ('stock_cost', 'wages', 'commissary_rent', 'fines'))
            fleet = rows.get('fleet')
            cells = [money_text(profit(rows[v])) if v in rows else '' for v in vehicles]
            out.append(f"| {d} | " + ' | '.join(cells) + f" | {money_text(-cents(fleet['delivery_fees']) - cents(fleet['purchases'])) if fleet else ''} | "
                       f"{money_text(cents(fleet['money_after'])) if fleet else ''} |")
        out.append('')
        out.append('Profit is sales less stock sold, wages, commissary rent and fines. Fleet lines are delivery fees and vehicle purchases. '
                   'The full rows are in `ledger.csv`.')
    else:
        out.append('No day finished yet.')
    out.append('')
    out.append('## Findings')
    out.append('')
    findings = meta.get('findings', [])
    notes = meta.get('notes', [])
    for f in findings:
        out.append(f"- Day {f['day']}, {f['check']} (stopped the run): {f['message']}")
        if f.get('details', {}).get('event'):
            out.append(f"  - log line: `{f['details']['event']['text']}`")
    for n in notes:
        out.append(f"- Note, {n}")
    if not findings and not notes:
        out.append('None.')
    out.append('')
    out.append('## Balance against tools/balance')
    out.append('')
    if balance_rows:
        out.append('| Vehicle | Days | Revenue | Calculator | Gap |')
        out.append('|---|---|---|---|---|')
        for r in balance_rows:
            gap = 'n/a' if r['gap'] is None else f"{r['gap']:+.0%}"
            out.append(f"| {names.get(r['vehicle'], r['vehicle'])} | {r['days']} | {money_text(r['actual'])} | {money_text(r['expected'])} | "
                       f"{gap}{' (outside ±25%)' if r['outside'] else ''} |")
        out.append('')
        out.append('A gap beyond ±25% is a finding to look into, not a failure. The calculator prices the same stops, foods and hours '
                   'with Core\'s demand model on approximate positions from `tools/balance/city.json`.')
    else:
        out.append(balance_note or 'Not run.')
    out.append('')
    if snaps:
        out.append(f"Snapshots: {len(snaps)}, from day {snaps[0]['day']} {snaps[0]['time']} to day {snaps[-1]['day']} {snaps[-1]['time']}.")
    path = folder / 'report.md'
    path.write_text('\n'.join(out) + '\n')
    return path

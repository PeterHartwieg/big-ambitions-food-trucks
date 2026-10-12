from datetime import datetime
import json
import math
from pathlib import Path
import re
import time

from .bridge import Bridge, BridgeError
from .paths import CONDITIONS, lookup, meets, value, PathError
from .shots import ROOT, contact_sheet, fetch_shot

KINDS = {'call', 'remember', 'expect', 'wait_event', 'wait_state', 'no_event', 'shoot', 'sleep_s', 'note'}
FIELDS = {
    'call': {'args', 'expect_ok', 'timeout_s'},
    'remember': {'from', 'path', 'scale'},
    'expect': {'path', 'where', 'pick'} | CONDITIONS,
    'wait_event': {'timeout_s', 'captures', 'after_step'},
    'wait_state': {'timeout_s', 'poll_s'} | CONDITIONS,
    'no_event': {'from_step', 'to_step'},
    'shoot': set(), 'sleep_s': set(), 'note': set(),
}


def validate(document):
    if not isinstance(document, dict) or not isinstance(document.get('name'), str) or not document['name']:
        raise ValueError('Scenario needs a name: add a nonempty name string')
    if not isinstance(document.get('checks'), str) or not document['checks']:
        raise ValueError('Scenario needs checks: name its CHECKS.md rows')
    if set(document) - {'name', 'checks', 'needs', 'steps'}:
        raise ValueError('Unknown scenario field: use name, checks, needs and steps')
    needs = document.get('needs', {})
    if not isinstance(needs, dict) or set(needs) - {'scene', 'save_prefix'} or any(not isinstance(v, str) for v in needs.values()):
        raise ValueError('Invalid needs: use scene and save_prefix strings')
    if not isinstance(document.get('steps'), list) or not document['steps']:
        raise ValueError('Scenario needs steps: add at least one step')
    for index, step in enumerate(document['steps'], 1):
        if not isinstance(step, dict) or len(KINDS.intersection(step)) != 1:
            raise ValueError(f'Step {index} needs one step kind: use a kind from the contract')
        kind = next(iter(KINDS.intersection(step)))
        if set(step) - ({kind, 'soft'} | FIELDS[kind]):
            raise ValueError(f'Step {index} has unknown fields: check the {kind} schema')
        if 'soft' in step and not isinstance(step['soft'], bool):
            raise ValueError(f'Step {index} soft must be true or false')
        if kind in {'call', 'remember', 'expect', 'wait_event', 'wait_state', 'no_event', 'note'} and not isinstance(step[kind], str):
            raise ValueError(f'Step {index} {kind} must be a string')
        if kind == 'call' and (not isinstance(step.get('args', {}), dict) or not isinstance(step.get('expect_ok', True), bool)):
            raise ValueError(f'Step {index} needs object args and boolean expect_ok')
        if kind == 'remember' and (step.get('from') not in ('state', 'result', 'last', 'call') or not isinstance(step.get('path', ''), str)):
            raise ValueError(f'Step {index} remember needs from state or result and a path')
        if kind == 'expect' and step[kind] not in ('state', 'result', 'last', 'call'):
            raise ValueError(f'Step {index} expect must read state or result')
        if kind in ('expect', 'wait_state') and not CONDITIONS.intersection(step):
            raise ValueError(f'Step {index} needs a condition: use equals, less, length or another contract condition')
        if 'path' in step and not isinstance(step['path'], str):
            raise ValueError(f'Step {index} path must be a string')
        if 'where' in step and (not isinstance(step['where'],dict) or any(not isinstance(k,str) for k in step['where'])):
            raise ValueError(f'Step {index} where needs an object of field equality filters')
        if 'pick' in step and (not isinstance(step['pick'],str) or 'where' not in step):
            raise ValueError(f'Step {index} pick needs a path after where')
        if 'scale' in step and (isinstance(step['scale'],bool) or not isinstance(step['scale'],(int,float)) or not math.isfinite(step['scale'])):
            raise ValueError(f'Step {index} scale needs a finite number')
        if kind=='wait_event' and 'captures' in step:
            if not isinstance(step['captures'],dict) or any(not isinstance(k,str) or not isinstance(v,str) for k,v in step['captures'].items()):
                raise ValueError(f'Step {index} captures maps variables to named regex groups')
            groups=re.compile(step[kind]).groupindex
            if set(step['captures'].values())-groups.keys(): raise ValueError(f'Step {index} captures names an absent regex group')
        if 'length' in step and (isinstance(step['length'], bool) or not isinstance(step['length'], int) or step['length'] < 0):
            raise ValueError(f'Step {index} length must be a nonnegative integer')
        for field in ('timeout_s', 'poll_s', 'sleep_s'):
            if field in step and (isinstance(step[field], bool) or not isinstance(step[field], (int, float)) or not math.isfinite(step[field]) or step[field] < 0 or (field == 'poll_s' and step[field] == 0)):
                raise ValueError(f'Step {index} {field} must be a finite positive duration')
        for field in ('wait_event', 'no_event', 'matches'):
            if field in step:
                try:
                    re.compile(step[field])
                except (re.error, TypeError) as exc:
                    raise ValueError(f'Step {index} {field} needs a valid regex: {exc}') from exc
        if kind == 'no_event':
            start, end = step.get('from_step', max(0, index - 1)), step.get('to_step', index)
            if any(isinstance(n, bool) or not isinstance(n, int) for n in (start, end)) or not (0 <= start <= end <= index and start < index):
                raise ValueError(f'Step {index} no_event needs completed step boundaries: use an earlier from_step and to_step no later than this step')
        if kind == 'wait_event' and 'after_step' in step:
            boundary = step['after_step']
            if isinstance(boundary, bool) or not isinstance(boundary, int) or not 0 <= boundary < index:
                raise ValueError(f'Step {index} after_step needs an earlier completed step number (0 is the run start)')
        if kind == 'shoot':
            shot = step[kind]
            if not isinstance(shot, dict) or not isinstance(shot.get('name'), str):
                raise ValueError(f'Step {index} shoot needs a name and camera arguments')
            if not ({'pose', 'target'} <= shot.keys() or {'x', 'y', 'z'} <= shot.keys()):
                raise ValueError(f'Step {index} shoot needs pose and target, or explicit camera coordinates')
    return document


def read_scenario(path):
    return validate(json.loads(Path(path).read_text()))


class Runner:
    def __init__(self, bridge=None):
        self.bridge = bridge or Bridge()
        self.variables = {}
        self.last = None
        self.events = []
        self.current_note = ''
        self.coverage_lost = False
        self.cursor = 0
        self.boundaries = {}
        self.starts = {}

    def collect(self):
        batch = self.bridge.events(self.cursor)
        self.events.extend(batch['events'])
        self.cursor = batch['next']
        if batch['dropped']:
            self.coverage_lost = True
            raise BridgeError(f"Scenario lost {batch['dropped']} events: rerun with a shorter scenario or poll more often")

    def resolve(self, obj):
        if isinstance(obj, str) and obj.startswith('vehicle:$'):
            return 'vehicle:' + str(self.resolve(obj[len('vehicle:'):]))
        if isinstance(obj, str) and obj.startswith('$'):
            if obj[1:] not in self.variables:
                raise ValueError(f'Unknown remembered value {obj}: add its remember step first')
            return self.variables[obj[1:]]
        if isinstance(obj, dict):
            return {key: self.resolve(val) for key, val in obj.items()}
        if isinstance(obj, list):
            return [self.resolve(val) for val in obj]
        return obj

    def source(self, name):
        return self.bridge.call('state')['result'] if name == 'state' else self.last

    def pause_collect(self, seconds):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            self.collect()
            time.sleep(min(.1, max(0, deadline - time.monotonic())))
        self.collect()

    def step(self, step, index, directory):
        kind = next(iter(KINDS.intersection(step)))
        if kind == 'call':
            args = self.resolve(step.get('args', {}))
            try:
                reply = self.bridge.call(step[kind], args, step.get('timeout_s', 10))
            except BridgeError as exc:
                self.last = exc.reply or {'ok': False, 'error': str(exc)}
                if step.get('expect_ok') is False and exc.remote:
                    return {'expected': 'ok: false', 'seen': self.last}
                raise
            self.last = reply['result']
            if step.get('expect_ok') is False:
                raise ValueError(f'Expected ok: false for {step[kind]}, saw ok: true')
            return {'expected': {'cmd': step[kind], 'args': args, 'ok': True}, 'seen': self.last, 'op':reply.get('op')}
        if kind == 'remember':
            actual = lookup(self.source(step['from']), self.resolve(step.get('path', '')))
            if 'scale' in step:
                actual = round(actual * step['scale'], 2)
            self.variables[step[kind]] = actual
            return {'expected': f'remember ${step[kind]}', 'seen': actual}
        if kind in ('expect', 'wait_state'):
            conditions = self.resolve({key: val for key, val in step.items() if key in CONDITIONS})
            path = self.resolve(step.get('path', '') if kind == 'expect' else step[kind])
            deadline = time.monotonic() + step.get('timeout_s', 30)
            while True:
                source = self.source(step[kind] if kind == 'expect' else 'state')
                present = True
                try:
                    actual = lookup(source, path)
                except PathError as exc:
                    if kind != 'wait_state': raise
                    actual = str(exc)
                    present = False
                if 'where' in step:
                    filters=self.resolve(step['where'])
                    actual=[item for item in actual if all(lookup(item,k)==v for k,v in filters.items())]
                    actual=lookup(actual,self.resolve(step.get('pick','')))
                self.collect()
                if present and meets(actual, conditions):
                    return {'expected': {'path': path, **conditions}, 'seen': actual}
                if kind == 'expect' or time.monotonic() >= deadline:
                    raise ValueError(f'{path}: expected {conditions!r}, saw {actual!r}')
                self.pause_collect(min(step.get('poll_s', .1), max(0, deadline - time.monotonic())))
        if kind == 'wait_event':
            regex = re.compile(self.resolve(step[kind]))
            deadline = time.monotonic() + step.get('timeout_s', 30)
            boundary = self.boundaries[step['after_step']] if 'after_step' in step else self.starts.get(index - 1, self.boundaries.get(0, 1))
            since = max(self.event_mark + 1, boundary)
            while True:
                self.collect()
                event = next((e for e in self.events if e['seq'] >= since and regex.search(e['text'])), None)
                if event:
                    match=regex.search(event['text'])
                    for name,group in step.get('captures',{}).items():
                        self.variables[name]=value(match.group(group).replace(',',''))
                    self.event_mark = event['seq']
                    return {'expected': step[kind], 'seen': event}
                if time.monotonic() >= deadline:
                    raise ValueError(f'Expected event {step[kind]!r}, saw no match in {step.get("timeout_s", 30)}s')
                self.pause_collect(min(.1, max(0, deadline - time.monotonic())))
        if kind == 'no_event':
            self.collect()
            start = self.boundaries[step.get('from_step', max(0, index - 1))]
            end = self.boundaries.get(step.get('to_step', index), self.cursor)
            pattern = re.compile(self.resolve(step[kind]))
            matches = [e for e in self.events if start <= e['seq'] < end and pattern.search(e['text'])]
            if matches:
                raise ValueError(f'Expected no {step[kind]!r}, saw {matches[0]["text"]!r}')
            return {'expected': f'no {step[kind]} between cursors {start} and {end}', 'seen': 'no match'}
        if kind == 'shoot':
            args = self.resolve(step[kind])
            self.last = self.bridge.call('shoot', args)['result']
            shot = fetch_shot(self.last['path'], directory, args['name'] + '.png')
            return {'expected': args, 'seen': self.last, 'shot': str(shot), 'note': f"{args.get('pose', 'camera')} at {args.get('target', 'explicit coordinates')}. {self.current_note}"}
        if kind == 'sleep_s':
            self.pause_collect(step[kind])
            return {'expected': f'wait {step[kind]} real seconds', 'seen': 'waited'}
        self.current_note = step[kind]
        return {'status': 'NOTE', 'expected': step[kind], 'seen': step[kind], 'note': step[kind]}

    def run(self, document, directory=None, keep_going=False):
        validate(document)
        self.__init__(self.bridge)
        name = re.sub(r'[^a-zA-Z0-9_.-]', '-', document['name'])
        directory = Path(directory or ROOT / 'build/scenarios' / f'{name}-{datetime.now():%Y%m%d-%H%M%S-%f}')
        directory.mkdir(parents=True, exist_ok=True)
        report = {'name': document['name'], 'checks': document['checks'], 'ok': True, 'steps': [],
                  'bridge_url': getattr(self.bridge, 'url', None),
                  'transport': getattr(self.bridge, 'transport', 'http')}
        try:
            health = self.bridge.health()
            report['health'] = health
            for key, wanted in document.get('needs', {}).items():
                actual = health.get('save' if key == 'save_prefix' else key)
                if (key == 'save_prefix' and (not isinstance(actual, str) or not actual.startswith(wanted))) or (key != 'save_prefix' and actual != wanted):
                    raise ValueError(f'Needs {key} {wanted!r}, saw {actual!r}: load a matching FT test save in the city')
            self.cursor = self.bridge.events(0)['next']
            self.event_mark = self.cursor - 1
            self.boundaries[0] = self.cursor
            for index, step in enumerate(document['steps'], 1):
                record = {'step': index, 'kind': next(iter(KINDS.intersection(step))), 'status': 'PASS',
                          'soft': step.get('soft', False), 'expected': step, 'seen': None}
                try:
                    self.collect()
                    self.starts[index] = self.cursor
                    record.update(self.step(step, index, directory))
                    self.collect()
                except (BridgeError, ValueError, OSError, TypeError, KeyError) as exc:
                    record.update(status='FAIL', seen=str(exc))
                    if not record['soft']:
                        report['ok'] = False
                report['steps'].append(record)
                self.boundaries[index] = self.event_mark + 1 if record['kind'] == 'wait_event' and record['status'] == 'PASS' else self.cursor
                if record['status'] == 'FAIL' and not record['soft'] and not keep_going:
                    break
        except (BridgeError, ValueError, OSError) as exc:
            report['ok'] = False
            report['steps'].append({'step': 0, 'kind': 'needs', 'status': 'FAIL', 'soft': False,
                                    'expected': document.get('needs', {}), 'seen': str(exc)})
        try:
            if self.boundaries:
                self.collect()
        except BridgeError as exc:
            report['ok'] = False
            report['steps'].append({'step': len(report['steps']) + 1, 'kind': 'events', 'status': 'FAIL', 'soft': False,
                                    'expected': 'all events retained', 'seen': str(exc)})
        if self.coverage_lost:
            report['ok'] = False
        failures = [s for s in report['steps'] if s['status'] == 'FAIL']
        report['summary'] = [f"Step {s['step']}: {s['seen']}" for s in failures] or ['All checks passed.']
        report['events'] = len(self.events)
        (directory / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
        markdown = [f"# {document['name']}", '', *report['summary'], '', document['checks'], '']
        for s in report['steps']:
            detail = f"{s['status']} step {s['step']}{' (soft)' if s['soft'] else ''}: expected {json.dumps(s['expected'], ensure_ascii=False)}; saw {json.dumps(s['seen'], ensure_ascii=False)}"
            markdown.append(detail + '  ')
        (directory / 'report.md').write_text('\n'.join(markdown) + '\n')
        (directory / 'events.jsonl').write_text(''.join(json.dumps(e, ensure_ascii=False) + '\n' for e in self.events))
        contact_sheet(directory)
        return report, directory.resolve()

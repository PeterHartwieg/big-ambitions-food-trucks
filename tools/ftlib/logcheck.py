from collections import Counter
from pathlib import Path
import re

from .shots import ROOT


class Rule:
    def __init__(self, identifier, kind, pattern, rows=()):
        self.id, self.kind, self.pattern, self.rows = identifier, kind, pattern, tuple(rows)
        if kind not in ('must', 'never', 'order') and not re.fullmatch(r'count(?:=|>=)\d+', kind):
            raise ValueError(f'Unknown log rule {kind}: use must, never, count=N, count>=N or order')
        try:
            self.regexes = [re.compile(p) for p in pattern.split(' >> ')] if kind == 'order' else [re.compile(pattern)]
        except re.error as exc:
            raise ValueError(f'Invalid regex in log rule {identifier}: {exc}; fix the rule file') from exc

    def check(self, text):
        count = len(list(self.regexes[0].finditer(text)))
        if self.kind == 'order':
            position = 0
            count = 0
            for regex in self.regexes:
                match = regex.search(text, position)
                if match is None:
                    break
                count += 1
                position = match.end()
            passed = count == len(self.regexes)
        elif self.kind == 'must':
            passed = count > 0
        elif self.kind == 'never':
            passed = count == 0
        elif self.kind.startswith('count>='):
            passed = count >= int(self.kind[7:])
        else:
            passed = count == int(self.kind[6:])
        return {'id': self.id, 'rule': self.kind, 'regex': self.pattern,
                'status': 'PASS' if passed else 'FAIL', 'seen': count, 'rows': list(self.rows)}


def read_rules(path):
    rules = []
    for n, line in enumerate(Path(path).read_text().splitlines(), 1):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        parts = [part.strip() for part in re.split(r'\s+\|\s+', line)]
        if len(parts) not in (3, 4):
            raise ValueError(f'Invalid log rule on line {n}: use id | rule | regex | row section/number')
        rows = ()
        if len(parts) == 4:
            if not parts[3].startswith('row '):
                raise ValueError(f'Invalid row on line {n}: start the last column with row')
            rows = parts[3][4:].split(',')
        rules.append(Rule(*parts[:3], rows))
    return rules


def always_on(text):
    lines = text.splitlines()
    active, inactive, errors, exceptions = [], [], [], []
    for index, line in enumerate(lines):
        if re.search(r'Patch .+: Active\.', line):
            active.append(line)
        if re.search(r'Patch .+: Not active', line):
            inactive.append(line)
        if re.search(r'\[Mod:Food ?Trucks\]|\[Food Trucks\]', line):
            block = [line]
            for following in lines[index + 1:]:
                if not following.strip() or following.startswith('[Mod:'):
                    break
                block.append(following)
            marked = '[Mod:FoodTrucks] ERROR: ' in line
            if marked or re.search(r'\b(?:ERROR|Error)\b|Exception', line) or any(re.search(r'LogError|(?:FoodTrucks\.Log|ModLogger)[:.]Error', part) for part in block):
                errors.append(line)
        if re.search(r'\b\w*Exception\b', line):
            block = [line]
            for following in lines[index + 1:]:
                if not following.strip() or following.startswith('[Mod:'):
                    break
                block.append(following)
            if any(re.search(r'FoodTrucks[.+:]', part) for part in block):
                exceptions.append('\n'.join(block))
    return {'patches_active': active, 'patches_inactive': inactive,
            'mod_errors': errors, 'exceptions': exceptions}


def check_log(path, version=None, rows=None, rules_dir=None):
    text = Path(path).read_text(encoding='utf-8', errors='replace')
    if version is None:
        matches = re.findall(r'Version ([\w.+-]+) loading\.', text)
        version = matches[-1] if matches else None
    if version and not re.fullmatch(r'[\w.+-]+', version):
        raise ValueError('Invalid version: use the version number from Player.log')
    rulefile = Path(rules_dir or ROOT / 'tests/game/logchecks') / f'{version}.txt'
    selected = set(rows.split(',')) if isinstance(rows, str) else set(rows or [])
    rules = read_rules(rulefile) if rulefile.exists() else []
    results = []
    for rule in rules:
        if rule.rows and 'all' not in selected and not selected.intersection(rule.rows):
            results.append({'id': rule.id, 'rule': rule.kind, 'regex': rule.pattern,
                            'status': 'NOT CHECKED', 'rows': list(rule.rows), 'seen': None})
        else:
            results.append(rule.check(text))
    always = always_on(text)
    ok = not any(row['status'] == 'FAIL' for row in results)
    ok = ok and not any(always[key] for key in ('patches_inactive', 'mod_errors', 'exceptions'))
    return {'ok': ok, 'version': version, 'rules_available': rulefile.exists(),
            'rules': results, 'always': always}


def format_report(report):
    lines = [f"Log version: {report['version'] or 'unknown'}"]
    if not report['rules_available']:
        lines.append('No rules for this version; only the always-on checks were run.')
    for status, label in [('FAIL', 'Failed'), ('PASS', 'Passed'), ('NOT CHECKED', 'Not checked (rows not chosen)')]:
        group = [r for r in report['rules'] if r['status'] == status]
        lines.append(f'{label}: {len(group)}')
        lines.extend(f"  {r['id']}: {r['rule']}, seen {r['seen']}" for r in group)
    lines.append('Always-on checks:')
    for key, label in [('patches_active', 'Active patches'), ('patches_inactive', 'Not active patches'),
                       ('mod_errors', 'Mod errors'), ('exceptions', 'FoodTrucks exceptions')]:
        entries = report['always'][key]
        lines.append(f'  {label}: {len(entries)}')
        for entry, count in Counter(entries).items():
            repeated = f' ({count} times)' if count > 1 else ''
            lines.append('    ' + entry.replace('\n', '\n    ') + repeated)
    return '\n'.join(lines)

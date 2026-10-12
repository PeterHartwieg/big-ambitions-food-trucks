import json
import re


class PathError(ValueError):
    pass


def value(text):
    try:
        return json.loads(text)
    except (ValueError, TypeError):
        return text


def lookup(data, path):
    if not path:
        return data
    tokens = re.findall(r'[^.\[\]]+|\[[^\]]+\]', path)
    if '.'.join(tokens).replace('.[', '[') != path:
        raise PathError(f"Invalid path {path!r}: use dotted keys, indexes or [key=value]")
    current = data
    try:
        for token in tokens:
            if token.startswith('['):
                selector = token[1:-1]
                if '=' in selector:
                    key, expected = selector.split('=', 1)
                    def matches(item):
                        try:
                            return lookup(item, key) == value(expected)
                        except PathError:
                            return False
                    current = next(item for item in current if matches(item))
                else:
                    current = current[int(selector)]
            elif isinstance(current, list):
                current = current[int(token)]
            else:
                current = current[token]
        return current
    except (KeyError, IndexError, StopIteration, TypeError, ValueError) as exc:
        raise PathError(f"No value at {path!r}: inspect tools/ft state for available fields") from exc


def parse_args(pairs, raw=None):
    result = json.loads(raw) if raw is not None else {}
    if not isinstance(result, dict):
        raise ValueError('Call arguments must be an object: use --json with a JSON object')
    for pair in pairs:
        if '=' not in pair:
            raise ValueError(f'Invalid argument {pair!r}: use key=value')
        key, text = pair.split('=', 1)
        keys = key.split('.')
        if any(not part for part in keys):
            raise ValueError(f'Invalid key {key!r}: use nonempty dotted keys')
        target = result
        for part in keys[:-1]:
            if part not in target:
                target[part] = {}
            if not isinstance(target[part], dict):
                raise ValueError(f'Conflicting key {key!r}: supply its parent as an object')
            target = target[part]
        target[keys[-1]] = value(text)
    return result


CONDITIONS = {'equals', 'not_equals', 'less', 'greater', 'length', 'contains', 'matches'}


def meets(actual, conditions):
    for op, expected in conditions.items():
        try:
            if op == 'equals':
                passed = actual == expected
            elif op == 'not_equals':
                passed = actual != expected
            elif op == 'less':
                passed = actual < expected
            elif op == 'greater':
                passed = actual > expected
            elif op == 'length':
                passed = len(actual) == expected
            elif op == 'contains':
                passed = expected in actual
            elif op == 'matches':
                passed = re.search(expected, str(actual)) is not None
            else:
                raise ValueError(f'Unknown condition {op}: use a supported check')
        except (TypeError, AttributeError):
            passed = False
        if not passed:
            return False
    return True

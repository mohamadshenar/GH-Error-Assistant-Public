"""Conservative comparison of already captured disposable Rhino test snapshots.

This module never starts Rhino or modifies a definition. Missing or unreliable
measurements produce PARTIAL; permitted canvas changes apply only to Locate.
"""
import argparse
import datetime
import json
import os
import uuid

REQUIRED = (
    'doc', 'runtime', 'objects', 'dirty', 'selection', 'viewport', 'zoom',
    'undo', 'redo', 'rhinoObjects', 'rhinoDirty', 'rhinoUndo', 'eventCount',
    'parameterSettings', 'persistentGeometry', 'rhinoGeometry',
)
LOCATE_ALLOWED = frozenset(('selection', 'viewport', 'zoom'))


def compare(action, before, after):
    if not isinstance(before, dict) or not isinstance(after, dict):
        return {'status': 'PARTIAL', 'missing': ['snapshot'], 'changes': []}
    missing = [key for key in REQUIRED if key not in before or key not in after
               or before.get(key) is None or after.get(key) is None]
    if before.get('captureComplete') is not True or after.get('captureComplete') is not True:
        missing.append('captureComplete')
    allowed = LOCATE_ALLOWED if action == 'locate' else frozenset()
    changes = []
    expected = []
    for key in REQUIRED:
        if key in missing:
            continue
        if before[key] != after[key]:
            (expected if key in allowed else changes).append(key)
    status = 'FAIL' if changes else 'PARTIAL' if missing else 'PASS'
    return {'status': status, 'missing': missing, 'changes': changes,
            'expectedChanges': expected, 'measuredFields': [k for k in REQUIRED if k not in missing]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', help='Native matrix-summary.json captured previously')
    parser.add_argument('output_directory', help='Directory for a new, uniquely named report')
    args = parser.parse_args()
    with open(args.input, encoding='utf-8-sig') as stream:
        source = json.load(stream)
    # Fixture setup, explicit repair/deletion, and switching are intentional
    # operations; their expected differences must be reviewed separately.
    read_only = {'locate', 'check-before-repair', 'check-after-repair'}
    results = []
    for item in source.get('actions', []):
        action = item.get('action', '')
        if action in read_only:
            result = compare('locate' if action == 'locate' else 'check-again',
                             item.get('before'), item.get('after'))
            results.append({'index': item.get('index'), 'action': action,
                            'execution': 'native harness / production handler', **result})
        else:
            results.append({'index': item.get('index'), 'action': action,
                            'execution': 'native harness / combined fixture operation',
                            'status': 'PARTIAL', 'missing': ['isolated assistant action'],
                            'changes': [], 'expectedChanges': [],
                            'measuredFields': [],
                            'reason': 'Fixture changes or document transitions are combined with the assistant action.'})
    report = {'source': os.path.abspath(args.input),
              'createdUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'scope': 'read-only comparisons; combined fixture operations marked partial',
              'results': results}
    os.makedirs(args.output_directory, exist_ok=True)
    name = 'fingerprint-comparisons-{}-{}.json'.format(
        datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'), uuid.uuid4().hex[:8])
    path = os.path.join(args.output_directory, name)
    with open(path, 'x', encoding='utf-8') as stream:
        json.dump(report, stream, indent=2, ensure_ascii=True)
    print(path)


if __name__ == '__main__':
    main()


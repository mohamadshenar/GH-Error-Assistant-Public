"""Compare recorded disposable GH state across a document lifecycle transition.

The result is limited to the listed fields and never certifies arbitrary plugin
behavior. Inputs are captured separately by fingerprint_capture inside Rhino.
"""
import json


PERSISTED_FIELDS = ('doc', 'objects', 'parameterSettings', 'persistentGeometry',
                    'rhinoObjects', 'rhinoGeometry')
TRANSIENT_FIELDS = ('runtime', 'eventCount', 'selection', 'viewport', 'zoom',
                    'dirty', 'undo', 'redo', 'rhinoDirty', 'rhinoUndo')


def compare_transition(before, after):
    missing = [name for name in PERSISTED_FIELDS
               if name not in before or name not in after
               or before[name] is None or after[name] is None]
    errors = {'before': before.get('captureErrors'), 'after': after.get('captureErrors')}
    if errors['before'] is None or errors['after'] is None:
        missing.append('captureErrors')
    changes = [name for name in PERSISTED_FIELDS if name not in missing
               and before[name] != after[name]]
    transient = [name for name in TRANSIENT_FIELDS if before.get(name) != after.get(name)]
    status = 'FAIL' if changes else 'PARTIAL' if missing or errors['before'] or errors['after'] else 'SCOPED_PASS'
    return {'status': status, 'measured': list(PERSISTED_FIELDS), 'missing': missing,
            'changes': changes, 'transientDifferences': transient, 'captureErrors': errors,
            'scope': 'listed saved-state fields only; not proof of full capture or plugin safety'}


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('before')
    parser.add_argument('after')
    args = parser.parse_args()
    with open(args.before, encoding='utf-8-sig') as stream:
        left = json.load(stream)
    with open(args.after, encoding='utf-8-sig') as stream:
        right = json.load(stream)
    print(json.dumps(compare_transition(left, right), indent=2))


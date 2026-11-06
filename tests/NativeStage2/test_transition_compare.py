import unittest
from transition_compare import compare_transition, PERSISTED_FIELDS


def snapshot():
    state = {key: [] for key in PERSISTED_FIELDS}
    state.update(captureErrors=[], runtime='1', eventCount=0)
    return state


class TransitionCompareTests(unittest.TestCase):
    def test_reopening_can_change_runtime_without_changing_saved_state(self):
        before = snapshot(); after = snapshot(); after['runtime'] = '2'
        result = compare_transition(before, after)
        self.assertEqual('SCOPED_PASS', result['status'])
        self.assertIn('runtime', result['transientDifferences'])

    def test_wire_or_persistent_change_fails(self):
        before = snapshot(); after = snapshot(); after['objects'] = [{'sources': ['unexpected']}]
        self.assertEqual('FAIL', compare_transition(before, after)['status'])

    def test_unreadable_or_missing_values_remain_partial(self):
        before = snapshot(); after = snapshot(); after['captureErrors'] = [{'field': 'tree'}]
        self.assertEqual('PARTIAL', compare_transition(before, after)['status'])
        del after['objects']
        self.assertEqual('PARTIAL', compare_transition(before, after)['status'])

    def test_detects_mutation_even_when_capture_is_incomplete(self):
        before = snapshot(); after = snapshot(); after['objects'] = ['changed']; del after['captureErrors']
        self.assertEqual('FAIL', compare_transition(before, after)['status'])


if __name__ == '__main__':
    unittest.main()


import unittest
import tempfile
import os
from fingerprint_compare import compare, REQUIRED
from fingerprint_capture import save_unique, _plain_int, _serialize_tree, _canonical_json_hash, _serialize_builtin_parameter, _without_saved_selection, _safe_builtin_tree


def snapshot():
    result = {key: ([] if key in ('objects', 'selection') else 1) for key in REQUIRED}
    result['captureComplete'] = True
    return result


class FingerprintCompareTests(unittest.TestCase):
    def test_equal_complete_snapshot_passes(self):
        self.assertEqual('PASS', compare('check-again', snapshot(), snapshot())['status'])

    def test_missing_fields_are_not_a_pass(self):
        old = snapshot(); new = snapshot(); del new['persistentGeometry']
        result = compare('locate', old, new)
        self.assertEqual('PARTIAL', result['status'])
        self.assertIn('persistentGeometry', result['missing'])

    def test_model_mutation_fails_even_with_missing_fields(self):
        old = snapshot(); new = snapshot(); new['objects'] = ['unexpected']; del new['parameterSettings']
        result = compare('check-again', old, new)
        self.assertEqual('FAIL', result['status'])
        self.assertIn('objects', result['changes'])

    def test_selection_and_viewport_are_only_allowed_for_locate(self):
        old = snapshot(); new = snapshot(); new['selection'] = ['target']; new['zoom'] = 2
        self.assertEqual('PASS', compare('locate', old, new)['status'])
        self.assertEqual('FAIL', compare('check-again', old, new)['status'])

    def test_solution_event_and_document_change_fail(self):
        old = snapshot(); new = snapshot(); new['eventCount'] += 1; new['runtime'] = 2
        result = compare('locate', old, new)
        self.assertEqual('FAIL', result['status'])
        self.assertCountEqual(['runtime', 'eventCount'], result['changes'])

    def test_missing_snapshot_is_partial(self):
        self.assertEqual('PARTIAL', compare('locate', None, snapshot())['status'])

    def test_capture_with_unreadable_values_is_partial(self):
        before = snapshot(); after = snapshot(); after['captureComplete'] = False
        self.assertIn('captureComplete', compare('locate', before, after)['missing'])
        self.assertEqual('PARTIAL', compare('locate', before, after)['status'])

    def test_saved_evidence_never_reuses_a_filename(self):
        with tempfile.TemporaryDirectory() as directory:
            a = save_unique(directory, 'locate', 'before', {'value': 1})
            b = save_unique(directory, 'locate', 'before', {'value': 2})
            self.assertNotEqual(a, b)
            self.assertTrue(os.path.exists(a) and os.path.exists(b))

    def test_rejects_unsafe_evidence_label(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                save_unique(directory, '../outside', 'before', {})

    def test_boxed_integer_is_converted_to_json_number(self):
        import json
        class BoxedNumber:
            def __int__(self): return 42
        self.assertEqual('{"count": 42}', json.dumps({'count': _plain_int(BoxedNumber())}))

    def test_tree_serialization_uses_detached_chunk_and_rejects_failed_write(self):
        import sys, types
        from unittest.mock import patch
        clr = types.ModuleType('clr'); clr.AddReference = lambda name: None
        package = types.ModuleType('GH_IO'); package.__path__ = []
        serialization = types.ModuleType('GH_IO.Serialization')
        class Chunk:
            def __init__(self, name): self.name = name
            def Serialize_Xml(self): return '<serialized />'
        serialization.GH_LooseChunk = Chunk
        class Tree:
            def __init__(self, success): self.success = success
            def Write(self, chunk): return self.success and chunk.name == 'PersistentData'
        with patch.dict(sys.modules, {'clr': clr, 'GH_IO': package, 'GH_IO.Serialization': serialization}):
            self.assertEqual('<serialized />', _serialize_tree(Tree(True)))
            with self.assertRaises(ValueError): _serialize_tree(Tree(False))

    def test_canonical_geometry_hash_ignores_mapping_order_but_detects_change(self):
        self.assertEqual(_canonical_json_hash({'a': 1, 'b': [2, 3]}), _canonical_json_hash({'b': [2, 3], 'a': 1}))
        self.assertNotEqual(_canonical_json_hash({'a': 1}), _canonical_json_hash({'a': 2}))

    def test_geometry_change_fails_even_with_same_rhino_identity(self):
        old = snapshot(); new = snapshot()
        old['rhinoGeometry'] = {'same-guid': {'geometryHash': 'old'}}
        new['rhinoGeometry'] = {'same-guid': {'geometryHash': 'new'}}
        result = compare('check-again', old, new)
        self.assertEqual('FAIL', result['status'])
        self.assertIn('rhinoGeometry', result['changes'])

    def test_unknown_parameter_serializer_is_never_invoked(self):
        import sys, types
        from unittest.mock import patch
        clr = types.ModuleType('clr'); clr.AddReference = lambda name: None
        package = types.ModuleType('GH_IO'); package.__path__ = []
        serialization = types.ModuleType('GH_IO.Serialization'); serialization.GH_LooseChunk = object
        class Name: Name = 'ThirdParty'
        class Assembly:
            def GetName(self): return Name()
        class PortType: pass
        PortType.Assembly = Assembly()
        class Port:
            def GetType(self): return PortType()
            def Write(self, writer): raise AssertionError('Third-party serializer ran')
        with patch.dict(sys.modules, {'clr': clr, 'GH_IO': package, 'GH_IO.Serialization': serialization}):
            self.assertIsNone(_serialize_builtin_parameter(Port()))

    def test_persistent_tree_rejects_third_party_writers(self):
        class Name:
            def __init__(self, name): self.Name = name
        class Assembly:
            def __init__(self, name): self.name = name
            def GetName(self): return Name(self.name)
        class Kind:
            def __init__(self, name): self.Assembly = Assembly(name)
        class Value:
            def __init__(self, name): self.name = name
            def GetType(self): return Kind(self.name)
        class Tree(Value):
            def __init__(self, name, values): super().__init__(name); self.values = values
            def AllData(self, flag): return self.values
            def Write(self, chunk): raise AssertionError('Unsafe serializer ran')
        class Port(Value):
            def __init__(self, name, tree): super().__init__(name); self.PersistentData = tree
        safe = Tree('Grasshopper', [Value('Grasshopper')])
        self.assertIs(safe, _safe_builtin_tree(Port('Grasshopper', safe)))
        for port in (Port('ThirdParty', safe), Port('Grasshopper', Tree('ThirdParty', [])),
                     Port('Grasshopper', Tree('Grasshopper', [Value('ThirdParty')]))):
            with self.assertRaises(ValueError): _safe_builtin_tree(port)

    def test_saved_selection_is_excluded_but_other_attributes_remain(self):
        before = '<chunk><chunks><chunk name="Attributes"><items count="1"><item name="Bounds">1</item></items></chunk></chunks></chunk>'
        selected = '<chunk><chunks><chunk name="Attributes"><items count="2"><item name="Bounds">1</item><item name="Selected">true</item></items></chunk></chunks></chunk>'
        moved = selected.replace('>1</item>', '>2</item>')
        self.assertEqual(_without_saved_selection(before), _without_saved_selection(selected))
        self.assertNotEqual(_without_saved_selection(before), _without_saved_selection(moved))

    def test_parameter_serialization_preserves_whitespace_only_values(self):
        xml = '<chunk><items><item name="Value"> </item></items></chunk>'
        self.assertIn('> </item>', _without_saved_selection(xml))


if __name__ == '__main__':
    unittest.main()


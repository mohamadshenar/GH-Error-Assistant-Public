"""Opt-in Rhino UI-thread snapshot helper for saved disposable test documents.

No test runs on import. Call only after obtaining a separate explicitly approved
native test session. This helper does not open documents, solve or navigate.
Persistent trees are serialized in memory and selected parameter settings are
captured. Full coverage remains deliberately incomplete until all object types,
parameter settings and external Rhino geometry have independent verification.
"""
import json
import os
import uuid
import datetime
import re
import hashlib
import xml.etree.ElementTree as ET


def _plain_int(value):
    # IronPython's json encoder does not accept boxed .NET integral values.
    return int(value)


def _serialize_tree(tree):
    """Serialize persistent GH data to a detached in-memory chunk, never the document."""
    import clr
    clr.AddReference('GH_IO')
    from GH_IO.Serialization import GH_LooseChunk
    chunk = GH_LooseChunk('PersistentData')
    if not tree.Write(chunk):
        raise ValueError('Grasshopper persistent-data serialization returned false')
    return str(chunk.Serialize_Xml())


def _safe_builtin_tree(port):
    """Return a built-in tree only if its values cannot invoke third-party writers.

    Grasshopper's tree and parameter Write methods can delegate to each item's
    Write method. Never call either serializer when an unknown item is present.
    This conservative check is not a guarantee for unexamined SDK behavior.
    """
    if str(port.GetType().Assembly.GetName().Name) != 'Grasshopper':
        raise ValueError('Third-party parameter serializer is not inspected')
    tree = getattr(port, 'PersistentData', None)
    if tree is None:
        return None
    if str(tree.GetType().Assembly.GetName().Name) != 'Grasshopper':
        raise ValueError('Third-party persistent tree serializer is not inspected')
    for item in tree.AllData(False):
        if item is not None and str(item.GetType().Assembly.GetName().Name) != 'Grasshopper':
            raise ValueError('Third-party persistent item serializer is not inspected')
    return tree


def _serialize_builtin_parameter(port):
    import clr
    clr.AddReference('GH_IO')
    from GH_IO.Serialization import GH_LooseChunk
    # Unknown third-party serializers are not executed automatically.
    if str(port.GetType().Assembly.GetName().Name) != 'Grasshopper':
        return None
    chunk = GH_LooseChunk('ParameterState')
    if not port.Write(chunk):
        raise ValueError('Grasshopper parameter serialization returned false')
    return _without_saved_selection(str(chunk.Serialize_Xml()))


def _without_saved_selection(xml):
    """Exclude only top-level canvas selection, measured separately in selection."""
    root = ET.fromstring(xml)
    for chunk in root.findall('./chunks/chunk'):
        if chunk.get('name') != 'Attributes':
            continue
        items = chunk.find('items')
        if items is None:
            continue
        for item in list(items):
            if item.tag == 'item' and item.get('name') == 'Selected':
                items.remove(item)
        if items.get('count') is not None:
            items.set('count', str(len(list(items))))
    for element in root.iter():
        if len(element) and element.text is not None and not element.text.strip(): element.text = None
        if element.tail is not None and not element.tail.strip(): element.tail = None
    return ET.tostring(root, encoding='utf-8').decode('utf-8')


def _canonical_json_hash(value):
    """Hash JSON values after deterministic key ordering, retaining array order."""
    canonical = json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True)
    return hashlib.sha256(canonical.encode('ascii')).hexdigest()


def _rhino_object_fingerprint(obj):
    from Rhino.FileIO import SerializationOptions
    options = SerializationOptions()
    options.RhinoVersion = SerializationOptions.MaxRhinoVersion
    options.WriteUserData = True
    geometry = json.loads(str(obj.Geometry.ToJSON(options)))
    attributes = json.loads(str(obj.Attributes.ToJSON(options)))
    return {'id': str(obj.Id), 'geometryHash': _canonical_json_hash(geometry),
            'attributesHash': _canonical_json_hash(attributes)}


def capture(doc, canvas, rhino_doc, disposable_root, solution_events, expected_rhino_ids=None):
    import System
    from Grasshopper.Kernel import IGH_Component, IGH_Param
    from Grasshopper.Kernel.Types import IGH_GeometricGoo

    root = os.path.normcase(os.path.abspath(disposable_root)) + os.sep
    path = getattr(doc, 'FilePath', None)
    if not path or not os.path.normcase(os.path.abspath(path)).startswith(root):
        raise ValueError('Document must be saved inside the explicitly disposable test directory')
    if canvas is None or not System.Object.ReferenceEquals(canvas.Document, doc):
        raise ValueError('The disposable test document must be the active canvas document')
    if rhino_doc is None or rhino_doc.Path:
        raise ValueError('A fresh empty disposable Rhino document is required')
    if expected_rhino_ids is None:
        if rhino_doc.Objects.Count or rhino_doc.Modified:
            raise ValueError('A fresh empty disposable Rhino document is required')
    else:
        actual = sorted(str(obj.Id) for obj in rhino_doc.Objects)
        if actual != sorted(str(value) for value in expected_rhino_ids):
            raise ValueError('Rhino document objects do not match the explicitly declared disposable fixture')
    result = {'doc': str(doc.DocumentID), 'runtime': str(doc.RuntimeID),
              'dirty': bool(doc.IsModified), 'selection': [str(o.InstanceGuid) for o in doc.SelectedObjects()],
              'viewport': str(canvas.Viewport.MidPoint), 'zoom': str(canvas.Viewport.Zoom),
              'undo': _plain_int(doc.UndoServer.UndoCount), 'redo': _plain_int(doc.UndoServer.RedoCount),
              'rhinoObjects': _plain_int(rhino_doc.Objects.Count), 'rhinoDirty': bool(rhino_doc.Modified),
              'rhinoUndo': _plain_int(rhino_doc.CurrentUndoRecordSerialNumber),
              'eventCount': _plain_int(solution_events), 'objects': [],
              'parameterSettings': {}, 'persistentGeometry': {}, 'rhinoGeometry': {},
              'captureComplete': False, 'captureErrors': []}
    for obj in doc.Objects:
        ports = list(obj.Params.Input) + list(obj.Params.Output) if isinstance(obj, IGH_Component) else [obj] if isinstance(obj, IGH_Param) else []
        item = {'id': str(obj.InstanceGuid), 'type': obj.GetType().FullName, 'ports': []}
        for port in ports:
            data = None
            key = str(port.InstanceGuid)
            safe_tree = None
            serialization_safe = False
            try:
                safe_tree = _safe_builtin_tree(port)
                serialization_safe = True
            except Exception as exc:
                result['captureErrors'].append({'port': key, 'field': 'serializationSafety', 'error': str(exc)})
            try:
                data = [{'type': g.GetType().FullName if g else '', 'value': str(g),
                         'reference': str(g.ReferenceID) if isinstance(g, IGH_GeometricGoo) else ''}
                        for g in port.PersistentData.AllData(False)]
            except Exception as exc:
                result['captureErrors'].append({'port': str(port.InstanceGuid), 'error': str(exc)})
            try:
                result['parameterSettings'][key] = {'optional': bool(port.Optional),
                    'access': str(port.Access), 'locked': bool(port.Locked),
                    'nickname': str(port.NickName), 'name': str(port.Name),
                    'mutableNickname': bool(port.MutableNickName),
                    'dataMapping': str(port.DataMapping), 'reverse': bool(port.Reverse),
                    'simplify': bool(port.Simplify), 'wireDisplay': str(port.WireDisplay),
                    'serializedBuiltinState': _serialize_builtin_parameter(port) if serialization_safe else None}
            except Exception as exc:
                result['captureErrors'].append({'port': key, 'field': 'parameterSettings', 'error': str(exc)})
            try:
                # Some ports are not persistent parameters. Mark this explicitly;
                # absence is not the same thing as an empty persistent tree.
                result['persistentGeometry'][key] = {'available': safe_tree is not None if serialization_safe else None,
                    'xml': _serialize_tree(safe_tree) if serialization_safe and safe_tree is not None else None}
            except Exception as exc:
                result['captureErrors'].append({'port': key, 'field': 'persistentGeometry', 'error': str(exc)})
            item['ports'].append({'id': str(port.InstanceGuid), 'type': port.GetType().FullName,
                                  'sources': [str(s.InstanceGuid) for s in port.Sources],
                                  'recipients': [str(r.InstanceGuid) for r in port.Recipients],
                                  'persistent': data, 'access': str(port.Access), 'count': _plain_int(port.VolatileDataCount)})
        result['objects'].append(item)
    for obj in sorted(list(rhino_doc.Objects), key=lambda value: str(value.Id)):
        try:
            result['rhinoGeometry'][str(obj.Id)] = _rhino_object_fingerprint(obj)
        except Exception as exc:
            result['captureErrors'].append({'rhinoObject': str(obj.Id), 'field': 'rhinoGeometry', 'error': str(exc)})
    return result


def save_unique(directory, action, phase, data):
    if phase not in ('before', 'after'):
        raise ValueError('Phase must be before or after')
    if not re.match(r'^[A-Za-z0-9_-]+$', action):
        raise ValueError('Action must be a simple label')
    name = '{}-{}-{}-{}.json'.format(action, phase,
        datetime.datetime.utcnow().strftime('%Y%m%dT%H%M%S%fZ'), uuid.uuid4().hex)
    path = os.path.join(directory, name)
    # Exclusive creation avoids overwriting an earlier piece of evidence.
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL)
    with os.fdopen(fd, 'w') as stream:
        json.dump(data, stream, indent=2, ensure_ascii=True)
    return path


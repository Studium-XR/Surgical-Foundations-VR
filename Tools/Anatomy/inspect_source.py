"""Inspect Z-Anatomy without running its embedded scripts."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

root = Path(__file__).resolve().parent
report = {
    'blender': bpy.app.version_string,
    'units': bpy.context.scene.unit_settings.scale_length,
    'collections': [{'name': c.name, 'objects': len(c.objects)} for c in bpy.data.collections],
    'objects': [],
}
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH':
        continue
    points = [obj.matrix_world @ Vector(p) for p in obj.bound_box]
    report['objects'].append({
        'name': obj.name,
        'vertices': len(obj.data.vertices),
        'faces': len(obj.data.polygons),
        'collections': [c.name for c in obj.users_collection],
        'min': [min(p[i] for p in points) for i in range(3)],
        'max': [max(p[i] for p in points) for i in range(3)],
        'hidden': obj.hide_viewport,
    })
(root / 'source_inventory.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('ANATOMY_SUMMARY', json.dumps({
    'meshes': len(report['objects']),
    'vertices': sum(o['vertices'] for o in report['objects']),
    'key_meshes': [o for o in report['objects'] if any(k in o['name'].lower() for k in ['skin', 'liver', 'lung', 'heart', 'kidney', 'stomach'])],
})[:14000])

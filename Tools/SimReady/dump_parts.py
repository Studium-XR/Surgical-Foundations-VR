"""Lists the mesh-node names in each GLB (reads the glTF JSON chunk only). Usage: dump_parts.py <raw_dir> <out.json>"""
import json, os, struct, sys
def nodes(path):
    with open(path, 'rb') as f:
        magic, ver, length = struct.unpack('<4sII', f.read(12))
        clen, ctype = struct.unpack('<II', f.read(8))
        j = json.loads(f.read(clen))
    return sorted({n.get('name', '') for n in j.get('nodes', []) if 'mesh' in n})
out = {}
for r, d, fs in os.walk(sys.argv[1]):
    for fn in fs:
        if fn.endswith('.glb'):
            out[fn[:-4]] = nodes(os.path.join(r, fn))
json.dump(out, open(sys.argv[2], 'w'), indent=0)

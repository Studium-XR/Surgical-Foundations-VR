"""
Downloads the SimReady healthcare set (see catalog.py) as GLB into <out>/<category>/<name>.glb.

    py -3 -I Tools/SimReady/download_simready.py Tools/SimReady/_raw

Resumable: files already present at the right size are skipped. Writes <out>/manifest.json (name, category, size).
"""
import json
import os
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from catalog import DATASET, URL, category  # noqa: E402

API = "https://huggingface.co/api/datasets/" + DATASET


def list_assets():
    import re
    names, url = [], API + "/tree/main/engine"
    while url:
        r = urllib.request.urlopen(url, timeout=60)
        names += [x["path"].split("/")[1] for x in json.load(r) if x["type"] == "directory"]
        m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link") or "")
        url = m.group(1) if m else None
    return sorted(n for n in names if category(n))


def sizes(names):
    out = {}
    paths = ["engine/%s/%s.glb" % (n, n) for n in names]
    for i in range(0, len(paths), 100):
        req = urllib.request.Request(API + "/paths-info/main", data=json.dumps({"paths": paths[i:i + 100]}).encode(),
                                     headers={"Content-Type": "application/json"})
        for x in json.load(urllib.request.urlopen(req, timeout=60)):
            out[x["path"].split("/")[1]] = x.get("size", 0)
    return out


def main(out_dir):
    names = list_assets()
    size = sizes(names)
    total = sum(size.values())
    print("%d assets, %.2f GB" % (len(names), total / 1e9), flush=True)
    manifest = []
    done = 0
    for i, n in enumerate(names):
        cat = category(n)
        dst = os.path.join(out_dir, cat, n + ".glb")
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        want = size.get(n, 0)
        if not (os.path.exists(dst) and os.path.getsize(dst) == want):
            for attempt in range(4):
                try:
                    tmp = dst + ".part"
                    urllib.request.urlretrieve(URL.format(name=n), tmp)
                    os.replace(tmp, dst)
                    break
                except Exception as e:  # network hiccup: back off and retry
                    print("  retry %s (%s)" % (n, e), flush=True)
                    time.sleep(2 + attempt * 3)
            else:
                print("FAILED " + n, flush=True)
                continue
        done += want
        manifest.append({"name": n, "category": cat, "bytes": want})
        print("[%3d/%d] %-48s %6.1f MB  (%.0f%%)" % (i + 1, len(names), n, want / 1e6, 100.0 * done / max(total, 1)), flush=True)
    with open(os.path.join(out_dir, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=1)
    print("done: %d files" % len(manifest), flush=True)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "_raw"))

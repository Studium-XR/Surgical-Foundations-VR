"""
Rigs every downloaded SimReady GLB with Blender, in parallel, into the Unity project:

    py -3 -I Tools/SimReady/rig_all.py [--blender PATH] [--jobs 3] [--only name1,name2] [--force]

Input:  Tools/SimReady/_raw/<Category>/<name>.glb      (download_simready.py)
Output: Assets/_Project/Imports/simready_rigged/<Category>/<name>.glb          rigged, animated, decimated
        Assets/_Project/Imports/simready_rigged/<Category>/<name>.joints.json  joint manifest for ArticulatedDevice
        Tools/SimReady/_work/rig_report.json                                    what was rigged, joints, failures

Up-to-date outputs are skipped unless --force, so it can be re-run as more downloads land.
"""
import argparse
import concurrent.futures as cf
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
RAW = os.path.join(HERE, "_raw")
OUT = os.path.join(ROOT, "Assets", "_Project", "Imports", "simready_rigged")
SCRIPT = os.path.join(HERE, "rig_simready.py")
DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"


def jobs(only, force):
    for cat in sorted(os.listdir(RAW)):
        d = os.path.join(RAW, cat)
        if not os.path.isdir(d):
            continue
        for fn in sorted(os.listdir(d)):
            if not fn.endswith(".glb"):
                continue
            name = fn[:-4]
            if only and name not in only:
                continue
            src = os.path.join(d, fn)
            dst = os.path.join(OUT, cat, name + ".glb")
            man = os.path.join(OUT, cat, name + ".joints.json")
            if not force and os.path.exists(dst) and os.path.exists(man) and os.path.getmtime(dst) >= max(os.path.getmtime(src), os.path.getmtime(SCRIPT)):
                continue
            yield name, cat, src, dst, man


def run(blender, name, cat, src, dst, man):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    t = time.time()
    p = subprocess.run([blender, "-b", "--factory-startup", "-P", SCRIPT, "--", src, dst, man, cat],
                       capture_output=True, text=True, timeout=1800)
    line = next((l for l in p.stdout.splitlines() if l.startswith("[rig]")), "")
    ok = p.returncode == 0 and os.path.exists(dst) and os.path.exists(man)
    err = "" if ok else (p.stderr[-800:] or p.stdout[-800:])
    return {"name": name, "category": cat, "ok": ok, "seconds": round(time.time() - t, 1), "summary": line, "error": err}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--blender", default=DEFAULT_BLENDER)
    ap.add_argument("--jobs", type=int, default=3)
    ap.add_argument("--only", default="")
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()
    only = set(x for x in a.only.split(",") if x)
    todo = list(jobs(only, a.force))
    print(f"{len(todo)} models to rig", flush=True)
    report_path = os.path.join(HERE, "_work", "rig_report.json")
    report = {}
    if os.path.exists(report_path):
        report = {r["name"]: r for r in json.load(open(report_path))}
    with cf.ThreadPoolExecutor(a.jobs) as ex:
        futs = {ex.submit(run, a.blender, *j): j[0] for j in todo}
        for i, f in enumerate(cf.as_completed(futs), 1):
            try:
                r = f.result()
            except Exception as e:  # timeout or crash: record and move on
                r = {"name": futs[f], "ok": False, "error": str(e)}
            report[r["name"]] = r
            print(f"[{i}/{len(todo)}] {'OK ' if r['ok'] else 'ERR'} {r['name']} {r.get('summary', '')[6:120]} {r.get('error', '')[-200:]}", flush=True)
            os.makedirs(os.path.dirname(report_path), exist_ok=True)
            json.dump(sorted(report.values(), key=lambda x: x["name"]), open(report_path, "w"), indent=1)
    bad = [r for r in report.values() if not r["ok"]]
    print(f"done: {len(report) - len(bad)} rigged, {len(bad)} failed", flush=True)


if __name__ == "__main__":
    main()

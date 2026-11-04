"""Generate one HY-Motion 1.0 clip through the tencent/HY-Motion-1.0 Hugging Face Space.

Usage (needs `pip install gradio_client` and, for more GPU quota, `hf auth login`):
    python gen_motion.py <name> "<prompt>" <seconds 0.5-12> <output folder>
Writes <output folder>/HYM_<name>.fbx (SMPL-X skeleton). Then: put it in Assets/_Project/Imports/hymotion, add it to
StaffClips in Assets/_Project/Scripts/Editor/Builders/StaffAnimationKit.cs and run Surgical Foundations > Build > 1i.
Prompts: English, under 60 words, one person, body movements only (no objects, clothing, camera or loops).
Licence: Tencent HY-MOTION 1.0 Community License; not for use in the EU, UK or South Korea (see CREDITS.md).
"""
import shutil
import sys
import time

from gradio_client import Client
from huggingface_hub import get_token

name, prompt, duration, out_dir = sys.argv[1], sys.argv[2], float(sys.argv[3]), sys.argv[4]
client = Client("tencent/HY-Motion-1.0", token=get_token())
start = time.time()
_, files = client.predict(prompt, prompt, "0", duration, 5.0, api_name="/generate_motion_func")
fbx = next(f for f in files if f.lower().endswith(".fbx") and not f.lower().endswith("_1.fbx"))
dst = f"{out_dir}/HYM_{name}.fbx"
shutil.copy(fbx, dst)
print(f"{name}: {time.time() - start:.0f}s -> {dst}")

using System.Collections.Generic;
using UnityEngine;

namespace SurgicalFoundations.Detection
{
    /// <summary>
    /// Height of the visible skin over the operative field, so marks, landmark rings and trocar depth follow the real
    /// (curved) abdomen instead of one flat plane: at the left working port the skin sits a centimetre above the
    /// umbilicus, and on a flat plane the ring there was buried and the depth only started counting once the trocar was
    /// a centimetre into the skin. Built once from the skin meshes into a grid of the highest surface per cell.
    /// </summary>
    public class SkinSurface
    {
        const float Cell = 0.004f;

        readonly float[] heights; // NaN where no skin
        readonly int nx, nz;
        readonly Vector2 min;
        readonly float fallback;

        SkinSurface(int nx, int nz, Vector2 min, float fallback)
        {
            this.nx = nx;
            this.nz = nz;
            this.min = min;
            this.fallback = fallback;
            heights = new float[nx * nz];
            for (int i = 0; i < heights.Length; i++) heights[i] = float.NaN;
        }

        /// <summary>Rasterises the meshes' triangles (world space). Unreadable meshes are skipped; with none, every height is <paramref name="fallback"/>.</summary>
        public static SkinSurface Build(IEnumerable<MeshFilter> meshes, float fallback)
        {
            var tris = new List<Vector3>();
            foreach (var mf in meshes)
            {
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                if (!mesh.isReadable) { Debug.LogWarning($"[SkinSurface] {mf.name}: mesh not readable, left out."); continue; }
                var v = mesh.vertices;
                var t = mesh.triangles;
                var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < t.Length; i++) tris.Add(m.MultiplyPoint3x4(v[t[i]]));
            }
            if (tris.Count == 0) return new SkinSurface(1, 1, Vector2.zero, fallback);

            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in tris)
            {
                lo = Vector2.Min(lo, new Vector2(p.x, p.z));
                hi = Vector2.Max(hi, new Vector2(p.x, p.z));
            }
            var s = new SkinSurface(Mathf.CeilToInt((hi.x - lo.x) / Cell) + 2, Mathf.CeilToInt((hi.y - lo.y) / Cell) + 2, lo, fallback);
            for (int i = 0; i < tris.Count; i += 3) s.Rasterise(tris[i], tris[i + 1], tris[i + 2]);
            return s;
        }

        void Rasterise(Vector3 a, Vector3 b, Vector3 c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - min.x) / Cell));
            int x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - min.x) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - min.y) / Cell));
            int z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - min.y) / Cell));
            float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(det) < 1e-12f) return; // edge-on (a wall, not a surface)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float px = min.x + x * Cell, pz = min.y + z * Cell;
                float w0 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
                float w1 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
                float w2 = 1f - w0 - w1;
                if (w0 < -0.01f || w1 < -0.01f || w2 < -0.01f) continue;
                float y = w0 * a.y + w1 * b.y + w2 * c.y;
                ref var h = ref heights[z * nx + x];
                if (float.IsNaN(h) || y > h) h = y;
            }
        }

        float Sample(int x, int z)
        {
            if (x < 0 || z < 0 || x >= nx || z >= nz) return float.NaN;
            return heights[z * nx + x];
        }

        /// <summary>Skin height under a point (bilinear); the fallback height off the skin.</summary>
        public float HeightAt(Vector3 p)
        {
            float fx = (p.x - min.x) / Cell, fz = (p.z - min.y) / Cell;
            int x = Mathf.FloorToInt(fx), z = Mathf.FloorToInt(fz);
            float tx = fx - x, tz = fz - z;
            float h00 = Sample(x, z), h10 = Sample(x + 1, z), h01 = Sample(x, z + 1), h11 = Sample(x + 1, z + 1);
            float sum = 0f, weight = 0f;
            void Add(float h, float w) { if (!float.IsNaN(h) && w > 0f) { sum += h * w; weight += w; } }
            Add(h00, (1 - tx) * (1 - tz));
            Add(h10, tx * (1 - tz));
            Add(h01, (1 - tx) * tz);
            Add(h11, tx * tz);
            return weight > 0f ? sum / weight : fallback;
        }

        /// <summary>The point on the skin straight under (or over) <paramref name="p"/>.</summary>
        public Vector3 OnSkin(Vector3 p) => new Vector3(p.x, HeightAt(p), p.z);

        /// <summary>Surface normal from the height slope over ±3 cm (wide enough to ride over the step where the
        /// wall-layer skin meets the prepped area).</summary>
        public Vector3 NormalAt(Vector3 p)
        {
            const float d = 0.03f;
            float dx = HeightAt(p + Vector3.right * d) - HeightAt(p - Vector3.right * d);
            float dz = HeightAt(p + Vector3.forward * d) - HeightAt(p - Vector3.forward * d);
            return new Vector3(-dx, 2f * d, -dz).normalized;
        }

        /// <summary>Where a ray first meets the skin: 5 mm steps until it passes below the surface, then refined.</summary>
        public bool Raycast(Ray ray, float maxDistance, out Vector3 point)
        {
            const float step = 0.005f;
            point = default;
            float prev = 0f;
            for (float t = 0f; t <= maxDistance; t += step)
            {
                var p = ray.GetPoint(t);
                if (p.y > HeightAt(p)) { prev = t; continue; }
                if (t == 0f) return false; // starts under the skin
                float lo = prev, hi = t;
                for (int i = 0; i < 8; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var q = ray.GetPoint(mid);
                    if (q.y > HeightAt(q)) lo = mid; else hi = mid;
                }
                point = ray.GetPoint(hi);
                return true;
            }
            return false;
        }
    }
}

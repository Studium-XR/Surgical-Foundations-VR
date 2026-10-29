using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Unit meshes the primitive set lacks, saved once as assets under Art/Meshes/Procedural and shared by every prefab
    /// that scales them: a cone frustum (cylinder-like, along Y, height 1, base diameter 1) and a torus (ring diameter 1).
    /// </summary>
    public static class ProcMesh
    {
        const string Folder = SFPaths.Root + "/Art/Meshes/Procedural";
        const int Sides = 24;

        /// <summary>Frustum with the wide end (diameter 1) at −Y and the narrow end (diameter <paramref name="ratio"/>) at +Y; flipped if <paramref name="wideAtTop"/>.</summary>
        public static Mesh Frustum(float ratio, bool wideAtTop)
        {
            var path = $"{Folder}/Frustum_{ratio:0.00}{(wideAtTop ? "_up" : "")}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            float r0 = 0.5f, r1 = 0.5f * ratio;
            if (wideAtTop) (r0, r1) = (r1, r0);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            float slope = (r0 - r1); // over a height of 1
            for (int i = 0; i <= Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var normal = new Vector3(dir.x, slope, dir.z).normalized;
                v.Add(dir * r0 + Vector3.down * 0.5f); n.Add(normal); uv.Add(new Vector2(i / (float)Sides, 0));
                v.Add(dir * r1 + Vector3.up * 0.5f); n.Add(normal); uv.Add(new Vector2(i / (float)Sides, 1));
            }
            for (int i = 0; i < Sides; i++)
            {
                int a = i * 2;
                tri.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }
            Cap(v, n, uv, tri, -0.5f, r0, Vector3.down);
            Cap(v, n, uv, tri, 0.5f, r1, Vector3.up);
            return Store(Build("Frustum", v, n, uv, tri), path);
        }

        /// <summary>Torus in the XZ plane, ring radius 0.5 (centre of the tube), tube radius 0.5 × <paramref name="tubeRatio"/>.</summary>
        public static Mesh Torus(float tubeRatio)
        {
            var path = $"{Folder}/Torus_{tubeRatio:0.00}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            const int ring = 32, tube = 12;
            float R = 0.5f, r = 0.5f * tubeRatio;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int i = 0; i <= ring; i++)
            {
                float a = i * Mathf.PI * 2f / ring;
                var centre = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R;
                var outward = centre.normalized;
                for (int j = 0; j <= tube; j++)
                {
                    float b = j * Mathf.PI * 2f / tube;
                    var normal = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(centre + normal * r); n.Add(normal); uv.Add(new Vector2(i / (float)ring, j / (float)tube));
                }
            }
            for (int i = 0; i < ring; i++)
            for (int j = 0; j < tube; j++)
            {
                int a = i * (tube + 1) + j, c = a + tube + 1;
                tri.AddRange(new[] { a, a + 1, c, c, a + 1, c + 1 });
            }
            return Store(Build("Torus", v, n, uv, tri), path);
        }

        static void Cap(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, float y, float radius, Vector3 normal)
        {
            if (radius < 1e-4f) return;
            int centre = v.Count;
            v.Add(new Vector3(0, y, 0)); n.Add(normal); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides;
                v.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius)); n.Add(normal);
                uv.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            for (int i = 0; i < Sides; i++)
            {
                if (normal.y > 0) tri.AddRange(new[] { centre, centre + i + 2, centre + i + 1 });
                else tri.AddRange(new[] { centre, centre + i + 1, centre + i + 2 });
            }
        }

        static Mesh Build(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        static Mesh Store(Mesh mesh, string path)
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}

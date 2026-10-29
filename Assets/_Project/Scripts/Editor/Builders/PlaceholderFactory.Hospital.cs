using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Ward equipment picked out of Imports/Hospital equi, a whole furnished room in one untextured FBX. Each piece is
    /// one mesh with one flat material, so it is split into its connected parts (mattress, frame, casters, handles…),
    /// every part gets a textured surface by a per-prop rule, and the result is baked into a mesh asset with
    /// box-projected UVs in metres (Art/Meshes/Hospital).
    /// </summary>
    public static partial class PlaceholderFactory
    {
        // The room is modelled at ~1:5.5 (walls ≈ 0.49 units tall for a ~2.7 m ceiling) and its furniture fronts face −X.
        const float HospitalScale = 5.5f;
        const float HospitalYaw = 90f;
        const string HospitalMeshes = SFPaths.Root + "/Art/Meshes/Hospital";

        /// <summary>A connected part of a prop, measured in the finished prefab (metres, +Y up, front +Z).</summary>
        class Part
        {
            public string source;              // FBX mesh the part came from ("Bed", "Serum"…)
            public readonly List<int> tris = new List<int>();
            public Bounds bounds;
            public Vector3 C, S;                // centre and size relative to the whole prop (0..1 per axis)
            public int Tris => tris.Count / 3;
            public bool Flat(float max) => S.x < max && S.z < max; // small footprint
        }

        const string Steel = "Stainless_Satin", Rubber = "Rubber_Black";

        static void HospitalRoomProps()
        {
            if (!ImportedAssets.Exists(ImportedAssets.HospitalRoom)) return;
            ImportedAssets.PrepareHospitalRoom();
            if (!HospitalTextures.Exist()) HospitalTextures.Build();

            (string prefab, string id, string title, string folder, bool movable, string[] parts, Func<Part, string> surface)[] items =
            {
                ("EQ_HospitalBed", "EQ-X4", "Hospital bed", "Equipment", false, new[] { "Bed" }, p =>
                    p.C.y < 0.12f && p.Flat(0.12f) ? Rubber :                                          // casters
                    p.S.x > 0.8f && p.S.z > 0.8f && p.C.y > 0.4f && p.C.y < 0.8f ? "Hosp_Mattress" :
                    p.S.x > 0.8f && p.S.z > 0.5f && p.S.y > 0.4f ? "Hosp_Blanket" :                   // sheet draped to the floor
                    p.C.y > 0.5f && p.S.z < 0.35f && p.S.x > 0.35f && p.S.x < 0.85f ? "Hosp_Linen" : // pillow
                    p.S.z < 0.06f && p.S.y > 0.3f ? "Hosp_BedPanel" :                                 // head and foot boards
                    "Hosp_PowderCoat"),
                ("EQ_IVStand", "EQ-X5", "IV stand with infusion bag", "Equipment", true, new[] { "IvPole", "Serum" }, p =>
                    p.source == "Serum" ? "Hosp_IVBag" : Steel),
                ("EQ_OxygenCylinder", "EQ-X6", "Oxygen cylinders on a rack", "Equipment", true, new[] { "OxygenTube" }, p =>
                    p.S.y > 0.8f ? "Hosp_O2Cylinder" :
                    p.C.y > 0.9f && p.Flat(0.5f) ? "Hosp_Brass" :                                     // valves
                    p.C.y < 0.05f && p.Flat(0.2f) ? Rubber :
                    "Hosp_PowderCoatGrey"),
                ("EQ_MedicalCart", "EQ-X7", "Medical equipment cart", "Equipment", true, new[] { "Machine", "Machine_O" }, p =>
                    p.source == "Machine_O" ? "Hosp_O2Cylinder" :
                    p.C.y < 0.1f && p.Flat(0.15f) ? Rubber :
                    p.bounds.size.magnitude < 0.1f ? "Polymer_Dark" :                                 // knobs and buttons
                    p.C.y < 0.3f ? "Polymer_Grey" : "Polymer_White"),
                ("EQ_SupplyCabinet", "EQ-X8", "Glass-door supply cabinet", "Equipment", false, new[] { "Cabinet1", "Cabinet1Door", "Cabinet1DoorGlass" }, p =>
                    p.source == "Cabinet1DoorGlass" ? "Glass" :
                    p.source == "Cabinet1Door" && p.S.x < 0.1f ? Steel : "Hosp_Laminate"),            // door bar handle
                ("EQ_StorageCabinet", "EQ-X9", "Storage cabinet", "Equipment", false, new[] { "Cabinet2" }, p =>
                    p.S.y > 0.5f ? "Hosp_Laminate" : Steel),
                ("EQ_BedsideCabinet", "EQ-X10", "Bedside cabinet", "Equipment", false, new[] { "Commode" }, p =>
                    p.S.y > 0.5f ? "Hosp_WoodBeech" : Steel),
                ("EQ_OverbedTable", "EQ-X11", "Overbed table", "Equipment", true, new[] { "FoodTable" }, p =>
                    p.C.y < 0.15f && p.Flat(0.15f) ? Rubber :
                    p.C.y > 0.6f && p.S.y < 0.4f && p.S.x * p.S.z > 0.15f ? "Hosp_WoodBeech" :
                    "Hosp_PowderCoat"),
                ("EQ_SideTable", "EQ-X12", "Side table", "Equipment", false, new[] { "Table" }, p =>
                    p.C.y > 0.8f && p.S.y < 0.3f ? "Hosp_WoodBeech" : "Hosp_WoodWalnut"),
                ("FURN_VisitorChair", "ENV-X1", "Visitor chair", "Environment", false, new[] { "Chair" }, p =>
                    p.C.y < 0.35f && p.S.y < 0.45f && p.Flat(0.2f) ? "Hosp_WoodWalnut" : "Hosp_Upholstery"),
                ("FURN_Sofa", "ENV-X2", "Waiting sofa", "Environment", false, new[] { "Sofa" }, p =>
                    p.C.y < 0.15f && p.Flat(0.2f) ? "Hosp_WoodWalnut" :
                    p.Tris >= 400 ? "Hosp_Upholstery" : "Hosp_UpholsteryDark"),                       // seat cushions vs frame
            };
            foreach (var it in items)
            {
                if (it.prefab == "EQ_IVStand" && SimReadyIVStand()) continue;
                var b = new PB(it.prefab);
                var inst = TryParts(b, ImportedAssets.HospitalRoom, HospitalScale, HospitalYaw, it.parts);
                var mesh = BakeSurfaces(b, inst, it.prefab, it.surface, out int tris);
                b.FitColliderFromRenderers();
                b.Info(it.id, it.title, it.folder == "Environment" ? "Environment" : "Equipment", AssetRelease.MVP, tris,
                    $"Imported from Imports/Hospital equi (ward room FBX), scaled ×{HospitalScale}; split into {mesh.subMeshCount} textured surfaces. Room dressing.")
                    .isPlaceholder = false;
                if (!it.movable) b.MakeStatic(false); // no lightmap UVs: light from probes
                b.Save(Folder(it.folder));
            }
        }

        /// <summary>Replaces the FBX instance with one baked mesh: a submesh per surface, box-projected UVs in metres.</summary>
        static Mesh BakeSurfaces(PB b, GameObject inst, string name, Func<Part, string> surface, out int triCount)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var parts = new List<Part>();
            var toVisual = b.Visual.worldToLocalMatrix;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                var src = mf.sharedMesh;
                var m = toVisual * mf.transform.localToWorldMatrix;
                var nm = m.inverse.transpose;
                bool flip = m.determinant < 0f;
                int offset = verts.Count;
                verts.AddRange(src.vertices.Select(v => m.MultiplyPoint3x4(v)));
                normals.AddRange(src.normals.Select(n => nm.MultiplyVector(n).normalized));
                var t = src.triangles;
                if (flip)
                    for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                for (int i = 0; i < t.Length; i++) t[i] += offset;
                parts.AddRange(Split(mf.name.Replace("-material", ""), t, verts));
            }
            Object.DestroyImmediate(inst);

            var all = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) all.Encapsulate(v);
            Vector3 Rel(Vector3 a, Vector3 s) => new Vector3(a.x / Mathf.Max(s.x, 1e-4f), a.y / Mathf.Max(s.y, 1e-4f), a.z / Mathf.Max(s.z, 1e-4f));
            foreach (var p in parts)
            {
                p.C = Rel(p.bounds.center - all.min, all.size);
                p.S = Rel(p.bounds.size, all.size);
            }

            var groups = parts.GroupBy(surface).OrderBy(g => g.Key).ToList();
            var uvs = verts.Select((v, i) =>
            {
                var n = normals[i];
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                return ax >= ay && ax >= az ? new Vector2(v.z, v.y) : ay >= az ? new Vector2(v.x, v.z) : new Vector2(v.x, v.y);
            }).ToList();

            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = groups.Count;
            for (int i = 0; i < groups.Count; i++) mesh.SetTriangles(groups[i].SelectMany(p => p.tris).ToList(), i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            triCount = parts.Sum(p => p.Tris);

            mesh = StoreMesh(mesh, $"{HospitalMeshes}/{name}.asset");

            var go = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(b.Visual, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = groups.Select(g => MaterialLibrary.Get(g.Key)).ToArray();
            return mesh;
        }

        /// <summary>Saves a generated mesh as an asset, overwriting in place so the GUID (and every reference) survives rebuilds.</summary>
        static Mesh StoreMesh(Mesh mesh, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>Connected components of a triangle list (vertices welded by position, so hard edges don't split a part).</summary>
        static IEnumerable<Part> Split(string source, int[] tris, List<Vector3> verts)
        {
            var weld = new Dictionary<Vector3Int, int>();
            int Key(int v)
            {
                var k = Vector3Int.RoundToInt(verts[v] * 1e4f);
                if (!weld.TryGetValue(k, out var id)) weld[k] = id = weld.Count;
                return id;
            }
            var ids = tris.Select(Key).ToArray();
            var parent = Enumerable.Range(0, weld.Count).ToArray();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            for (int i = 0; i < ids.Length; i += 3)
            {
                int a = Find(ids[i]);
                parent[Find(ids[i + 1])] = a;
                parent[Find(ids[i + 2])] = a;
            }

            var byRoot = new Dictionary<int, Part>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                int r = Find(ids[i]);
                if (!byRoot.TryGetValue(r, out var p))
                    byRoot[r] = p = new Part { source = source, bounds = new Bounds(verts[tris[i]], Vector3.zero) };
                for (int k = 0; k < 3; k++)
                {
                    p.tris.Add(tris[i + k]);
                    p.bounds.Encapsulate(verts[tris[i + k]]);
                }
            }
            return byRoot.Values;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Import rules and setup for the Z-Anatomy derived body built by Tools/Anatomy/build_unity_anatomy.py.
    /// The FBX is metres, Y-up, facing +Z, with organs nested as group / organ / organ_LOD0 + organ_LOD1
    /// (Unity creates one LODGroup per organ from the _LODn suffixes).
    /// Materials are remapped by name onto Assets/_Project/Art/Materials/Anatomy/M_*.mat.
    /// </summary>
    public class AnatomyModelImport : AssetPostprocessor
    {
        const string ModelFolder = "Assets/_Project/Art/Models/Anatomy/";
        const string MaterialFolder = "Assets/_Project/Art/Materials/Anatomy";
        const string FbxPath = ModelFolder + "ANA_HumanMale.fbx";
        const string PrefabPath = "Assets/_Project/Prefabs/Anatomy/ANA_HumanMale_Anatomy.prefab";

        // Fallback colours for materials that do not exist yet (kept in sync with MATERIALS in build_unity_anatomy.py).
        static readonly Dictionary<string, (Color color, float smooth, float alpha)> Defaults = new()
        {
            ["M_Skin"] = (new Color(0.80f, 0.58f, 0.48f), 0.45f, 1f),
            ["M_Skin_Ghost"] = (new Color(0.80f, 0.58f, 0.48f), 0.55f, 0.18f),
            ["M_Hair"] = (new Color(0.10f, 0.06f, 0.04f), 0.40f, 1f),
            ["M_Eye"] = (new Color(0.92f, 0.90f, 0.88f), 0.90f, 1f),
            ["M_Iris"] = (new Color(0.25f, 0.35f, 0.45f), 0.80f, 1f),
            ["M_Lung"] = (new Color(0.86f, 0.56f, 0.54f), 0.50f, 1f),
            ["M_Pleura"] = (new Color(0.55f, 0.45f, 0.65f), 0.80f, 0.22f),
            ["M_Cartilage"] = (new Color(0.84f, 0.88f, 0.92f), 0.60f, 1f),
            ["M_Heart"] = (new Color(0.55f, 0.13f, 0.11f), 0.60f, 1f),
            ["M_Vessel_Artery"] = (new Color(0.70f, 0.12f, 0.10f), 0.65f, 1f),
            ["M_Vessel_Vein"] = (new Color(0.20f, 0.22f, 0.55f), 0.65f, 1f),
            ["M_Liver"] = (new Color(0.32f, 0.12f, 0.08f), 0.70f, 1f),
            ["M_Gallbladder"] = (new Color(0.36f, 0.42f, 0.14f), 0.75f, 1f),
            ["M_Stomach"] = (new Color(0.80f, 0.36f, 0.34f), 0.65f, 1f),
            ["M_Bowel"] = (new Color(0.90f, 0.60f, 0.60f), 0.70f, 1f),
            ["M_Colon"] = (new Color(0.80f, 0.47f, 0.37f), 0.65f, 1f),
            ["M_Gland"] = (new Color(0.88f, 0.66f, 0.48f), 0.55f, 1f),
            ["M_Spleen"] = (new Color(0.30f, 0.08f, 0.16f), 0.70f, 1f),
            ["M_Kidney"] = (new Color(0.55f, 0.15f, 0.12f), 0.70f, 1f),
            ["M_Duct"] = (new Color(0.92f, 0.82f, 0.55f), 0.65f, 1f),
            ["M_Bladder"] = (new Color(0.58f, 0.20f, 0.16f), 0.65f, 1f),
            ["M_Tissue_Pink"] = (new Color(0.86f, 0.52f, 0.48f), 0.60f, 1f),
            ["M_Tissue_Wet"] = (new Color(0.72f, 0.28f, 0.26f), 0.75f, 1f),
            ["M_Muscle"] = (new Color(0.55f, 0.16f, 0.14f), 0.50f, 1f),
            ["M_Peritoneum"] = (new Color(0.92f, 0.80f, 0.62f), 0.70f, 0.60f),
        };

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true; // Blender Z-up -> Unity Y-up baked into the meshes, no root rotation
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;

            foreach (var name in Defaults.Keys)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
                if (mat != null) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            }
        }

        /// <summary>One LODGroup per organ (Unity's automatic _LODn detection only handles the top level).</summary>
        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            // Unity's automatic root LODGroup would claim every organ's renderers; per-organ groups replace it.
            if (root.TryGetComponent<LODGroup>(out var auto)) Object.DestroyImmediate(auto);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var lod0 = t.Find(t.name + "_LOD0");
                if (lod0 == null) continue;
                var lod1 = t.Find(t.name + "_LOD1");
                if (!t.TryGetComponent<LODGroup>(out var group)) group = t.gameObject.AddComponent<LODGroup>();
                var r0 = lod0.GetComponents<Renderer>();
                group.SetLODs(lod1 == null
                    ? new[] { new LOD(0.01f, r0) }
                    : new[] { new LOD(0.12f, r0), new LOD(0.01f, lod1.GetComponents<Renderer>()) });
                group.RecalculateBounds();
            }
        }

        [MenuItem(SFPaths.MenuRoot + "Build/1k · Human Anatomy Model (Z-Anatomy)", priority = 4)]
        public static void SetUp()
        {
            if (!File.Exists(FbxPath))
            {
                EditorUtility.DisplayDialog("Anatomy", $"{FbxPath} not found.\nRun Tools/Anatomy/build_unity_anatomy.py in Blender first (see Docs/AnatomyPipeline.md).", "OK");
                return;
            }
            int created = 0;
            foreach (var kv in Defaults)
                if (EnsureMaterial(kv.Key, kv.Value.color, kv.Value.smooth, kv.Value.alpha)) created++;
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
            BuildPrefab();
            Debug.Log($"[Anatomy] Created {created} material(s), reimported {FbxPath}, wrote {PrefabPath}");
        }

        /// <summary>Teaching prefab: translucent skin so the organs show, peritoneal layers hidden.</summary>
        static void BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var ghost = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/M_Skin_Ghost.mat");
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith("Skin_LOD") && ghost != null) r.sharedMaterial = ghost;
                    r.shadowCastingMode = r.name.StartsWith("Skin_LOD") || r.name.StartsWith("Pleura_LOD")
                        ? UnityEngine.Rendering.ShadowCastingMode.Off
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Peritoneum") t.gameObject.SetActive(false);
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)!);
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static bool EnsureMaterial(string name, Color color, float smoothness, float alpha)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return false;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
            m.SetFloat("_Smoothness", smoothness);
            if (alpha < 1f)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.CreateAsset(m, path);
            return true;
        }
    }
}

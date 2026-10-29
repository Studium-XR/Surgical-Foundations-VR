using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Brings the third-party models in Assets/_Project/Imports up to project standard:
    ///  • texture import settings (normal maps, linear data maps, DirectX→OpenGL normal flip, 2K cap for Quest)
    ///  • URP Lit materials built from each texture set (metalness + roughness packed into MetallicSmoothness)
    ///  • material maps from the FBX's embedded material names to those project materials
    /// The prefab side lives in PlaceholderFactory.Imported.cs.
    /// </summary>
    public static class ImportedAssets
    {
        public const string Root = SFPaths.Root + "/Imports";
        public const string Nurse = Root + "/nurse-freemodel-fbx-simple/source/nurse.fbx";
        public const string Doctor = Root + "/doutordoctor/source/Doutor.fbx";
        public const string HeartLung = Root + "/heartlung-machine/source/Heartlung_Machine.fbx";
        public const string Microscope = Root + "/microscope/source/Microscope.fbx";
        public const string Monitor = Root + "/monitor-computer-realistic-computer-monitor/source/Monitor.fbx";
        public const string SurgeryTools = Root + "/surgery-tools-height-rez/source/Surgery Tools.fbx";
        public const string Patient = Root + "/patient/source/Patient.fbx";
        /// <summary>Floor-standing examination lamp. Ships without its texture, so it renders with the default material.</summary>
        public const string SurgeryLamp = Root + "/surgery-lamp/source/SurgeryLamp.fbx";
        /// <summary>A flat pair of surgical gloves (static meshes "gloves" = right, "glove_left"), fingers along +Z.</summary>
        public const string Gloves = Root + "/surgical-gloves/source/gloves.fbx";
        /// <summary>glTF binary: needs the glTFast package (com.unity.cloud.gltfast) to import.</summary>
        public const string ScrubSink = Root + "/scrubbing-sink/source/OT-sink.glb";
        /// <summary>A whole furnished ward room (Sketchfab) in one FBX; PlaceholderFactory picks the equipment out of it.</summary>
        public const string HospitalRoom = Root + "/Hospital equi/Untitled.fbx";

        const string MatFolder = SFPaths.Materials + "/Imported";
        const string PackedFolder = SFPaths.Textures + "/Imported";

        public static bool Exists(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

        [MenuItem(SFPaths.MenuRoot + "Build/1b · Imported Model Materials", priority = 2)]
        public static void BuildMaterials()
        {
            ConfigureTextures();
            Directory.CreateDirectory(MatFolder);
            Directory.CreateDirectory(PackedFolder);

            string hlm = Root + "/heartlung-machine/textures/";
            Pbr("Imp_HLM_Machine", hlm + "Machines_Albedo.tga.png", hlm + "Machines_Normal.tga.png", hlm + "Machines_Metalness.tga.png", hlm + "Machines_Roughness.tga.png", hlm + "Machines_AO.tga.png");
            Pbr("Imp_HLM_Structure", hlm + "Structure_Albedo.tga.png", hlm + "Structure_Normal.tga.png", hlm + "Structure_Metalness.tga.png", hlm + "Structure_Roughness.tga.png", hlm + "Structure_AO.tga.png");

            string mic = Root + "/microscope/textures/";
            Pbr("Imp_Microscope", mic + "Microscope_Base_Color.png", mic + "Microscope_Normal_DirectX.png", mic + "Microscope_Metallic.png", mic + "Microscope_Roughness.png", mic + "Microscope_Mixed_AO.png");

            string tools = Root + "/surgery-tools-height-rez/textures/";
            Pbr("Imp_Tools_Iron", tools + "Hospital_Iron_BaseColor.jpeg", tools + "Hospital_Iron_Normal.jpeg", tools + "Hospital_Iron_Metallic.jpeg", tools + "Hospital_Iron_Roughness.jpeg", null);
            Pbr("Imp_Tools_Steel", tools + "Hospital_Steel_BaseColor.jpeg", null, tools + "Hospital_Steel_Metallic.jpeg", tools + "Hospital_Steel_Roughness.jpeg", null);

            string nurse = Root + "/nurse-freemodel-fbx-simple/textures/nurse_";
            foreach (var part in new[] { "Body", "Bottom", "Hat", "Mask", "Shoes", "Top" })
                Dielectric("Imp_Nurse_" + part, nurse + part + "_diffuse.png", nurse + part + "_normal.png", part == "Shoes" ? 0.45f : part == "Body" ? 0.38f : 0.22f);

            string doc = Root + "/doutordoctor/textures/";
            Dielectric("Imp_Doctor", doc + "Doutor_color.jpeg", doc + "Doutor_low_nm.jpeg", 0.3f);

            string pat = Root + "/patient/textures/Patient_Packed0_";
            Pbr("Imp_Patient", pat + "BaseColor.png", pat + "Normal.png", pat + "Metallic.png", pat + "Roughness.png", null);

            // The hospital room FBX has no textures: its furniture gets generated surfaces (MaterialLibrary "Hosp_*").
            HospitalTextures.Build();
            PrepareHospitalRoom();

            AssetDatabase.SaveAssets();
            Debug.Log("[Surgical Foundations] Imported-model materials built in " + MatFolder);
        }

        /// <summary>FBX embedded material name → project material, per model.</summary>
        public static Dictionary<string, Material> MaterialMap(string model)
        {
            Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/M_{n}.mat");
            var d = new Dictionary<string, Material>();
            if (model == HeartLung)
            {
                d["Machine_Material"] = M("Imp_HLM_Machine");
                d["Structure_material"] = M("Imp_HLM_Structure");
                d["Couvercle_transparent_Material"] = MaterialLibrary.Get("Glass");
            }
            else if (model == Microscope) d["Microscope"] = M("Imp_Microscope");
            else if (model == SurgeryTools) { d["Iron"] = M("Imp_Tools_Iron"); d["Steel"] = M("Imp_Tools_Steel"); }
            else if (model == Nurse)
            {
                foreach (var part in new[] { "Body", "Bottom", "Hat", "Mask", "Shoes", "Top" }) d[part + "mat"] = M("Imp_Nurse_" + part);
            }
            else if (model == Doctor) d["_Body_Low"] = M("Imp_Doctor");
            else if (model == Patient)
            {
                d["Patient"] = M("Imp_Patient");
                d["Serviette"] = MaterialLibrary.Get("Drape_Teal"); // modesty towel; ships as a 20 % grey placeholder
            }
            // The sink's own texture set has lighting baked into it and renders near-black as metal under the scene's
            // probes, so it gets plain satin stainless instead.
            else if (model == ScrubSink) d["Sink_Baked.001"] = MaterialLibrary.Get("Hosp_SinkSteel");
            else if (model == Monitor)
            {
                d["Black metal"] = MaterialLibrary.Get("Polymer_Dark");
                d["White metal"] = MaterialLibrary.Get("Polymer_White");
            }
            return d;
        }

        /// <summary>
        /// PlaceholderFactory splits the room's meshes into parts (mattress, frame, handles…) to material them,
        /// which needs readable mesh data. The split meshes are saved as their own assets, so this costs no runtime memory.
        /// </summary>
        public static void PrepareHospitalRoom() => EnsureReadable(HospitalRoom);

        /// <summary>Editor-side mesh access for builders that measure or split a model (patient navel height, room parts).</summary>
        public static void EnsureReadable(string model)
        {
            if (!(AssetImporter.GetAtPath(model) is ModelImporter mi) || mi.isReadable) return;
            mi.isReadable = true;
            mi.SaveAndReimport();
        }

        public static void Remap(GameObject instance, Dictionary<string, Material> map)
        {
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && map.TryGetValue(mats[i].name, out var m) && m != null) mats[i] = m;
                r.sharedMaterials = mats;
            }
        }

        // ───────────── textures ─────────────

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                var file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool normal = file.Contains("normal") || file.EndsWith("_nm");
                bool data = file.Contains("metal") || file.Contains("rough") || file.Contains("_ao") || file.Contains("specular") || file.Contains("emissive");
                bool changed = false;
                void Set<T>(T current, T wanted, System.Action apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(); changed = true; } }

                if (normal)
                {
                    Set(ti.textureType, TextureImporterType.NormalMap, () => ti.textureType = TextureImporterType.NormalMap);
                    Set(ti.flipGreenChannel, file.Contains("directx"), () => ti.flipGreenChannel = file.Contains("directx"));
                }
                else if (data) Set(ti.sRGBTexture, false, () => ti.sRGBTexture = false);
                Set(ti.maxTextureSize, 2048, () => ti.maxTextureSize = 2048);
                if (changed) ti.SaveAndReimport();
            }
        }

        static Texture2D Tex(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        /// <summary>Metalness (R) + roughness (R) → RGBA with metal in R and smoothness (1 − roughness) in A.</summary>
        static Texture2D PackMetallicSmoothness(string name, Texture2D metal, Texture2D rough)
        {
            var outPath = $"{PackedFolder}/T_{name}_MetallicSmoothness.png";
            int size = Mathf.Min(2048, Mathf.Max(rough.width, metal != null ? metal.width : 0));
            var mat = new Material(Shader.Find("Hidden/SF/PackMetallicSmoothness"));
            mat.SetTexture("_MetalTex", metal);
            mat.SetTexture("_RoughTex", rough);
            mat.SetFloat("_HasMetal", metal != null ? 1 : 0);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(Texture2D.whiteTexture, rt, mat);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(mat);
            AssetDatabase.ImportAsset(outPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
            ti.sRGBTexture = false;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.maxTextureSize = 2048;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        // ───────────── materials ─────────────

        static Material NewLit(string name)
        {
            var path = $"{MatFolder}/M_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white);
            m.enableInstancing = true;
            return m;
        }

        static void Pbr(string name, string albedo, string normal, string metal, string rough, string ao)
        {
            var m = NewLit(name);
            m.SetTexture("_BaseMap", Tex(albedo));
            if (Tex(normal) != null) { m.SetTexture("_BumpMap", Tex(normal)); m.EnableKeyword("_NORMALMAP"); }
            if (Tex(rough) != null)
            {
                var packed = PackMetallicSmoothness(name, Tex(metal), Tex(rough));
                m.SetTexture("_MetallicGlossMap", packed);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", 1f);
                m.SetFloat("_Smoothness", 1f); // multiplier on the packed alpha
            }
            if (Tex(ao) != null) { m.SetTexture("_OcclusionMap", Tex(ao)); m.EnableKeyword("_OCCLUSIONMAP"); m.SetFloat("_OcclusionStrength", 1f); }
            EditorUtility.SetDirty(m);
        }

        static void Dielectric(string name, string albedo, string normal, float smoothness)
        {
            var m = NewLit(name);
            m.SetTexture("_BaseMap", Tex(albedo));
            if (Tex(normal) != null) { m.SetTexture("_BumpMap", Tex(normal)); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
        }
    }
}

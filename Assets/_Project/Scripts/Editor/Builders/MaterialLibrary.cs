using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Physically plausible URP materials for the placeholder art. Values are chosen so the grey-box already reads as
    /// an operating theatre under baked lighting (brushed steel, matte polymer, wet tissue, surgical drape teal).
    /// Re-running updates the existing assets in place, so references stay valid.
    /// </summary>
    public static class MaterialLibrary
    {
        public enum Kind { Lit, Unlit }

        class Spec
        {
            public string folder;
            public Color color;
            public float metallic;
            public float smoothness;
            public Color emission;
            public float emissionIntensity;
            public bool bakedEmission;
            public bool transparent;
            public bool doubleSided;
            public Kind kind = Kind.Lit;
            public string texture;
            public string normal;
            public float normalScale = 1f;
            public Vector2 tiling = Vector2.one;
            // Custom SF shader (Art/Shaders). When set, only the listed properties are applied.
            public string shader;
            public Dictionary<string, Color> colors;
            public Dictionary<string, float> floats;
        }

        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>();
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

        static void Def(string key, string folder, string hex, float metallic, float smooth) =>
            Specs[key] = new Spec { folder = folder, color = C(hex), metallic = metallic, smoothness = smooth };

        static void Emissive(string key, string folder, string hex, string emissionHex, float intensity, bool baked) =>
            Specs[key] = new Spec { folder = folder, color = C(hex), smoothness = 0.3f, emission = C(emissionHex), emissionIntensity = intensity, bakedEmission = baked };

        static void Custom(string key, string folder, string shader, Dictionary<string, Color> colors = null, Dictionary<string, float> floats = null) =>
            Specs[key] = new Spec { folder = folder, shader = shader, colors = colors ?? new Dictionary<string, Color>(), floats = floats ?? new Dictionary<string, float>() };

        static Dictionary<string, Color> Cols(params (string name, string hex)[] c)
        {
            var d = new Dictionary<string, Color>();
            foreach (var (n, h) in c) d[n] = C(h);
            return d;
        }

        static Dictionary<string, float> Flt(params (string name, float v)[] f)
        {
            var d = new Dictionary<string, float>();
            foreach (var (n, v) in f) d[n] = v;
            return d;
        }

        static MaterialLibrary()
        {
            // Environment — hospital-grade surfaces: cool grey-green walls, epoxy floor, satin ceiling.
            Def("Floor_Epoxy", "Environment", "6F7C7E", 0f, 0.62f);
            Def("Wall_OR", "Environment", "B9C7C4", 0f, 0.28f);
            Def("Wall_Skirting", "Environment", "4E5B5D", 0f, 0.4f);
            Def("Ceiling", "Environment", "E4E8E7", 0f, 0.15f);
            Def("Door_Steel", "Environment", "A7ADB0", 0.85f, 0.55f);
            Def("Cabinet_White", "Environment", "D8DDDC", 0f, 0.45f);
            Def("Lobby_Floor", "Environment", "FFFFFF", 0f, 0.55f);
            Specs["Lobby_Floor"].texture = SFPaths.Textures + "/T_LobbyGrid.png";
            Specs["Lobby_Floor"].tiling = new Vector2(10, 10);
            Def("Lobby_Wall", "Environment", "0E1B1D", 0f, 0.35f);
            Def("Lobby_Rug", "Environment", "123032", 0f, 0.1f);
            Def("Lab_Wall", "Environment", "C9D1CF", 0f, 0.25f);
            Def("Lab_Bench", "Environment", "D4D2CB", 0f, 0.5f);
            Def("Lab_Pegboard", "Environment", "8FA09D", 0f, 0.2f);
            Specs["Glass"] = new Spec { folder = "Environment", color = new Color(0.75f, 0.86f, 0.88f, 0.18f), smoothness = 0.95f, transparent = true };
            Emissive("Light_CeilingPanel", "Lighting", "FFFFFF", "FFF8F0", 2.2f, true);
            Emissive("Light_CovePanel", "Lighting", "5CE0C8", "5CE0C8", 1.6f, true);
            Emissive("Light_Lens", "Lighting", "FFFFFF", "FFF6EA", 6f, false);
            // Black until LaparoscopeFeed powers it on (keyword stays enabled so the property block can drive emission).
            Emissive("Screen_Laparoscope", "Equipment", "000000", "000000", 1f, false);
            Emissive("Screen_Vitals", "Equipment", "05100F", "3BE0A0", 1.4f, false);
            Emissive("Indicator_Mint", "Equipment", "5CE0C8", "5CE0C8", 2f, false);
            Emissive("Indicator_Amber", "Equipment", "F5B54B", "F5B54B", 2f, false);

            // Equipment
            Def("Stainless_Brushed", "Equipment", "B7BEC2", 0.95f, 0.72f);
            Def("Stainless_Satin", "Equipment", "9CA3A7", 0.9f, 0.5f);
            Def("Polymer_White", "Equipment", "E6E9E8", 0f, 0.5f);
            Def("Polymer_Grey", "Equipment", "5E676B", 0f, 0.45f);
            Def("Polymer_Dark", "Equipment", "1E2427", 0f, 0.5f);
            Def("Rubber_Black", "Equipment", "121416", 0f, 0.2f);
            Def("Mattress_Black", "Equipment", "1A1D20", 0f, 0.35f);
            Def("Screen_Off", "Equipment", "07090A", 0.2f, 0.9f);

            // Instruments — laparoscopic shafts are black insulated; jaws and trocars brushed steel.
            Def("Instrument_Insulation", "Instruments", "15191C", 0f, 0.65f);
            Def("Instrument_Handle", "Instruments", "2E363B", 0f, 0.55f);
            Def("Instrument_Steel", "Instruments", "C3C9CC", 1f, 0.8f);
            Def("Instrument_Titanium", "Instruments", "A9A39A", 1f, 0.7f);
            Def("Trocar_Polymer", "Instruments", "8A9499", 0f, 0.55f);
            // Modern laparoscopic set: white moulded handles, glossy black knobs, colour-coded threaded trocars.
            Def("Instrument_HandleWhite", "Instruments", "EEF1F2", 0f, 0.72f);
            Def("Instrument_KnobBlack", "Instruments", "0C0E10", 0.1f, 0.85f);
            Def("Trocar_Head", "Instruments", "A8AFB3", 0.1f, 0.55f);
            Def("Trocar_Green", "Instruments", "2FB59A", 0.05f, 0.75f);
            Def("Trocar_Blue", "Instruments", "4E5FC9", 0.05f, 0.75f);
            Def("Trocar_Clear", "Instruments", "D6E4EA", 0.1f, 0.88f);
            Def("Suture_Violet", "Instruments", "5B3E8C", 0f, 0.4f);
            Def("Swab_White", "Instruments", "F2F2EE", 0f, 0.05f);
            Def("Swab_Stripe", "Instruments", "2F63C9", 0f, 0.1f);

            // PPE
            // Surgical drapes (patient, back table, Mayo tray): the NVIDIA SimReady drape_surgical_a02 fabric, i.e. its
            // measured colour and a seamless tile of its crinkle normal map (CC BY 4.0). The key keeps its old name so
            // every prefab using M_Drape_Teal picks it up; the drape UVs span 0–1 over ~2.4 m, so 12 repeats ≈ 20 cm.
            Def("Drape_Teal", "PPE", "5F83AB", 0f, 0.1f);
            Specs["Drape_Teal"].normal = SFPaths.Textures + "/SimReady/T_SimReadyDrape_Normal.png";
            Specs["Drape_Teal"].normalScale = 1.2f;
            Specs["Drape_Teal"].tiling = new Vector2(12, 12);
            Def("Gown_Blue", "PPE", "4A7FA8", 0f, 0.15f);
            Def("Glove_Latex", "PPE", "D8C79E", 0f, 0.55f);
            Def("Glove_Contaminated", "PPE", "C8866F", 0f, 0.5f);
            Def("Packet_Paper", "PPE", "F0EEE6", 0f, 0.1f);
            Def("Scrubs_Teal", "PPE", "3E8A86", 0f, 0.1f);
            Def("Scrubs_Green", "PPE", "4E7F5B", 0f, 0.1f);
            Def("Cap_Blue", "PPE", "7FAFD1", 0f, 0.1f);
            Def("Mask_Blue", "PPE", "A9CDE3", 0f, 0.1f);

            // Anatomy — wet tissue is glossy; the insufflated cavity is seen from inside, so it is double sided.
            Def("Skin", "Anatomy", "D9A48A", 0f, 0.38f);
            Def("Skin_Prepped", "Anatomy", "C98A5A", 0f, 0.55f);
            Def("Fat", "Anatomy", "F0D27A", 0f, 0.55f);
            Def("Fascia", "Anatomy", "EDE6DA", 0f, 0.6f);
            Def("Peritoneum", "Anatomy", "E3A6A0", 0f, 0.75f);
            Def("Tissue_Wet", "Anatomy", "9E3A33", 0f, 0.78f);
            Specs["Tissue_Wet"].doubleSided = true;
            Def("Tissue_Pink", "Anatomy", "D98A83", 0f, 0.75f);
            Def("Liver", "Anatomy", "6B1F18", 0f, 0.8f);
            Def("Bowel", "Anatomy", "E0948A", 0f, 0.78f);
            Def("Duct", "Anatomy", "C9C06A", 0f, 0.7f);
            Def("Vessel_Artery", "Anatomy", "A3141C", 0f, 0.8f);
            Def("Vessel_Vein", "Anatomy", "3C3F8F", 0f, 0.8f);

            // Training props
            Def("PegBoard", "TrainingProps", "D9D4C7", 0f, 0.3f);
            Def("Peg", "TrainingProps", "8C8374", 0f, 0.4f);
            Def("Ring_Orange", "TrainingProps", "F4A43A", 0f, 0.5f);
            Def("Bead_Mint", "TrainingProps", "5CE0C8", 0f, 0.6f);
            Def("Bead_Coral", "TrainingProps", "FF8A79", 0f, 0.6f);
            Def("Controller_Neutral", "TrainingProps", "3A4448", 0f, 0.45f);
            Specs["Guided_Mint"] = new Spec { folder = "TrainingProps", color = new Color(0.36f, 0.88f, 0.78f, 0.9f), kind = Kind.Unlit, transparent = true };
            Specs["Guided_Amber"] = new Spec { folder = "TrainingProps", color = new Color(0.96f, 0.71f, 0.29f, 0.9f), kind = Kind.Unlit, transparent = true };
            Specs["UI_Dimmer"] = new Spec { folder = "Lighting", color = new Color(0.02f, 0.04f, 0.05f, 0.7f), kind = Kind.Unlit, transparent = true, doubleSided = true };
            Specs["Guided_DashedRing"] = new Spec { folder = "TrainingProps", color = new Color(0.36f, 0.88f, 0.78f, 1f), kind = Kind.Unlit, transparent = true, texture = SFPaths.Sprites + "/circle_dashed.png" };

            HospitalSurfaces();
            ApplyShaderLibrary();
        }

        /// <summary>
        /// Textured surfaces for the hospital-room furniture (HospitalTextures). Mesh UVs are box-projected in metres,
        /// so tiling = repeats per metre.
        /// </summary>
        static void HospitalSurfaces()
        {
            void Tex(string key, string surface, string hex, float metallic, float smooth, float tilesPerMetre, float normalScale = 1f, bool albedo = true) =>
                Specs[key] = new Spec
                {
                    folder = "Hospital", color = C(hex), metallic = metallic, smoothness = smooth,
                    texture = albedo ? HospitalTextures.Path(surface + "_Albedo") : null,
                    normal = HospitalTextures.Path(surface + "_Normal"), normalScale = normalScale,
                    tiling = Vector2.one * tilesPerMetre,
                };
            Tex("Hosp_Mattress", "Vinyl", "3D6E8E", 0f, 0.5f, 4f);
            Tex("Hosp_Linen", "Fabric", "EEF1F1", 0f, 0.08f, 8f, 0.8f);
            Tex("Hosp_Blanket", "Fabric", "C9D8E0", 0f, 0.06f, 6f, 0.9f);
            Tex("Hosp_BedPanel", "Wood", "FFFFFF", 0f, 0.45f, 1.25f, 0.5f);
            Tex("Hosp_PowderCoat", "PowderCoat", "D5DADA", 0.15f, 0.45f, 5f, 0.6f);
            Tex("Hosp_PowderCoatGrey", "PowderCoat", "7D878B", 0.2f, 0.45f, 5f, 0.6f);
            Tex("Hosp_Laminate", "Laminate", "F0F1EE", 0f, 0.42f, 2f, 0.4f);
            Tex("Hosp_WoodBeech", "Wood", "FFFFFF", 0f, 0.4f, 1.25f, 0.6f);
            Tex("Hosp_WoodWalnut", "Wood", "7A5A40", 0f, 0.35f, 1.5f, 0.6f);
            Tex("Hosp_Upholstery", "Fabric", "2F6F73", 0f, 0.12f, 8f);
            Tex("Hosp_UpholsteryDark", "Fabric", "22474A", 0f, 0.12f, 8f);
            Tex("Hosp_O2Cylinder", "PowderCoat", "2E7D4F", 0.2f, 0.55f, 5f, 0.4f);
            Def("Hosp_Brass", "Hospital", "C9A45C", 1f, 0.6f);
            // Plain URP Lit steel for the imported scrub sink: its UVs don't suit the Brushed Steel shader's streaks.
            Def("Hosp_SinkSteel", "Hospital", "C4CACD", 1f, 0.8f);
            Specs["Hosp_IVBag"] = new Spec { folder = "Hospital", color = new Color(0.92f, 0.96f, 1f, 0.35f), smoothness = 0.85f, transparent = true, doubleSided = true };
        }

        /// <summary>
        /// Moves materials onto the 20 custom SF shaders (Art/Shaders). Same keys give the same asset paths, so every
        /// prefab that already uses e.g. M_Drape_Teal picks up the new shader on the next build.
        /// </summary>
        static void ApplyShaderLibrary()
        {
            const string Tissue = "SF/Surgical/Wet Tissue", Steel = "SF/Surgical/Brushed Steel", Drape = "SF/Surgical/Surgical Drape",
                Glove = "SF/Surgical/Surgical Glove", SkinS = "SF/Surgical/Skin";

            // 01 Wet tissue (the cavity is viewed from inside, so it is double sided)
            Custom("Tissue_Wet", "Anatomy", Tissue, Cols(("_BaseColor", "9E3A33"), ("_MottleColor", "6E1E1A"), ("_VeinColor", "4A0710")), Flt(("_Cull", 0f), ("_MottleScale", 30f)));
            Custom("Tissue_Pink", "Anatomy", Tissue, Cols(("_BaseColor", "D98A83"), ("_MottleColor", "B8625C"), ("_VeinColor", "8C2030")), Flt(("_VeinAmount", 0.35f)));
            Custom("Liver", "Anatomy", Tissue, Cols(("_BaseColor", "6B1F18"), ("_MottleColor", "4A120E"), ("_VeinColor", "3A0A0C")), Flt(("_VeinAmount", 0.2f), ("_Wetness", 0.9f)));
            Custom("Bowel", "Anatomy", Tissue, Cols(("_BaseColor", "E0948A"), ("_MottleColor", "C87468"), ("_VeinColor", "A02838")), Flt(("_VeinAmount", 0.6f)));
            Custom("Peritoneum", "Anatomy", Tissue, Cols(("_BaseColor", "E3A6A0"), ("_MottleColor", "D08C86")), Flt(("_VeinAmount", 0.4f), ("_SSS", 0.6f)));
            // 02 Brushed steel
            Custom("Instrument_Steel", "Instruments", Steel, Cols(("_BaseColor", "C3C9CC")), Flt(("_Smoothness", 0.82f), ("_BrushAxis", 2f)));
            Custom("Instrument_Titanium", "Instruments", Steel, Cols(("_BaseColor", "A9A39A")), Flt(("_Smoothness", 0.7f)));
            Custom("Stainless_Brushed", "Equipment", Steel, Cols(("_BaseColor", "B7BEC2")), Flt(("_Smoothness", 0.72f), ("_BrushAxis", 0f), ("_BrushDensity", 500f)));
            Custom("Stainless_Satin", "Equipment", Steel, Cols(("_BaseColor", "9CA3A7")), Flt(("_Smoothness", 0.55f), ("_BrushAxis", 1f), ("_BrushStrength", 0.5f)));
            // 03 Drape, gown and scrubs fabric
            Custom("Gown_Blue", "PPE", Drape, Cols(("_BaseColor", "4A7FA8"), ("_SheenColor", "A8C8E8")), Flt(("_FoldScale", 5f)));
            Custom("Scrubs_Teal", "PPE", Drape, Cols(("_BaseColor", "3E8A86"), ("_SheenColor", "90C8C0")), Flt(("_WeaveScale", 800f)));
            Custom("Scrubs_Green", "PPE", Drape, Cols(("_BaseColor", "4E7F5B"), ("_SheenColor", "98C0A0")), Flt(("_WeaveScale", 800f)));
            Custom("Cap_Blue", "PPE", Drape, Cols(("_BaseColor", "7FAFD1"), ("_SheenColor", "C8E0F0")), Flt(("_FoldScale", 12f)));
            Custom("Mask_Blue", "PPE", Drape, Cols(("_BaseColor", "A9CDE3"), ("_SheenColor", "E0F0F8")), Flt(("_FoldScale", 15f)));
            // 04 Gloves
            Custom("Glove_Latex", "PPE", Glove, Cols(("_BaseColor", "D8C79E")), Flt(("_Contamination", 0f)));
            Custom("Glove_Contaminated", "PPE", Glove, Cols(("_BaseColor", "D8C79E")), Flt(("_Contamination", 0.75f)));
            // 05 Skin
            Custom("Skin", "Anatomy", SkinS, Cols(("_BaseColor", "D9A48A")));
            Custom("Skin_Prepped", "Anatomy", SkinS, Cols(("_BaseColor", "D9A48A")), Flt(("_Prep", 1f)));
            // 06 Blood, 13 Dissolve
            Custom("FX_Blood", "Anatomy", "SF/Surgical/Blood Pool");
            Custom("FX_Dissolve", "Lighting", "SF/FX/Dissolve");
            // 07 to 12 and 14: effects
            Custom("Guided_Mint", "TrainingProps", "SF/FX/Guided Highlight", Cols(("_Color", "5CE0C8")));
            Custom("Guided_Amber", "TrainingProps", "SF/FX/Guided Highlight", Cols(("_Color", "F5B54B")));
            Custom("Guided_DashedRing", "TrainingProps", "SF/FX/Dashed Ring", Cols(("_Color", "5CE0C8")), Flt(("_DotRadius", 0f)));
            Custom("FX_TargetPulse", "TrainingProps", "SF/FX/Target Pulse", Cols(("_Color", "5CE0C8")));
            Custom("FX_GhostHand", "Characters", "SF/FX/Ghost Hand");
            Custom("FX_WaterStream", "Equipment", "SF/FX/Water Stream");
            Custom("FX_SoapLather", "Equipment", "SF/FX/Soap Lather");
            Custom("FX_LaminarFlow", "Lighting", "SF/FX/Laminar Flow");
            // 15 and 16: screens (Screen_Laparoscope keeps its property names for LaparoscopeFeed)
            Custom("Screen_Laparoscope", "Equipment", "SF/UI/Laparoscope Screen", Cols(("_BaseColor", "FFFFFF"), ("_EmissionColor", "000000")));
            Custom("Screen_Vitals", "Equipment", "SF/UI/Vitals Monitor");
            // 17 and 18: environment
            Custom("Lobby_Floor", "Environment", "SF/Environment/Grid Floor");
            Custom("Light_CovePanel", "Lighting", "SF/Environment/Cove Glow", Cols(("_Color", "5CE0C8")), Flt(("_Intensity", 1.6f)));
            // 19 view vignette, 20 hologram
            Custom("UI_Dimmer", "Lighting", "SF/FX/View Vignette", Cols(("_Color", "050A0D")), Flt(("_Opacity", 0.62f), ("_EdgeOpacity", 0.3f)));
            Custom("FX_DeviationVignette", "Lighting", "SF/FX/View Vignette", Cols(("_Color", "FF8A79")), Flt(("_Opacity", 0f), ("_EdgeOpacity", 0f), ("_Inner", 0.35f), ("_Outer", 0.75f)));
            Custom("FX_Hologram", "Characters", "SF/FX/Hologram", Cols(("_Color", "5CE0C8")));
        }

        public static IEnumerable<string> Keys => Specs.Keys;

        public static void ClearCache() => Cache.Clear();

        public static Material Get(string key)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            if (!Specs.TryGetValue(key, out var spec))
            {
                Debug.LogError($"[MaterialLibrary] Unknown material key '{key}'");
                spec = new Spec { folder = "Environment", color = Color.magenta };
            }

            var dir = $"{SFPaths.Materials}/{spec.folder}";
            Directory.CreateDirectory(dir);
            var path = $"{dir}/M_{key}.mat";
            var shader = Shader.Find(spec.shader ?? (spec.kind == Kind.Unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            if (shader == null) { Debug.LogError($"[MaterialLibrary] Shader '{spec.shader}' not found for {key}"); shader = Shader.Find("Universal Render Pipeline/Lit"); }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;

            Apply(mat, spec);
            EditorUtility.SetDirty(mat);
            Cache[key] = mat;
            return mat;
        }

        static void Apply(Material m, Spec s)
        {
            if (s.shader != null)
            {
                // Reset to the shader's defaults first: a material migrated from URP Lit keeps stale values
                // (e.g. a white _BaseColor) for properties the new shader shares with Lit.
                var defaults = new Material(m.shader);
                m.CopyPropertiesFromMaterial(defaults);
                Object.DestroyImmediate(defaults);
                m.shaderKeywords = new string[0];
                m.renderQueue = -1; // queue comes from the shader
                foreach (var kv in s.colors) m.SetColor(kv.Key, kv.Value);
                foreach (var kv in s.floats) m.SetFloat(kv.Key, kv.Value);
                m.enableInstancing = true;
                return;
            }

            m.SetColor("_BaseColor", s.color);
            if (!string.IsNullOrEmpty(s.texture))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(s.texture);
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", s.tiling);
            }
            if (!string.IsNullOrEmpty(s.normal))
            {
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(s.normal));
                m.SetFloat("_BumpScale", s.normalScale);
                m.EnableKeyword("_NORMALMAP");
                m.SetTextureScale("_BaseMap", s.tiling); // URP Lit tiles every map with _BaseMap_ST
            }

            if (s.kind == Kind.Lit)
            {
                m.SetFloat("_Metallic", s.metallic);
                m.SetFloat("_Smoothness", s.smoothness);
                m.SetFloat("_EnvironmentReflections", 1f);
                m.SetFloat("_SpecularHighlights", 1f);
            }

            if (s.emissionIntensity > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", s.emission * s.emissionIntensity);
                m.globalIlluminationFlags = s.bakedEmission ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            m.SetFloat("_Cull", s.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.doubleSidedGI = s.doubleSided;

            if (s.transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }
        }
    }
}

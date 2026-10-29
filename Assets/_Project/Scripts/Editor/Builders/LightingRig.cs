using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Lighting pipeline for Quest-class hardware:
    ///  • Rooms are fully baked (GPU lightmapper, area lights + emissive panels, AO, 3 bounces).
    ///  • Surgical heads are Mixed spots (Baked Indirect): real-time specular on steel instruments and one soft shadow
    ///    over the operative field, baked bounce everywhere else.
    ///  • Dense light probes around the table so moving instruments, hands and characters pick up the bounce light.
    ///  • Box-projected reflection probes (room + a small high-priority probe at the table for instrument reflections).
    ///  • Neutral tonemapping + gentle grading (PC/editor; Quest stays post-free by default — see PlatformPostProcessing).
    /// </summary>
    public static class LightingRig
    {
        // ───────────── project-wide ─────────────

        [MenuItem(SFPaths.MenuRoot + "Build/0 · Rendering Settings", priority = 0)]
        public static void ConfigureRendering()
        {
            GraphicsSettings.lightsUseLinearIntensity = true;
            GraphicsSettings.lightsUseColorTemperature = true;

            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                var so = new SerializedObject(asset);
                TrySet(so, "m_AdditionalLightsRenderingMode", 1);        // per pixel
                TrySet(so, "m_AdditionalLightsPerObjectLimit", 4);       // two surgical heads + scope light + spare
                TrySet(so, "m_AdditionalLightShadowsSupported", 1);      // one shadowed surgical head
                TrySet(so, "m_AdditionalLightsShadowmapResolution", 1024);
                TrySet(so, "m_ReflectionProbeBlending", 1);
                TrySet(so, "m_ReflectionProbeBoxProjection", 1);         // correct reflections on steel in a box room
                TrySet(so, "m_SoftShadowsSupported", 1);
                TrySet(so, "m_MixedLightingSupported", 1);
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[Surgical Foundations] Configured {path}");
            }
            AssetDatabase.SaveAssets();
        }

        static void TrySet(SerializedObject so, string field, int value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[LightingRig] {so.targetObject.name}: no field {field}"); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value != 0;
            else p.intValue = value;
        }

        // ───────────── per-scene assets ─────────────

        public enum Profile { Theatre, Lobby, Lab }

        public static LightingSettings Settings(string name, float texelsPerUnit, int maxAtlas)
        {
            Directory.CreateDirectory(SFPaths.LightingSettings);
            var path = $"{SFPaths.LightingSettings}/LS_{name}.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null)
            {
                ls = new LightingSettings { name = "LS_" + name };
                AssetDatabase.CreateAsset(ls, path);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;          // "Baked Indirect"
            ls.lightmapResolution = texelsPerUnit;
            ls.lightmapPadding = 4;
            ls.lightmapMaxSize = maxAtlas;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directionalityMode = LightmapsMode.NonDirectional;       // half the lightmap memory on Quest
            ls.ao = true;
            ls.aoMaxDistance = 0.6f;
            ls.aoExponentIndirect = 1f;
            ls.aoExponentDirect = 0.2f;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 512;
            ls.environmentSampleCount = 256;
            ls.maxBounces = 3;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            ls.lightProbeSampleCountMultiplier = 4f;
            EditorUtility.SetDirty(ls);
            return ls;
        }

        public static VolumeProfile PostProfile(Profile p)
        {
            Directory.CreateDirectory(SFPaths.VolumeProfiles);
            var path = $"{SFPaths.VolumeProfiles}/VP_{p}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            var wb = profile.Add<WhiteBalance>(true);
            var bloom = profile.Add<Bloom>(true);
            switch (p)
            {
                case Profile.Theatre: // clinical: slightly cool, crisp, restrained highlights
                    ca.postExposure.Override(-0.25f); ca.contrast.Override(10f); ca.saturation.Override(-6f);
                    wb.temperature.Override(-6f); wb.tint.Override(-2f);
                    bloom.intensity.Override(0.12f); bloom.threshold.Override(1.2f); bloom.scatter.Override(0.5f);
                    break;
                case Profile.Lobby: // calm dark teal to match the UI palette
                    ca.postExposure.Override(0.1f); ca.contrast.Override(14f); ca.saturation.Override(-4f);
                    wb.temperature.Override(-10f); wb.tint.Override(-6f);
                    bloom.intensity.Override(0.35f); bloom.threshold.Override(0.9f); bloom.scatter.Override(0.65f);
                    break;
                default:
                    ca.postExposure.Override(0.1f); ca.contrast.Override(8f);
                    wb.temperature.Override(-3f);
                    bloom.intensity.Override(0.1f); bloom.threshold.Override(1.2f);
                    break;
            }
            foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        public static void ApplyEnvironment(Color ambient, LightingSettings ls)
        {
            Lightmapping.lightingSettings = ls;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.subtractiveShadowColor = new Color(0.25f, 0.29f, 0.3f);
        }

        // ───────────── scene objects ─────────────

        public static Light Area(Transform parent, string name, Vector3 pos, Vector2 size, float intensity, float kelvin, float range = 8f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90, 0, 0); // emit downward
            var l = go.AddComponent<Light>();
            l.type = LightType.Rectangle;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.areaSize = size;
            l.intensity = intensity;
            l.range = range;
            l.useColorTemperature = true;
            l.colorTemperature = kelvin;
            l.color = Color.white;
            return l;
        }

        public static Light Spot(Transform parent, string name, Vector3 pos, Vector3 lookAt, float angle, float intensity, float kelvin,
            LightmapBakeType mode, LightShadows shadows, float range = 5f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.lightmapBakeType = mode;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.55f;
            l.intensity = intensity;
            l.range = range;
            l.useColorTemperature = true;
            l.colorTemperature = kelvin;
            l.shadows = shadows;
            l.shadowStrength = 0.8f;
            return l;
        }

        public static ReflectionProbe Reflection(Transform parent, string name, Vector3 center, Vector3 size, int importance = 1, int resolution = 256)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = size;
            rp.importance = importance;
            rp.resolution = resolution;
            rp.blendDistance = 0.4f;
            rp.hdr = false; // LDR cubemaps: half the memory, fine for Quest
            return rp;
        }

        /// <summary>Grid of probes over a volume plus an optional dense block (e.g. around the operating table).</summary>
        public static LightProbeGroup Probes(Transform parent, string name, Bounds room, float spacing, float[] heights, Bounds? dense = null, float denseSpacing = 0.3f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var pts = new List<Vector3>();
            for (float x = room.min.x + spacing * 0.5f; x < room.max.x; x += spacing)
            for (float z = room.min.z + spacing * 0.5f; z < room.max.z; z += spacing)
                foreach (var y in heights) pts.Add(new Vector3(x, y, z));
            if (dense.HasValue)
            {
                var d = dense.Value;
                for (float x = d.min.x; x <= d.max.x + 0.001f; x += denseSpacing)
                for (float y = d.min.y; y <= d.max.y + 0.001f; y += denseSpacing)
                for (float z = d.min.z; z <= d.max.z + 0.001f; z += denseSpacing)
                    pts.Add(new Vector3(x, y, z));
            }
            var g = go.AddComponent<LightProbeGroup>();
            g.probePositions = pts.ToArray();
            return g;
        }

        public static Volume GlobalVolume(Transform parent, VolumeProfile profile)
        {
            var go = new GameObject("PostProcess_Global");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = profile;
            v.priority = 0;
            return v;
        }

        // ───────────── room rigs ─────────────

        // Theatre baked light levels. Post-processing (and so tonemapping) is off on the headset, so these set what the
        // learner sees directly; at 4.5 / 5 the white walls and drapes clipped.
        public const float TheatreLaminarIntensity = 3f, TheatreRoomIntensity = 3.5f;

        /// <summary>Theatre: 8 baked area lights matching the ceiling panels, table-focused probes and reflections.</summary>
        public static void TheatreRig(Transform parent)
        {
            const float H = 3f;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                Area(parent, $"Area_Laminar_{(ix < 0 ? "W" : "E")}{(iz < 0 ? "S" : "N")}", new Vector3(ix * 0.78f, H - 0.06f, iz * 0.78f), new Vector2(1.45f, 1.45f), TheatreLaminarIntensity, 5000f);
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                Area(parent, $"Area_Room_{(ix < 0 ? "W" : "E")}{(iz < 0 ? "S" : "N")}", new Vector3(ix * 2.5f, H - 0.03f, iz * 2.5f), new Vector2(1.2f, 0.6f), TheatreRoomIntensity, 5000f);
            Area(parent, "Area_ScrubAlcove", new Vector3(-2f, H - 0.03f, -4.82f), new Vector2(1.2f, 0.6f), TheatreRoomIntensity, 4800f);

            Probes(parent, "LightProbes", new Bounds(new Vector3(-0.0f, 1.5f, -0.9f), new Vector3(7f, 3f, 8.8f)), 1.0f, new[] { 0.3f, 1.1f, 1.8f, 2.6f },
                new Bounds(new Vector3(0, 1.25f, 0), new Vector3(2.4f, 0.7f, 1.4f)), 0.35f);
            Reflection(parent, "Reflection_Room", new Vector3(0, 1.5f, 0), new Vector3(7f, 3f, 7f), 1);
            Reflection(parent, "Reflection_Table", new Vector3(0, 1.2f, 0), new Vector3(2.8f, 1.2f, 1.8f), 2);
            Reflection(parent, "Reflection_Alcove", new Vector3(-2f, 1.5f, -4.62f), new Vector3(3f, 3f, 2f), 1, 128);
        }

        public static void LobbyRig(Transform parent)
        {
            Area(parent, "Area_Centre", new Vector3(0, 3.9f, 0), new Vector2(3f, 3f), 1.2f, 6500f, 10f);
            Spot(parent, "Spot_FeatureWall", new Vector3(0, 3.7f, 2.2f), new Vector3(0, 1.4f, 4.9f), 70f, 2.5f, 5200f, LightmapBakeType.Baked, LightShadows.Soft, 6f);
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                var l = Spot(parent, $"Spot_Cove_{i}", dir * 4.7f + Vector3.up * 3.55f, dir * 4.95f + Vector3.up * 0.5f, 100f, 0.8f, 7500f, LightmapBakeType.Baked, LightShadows.None, 4f);
                l.color = new Color(0.6f, 1f, 0.92f);
                l.useColorTemperature = false;
            }
            Probes(parent, "LightProbes", new Bounds(new Vector3(0, 2f, 0), new Vector3(9f, 4f, 9f)), 1.5f, new[] { 0.4f, 1.4f, 2.6f });
            Reflection(parent, "Reflection_Room", new Vector3(0, 2f, 0), new Vector3(10f, 4f, 10f), 1, 128);
        }

        public static void LabRig(Transform parent)
        {
            Area(parent, "Area_A", new Vector3(0, 2.94f, 1.0f), new Vector2(1.2f, 0.6f), 5f, 4800f);
            Area(parent, "Area_B", new Vector3(0, 2.94f, -1.2f), new Vector2(1.2f, 0.6f), 5f, 4800f);
            // Task light over the training table: mixed so props and hands get a real-time highlight + soft shadow.
            Spot(parent, "Spot_TaskLight", new Vector3(0.3f, 2.4f, 0.2f), new Vector3(0, 0.9f, 0.6f), 55f, 4f, 4300f, LightmapBakeType.Mixed, LightShadows.Soft, 4f);
            Probes(parent, "LightProbes", new Bounds(new Vector3(0, 1.5f, 0), new Vector3(6f, 3f, 6f)), 1f, new[] { 0.3f, 1.1f, 1.9f },
                new Bounds(new Vector3(0, 1.15f, 0.6f), new Vector3(1.4f, 0.5f, 0.7f)), 0.35f);
            Reflection(parent, "Reflection_Room", new Vector3(0, 1.5f, 0), new Vector3(6f, 3f, 6f), 1, 128);
        }
    }
}

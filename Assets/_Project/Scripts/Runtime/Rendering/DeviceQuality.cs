using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace SurgicalFoundations.Rendering
{
    /// <summary>The headsets the app recognises, grouped by what their GPU can carry.</summary>
    public enum HeadsetTier
    {
        /// <summary>Quest 1/2, Pico Neo 3, Vive Focus 3: XR2 Gen 1 or older.</summary>
        Entry,
        /// <summary>Quest Pro, Pico 4: XR2 Gen 1 / XR2+ with more memory and better optics.</summary>
        Standard,
        /// <summary>Quest 3, Quest 3S, Pico 4 Ultra: XR2 Gen 2.</summary>
        High,
        /// <summary>Android XR (Galaxy XR, XR2+ Gen 2) and capable PC VR.</summary>
        Ultra,
    }

    /// <summary>What the learner picked in the palm menu. Auto follows the detected headset.</summary>
    public enum QualityPreference { Auto, Performance, Fidelity }

    /// <summary>Everything one quality level sets. Values were chosen against the 72/90 Hz budget of each tier.</summary>
    [Serializable]
    public struct QualityProfile
    {
        public string name;
        public float renderScale;      // eye buffer resolution (1 = native panel resolution)
        public float minRenderScale;   // floor for the adaptive controller
        public int msaa;               // 1, 2, 4, 8
        public float refreshRate;      // Hz requested from the runtime (0 = leave as is)
        public float foveation;        // 0 (off) … 1 (strongest fixed foveation)
        public bool shadows;
        public int shadowResolution;   // main light shadow map
        public float shadowDistance;   // metres
        public int shadowCascades;
        public int perObjectLights;
        public bool postProcessing;
        public bool hdr;
        public float lodBias;
        public int textureMipLimit;    // 0 = full resolution textures, 1 = half
        public bool softParticles;
    }

    /// <summary>
    /// Detects the headset, picks a quality profile for it and applies it to URP and the XR display; then keeps the
    /// frame rate up by trimming the eye-buffer resolution when frames are missed and restoring it when there is room
    /// again (dynamic resolution). The learner can override the automatic choice in the palm menu; the choice is saved.
    /// Installs itself.
    /// </summary>
    public class DeviceQuality : MonoBehaviour
    {
        public static DeviceQuality Instance { get; private set; }

        public HeadsetTier Tier { get; private set; }
        public string HeadsetName { get; private set; }
        public QualityPreference Preference { get; private set; }
        public QualityProfile Active { get; private set; }
        public float CurrentRenderScale => urp != null ? urp.renderScale : 1f;
        public event Action Changed;

        const string PrefKey = "sf.quality";

        UniversalRenderPipelineAsset urp;
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        // Adaptive resolution.
        float smoothedFrame;
        float lastAdapt;
        const float AdaptInterval = 1.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (FindAnyObjectByType<DeviceQuality>() != null) return;
            var go = new GameObject("DeviceQuality");
            DontDestroyOnLoad(go);
            go.AddComponent<DeviceQuality>();
        }

        void Awake()
        {
            Instance = this;
            // Work on a copy so play mode and runtime tuning never edit the project's pipeline asset.
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (source != null)
            {
                urp = Instantiate(source);
                urp.name = source.name + " (runtime)";
                QualitySettings.renderPipeline = urp;
            }
            (Tier, HeadsetName) = Detect();
            Preference = (QualityPreference)PlayerPrefs.GetInt(PrefKey, (int)QualityPreference.Auto);
            Apply();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─────────────────────────────── detection ───────────────────────────────

        /// <summary>The headset tier from the device model and GPU. Pure, so it can be unit-tested.</summary>
        public static HeadsetTier Classify(string deviceModel, string gpu, RuntimePlatform platform, int vramMb)
        {
            var m = (deviceModel ?? "").ToLowerInvariant();
            var g = (gpu ?? "").ToLowerInvariant();

            if (platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.LinuxPlayer
                || platform == RuntimePlatform.WindowsEditor || platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.LinuxEditor)
                return vramMb >= 8000 ? HeadsetTier.Ultra : vramMb >= 4000 ? HeadsetTier.High : HeadsetTier.Standard;

            if (m.Contains("quest 3") || m.Contains("eureka") || m.Contains("panther")) return HeadsetTier.High; // Quest 3 / 3S
            if (m.Contains("quest pro") || m.Contains("seacliff")) return HeadsetTier.Standard;
            if (m.Contains("quest")) return HeadsetTier.Entry;                                                    // Quest 1 / 2
            if (m.Contains("pico 4 ultra") || m.Contains("a9210")) return HeadsetTier.High;
            if (m.Contains("pico 4") || m.Contains("a8110") || m.Contains("a8150")) return HeadsetTier.Standard;
            if (m.Contains("sm-i610") || m.Contains("galaxy xr") || m.Contains("moohan")) return HeadsetTier.Ultra; // Android XR

            // Unknown Android headset: go by the GPU generation.
            if (g.Contains("adreno") && (g.Contains("750") || g.Contains("adreno (tm) 8"))) return HeadsetTier.Ultra;
            if (g.Contains("adreno") && g.Contains("740")) return HeadsetTier.High;
            if (g.Contains("adreno") && g.Contains("650")) return HeadsetTier.Entry;
            return HeadsetTier.Entry;
        }

        static (HeadsetTier, string) Detect()
        {
            var model = SystemInfo.deviceModel;
            var tier = Classify(model, SystemInfo.graphicsDeviceName, Application.platform, SystemInfo.graphicsMemorySize);
            var name = Application.isMobilePlatform ? model : $"PC VR · {SystemInfo.graphicsDeviceName}";
            return (tier, name);
        }

        // ─────────────────────────────── profiles ───────────────────────────────

        public static QualityProfile ProfileFor(HeadsetTier tier) => tier switch
        {
            HeadsetTier.Entry => new QualityProfile
            {
                name = "Entry", renderScale = 1.0f, minRenderScale = 0.8f, msaa = 2, refreshRate = 72f, foveation = 1f,
                shadows = false, shadowResolution = 512, shadowDistance = 3f, shadowCascades = 1, perObjectLights = 2,
                postProcessing = false, hdr = false, lodBias = 0.7f, textureMipLimit = 1, softParticles = false,
            },
            HeadsetTier.Standard => new QualityProfile
            {
                name = "Standard", renderScale = 1.1f, minRenderScale = 0.85f, msaa = 4, refreshRate = 72f, foveation = 0.66f,
                shadows = true, shadowResolution = 1024, shadowDistance = 5f, shadowCascades = 1, perObjectLights = 4,
                postProcessing = false, hdr = false, lodBias = 1.0f, textureMipLimit = 0, softParticles = false,
            },
            HeadsetTier.High => new QualityProfile
            {
                name = "High", renderScale = 1.25f, minRenderScale = 0.9f, msaa = 4, refreshRate = 90f, foveation = 0.33f,
                shadows = true, shadowResolution = 2048, shadowDistance = 8f, shadowCascades = 2, perObjectLights = 4,
                postProcessing = true, hdr = false, lodBias = 1.5f, textureMipLimit = 0, softParticles = true,
            },
            _ => new QualityProfile
            {
                name = "Ultra", renderScale = 1.4f, minRenderScale = 1.0f, msaa = 4, refreshRate = 90f, foveation = 0.2f,
                shadows = true, shadowResolution = 4096, shadowDistance = 12f, shadowCascades = 4, perObjectLights = 8,
                postProcessing = true, hdr = true, lodBias = 2.0f, textureMipLimit = 0, softParticles = true,
            },
        };

        /// <summary>The tier a preference resolves to on this headset: Performance one step below, Fidelity one above.</summary>
        public static HeadsetTier Resolve(HeadsetTier detected, QualityPreference pref)
        {
            int t = (int)detected + pref switch
            {
                QualityPreference.Performance => -1,
                QualityPreference.Fidelity => 1,
                _ => 0,
            };
            return (HeadsetTier)Mathf.Clamp(t, 0, (int)HeadsetTier.Ultra);
        }

        public void SetPreference(QualityPreference pref)
        {
            Preference = pref;
            PlayerPrefs.SetInt(PrefKey, (int)pref);
            PlayerPrefs.Save();
            Apply();
        }

        public static string Label(QualityPreference p) => p switch
        {
            QualityPreference.Performance => "Performance",
            QualityPreference.Fidelity => "Fidelity",
            _ => "Auto",
        };

        // ─────────────────────────────── applying ───────────────────────────────

        void Apply()
        {
            var tier = Resolve(Tier, Preference);
            var p = ProfileFor(tier);
            Active = p;

            if (urp != null)
            {
                urp.renderScale = p.renderScale;
                urp.msaaSampleCount = p.msaa;
                urp.shadowDistance = p.shadows ? p.shadowDistance : 0f;
                urp.mainLightShadowmapResolution = p.shadowResolution;
                urp.shadowCascadeCount = p.shadowCascades;
                urp.maxAdditionalLightsCount = p.perObjectLights;
                urp.supportsHDR = p.hdr;
            }
            QualitySettings.lodBias = p.lodBias;
            QualitySettings.globalTextureMipmapLimit = p.textureMipLimit;
            QualitySettings.softParticles = p.softParticles;
            QualitySettings.anisotropicFiltering = tier >= HeadsetTier.High ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;

            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                if (!d.running) continue;
                d.foveatedRenderingLevel = p.foveation;
                // Eye-tracked foveation where the headset has eye tracking (Quest Pro); fixed elsewhere.
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                RequestRefreshRate(d, p.refreshRate);
            }

            ApplyPostProcessing(p.postProcessing);
            smoothedFrame = 0f;
            Debug.Log($"[Quality] {HeadsetName} → tier {Tier}, preference {Preference}, profile {p.name} " +
                      $"(scale {p.renderScale}, MSAA {p.msaa}x, {p.refreshRate} Hz, foveation {p.foveation})");
            Changed?.Invoke();
        }

        static void ApplyPostProcessing(bool on)
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!cam.CompareTag("MainCamera")) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = on;
            }
        }

        static void RequestRefreshRate(XRDisplaySubsystem display, float hz)
        {
            if (hz <= 0f) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var meta = UnityEngine.XR.OpenXR.OpenXRSettings.Instance != null
                    ? UnityEngine.XR.OpenXR.OpenXRSettings.Instance.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.DisplayUtilitiesFeature>()
                    : null;
                if (meta == null || !meta.enabled) return;
                if (!UnityEngine.XR.OpenXR.Features.Meta.MetaOpenXRDisplaySubsystemExtensions.TryGetSupportedDisplayRefreshRates(display, Allocator.Temp, out NativeArray<float> rates)) return;
                // The highest supported rate not above the request.
                float best = 0f;
                foreach (var r in rates) if (r <= hz + 0.5f && r > best) best = r;
                rates.Dispose();
                if (best > 0f) UnityEngine.XR.OpenXR.Features.Meta.MetaOpenXRDisplaySubsystemExtensions.TryRequestDisplayRefreshRate(display, best);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Quality] Refresh rate request failed: " + e.Message);
            }
#endif
        }

        void ReapplyAfterSceneLoad() => ApplyPostProcessing(Active.postProcessing);

        void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => ReapplyAfterSceneLoad();

        // ─────────────────────────────── adaptive resolution ───────────────────────────────

        void Update()
        {
            if (urp == null || !XRSettings.isDeviceActive) return;
            float target = Active.refreshRate > 0f ? 1f / Active.refreshRate : 1f / 72f;
            float dt = Time.unscaledDeltaTime;
            smoothedFrame = smoothedFrame <= 0f ? dt : Mathf.Lerp(smoothedFrame, dt, 0.05f);
            if (Time.unscaledTime - lastAdapt < AdaptInterval) return;

            // More than 10 % over budget: step the eye buffer down. Under 80 % of budget: step back up to the profile.
            if (smoothedFrame > target * 1.1f && urp.renderScale > Active.minRenderScale + 0.001f)
            {
                urp.renderScale = Mathf.Max(Active.minRenderScale, urp.renderScale - 0.05f);
                lastAdapt = Time.unscaledTime;
            }
            else if (smoothedFrame < target * 0.8f && urp.renderScale < Active.renderScale - 0.001f)
            {
                urp.renderScale = Mathf.Min(Active.renderScale, urp.renderScale + 0.05f);
                lastAdapt = Time.unscaledTime;
            }
        }
    }
}

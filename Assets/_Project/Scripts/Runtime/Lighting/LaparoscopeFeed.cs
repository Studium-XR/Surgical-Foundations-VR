using SurgicalFoundations.Audio;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SurgicalFoundations.Lighting
{
    /// <summary>
    /// Renders the laparoscope camera into a RenderTexture shown on the tower monitor. The feed fades in when the
    /// Operate stage loads. Resolution and refresh rate are the main performance levers for the second camera
    /// (see ExecutionPlan decision D7).
    /// </summary>
    public class LaparoscopeFeed : MonoBehaviour
    {
        [SerializeField] Camera scopeCamera;
        [SerializeField] Vector2Int resolution = new Vector2Int(960, 540);
        [SerializeField, Tooltip("Render every Nth frame. 1 = every frame, 2 = 36 fps on a 72 Hz headset.")]
        [Range(1, 4)] int renderEveryNthFrame = 1;
        [SerializeField] float powerOnTime = 1.2f;
        [SerializeField, Tooltip("The scope works 3–12 cm from tissue: light falls off with distance², so this is small.")]
        float scopeLightIntensity = 0.015f;

        Renderer monitorScreen;
        RenderTexture target;
        float power;
        MaterialPropertyBlock mpb;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Inside the abdomen only the scope lights the picture. The theatre's surgical lights and its bright light
        /// probes reached the cavity props too (nothing blocks them), and with no exposure control the feed was half
        /// white: the cavity props go on the Cavity layer without probes, the room's realtime lights skip that layer,
        /// and the scope light is set for the few centimetres it works at.
        /// </summary>
        void IsolateCavity()
        {
            var cavity = LayerMask.NameToLayer("Cavity");
            if (cavity < 0) return;
            var scopeLight = GetComponentInChildren<Light>(true);
            if (scopeLight != null) { scopeLight.intensity = scopeLightIntensity; scopeLight.range = 0.35f; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "InCavity" || t.name.StartsWith("ANA_AbdominalCavity"))
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        {
                            r.gameObject.layer = cavity;
                            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                        }
                    var l = t.GetComponent<Light>();
                    if (l != null && l != scopeLight) l.cullingMask &= ~(1 << cavity);
                }
            }
        }

        void Start()
        {
            IsolateCavity();
            if (scopeCamera == null) scopeCamera = GetComponentInChildren<Camera>();
            var screenGo = GameObject.Find("LapTower_Screen");
            if (screenGo != null) monitorScreen = screenGo.GetComponent<Renderer>();

            target = new RenderTexture(resolution.x, resolution.y, 24, RenderTextureFormat.ARGB32) { name = "RT_Laparoscope", antiAliasing = 2 };
            scopeCamera.targetTexture = target;
            scopeCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            mpb = new MaterialPropertyBlock();
            AudioManager.Instance?.PlayAt(SoundId.ENV_MonitorPowerOn, monitorScreen != null ? monitorScreen.transform.position : transform.position);
        }

        void Update()
        {
            scopeCamera.enabled = Time.frameCount % renderEveryNthFrame == 0;
            if (monitorScreen == null || power >= 1f) return;
            power = Mathf.Clamp01(power + Time.deltaTime / powerOnTime);
            monitorScreen.GetPropertyBlock(mpb);
            mpb.SetTexture(BaseMap, target);
            mpb.SetTexture(EmissionMap, target);
            mpb.SetColor(BaseColor, Color.white * power);
            mpb.SetColor(EmissionColor, Color.white * (1.2f * power));
            monitorScreen.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            if (monitorScreen != null) monitorScreen.SetPropertyBlock(null);
            if (target != null) { target.Release(); Destroy(target); }
        }
    }
}

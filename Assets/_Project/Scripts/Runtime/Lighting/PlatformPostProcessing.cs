using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SurgicalFoundations.Lighting
{
    /// <summary>
    /// Post-processing (neutral tonemapping + grading) is on for PC/editor previews and off on standalone
    /// headsets by default, where a full-screen pass costs too much of the 72 fps budget (NFR-01).
    /// Flip <see cref="enableOnMobileXR"/> if profiling shows headroom.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PlatformPostProcessing : MonoBehaviour
    {
        [SerializeField] bool enableOnMobileXR;

        void Start()
        {
            // The headset quality profile decides when it is installed (it knows the headset, not just the OS).
            if (Rendering.DeviceQuality.Instance != null) return;
            var data = GetComponent<Camera>().GetUniversalAdditionalCameraData();
            bool mobile = Application.platform == RuntimePlatform.Android;
            data.renderPostProcessing = !mobile || enableOnMobileXR;
        }
    }
}

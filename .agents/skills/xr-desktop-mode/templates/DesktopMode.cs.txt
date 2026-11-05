using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace SurgicalFoundations.Core
{
    /// <summary>
    /// The simulator without a headset, for desktop builds (Windows/macOS/Linux): the XRI XR Interaction Simulator stands
    /// in for the headset and both controllers, driven by mouse and keyboard, so every interaction in the app (grabbing,
    /// triggers, rays, UI) works unchanged. Its on-screen panel lists the controls.
    /// Start a desktop build with <c>-vr</c> to use a PC headset (Quest Link, SteamVR) instead; if none starts it falls
    /// back to desktop. Android (Quest) builds never enter desktop mode. In the editor, the Play Mode menu chooses
    /// (Surgical Foundations ▸ Play Mode).
    /// </summary>
    public static class DesktopMode
    {
        /// <summary>True when the app is running on mouse and keyboard rather than a headset.</summary>
        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Startup()
        {
            Active = false;
            if (Application.isEditor || Application.platform == RuntimePlatform.Android) return;

            if (HasArgument("-vr") && TryStartHeadset()) return;
            if (HeadsetRunning()) return;

            var settings = Resources.Load<DesktopModeSettings>("DesktopModeSettings");
            if (settings == null || settings.simulatorPrefab == null)
            {
                Debug.LogError("[DesktopMode] No headset and no simulator prefab (Resources/DesktopModeSettings): build with Surgical Foundations ▸ Build ▸ Windows Desktop.");
                return;
            }
            var simulator = UnityEngine.Object.Instantiate(settings.simulatorPrefab);
            simulator.name = "XR Interaction Simulator (desktop mode)";
            UnityEngine.Object.DontDestroyOnLoad(simulator);
            Active = true;
            Cursor.lockState = CursorLockMode.None;
            Debug.Log("[DesktopMode] No headset: running on mouse and keyboard (XR Interaction Simulator).");
        }

        static bool HasArgument(string name) =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        static bool HeadsetRunning()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.Exists(d => d.running);
        }

        /// <summary>Starts OpenXR on the PC (desktop builds don't start XR by themselves). False if no headset answers.</summary>
        static bool TryStartHeadset()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null) return false;
            if (manager.activeLoader == null) manager.InitializeLoaderSync();
            if (manager.activeLoader == null) return false;
            manager.StartSubsystems();
            return true;
        }
    }
}

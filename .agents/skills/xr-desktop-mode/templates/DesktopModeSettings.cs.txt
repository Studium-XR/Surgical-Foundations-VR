using UnityEngine;

namespace SurgicalFoundations.Core
{
    /// <summary>
    /// What desktop mode spawns (Resources/DesktopModeSettings). The XRI simulator prefab lives in a package sample and
    /// XRI's own settings for it are internal, so the reference is kept here, where a build can load it.
    /// Created and kept up to date by Surgical Foundations ▸ Build ▸ Windows Desktop.
    /// </summary>
    public class DesktopModeSettings : ScriptableObject
    {
        [Tooltip("XR Interaction Simulator (XRI sample): simulated headset, controllers and hands driven by mouse and keyboard, with its own on-screen controls panel.")]
        public GameObject simulatorPrefab;
    }
}

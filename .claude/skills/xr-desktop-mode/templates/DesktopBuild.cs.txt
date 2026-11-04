using System.IO;
using System.Linq;
using SurgicalFoundations.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Desktop (no headset) build for Windows. Sets up <see cref="DesktopMode"/>: the simulator prefab reference in
    /// Resources/DesktopModeSettings, and OpenXR left off at start on PC so the app decides at runtime (desktop, or
    /// a PC headset with -vr). Then builds every enabled scene to Build/Desktop. The Quest (Android) build is untouched.
    /// </summary>
    public static class DesktopBuild
    {
        const string SettingsPath = SFPaths.Root + "/Resources/DesktopModeSettings.asset";
        const string OutputFolder = "Build/Desktop";

        [MenuItem(SFPaths.MenuRoot + "Build/Desktop Mode · Set Up", priority = 60)]
        public static void SetUp()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DesktopModeSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                settings = ScriptableObject.CreateInstance<DesktopModeSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XRPlayModeTarget.SimulatorPrefab);
            if (settings.simulatorPrefab == null)
                Debug.LogError("[Surgical Foundations] XR Interaction Simulator sample missing: import it from Package Manager ▸ XR Interaction Toolkit ▸ Samples.");
            EditorUtility.SetDirty(settings);

            var pc = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (pc != null) { pc.InitManagerOnStart = false; EditorUtility.SetDirty(pc); }

            // Mono for the PC player: it ships with Windows Build Support, whereas IL2CPP for Windows is a separate
            // Unity Hub module ("IL2CPP scripting backend is not installed"). Only Standalone changes; the Quest
            // (Android) build keeps IL2CPP, which it requires.
            var standalone = UnityEditor.Build.NamedBuildTarget.Standalone;
            if (PlayerSettings.GetScriptingBackend(standalone) != ScriptingImplementation.Mono2x && !WindowsIl2CppInstalled())
                PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            Debug.Log("[Surgical Foundations] Desktop mode set up: " + SettingsPath);
        }

        /// <summary>Whether the Windows IL2CPP player variations are installed next to this editor.</summary>
        static bool WindowsIl2CppInstalled()
        {
            var variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations");
            return Directory.Exists(variations) && Directory.GetDirectories(variations).Any(d => Path.GetFileName(d).Contains("il2cpp"));
        }

        [MenuItem(SFPaths.MenuRoot + "Build/Windows Desktop", priority = 61)]
        public static void BuildWindows()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[Surgical Foundations] Windows Build Support isn't installed: add it in Unity Hub ▸ Installs ▸ Add modules.");
                return;
            }
            SetUp();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = $"{OutputFolder}/SurgicalFoundations.exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log(report.summary.result == BuildResult.Succeeded
                ? $"[Surgical Foundations] Desktop build ready: {Path.GetFullPath(options.locationPathName)} ({report.summary.totalSize / (1024 * 1024)} MB). Run it without a headset for mouse and keyboard, or with -vr for a PC headset."
                : $"[Surgical Foundations] Desktop build {report.summary.result}: {report.summary.totalErrors} error(s), see the Console.");
        }
    }
}

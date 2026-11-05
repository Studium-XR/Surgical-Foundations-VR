---
name: xr-desktop-mode
description: Add a desktop (no headset, mouse and keyboard) mode to a Unity XR project built on the XR Interaction Toolkit, so people without a VR headset can run the same simulation on Windows/macOS. Use when asked to make an XR/VR Unity app work on desktop, for non-VR users, as a PC build without a headset, or to add a keyboard-and-mouse fallback; also covers building that desktop player.
---

# XR desktop mode (Unity + XR Interaction Toolkit)

Goal: one project, two products. The headset build (e.g. Quest/Android) is unchanged; a desktop build runs the same
scenes and interactions on mouse and keyboard. Do it by **simulating the XR devices**, not by writing a second set of
interactions: the XRI **XR Interaction Simulator** creates a fake headset, controllers and hands driven by mouse and
keyboard, so every `XRGrabInteractable`, ray, trigger and world-space UI keeps working, and its prefab brings an
on-screen panel listing the controls.

## Before you start: check the project

1. XRI version (`Packages/manifest.json`, `com.unity.xr.interaction.toolkit`). The Interaction Simulator ships with
   XRI 3.x as a package sample; on 2.x use the older "XR Device Simulator" sample the same way.
2. Is the sample imported? Look for `Assets/Samples/XR Interaction Toolkit/<ver>/XR Interaction Simulator/XR Interaction Simulator.prefab`.
   If not: Package Manager ▸ XR Interaction Toolkit ▸ Samples ▸ XR Interaction Simulator ▸ Import.
   Check its asmdef has empty `includePlatforms` (it must compile into player builds, not editor only).
3. Which runtime assembly will hold the code, and does it reference `Unity.XR.Management` (needed for the optional
   PC-headset switch)? Add the reference if not.
4. How the project starts XR: `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` has `m_InitManagerOnStart` per platform.
   Android/Quest should stay `1`; Standalone (PC) should be `0` so the app decides at runtime.
5. Windows Build Support installed (`BuildPipeline.IsBuildTargetSupported(Standalone, StandaloneWindows64)`).
6. Scripting backend for Standalone. Quest projects often set IL2CPP everywhere, but Windows IL2CPP is a separate Hub
   module; without it the build fails with "Currently selected scripting backend (IL2CPP) is not installed". Look in
   `<Editor>/Data/PlaybackEngines/windowsstandalonesupport/Variations/` for `*il2cpp*` folders; if there are none, set
   `PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x)` (Android keeps IL2CPP).

## Do NOT use XRI's global "instantiate in builds" switch

XRI's `XRDeviceSimulatorSettings` (`Automatically Instantiate Simulator Prefab`, `In Editor Only`) is one setting for all
platforms. Unticking "In Editor Only" also spawns the simulator in the **headset** build, where its fake devices fight
the real ones. Keep that setting editor-only (fine for testing in Play mode) and spawn the simulator yourself, at runtime,
only on desktop platforms.

## Implementation (templates in `templates/`)

Copy and adapt the namespaces, paths and menu roots:

- `DesktopModeSettings.cs.txt`: a `ScriptableObject` saved at `Resources/DesktopModeSettings.asset` holding the simulator
  prefab reference (XRI's own settings type is internal, and a build can only load what it references).
- `DesktopMode.cs.txt`: `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. It returns early in the editor and on Android;
  with the `-vr` argument it tries to start OpenXR (`XRGeneralSettings.Instance.Manager.InitializeLoaderSync()` +
  `StartSubsystems()`) and stays out of the way if a headset answers. If an `XRDisplaySubsystem` is already running it
  does nothing. Otherwise it instantiates the simulator, `DontDestroyOnLoad`, and sets `DesktopMode.Active = true` for
  any code that wants to adapt (bigger UI, hints, no "look at your palm" gestures).
- `DesktopBuild.cs.txt` (Editor): a "Set Up" menu that creates/refreshes the settings asset and sets Standalone
  `InitManagerOnStart = false`, and a "Windows Desktop" menu that builds all enabled scenes with
  `BuildPipeline.BuildPlayer` to `Build/Desktop/<App>.exe`.

## Things that commonly break on desktop, and what to check

- **Gestures that need real hands or a real head**: palm-facing menus, gaze dwell, physical walking between stations,
  height calibration from the user's real eye height. Give each a desktop path: buttons, teleport, a default height.
  Branch on `DesktopMode.Active`.
- **Fixed-distance UI**: panels placed for a headset may be too small on a monitor; scale world-space canvases up or
  move them closer when `DesktopMode.Active`.
- **Post-processing**: often disabled on mobile headsets for performance; desktop GPUs can keep it on. Check any
  platform switch (e.g. a `PlatformPostProcessing` component) treats Standalone as non-mobile.
- **Recentering / tracking-origin code** runs too: the simulated HMD starts at a default height; make sure spawn
  placement uses camera-relative moves (`XROrigin.MoveCameraToWorldLocation`).
- **Cursor**: the simulator locks or hides it while looking around; leave `Cursor.lockState` free at start.
- **Performance budgets** written for the headset (triangle counts, texture caps) are fine on desktop; nothing to undo.

## Building

1. Run the "Set Up" menu once (or let the build menu do it).
2. Build: switching from Android to Windows reimports assets and can take a long time; warn the user first, and say the
   editor stays on the Windows target afterwards (switch back to Android for the next headset build).
3. Test: run the `.exe` with no headset (desktop mode, simulator panel visible), then with `-vr` and a PC headset.
   In the editor, test via Play mode with XRI's simulator enabled (editor-only) instead.

## Controls to tell users (XR Interaction Simulator defaults; the in-app panel is authoritative)

Mouse moves the selected device or the view; keyboard keys switch between head, left and right controller and trigger
grip/trigger/primary buttons. The panel shows the live bindings; point users at it rather than hard-coding a list, since
bindings change between XRI versions.

## When the simulator isn't friendly enough

The simulator is complete but developer-flavoured. For a polished desktop product, add a thin layer on top: mouse-look on
the head, WASD/teleport for movement, left click = select with the active controller, right click = trigger, scroll =
move the held object nearer/farther. Implement it by writing to the simulator's input actions or by driving the XROrigin
camera and a ray interactor directly, keeping all interactables unchanged.

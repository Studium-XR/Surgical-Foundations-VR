using System.Collections.Generic;
using System.IO;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Core;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Lighting;
using SurgicalFoundations.Rendering;
using SurgicalFoundations.Scenario;
using SurgicalFoundations.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Assembles the 9 scenes from the placeholder prefabs, UI screens, lighting rigs and services.
    /// World layout (metres): table centred at the origin, long axis X (patient head at −X); the learner stands at
    /// z = −0.85 facing +Z; laparoscopic tower across the table; scrub alcove behind the south wall.
    /// </summary>
    public static class SceneBuilder
    {
        static readonly Vector3 Umbilicus = new Vector3(-0.02f, 1.165f, 0f);
        static readonly Vector3 TaskArea = new Vector3(-0.02f, 0.955f, -0.02f);
        static readonly Vector3 LearnerEye = new Vector3(0f, 1.6f, -0.85f);
        static readonly Vector3 MonitorScreen = new Vector3(0.25f, 1.72f, 1.212f);
        static readonly Dictionary<string, Vector3> Ports = new Dictionary<string, Vector3>
        {
            { "Camera", Umbilicus },
            { "Left", Umbilicus + new Vector3(-0.09f, 0, 0.1f) },
            { "Right", Umbilicus + new Vector3(0.09f, 0, 0.1f) },
        };

        [MenuItem(SFPaths.MenuRoot + "Build/4 · Scenes", priority = 4)]
        public static void BuildAll()
        {
            UIKit.Init(UIScreensBuilder.EnsureTheme());
            if (!PrepareEditorScenes(out var reopen)) return;
            var previouslyActive = SceneManager.GetActiveScene();

            Bootstrap();
            Lobby();
            SkillsLab();
            OperatingTheatre();
            StagePrep();
            StageAccess();
            StageOperate();
            StageClose();
            ReplayViewer();
            ShaderGallery();
            RegisterBuildScenes();

            if (previouslyActive.IsValid() && previouslyActive.isLoaded) SceneManager.SetActiveScene(previouslyActive);
            RestoreEditorScenes(reopen);
            Debug.Log("[Surgical Foundations] 9 scenes built. Bake lighting via Surgical Foundations ▸ Build ▸ 5 · Bake Lighting.");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static readonly string[] Generated =
        {
            ScenePath("Core", SceneIds.Bootstrap), ScenePath("Frontend", SceneIds.Lobby), ScenePath("Frontend", SceneIds.SkillsLab),
            ScenePath("Theatre", SceneIds.OperatingTheatre), ScenePath("Theatre", SceneIds.StagePrep), ScenePath("Theatre", SceneIds.StageAccess),
            ScenePath("Theatre", SceneIds.StageOperate), ScenePath("Theatre", SceneIds.StageClose), ScenePath("Tools", SceneIds.ReplayViewer),
            ScenePath("Tools", "91_ShaderGallery"),
        };

        const string HolderPath = "Assets/_Sandbox/_SceneBuilderHolder.unity";

        /// <summary>
        /// Unity refuses to save a new scene over the path of a scene that is open, and refuses to create scenes
        /// additively while an untitled scene is unsaved. So: save pending edits in open generated scenes, then park
        /// the editor on a saved, empty holder scene while building. Unsaved work in any other scene is never
        /// discarded; the build stops with a message instead. Returns the scene to reopen afterwards.
        /// </summary>
        static bool PrepareEditorScenes(out string reopen)
        {
            reopen = null;
            bool needSwitch = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                bool generated = System.Array.IndexOf(Generated, sc.path) >= 0;
                bool untitled = string.IsNullOrEmpty(sc.path);
                if (generated)
                {
                    if (sc.isDirty) EditorSceneManager.SaveScene(sc);
                    reopen ??= sc.path;
                    needSwitch = true;
                }
                else if (untitled) needSwitch = true;
            }
            if (!needSwitch) return true;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (sc.isDirty && System.Array.IndexOf(Generated, sc.path) < 0)
                {
                    Debug.LogError($"[SceneBuilder] '{(string.IsNullOrEmpty(sc.path) ? "Untitled" : sc.path)}' has unsaved changes. Save or discard them, then build again.");
                    return false;
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HolderPath) == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HolderPath));
                var holder = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(holder, HolderPath);
            }
            else EditorSceneManager.OpenScene(HolderPath, OpenSceneMode.Single);
            reopen ??= ScenePath("Core", SceneIds.Bootstrap);
            return true;
        }

        static void RestoreEditorScenes(string reopen)
        {
            if (reopen == null) return;
            EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HolderPath) != null) AssetDatabase.DeleteAsset(HolderPath);
        }

        static string ScenePath(string folder, string name) => $"{SFPaths.Scenes}/{folder}/{name}.unity";

        /// <summary>
        /// Scenes are built additively and closed after saving, so whatever the user has open (including unsaved
        /// work) is never touched. The new scene is made active because lighting settings apply to the active scene.
        /// </summary>
        static Scene NewScene()
        {
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(s);
            return s;
        }

        static void Save(Scene s, string folder, string name)
        {
            Directory.CreateDirectory($"{SFPaths.Scenes}/{folder}");
            EditorSceneManager.SaveScene(s, ScenePath(folder, name));
            EditorSceneManager.CloseScene(s, true);
        }

        static Transform Group(string name, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            if (parent != null) t.SetParent(parent, false);
            return t;
        }

        static GameObject Inst(string relPath, Transform parent, Vector3 pos, float yaw = 0f) =>
            Inst(relPath, parent, pos, Quaternion.Euler(0, yaw, 0));

        static GameObject Inst(string relPath, Transform parent, Vector3 pos, Quaternion rot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{SFPaths.Prefabs}/{relPath}.prefab");
            if (prefab == null) { Debug.LogError($"[SceneBuilder] Missing prefab {relPath}"); return new GameObject("MISSING_" + relPath); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            return go;
        }

        /// <summary>Like Inst, but silently skips optional prefabs (e.g. imported models that may not be present).</summary>
        static GameObject TryInst(string relPath, Transform parent, Vector3 pos, float yaw = 0f) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{SFPaths.Prefabs}/{relPath}.prefab") != null ? Inst(relPath, parent, pos, yaw) : null;

        static GameObject UI(string prefab, Transform parent, Vector3 pos, float yaw)
        {
            var folder = prefab.StartsWith("UI_Monitor") || prefab == "UI_HUD" || prefab == "UI_Subtitles" ? "UI/HUD" : "UI/Screens";
            return Inst($"{folder}/{prefab}", parent, pos, yaw);
        }

        /// <summary>Yaw so a canvas at <paramref name="pos"/> faces a viewer at <paramref name="viewer"/> (canvas front = −Z).</summary>
        static float Facing(Vector3 pos, Vector3 viewer)
        {
            var d = pos - viewer;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        static GameObject FacingUI(string prefab, Transform parent, Vector3 pos, Vector3 viewer) => UI(prefab, parent, pos, Facing(pos, viewer));

        static void Teleportable(GameObject env)
        {
            var floor = env.transform.Find("Colliders/Floor");
            if (floor != null) floor.gameObject.AddComponent<TeleportationArea>();
        }

        static void Kinematic(GameObject go)
        {
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        static PlayerSpawnPoint Spawn(Transform parent, Vector3 pos, float yaw, int priority = 10, string name = "PlayerSpawn")
        {
            var t = Group(name, parent);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var sp = t.gameObject.AddComponent<PlayerSpawnPoint>();
            Ser.Set(sp, "priority", priority);
            return sp;
        }

        static Transform Spot(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = Group(name, parent);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return t;
        }

        static void Ambient(Transform parent, SoundId id, bool spatial, Vector3 pos)
        {
            var t = Group("Ambience_" + id, parent);
            t.position = pos;
            var e = t.gameObject.AddComponent<AmbientEmitter>();
            e.Sound = id;
            Ser.Set(e, "spatial", spatial);
        }

        static StepSequence Sequence(Transform parent, GameObject[] steps, SoundId[] voice, Transform[] spawns = null, bool advance = true)
        {
            var go = new GameObject("StepSequence");
            go.transform.SetParent(parent, false);
            var seq = go.AddComponent<StepSequence>();
            for (int i = 0; i < steps.Length; i++)
            {
                steps[i].transform.SetParent(go.transform, true);
                steps[i].SetActive(i == 0); // StepSequence does the same at runtime; keeps the editor view readable
            }
            Ser.SetArray(seq, "steps", steps);
            var ids = new int[voice.Length];
            for (int i = 0; i < voice.Length; i++) ids[i] = (int)voice[i];
            Ser.SetIntArray(seq, "voiceOnStep", ids);
            if (spawns != null) Ser.SetArray(seq, "stepSpawns", spawns);
            Ser.Set(seq, "advanceScenarioAtEnd", advance);
            return seq;
        }

        static void Hands(GameObject step, HandLook left, HandLook right) =>
            step.AddComponent<HandLookCue>().Configure(left, right);

        static GameObject StepGroup(string name, params GameObject[] children)
        {
            var g = new GameObject(name);
            foreach (var c in children) c.transform.SetParent(g.transform, true);
            return g;
        }

        static void Lighting(string name, float texels, Color ambient, LightingRig.Profile post, Transform parent)
        {
            var ls = LightingRig.Settings(name, texels, 1024);
            LightingRig.ApplyEnvironment(ambient, ls);
            LightingRig.GlobalVolume(parent, LightingRig.PostProfile(post));
        }

        /// <summary>Additive stage scenes bake nothing: their props light from 10_OR_Base's probes and reflections.</summary>
        static void StageLighting()
        {
            var ls = LightingRig.Settings("Stage_Additive", 8, 256);
            ls.bakedGI = false;
            LightingRig.ApplyEnvironment(new Color(0.05f, 0.06f, 0.065f), ls);
        }

        static void LazyFollowCamera(GameObject go, Vector3 offset)
        {
            var lf = go.AddComponent<LazyFollow>();
            lf.targetOffset = offset;
            lf.positionFollowMode = LazyFollow.PositionFollowMode.Follow;
            lf.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
        }

        /// <summary>Instrument placed through a port: its Pivot_Port sits on the skin, shaft aimed at the target.</summary>
        static GameObject Inserted(string prefab, Transform parent, Vector3 port, Vector3 aimAt)
        {
            var dir = (aimAt - port).normalized;
            var go = Inst(prefab, parent, port, Quaternion.LookRotation(dir, Vector3.up));
            var pivot = go.transform.Find("Pivot_Port");
            float along = pivot != null ? pivot.localPosition.z : 0.2f;
            go.transform.position = port - dir * along;
            Kinematic(go);
            return go;
        }

        static void Trocars(Transform parent)
        {
            foreach (var kv in Ports)
            {
                var prefab = kv.Key == "Camera" ? "Instruments/INST_Trocar12" : "Instruments/INST_Trocar5";
                var go = Inst(prefab, parent, kv.Value + Vector3.up * 0.09f, Quaternion.Euler(90, 0, 0));
                go.name = $"Trocar_{kv.Key}";
                var obt = go.transform.Find("Visual/Obturator");
                if (obt != null) obt.gameObject.SetActive(false); // obturator withdrawn once the port is placed
                Kinematic(go);
            }
        }

        /// <summary>Laparoscope camera → RenderTexture on the tower monitor, with its own light inside the cavity.</summary>
        static void ScopeRig(Transform parent, Vector3 from, Vector3 lookAt, float fov = 72f)
        {
            var rig = Group("LaparoscopeRig", parent);
            rig.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from, Vector3.up));
            var cam = rig.gameObject.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.003f;
            cam.farClipPlane = 1.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.03f, 0.03f);
            cam.cullingMask = ~(1 << 5); // no UI
            cam.depth = -5;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.allowXRRendering = false; // mono render-to-texture camera, never into the headset eyes
            var lightGo = Group("ScopeLight", rig);
            var l = lightGo.gameObject.AddComponent<Light>();
            l.type = LightType.Spot;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.spotAngle = 95f; l.innerSpotAngle = 40f; l.range = 0.6f; l.intensity = 2.5f;
            l.useColorTemperature = true; l.colorTemperature = 5600f;
            l.shadows = LightShadows.None;
            rig.gameObject.AddComponent<LaparoscopeFeed>();
        }

        static Scene Begin(out Transform env, out Transform props, out Transform ui, out Transform lights, out Transform flow)
        {
            var s = NewScene();
            env = Group("--- Environment ---");
            lights = Group("--- Lighting ---");
            props = Group("--- Props ---");
            ui = Group("--- UI ---");
            flow = Group("--- Flow ---");
            return s;
        }

        // ───────────────────────────── 00 Bootstrap ─────────────────────────────

        static void Bootstrap()
        {
            var s = NewScene();
            var xrRoot = Group("--- XR ---");
            var xrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SFPaths.XROriginPrefab);
            var xr = (GameObject)PrefabUtility.InstantiatePrefab(xrPrefab, xrRoot);
            xr.name = "XR Origin";
            var cam = xr.GetComponentInChildren<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.04f, 0.05f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.gameObject.AddComponent<PlatformPostProcessing>();
            var hands = xr.AddComponent<HandAppearance>();
            Ser.Set(hands, "bare", MaterialLibrary.Get("Skin"));
            Ser.Set(hands, "gloved", MaterialLibrary.Get("Glove_Latex"));
            Ser.Set(hands, "contaminated", MaterialLibrary.Get("Glove_Contaminated"));
            var gaze = xr.transform.Find("Camera Offset/Gaze Interactor");
            if (gaze != null)
            {
                gaze.gameObject.SetActive(false); // Quest 3 has no eye tracking; see EyeGazeActivator
                Ser.Set(xr.AddComponent<EyeGazeActivator>(), "gazeInteractor", gaze.gameObject);
            }
            new GameObject("XR Interaction Manager", typeof(XRInteractionManager)).transform.SetParent(xrRoot);
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule)).transform.SetParent(xrRoot);

            var services = Group("--- Services ---");
            var session = new GameObject("SessionManager", typeof(SessionManager));
            session.transform.SetParent(services);
            new GameObject("EventLogger", typeof(EventLogger)).transform.SetParent(services);
            var audioGo = new GameObject("AudioManager", typeof(AudioManager));
            audioGo.transform.SetParent(services);
            Ser.Set(audioGo.GetComponent<AudioManager>(), "bank", AssetDatabase.LoadAssetAtPath<SoundBank>(SFPaths.SoundBank));
            var loaderGo = new GameObject("SceneLoader", typeof(SceneLoader));
            loaderGo.transform.SetParent(services);
            var fader = new GameObject("ScreenFader", typeof(ScreenFader));
            fader.transform.SetParent(loaderGo.transform);
            Ser.Set(loaderGo.GetComponent<SceneLoader>(), "fader", fader.GetComponent<ScreenFader>());
            Ser.Set(loaderGo.GetComponent<SceneLoader>(), "xrOrigin", xr.GetComponent<Unity.XR.CoreUtils.XROrigin>());
            new GameObject("AppBootstrap", typeof(AppBootstrap)).transform.SetParent(services);
            // Sign-in, offline upload queue, session recorder, scoring config (see Backend/). Lives here so it never unloads.
            new GameObject("BackendServices", typeof(SurgicalFoundations.Backend.BackendServices)).transform.SetParent(services);

            // Global UI: subtitles and pause live here so they work in every scene.
            var ui = Group("--- UI ---");
            var subs = UI("UI_Subtitles", ui, new Vector3(0, 1.2f, 1.3f), 0);
            LazyFollowCamera(subs, new Vector3(0, -0.42f, 1.3f));

            var pauseRoot = Group("PauseRoot", ui);
            var pauseMenu = pauseRoot.gameObject.AddComponent<PauseMenu>();
            var pause = UI("UI_13_Pause", pauseRoot, new Vector3(0, 1.5f, 1.1f), 0);
            var dimmer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dimmer.name = "Dimmer";
            Object.DestroyImmediate(dimmer.GetComponent<Collider>());
            dimmer.transform.SetParent(ui);
            dimmer.transform.localScale = Vector3.one * 0.6f;
            var dr = dimmer.GetComponent<MeshRenderer>();
            dr.sharedMaterial = MaterialLibrary.Get("UI_Dimmer");
            dr.shadowCastingMode = ShadowCastingMode.Off;
            dr.receiveShadows = false;
            dimmer.SetActive(false);
            Ser.Set(pauseMenu, "panel", pause.GetComponent<UIPanel>());
            Ser.Set(pauseMenu, "dimmer", dimmer);
            void Wire(string button, UnityEngine.Events.UnityAction action)
            {
                var b = FindButton(pause.transform, button);
                if (b != null) UnityEventTools.AddPersistentListener(b.onClick, action);
                else Debug.LogWarning($"[SceneBuilder] Pause button {button} not found");
            }
            Wire("Btn_Resume", pauseMenu.Resume);
            Wire("Btn_Settings", pauseMenu.Resume);
            Wire("Btn_Recalibrate", pauseMenu.Recalibrate);
            Wire("Btn_Restartscenario", pauseMenu.RestartScenario);
            Wire("Btn_Endsession", pauseMenu.EndSession);

            var devGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            devGo.name = "DeviationVignette";
            Object.DestroyImmediate(devGo.GetComponent<Collider>());
            devGo.transform.SetParent(ui);
            devGo.transform.localScale = Vector3.one * 0.5f;
            var devR = devGo.GetComponent<MeshRenderer>();
            devR.sharedMaterial = MaterialLibrary.Get("FX_DeviationVignette");
            devR.shadowCastingMode = ShadowCastingMode.Off;
            devR.receiveShadows = false;
            Ser.Set(devGo.AddComponent<DeviationVignette>(), "sphere", devR);

            LightingRig.ApplyEnvironment(new Color(0.03f, 0.05f, 0.055f), LightingRig.Settings("Bootstrap", 8, 256));
            Save(s, "Core", SceneIds.Bootstrap);
        }

        static Button FindButton(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.name == name) return b;
            return null;
        }

        // ───────────────────────────── 01 Lobby ─────────────────────────────

        static void Lobby()
        {
            var s = Begin(out var env, out var props, out var ui, out var lights, out var flow);
            Teleportable(Inst("Environment/ENV_Lobby", env, Vector3.zero));
            LightingRig.LobbyRig(lights);
            Lighting("Lobby", 10, new Color(0.03f, 0.06f, 0.065f), LightingRig.Profile.Lobby, lights);

            // Waiting area behind the learner's start position.
            TryInst("Environment/FURN_Sofa", props, new Vector3(0, 0, -4.3f), 0f);
            TryInst("Equipment/EQ_SideTable", props, new Vector3(0, 0, -3.1f), 0f);
            foreach (var x in new[] { -1.9f, 1.9f })
            {
                var pos = new Vector3(x, 0, -3.7f);
                TryInst("Environment/FURN_VisitorChair", props, pos, Facing(Vector3.zero, pos));
            }

            var signIn = UI("UI_01_SignIn", ui, new Vector3(0, 1.5f, 1.45f), 0);
            var lobby = UI("UI_02_Lobby", ui, new Vector3(0, 1.4f, 1.55f), 0);
            Sequence(flow, new[] { signIn, lobby }, new[] { SoundId.None, SoundId.VO_Lobby_Welcome }, null, false);
            Hands(signIn, HandLook.Bare, HandLook.Bare);

            Ambient(env, SoundId.AMB_Lobby_Pad, false, Vector3.up * 2f);
            Spawn(flow, Vector3.zero, 0);
            Save(s, "Frontend", SceneIds.Lobby);
        }

        // ───────────────────────────── 02 Skills lab ─────────────────────────────

        static void SkillsLab()
        {
            var s = Begin(out var env, out var props, out var ui, out var lights, out var flow);
            Teleportable(Inst("Environment/ENV_SkillsLab", env, Vector3.zero));
            LightingRig.LabRig(lights);
            Lighting("SkillsLab", 14, new Color(0.05f, 0.06f, 0.06f), LightingRig.Profile.Lab, lights);

            Inst("TrainingProps/PROP_PegBoard", props, new Vector3(0.1f, 0.92f, 0.62f), 0);
            var grasper = Inst("Instruments/INST_AtraumaticGrasper", props, new Vector3(-0.3f, 0.94f, 0.5f), Quaternion.Euler(0, 25, 90));
            Kinematic(grasper);
            var diagram = Inst("TrainingProps/PROP_ControllerDiagram", props, new Vector3(0.62f, 1.25f, 0.62f), -35f);
            diagram.transform.localScale = Vector3.one * 2.5f; // enlarged for the tutorial callouts
            TryInst("Equipment/EQ_Microscope", props, new Vector3(-0.5f, 0.92f, 0.78f), 30f);
            Inst("Equipment/EQ_BackTable", props, new Vector3(-2.3f, 0, 1.4f), 90f);
            TryInst("TrainingProps/PROP_OpenSurgerySet", props, new Vector3(-2.3f, 0.926f, 1.4f), 90f);

            // Ward bay along the east wall (hospital room imports; prefab fronts face +Z).
            var ward = Group("WardBay", props);
            TryInst("Equipment/EQ_HospitalBed", ward, new Vector3(1.75f, 0, 1.2f), -90f);
            TryInst("Equipment/EQ_BedsideCabinet", ward, new Vector3(2.7f, 0, 2.35f), -90f);
            TryInst("Equipment/EQ_IVStand", ward, new Vector3(2.6f, 0, 0.1f), -90f);
            TryInst("Equipment/EQ_OxygenCylinder", ward, new Vector3(2.75f, 0, -0.5f), -90f);
            TryInst("Equipment/EQ_OverbedTable", ward, new Vector3(0.95f, 0, 2.3f), 180f);
            TryInst("Equipment/EQ_SupplyCabinet", ward, new Vector3(1.1f, 0, -2.7f), 0f);
            TryInst("Equipment/EQ_StorageCabinet", ward, new Vector3(2.35f, 0, -2.6f), 0f);
            TryInst("Equipment/EQ_MedicalCart", ward, new Vector3(-1.2f, 0, 2.6f), 180f);
            TryInst("Environment/FURN_VisitorChair", ward, new Vector3(-2.4f, 0, -2.4f), 45f);

            var viewer = new Vector3(0, 1.6f, -0.2f);
            var cal = FacingUI("UI_03_Calibrate", ui, new Vector3(-0.85f, 1.45f, 0.95f), viewer);
            var tut = FacingUI("UI_04_Tutorial", ui, new Vector3(-0.85f, 1.45f, 0.95f), viewer);
            Sequence(flow, new[] { cal, tut }, new[] { SoundId.VO_Calibrate_Height, SoundId.VO_Tutorial_SqueezeTrigger }, null, false);
            Hands(cal, HandLook.Bare, HandLook.Bare);

            Ambient(env, SoundId.AMB_SkillsLab_RoomTone, false, Vector3.up * 2f);
            Spawn(flow, new Vector3(0, 0, -0.2f), 0);
            Save(s, "Frontend", SceneIds.SkillsLab);
        }

        // ───────────────────────────── 10 OR base ─────────────────────────────

        static void OperatingTheatre()
        {
            var s = Begin(out var env, out var props, out var ui, out var lights, out var flow);
            Teleportable(Inst("Environment/ENV_OperatingTheatre", env, Vector3.zero));
            Teleportable(Inst("Environment/ENV_ScrubAlcove", env, new Vector3(-2f, 0, -4.62f)));

            var eq = Group("Equipment", props);
            Inst("Equipment/EQ_ORTable", eq, Vector3.zero);
            Inst("Equipment/EQ_SurgicalLights", eq, Vector3.zero);
            Inst("Equipment/EQ_LapTower", eq, new Vector3(0.25f, 0, 1.25f), 180f);
            // Head end, towards −Z: its swing-arm monitor reaches ~0.8 m sideways and would sit in the anaesthetist's face.
            Inst("Equipment/EQ_AnaesthesiaMachine", eq, new Vector3(-1.85f, 0, -0.35f), 110f);
            Inst("Equipment/EQ_MayoStand", eq, new Vector3(0.8f, 0, -0.7f), 0f);
            Inst("Equipment/EQ_BackTable", eq, new Vector3(2.0f, 0, -1.3f), 90f);
            TryInst("Equipment/EQ_HeartLungMachine", eq, new Vector3(2.75f, 0, 2.55f), 225f);
            TryInst("Equipment/EQ_SurgeryLamp", eq, new Vector3(1.1f, 0, 0.6f), -90f);
            TryInst("Equipment/EQ_PatientMonitor", eq, new Vector3(-1.05f, 0, 1.1f), 150f);
            TryInst("Equipment/EQ_IVStand", eq, new Vector3(-1.05f, 0, -1.05f), 30f); // clear of the anaesthesia machine
            TryInst("Equipment/EQ_OxygenCylinder", eq, new Vector3(-3.1f, 0, -0.45f), 90f);
            TryInst("Equipment/EQ_SupplyCabinet", eq, new Vector3(-3.2f, 0, -2.35f), 90f);
            TryInst("Equipment/EQ_StorageCabinet", eq, new Vector3(3.2f, 0, -2.6f), -90f);
            TryInst("Equipment/EQ_MedicalCart", eq, new Vector3(2.95f, 0, 0.6f), -90f);

            var laminar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            laminar.name = "LaminarFlow_FX";
            Object.DestroyImmediate(laminar.GetComponent<Collider>());
            laminar.transform.SetParent(props);
            laminar.transform.position = new Vector3(0, 2.0f, 0);
            laminar.transform.localScale = new Vector3(3.0f, 1.9f, 3.0f);
            var lr = laminar.GetComponent<MeshRenderer>();
            lr.sharedMaterial = MaterialLibrary.Get("FX_LaminarFlow");
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            laminar.AddComponent<DisableOnMobileXR>();

            var patient = Group("Patient", props);
            Inst("Anatomy/ANA_PatientBody", patient, new Vector3(0, 0.92f, 0));
            Inst("PPE/PPE_DrapeSet", patient, new Vector3(-0.02f, 1.167f, 0));
            Inst("Anatomy/ANA_AbdominalCavity", patient, new Vector3(-0.06f, 0.95f, 0));

            var staff = Group("Staff", props);
            Inst("Characters/CHR_Anaesthetist", staff, new Vector3(-1.55f, 0, 1.05f), 200f);

            LightingRig.TheatreRig(lights);
            Lighting("OperatingTheatre", 16, new Color(0.05f, 0.06f, 0.065f), LightingRig.Profile.Theatre, lights);

            var hud = UI("UI_HUD", ui, new Vector3(0, 2.05f, 0.7f), 0);
            LazyFollowCamera(hud, new Vector3(0, 0.45f, 1.6f));
            var summary = UI("UI_16_Summary", ui, new Vector3(0, 1.62f, 0.95f), 0);

            var directorGo = new GameObject("ScenarioDirector", typeof(ScenarioDirector));
            directorGo.transform.SetParent(flow);
            Ser.Set(directorGo.GetComponent<ScenarioDirector>(), "summaryPanel", summary);
            summary.SetActive(false);

            Ambient(env, SoundId.AMB_OR_RoomTone, false, Vector3.up * 2f);
            Spawn(flow, new Vector3(0, 0, -0.85f), 0, 0);
            Save(s, "Theatre", SceneIds.OperatingTheatre);
        }

        // ───────────────────────────── 11 Prep ─────────────────────────────

        static void StagePrep()
        {
            var s = Begin(out _, out var props, out var ui, out _, out var flow);

            var sink = Inst("Equipment/EQ_ScrubSink", props, new Vector3(-2.0f, 0, -5.3f), 0f);
            // The learner stands centred on the sink: run the middle tap (the primitive fallback sink only has L/R).
            var tap = sink.transform.Find("WaterSocket_C") ?? sink.transform.Find("WaterSocket_L");
            if (tap != null) tap.gameObject.SetActive(true);
            Inst("PPE/PPE_SurgicalGown", props, new Vector3(-3.15f, 0, -4.1f), 90f);
            // Demo hands just under the running tap.
            var ghostPos = tap != null ? tap.position + new Vector3(0, -0.2f, 0.05f) : new Vector3(-2.0f, 1.32f, -5.12f);
            var ghost = Inst("PPE/PPE_SurgeonHands", props, ghostPos, Quaternion.Euler(-35, 180, 0));
            ghost.name = "GhostHands_ScrubDemo";
            foreach (var r in ghost.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = MaterialLibrary.Get("FX_GhostHand");
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            ghost.AddComponent<SimpleMotion>().Configure(new Vector3(0, 0.03f, 0), 0.5f, new Vector3(0, 0, 10), 0.35f, 0);
            Inst("Characters/CHR_ScrubNurse", props, new Vector3(1.35f, 0, -2.05f), -40f);

            // Back table (in OR_Base at (2.0, 0, −1.3), long axis along Z): gloves, tray with the 8 counted items, 5 swabs.
            Kinematic(Inst("PPE/PPE_GlovesPacket", props, new Vector3(1.95f, 0.935f, -0.9f), 90f));
            var tray = Inst("Equipment/EQ_InstrumentTray", props, new Vector3(1.95f, 0.928f, -1.45f), 90f);
            (string socket, string prefab)[] load =
            {
                ("Socket_Trocar12", "INST_Trocar12"), ("Socket_Trocar5_A", "INST_Trocar5"), ("Socket_Trocar5_B", "INST_Trocar5"),
                ("Socket_Laparoscope", "INST_Laparoscope30"), ("Socket_Grasper_A", "INST_AtraumaticGrasper"), ("Socket_Grasper_B", "INST_AtraumaticGrasper"),
                ("Socket_Scissors", "INST_LapScissors"), ("Socket_ClipApplier", "INST_ClipApplier"),
            };
            foreach (var (socket, prefab) in load)
            {
                var sock = tray.transform.Find(socket);
                var go = Inst("Instruments/" + prefab, props, sock.position + Vector3.up * 0.012f, sock.rotation * Quaternion.Euler(0, 0, 90));
                Kinematic(go);
            }
            for (int i = 0; i < 5; i++)
                Kinematic(Inst("Instruments/INST_Swab", props, new Vector3(2.22f, 0.935f, -1.85f + i * 0.11f), 0f));

            // Flow: scrub at the sink → sterility deviation at the back table → opening count + drape.
            var sinkSpot = new Vector3(-2.0f, 0, -4.45f); // just in front of the trough lip (sink wall side at z = −5.6)
            var tableSpot = new Vector3(1.15f, 0, -1.3f);
            var sinkEye = sinkSpot + Vector3.up * 1.6f;
            var tableEye = tableSpot + Vector3.up * 1.6f;
            var scrub = FacingUI("UI_05_Scrub", ui, new Vector3(-2.95f, 1.5f, -5.15f), sinkEye);
            var steril = FacingUI("UI_06_Sterility", ui, new Vector3(2.75f, 1.55f, -1.05f), tableEye);
            var tray2 = FacingUI("UI_07_TrayAndDrape", ui, new Vector3(2.75f, 1.55f, -1.05f), tableEye);
            var tableStep = Spot(flow, "StepSpot_BackTable", tableSpot, 90f);
            Sequence(flow, new[] { scrub, steril, tray2 },
                new[] { SoundId.VO_Prep_HandsAboveElbows, SoundId.None, SoundId.None }, // "sterility broken" plays on a real break (DetectionLog)
                new Transform[] { null, tableStep, null });
            // Hand appearance in the theatre follows the real sterile state (SterilityMonitor), not the step being shown.

            Spawn(flow, sinkSpot, 180f);
            StageLighting();
            Save(s, "Theatre", SceneIds.StagePrep);
        }

        // ───────────────────────────── 12 Access ─────────────────────────────

        static void StageAccess()
        {
            var s = Begin(out _, out var props, out var ui, out _, out var flow);
            Inst("Anatomy/ANA_AbdominalWallLayers", props, Umbilicus + Vector3.down * 0.002f);
            Inst("TrainingProps/PROP_PortSiteMarkers", props, Umbilicus + Vector3.up * 0.003f);
            Inst("Anatomy/ANA_EpigastricVessels", props, Ports["Right"] + new Vector3(0.02f, -0.03f, 0));

            // Mayo stand tray (OR_Base, (0.8, 0, −0.7)), tray top ≈ 1.07 m
            string[] mayo = { "INST_Scalpel", "INST_Trocar12", "INST_Trocar5", "INST_Trocar5", "INST_Laparoscope30" };
            for (int i = 0; i < mayo.Length; i++)
                Kinematic(Inst("Instruments/" + mayo[i], props, new Vector3(0.62f + i * 0.09f, 1.085f, -0.82f), Quaternion.Euler(0, 0, 90)));

            ScopeRig(props, Umbilicus + new Vector3(0, -0.05f, -0.04f), TaskArea + new Vector3(0.06f, 0, 0.06f));

            var entryPos = new Vector3(-1.3f, 1.5f, 0.1f);
            var ports = FacingUI("UI_08_PortSites", ui, new Vector3(-0.95f, 1.5f, 0.15f), LearnerEye);
            var entry = StepGroup("Step_TrocarEntry", FacingUI("UI_09_TrocarEntry", ui, entryPos, LearnerEye), UI("UI_Monitor_Access", ui, MonitorScreen, 0));
            var blood = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blood.name = "BleedPool_FX";
            Object.DestroyImmediate(blood.GetComponent<Collider>());
            blood.transform.SetPositionAndRotation(Ports["Right"] + Vector3.up * 0.006f, Quaternion.Euler(90, 0, 0));
            blood.transform.localScale = Vector3.one * 0.08f;
            blood.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("FX_Blood");
            blood.AddComponent<ShaderPropertyAnimator>().Configure("_Spread", 0f, 0.85f, 8f, ShaderPropertyAnimator.Mode.Once, 0.5f);
            var branch = StepGroup("Step_BleedBranch", FacingUI("UI_10_BleedBranch", ui, new Vector3(-1.0f, 1.5f, 0.15f), LearnerEye), UI("UI_Monitor_Access", ui, MonitorScreen, 0), blood);
            Sequence(flow, new[] { ports, entry, branch }, new[] { SoundId.VO_Access_MarkRightPort, SoundId.None, SoundId.None });
            Hands(ports, HandLook.Gloved, HandLook.Gloved);

            Spawn(flow, new Vector3(0, 0, -0.85f), 0);
            StageLighting();
            Save(s, "Theatre", SceneIds.StageAccess);
        }

        // ───────────────────────────── 13 Operate ─────────────────────────────

        static void StageOperate()
        {
            var s = Begin(out _, out var props, out var ui, out _, out var flow);
            var cavity = Group("InCavity", props);
            Inst("TrainingProps/PROP_PegBoard", cavity, TaskArea, 0f);
            Inst("Anatomy/ANA_DissectionTissue", cavity, new Vector3(0.1f, 0.97f, 0.08f), 20f);
            Inst("Anatomy/ANA_ClipCutStructure", cavity, new Vector3(0.12f, 0.965f, -0.08f), -10f);

            var inst = Group("Instruments", props);
            Trocars(inst);
            Inserted("Instruments/INST_AtraumaticGrasper", inst, Ports["Left"], TaskArea + new Vector3(0.02f, 0.02f, 0));
            Inserted("Instruments/INST_MarylandDissector", inst, Ports["Right"], TaskArea + new Vector3(-0.02f, 0.02f, 0));
            Inserted("Instruments/INST_Laparoscope30", inst, Ports["Camera"], TaskArea);
            Kinematic(Inst("Instruments/INST_ClipApplier", props, new Vector3(0.7f, 1.085f, -0.82f), Quaternion.Euler(0, 0, 90)));
            Kinematic(Inst("Instruments/INST_LapScissors", props, new Vector3(0.8f, 1.085f, -0.82f), Quaternion.Euler(0, 0, 90)));

            ScopeRig(props, Umbilicus + new Vector3(0, -0.07f, -0.05f), TaskArea + new Vector3(0, 0.02f, 0.02f), 70f);

            var root = new Vector3(0.25f, 1.62f, 1.05f);
            var operate = StepGroup("Step_Operate", UI("UI_11_Operate", ui, root, 0), UI("UI_Monitor_Operate", ui, MonitorScreen, 0));
            var drift = StepGroup("Step_Drift", UI("UI_12_Drift", ui, root, 0), UI("UI_Monitor_Drift", ui, MonitorScreen, 0));
            Sequence(flow, new[] { operate, drift }, new[] { SoundId.None, SoundId.VO_Operate_DriftLeft });
            Hands(operate, HandLook.Gloved, HandLook.Gloved);

            Spawn(flow, new Vector3(0, 0, -0.85f), 0);
            StageLighting();
            Save(s, "Theatre", SceneIds.StageOperate);
        }

        // ───────────────────────────── 14 Close ─────────────────────────────

        static void StageClose()
        {
            var s = Begin(out _, out var props, out var ui, out _, out var flow);
            Inst("Equipment/EQ_KickBucket", props, new Vector3(-0.7f, 0, -1.05f));
            var inst = Group("Instruments", props);
            Trocars(inst);
            Inserted("Instruments/INST_Laparoscope30", inst, Ports["Camera"], Ports["Left"] + Vector3.down * 0.12f);
            Kinematic(Inst("Instruments/INST_NeedleHolder", props, new Vector3(0.75f, 1.085f, -0.82f), Quaternion.Euler(0, 0, 90)));

            // Opening count 5 swabs, 4 on the table, 1 hidden (seeded spawn, US-CLS-05).
            for (int i = 0; i < 4; i++)
                Kinematic(Inst("Instruments/INST_Swab", props, new Vector3(2.22f, 0.935f, -1.85f + i * 0.11f), 0f));
            var hidden = Inst("Instruments/INST_Swab", props, new Vector3(0.45f, 0.01f, 0.38f), 35f);
            hidden.name = "Swab_Hidden (seeded spawn)";

            ScopeRig(props, Umbilicus + new Vector3(0.02f, -0.06f, -0.05f), Ports["Left"] + Vector3.down * 0.03f, 65f);

            var count = FacingUI("UI_14_Count", ui, new Vector3(-1.05f, 1.5f, 0.1f), LearnerEye);
            var removal = StepGroup("Step_PortRemoval", FacingUI("UI_15_PortRemoval", ui, new Vector3(1.3f, 1.55f, 0.85f), LearnerEye), UI("UI_Monitor_Close", ui, MonitorScreen, 0));
            Sequence(flow, new[] { count, removal }, new[] { SoundId.None, SoundId.VO_Close_WatchPortSite });
            Hands(count, HandLook.Gloved, HandLook.Gloved);

            Spawn(flow, new Vector3(0, 0, -0.85f), 0);
            StageLighting();
            Save(s, "Theatre", SceneIds.StageClose);
        }

        // ───────────────────────────── 90 Replay viewer ─────────────────────────────

        static void ReplayViewer()
        {
            var s = Begin(out var env, out var props, out var ui, out var lights, out var flow);
            Inst("Environment/ENV_OperatingTheatre", env, Vector3.zero);
            Inst("Equipment/EQ_ORTable", props, Vector3.zero);
            Inst("Equipment/EQ_SurgicalLights", props, Vector3.zero);
            Inst("Equipment/EQ_LapTower", props, new Vector3(0.25f, 0, 1.25f), 180f);
            Inst("Anatomy/ANA_PatientBody", props, new Vector3(0, 0.92f, 0));
            Inst("PPE/PPE_DrapeSet", props, new Vector3(-0.02f, 1.167f, 0));

            // Pose playback targets (driven from the replay file at ≥ 30 Hz)
            var ghost = Group("LearnerGhost", props);
            ghost.position = new Vector3(0, 0, -0.85f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(ghost, false);
            head.transform.localPosition = new Vector3(0, 1.65f, 0);
            head.transform.localScale = new Vector3(0.18f, 0.22f, 0.2f);
            head.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("FX_Hologram");
            Inst("PPE/PPE_SurgeonHands", ghost, new Vector3(0, 1.15f, -0.45f), 0);

            LightingRig.TheatreRig(lights);
            Lighting("ReplayViewer", 12, new Color(0.05f, 0.06f, 0.065f), LightingRig.Profile.Theatre, lights);

            var camGo = new GameObject("ReplayCamera_Free", typeof(Camera), typeof(AudioListener));
            camGo.transform.SetParent(flow);
            camGo.transform.position = new Vector3(-1.9f, 2.1f, -2.4f);
            camGo.transform.LookAt(new Vector3(0, 1.1f, 0));
            var cam = camGo.GetComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camGo.tag = "MainCamera";

            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(flow);
            var controls = UI("UI_90_ReplayControls", ui, Vector3.zero, 0);
            var canvas = controls.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.DestroyImmediate(controls.GetComponent<TrackedDeviceGraphicRaycaster>());
            controls.AddComponent<GraphicRaycaster>();
            var scaler = controls.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var panel = (RectTransform)controls.transform.GetChild(0);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0, 40);

            ReplayViewerWiring.Apply(s); // playback: ReplayPlayer drives the ghost, instruments and cameras
            Save(s, "Tools", SceneIds.ReplayViewer);
        }

        // ───────────────────────────── 91 Shader gallery (review scene, not in the build) ─────────────────────────────

        static void ShaderGallery()
        {
            var s = Begin(out var env, out var props, out var ui, out var lights, out var flow);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(env);
            floor.transform.localScale = new Vector3(2.4f, 1, 1.2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("Lobby_Floor");
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Backdrop";
            back.transform.SetParent(env);
            back.transform.position = new Vector3(0, 2f, 3.2f);
            back.transform.localScale = new Vector3(24f, 4f, 0.1f);
            back.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("Lobby_Wall");

            var sun = new GameObject("KeyLight", typeof(Light)).GetComponent<Light>();
            sun.transform.SetParent(lights);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40, -25, 0);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.useColorTemperature = true;
            sun.colorTemperature = 5200;
            var fill = LightingRig.Spot(lights, "Spot_Fill", new Vector3(0, 3.2f, -1.5f), new Vector3(0, 1, 1.4f), 110f, 6f, 6500f, LightmapBakeType.Realtime, LightShadows.None, 8f);
            fill.intensity = 3f;
            LightingRig.Reflection(lights, "Reflection", new Vector3(0, 1.5f, 0.5f), new Vector3(16, 4, 6), 1, 128);
            var ls = LightingRig.Settings("ShaderGallery", 8, 256);
            ls.bakedGI = false;
            LightingRig.ApplyEnvironment(new Color(0.18f, 0.21f, 0.22f), ls);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.4f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.23f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.1f, 0.1f);

            // Gallery-only laparoscope screen material with a test image so the optics are visible.
            var lapPath = SFPaths.Materials + "/Lighting/M_Gallery_LapScreen.mat";
            var lap = AssetDatabase.LoadAssetAtPath<Material>(lapPath);
            if (lap == null) { lap = new Material(MaterialLibrary.Get("Screen_Laparoscope")); AssetDatabase.CreateAsset(lap, lapPath); }
            var testImage = AssetDatabase.LoadAssetAtPath<Texture2D>(ImportedAssets.Root + "/heartlung-machine/textures/Machines_Albedo.tga.png")
                ?? AssetDatabase.LoadAssetAtPath<Texture2D>(SFPaths.Textures + "/T_LobbyGrid.png");
            lap.SetTexture("_BaseMap", testImage);
            lap.SetColor("_EmissionColor", Color.white * 1.3f);

            (string label, string mat, PrimitiveType prim, Vector3 scale, Vector3 euler, string anim)[] items =
            {
                ("01 Wet Tissue", "Tissue_Pink", PrimitiveType.Sphere, new Vector3(0.4f, 0.28f, 0.34f), Vector3.zero, null),
                ("02 Brushed Steel", "Stainless_Brushed", PrimitiveType.Cylinder, new Vector3(0.28f, 0.22f, 0.28f), new Vector3(0, 0, 0), null),
                ("03 Surgical Drape", "Drape_Teal", PrimitiveType.Sphere, new Vector3(0.4f, 0.4f, 0.4f), Vector3.zero, null),
                ("04 Surgical Glove", "Glove_Contaminated", PrimitiveType.Capsule, new Vector3(0.22f, 0.2f, 0.22f), Vector3.zero, "_Contamination"),
                ("05 Skin", "Skin", PrimitiveType.Sphere, new Vector3(0.36f, 0.36f, 0.36f), Vector3.zero, null),
                ("06 Blood Pool", "FX_Blood", PrimitiveType.Quad, new Vector3(0.42f, 0.42f, 1f), new Vector3(20, 0, 0), "_Spread"),
                ("07 Guided Highlight", "Guided_Mint", PrimitiveType.Sphere, new Vector3(0.36f, 0.36f, 0.36f), Vector3.zero, null),
                ("08 Dashed Ring", "Guided_DashedRing", PrimitiveType.Quad, new Vector3(0.42f, 0.42f, 1f), Vector3.zero, null),
                ("09 Target Pulse", "FX_TargetPulse", PrimitiveType.Quad, new Vector3(0.42f, 0.42f, 1f), Vector3.zero, null),
                ("10 Ghost Hand", "FX_GhostHand", PrimitiveType.Capsule, new Vector3(0.22f, 0.2f, 0.22f), Vector3.zero, null),
                ("11 Water Stream", "FX_WaterStream", PrimitiveType.Cylinder, new Vector3(0.05f, 0.25f, 0.05f), Vector3.zero, null),
                ("12 Soap Lather", "FX_SoapLather", PrimitiveType.Quad, new Vector3(0.42f, 0.42f, 1f), Vector3.zero, null),
                ("13 Dissolve", "FX_Dissolve", PrimitiveType.Sphere, new Vector3(0.36f, 0.36f, 0.36f), Vector3.zero, "_Dissolve"),
                ("14 Laminar Flow", "FX_LaminarFlow", PrimitiveType.Cube, new Vector3(0.36f, 0.5f, 0.36f), Vector3.zero, null),
                ("15 Laparoscope Screen", null, PrimitiveType.Quad, new Vector3(0.5f, 0.3f, 1f), Vector3.zero, null),
                ("16 Vitals Monitor", "Screen_Vitals", PrimitiveType.Quad, new Vector3(0.5f, 0.3f, 1f), Vector3.zero, null),
                ("17 Grid Floor", "Lobby_Floor", PrimitiveType.Quad, new Vector3(0.45f, 0.45f, 1f), new Vector3(60, 0, 0), null),
                ("18 Cove Glow", "Light_CovePanel", PrimitiveType.Cube, new Vector3(0.45f, 0.05f, 0.05f), Vector3.zero, null),
                ("19 View Vignette", "UI_Dimmer", PrimitiveType.Sphere, new Vector3(0.36f, 0.36f, 0.36f), Vector3.zero, null),
                ("20 Hologram", "FX_Hologram", PrimitiveType.Capsule, new Vector3(0.22f, 0.2f, 0.22f), Vector3.zero, null),
            };

            for (int i = 0; i < items.Length; i++)
            {
                var (label, mat, prim, scale, euler, anim) = items[i];
                int row = i / 10, col = i % 10;
                // Back row stands taller so the front row never hides it from the gallery camera.
                float pedHeight = row == 0 ? 1.35f : 0.7f;
                var basePos = new Vector3((col - 4.5f) * 0.75f + (row == 0 ? 0 : 0.375f), 0, row == 0 ? 1.7f : 0.3f);
                var ped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ped.name = "Pedestal_" + (i + 1);
                ped.transform.SetParent(props);
                ped.transform.position = basePos + Vector3.up * pedHeight * 0.5f;
                ped.transform.localScale = new Vector3(0.34f, pedHeight * 0.5f, 0.34f);
                ped.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get("Polymer_Dark");

                var go = GameObject.CreatePrimitive(prim);
                go.name = label;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(props);
                go.transform.position = basePos + Vector3.up * (pedHeight + 0.28f);
                go.transform.rotation = Quaternion.Euler(euler);
                go.transform.localScale = scale;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat == null ? lap : MaterialLibrary.Get(mat);
                if (anim != null)
                {
                    float to = anim == "_Dissolve" ? 0.9f : anim == "_EdgeOpacity" ? 0.8f : 1f;
                    go.AddComponent<ShaderPropertyAnimator>().Configure(anim, anim == "_Spread" ? 0.15f : 0f, to, 3f, ShaderPropertyAnimator.Mode.PingPong);
                }
                if (prim == PrimitiveType.Sphere || prim == PrimitiveType.Capsule || prim == PrimitiveType.Cylinder)
                    go.AddComponent<SimpleMotion>().Configure(Vector3.zero, 0f, Vector3.zero, 0f, 20f);

                var labelGo = new GameObject("Label_" + (i + 1), typeof(RectTransform));
                labelGo.transform.SetParent(props);
                labelGo.transform.position = basePos + new Vector3(0, pedHeight + 0.02f, -0.21f);
                var tmp = labelGo.AddComponent<TextMeshPro>();
                tmp.text = label;
                tmp.fontSize = 0.6f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.9f, 0.95f, 0.94f);
                ((RectTransform)labelGo.transform).sizeDelta = new Vector2(0.7f, 0.12f);
            }

            var camGo = new GameObject("GalleryCamera", typeof(Camera), typeof(AudioListener));
            camGo.transform.SetParent(flow);
            camGo.transform.position = new Vector3(0.2f, 1.85f, -3.9f);
            camGo.transform.LookAt(new Vector3(0.2f, 1.25f, 1.0f));
            camGo.GetComponent<Camera>().fieldOfView = 52f;
            camGo.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camGo.GetComponent<Camera>().backgroundColor = new Color(0.05f, 0.07f, 0.08f);
            camGo.GetComponent<Camera>().GetUniversalAdditionalCameraData().renderPostProcessing = true;
            LightingRig.GlobalVolume(lights, LightingRig.PostProfile(LightingRig.Profile.Lab));

            Save(s, "Tools", "91_ShaderGallery");
        }

        // ───────────────────────────── build settings ─────────────────────────────

        static void RegisterBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath("Core", SceneIds.Bootstrap), true),
                new EditorBuildSettingsScene(ScenePath("Frontend", SceneIds.Lobby), true),
                new EditorBuildSettingsScene(ScenePath("Frontend", SceneIds.SkillsLab), true),
                new EditorBuildSettingsScene(ScenePath("Theatre", SceneIds.OperatingTheatre), true),
                new EditorBuildSettingsScene(ScenePath("Theatre", SceneIds.StagePrep), true),
                new EditorBuildSettingsScene(ScenePath("Theatre", SceneIds.StageAccess), true),
                new EditorBuildSettingsScene(ScenePath("Theatre", SceneIds.StageOperate), true),
                new EditorBuildSettingsScene(ScenePath("Theatre", SceneIds.StageClose), true),
                // Separate desktop/WebGL build; listed but disabled for the headset build.
                new EditorBuildSettingsScene(ScenePath("Tools", SceneIds.ReplayViewer), false),
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}

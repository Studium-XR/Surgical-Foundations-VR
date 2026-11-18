using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using SurgicalFoundations.Interaction;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Renders the picture gallery that shows what the simulation is about, from several angles, into Docs/Images/Gallery:
    ///   Equipment/&lt;id&gt;.png  — each key SimReady device from four angles (the last with every moving part open),
    ///                               rendered in an isolated preview scene, so the open scenes are not touched;
    ///   &lt;Area&gt;/&lt;area&gt;_&lt;n&gt;.png — the lobby and course browser, the skills lab and equipment lab, the theatre from
    ///                               four sides, each stage, and a procedure lab, captured in play mode through the game's
    ///                               own scene loader (URP fills reflection probes only during real frames).
    /// Unsaved scene changes are kept: Unity restores the edit-mode scenes after play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class SimulationGallery
    {
        const int W = 1600, H = 900;
        const string KeyOut = "SF.Gallery.OutDir", KeyShot = "SF.Gallery.Shot", KeyPhase = "SF.Gallery.Phase", KeyStart = "SF.Gallery.PrevStart";
        const int Settle = 60, Extra = 40;

        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/Images/Gallery"));

        /// <summary>A view: fixed, or found at shot time from objects in the scene.</summary>
        class Shot
        {
            public string area, file;
            public Action<SceneLoader> load;
            public Func<(Vector3 eye, Vector3 target, float fov)?> view;
        }

        static Shot Fixed(string area, string file, Action<SceneLoader> load, Vector3 eye, Vector3 target, float fov = 72f) =>
            new Shot { area = area, file = file, load = load, view = () => (eye, target, fov) };

        // Around the operating table (at the origin, long axis X, head at −X).
        static readonly Vector3 Table = new Vector3(0f, 1.0f, 0f);

        static readonly Shot[] Shots =
        {
            // Lobby and the course browser.
            Fixed("Lobby", "lobby_1.png", l => l.LoadFrontend(SceneIds.Lobby), new Vector3(3.8f, 1.8f, -1.4f), new Vector3(-0.5f, 0.9f, -1.4f), 70f),
            new Shot { area = "Lobby", file = "lobby_2_course_browser.png", load = l => l.LoadFrontend(SceneIds.Lobby), view = () => FacePanel("CourseBrowser_Canvas", 1.55f, 0f, 62f) },
            new Shot { area = "Lobby", file = "lobby_3_course_browser_angle.png", load = l => l.LoadFrontend(SceneIds.Lobby), view = () => FacePanel("CourseBrowser_Canvas", 1.9f, 35f, 66f) },

            // Skills lab.
            Fixed("SkillsLab", "skills_lab_1.png", l => l.LoadFrontend(SceneIds.SkillsLab), new Vector3(-2.45f, 1.9f, -2.45f), new Vector3(1.4f, 0.7f, 0.9f), 78f),
            Fixed("SkillsLab", "skills_lab_2.png", l => l.LoadFrontend(SceneIds.SkillsLab), new Vector3(2.6f, 1.8f, -2.3f), new Vector3(-1.2f, 0.8f, 1.0f), 78f),
            Fixed("SkillsLab", "skills_lab_3.png", l => l.LoadFrontend(SceneIds.SkillsLab), new Vector3(0.2f, 2.6f, 2.8f), new Vector3(0f, 0.6f, -0.8f), 80f),

            // Equipment lab: an Explore lesson's devices laid out in the skills lab.
            new Shot { area = "EquipmentLab", file = "equipment_lab_1.png", load = l => { EquipmentLabRequest.Equipment = EquipmentLabSet(); l.LoadFrontend(SceneIds.SkillsLab); }, view = () => (new Vector3(0.1f, 1.62f, -0.1f), new Vector3(-0.2f, 1.0f, -1.6f), 74f) },
            new Shot { area = "EquipmentLab", file = "equipment_lab_2.png", load = null, view = () => (new Vector3(2.0f, 1.75f, -0.5f), new Vector3(0.3f, 0.9f, -1.6f), 70f) },
            new Shot { area = "EquipmentLab", file = "equipment_lab_3.png", load = null, view = () => FacePanel("Showroom_Panel", 1.0f, 0f, 62f) },

            // The operating theatre from four sides and above.
            Fixed("Theatre", "theatre_1_overview.png", l => l.LoadTheatreOnly(), new Vector3(3.15f, 2.35f, -3.15f), new Vector3(-0.9f, 0.75f, 0.5f), 82f),
            Fixed("Theatre", "theatre_2_head_end.png", l => l.LoadTheatreOnly(), new Vector3(-3.3f, 2.0f, 1.6f), Table, 75f),
            Fixed("Theatre", "theatre_3_foot_end.png", l => l.LoadTheatreOnly(), new Vector3(3.4f, 1.9f, 1.4f), Table + new Vector3(-0.4f, 0f, 0f), 75f),
            Fixed("Theatre", "theatre_4_surgeons_view.png", l => l.LoadTheatreOnly(), new Vector3(0.1f, 1.68f, -0.95f), new Vector3(-0.1f, 1.1f, 0.6f), 85f),
            Fixed("Theatre", "theatre_5_above.png", l => l.LoadTheatreOnly(), new Vector3(0.9f, 2.75f, -1.7f), Table, 84f),

            // The four stages of the scenario.
            Fixed("Stages", "stage_1_prep_scrub.png", l => l.LoadStage(SceneIds.StagePrep), new Vector3(-1.45f, 1.72f, -4.2f), new Vector3(-2.15f, 1.05f, -5.35f), 72f),
            Fixed("Stages", "stage_1_prep_back_table.png", l => l.LoadStage(SceneIds.StagePrep), new Vector3(0.9f, 1.75f, -2.4f), new Vector3(2.0f, 0.9f, -1.3f), 72f),
            Fixed("Stages", "stage_2_access.png", l => l.LoadStage(SceneIds.StageAccess), new Vector3(0.25f, 1.64f, -1.0f), new Vector3(-0.4f, 1.28f, 0.55f), 80f),
            Fixed("Stages", "stage_2_access_side.png", l => l.LoadStage(SceneIds.StageAccess), new Vector3(1.6f, 1.9f, -1.4f), Table, 70f),
            Fixed("Stages", "stage_3_operate.png", l => l.LoadStage(SceneIds.StageOperate), new Vector3(0.1f, 1.7f, -1.0f), new Vector3(0.2f, 1.4f, 1.2f), 80f),
            Fixed("Stages", "stage_3_operate_tower.png", l => l.LoadStage(SceneIds.StageOperate), new Vector3(-0.6f, 1.6f, 0.2f), new Vector3(0.25f, 1.45f, 1.25f), 70f),
            Fixed("Stages", "stage_4_close.png", l => l.LoadStage(SceneIds.StageClose), new Vector3(0.25f, 1.7f, -1.1f), Table, 80f),
            Fixed("Stages", "stage_4_close_count.png", l => l.LoadStage(SceneIds.StageClose), new Vector3(1.0f, 1.8f, -2.2f), new Vector3(2.0f, 0.9f, -1.3f), 72f),

            // A procedure lab (the runner's default procedure when no lesson is chosen).
            Fixed("ProcedureLab", "procedure_lab_1.png", l => l.LoadStage(SceneIds.ProcedureLab), new Vector3(0.2f, 1.7f, -1.15f), new Vector3(0.1f, 1.25f, 0.5f), 82f),
            new Shot { area = "ProcedureLab", file = "procedure_lab_2_panel.png", load = null, view = () => FacePanel("ProcedurePanel", 1.3f, 0f, 60f) },
            new Shot { area = "ProcedureLab", file = "procedure_lab_3_trolley.png", load = null, view = () => LookAtObject("SR_trolley_instrument_b01", new Vector3(-0.5f, 1.1f, -0.9f), 62f) },
            Fixed("ProcedureLab", "procedure_lab_4_wide.png", null, new Vector3(2.6f, 2.2f, -2.4f), Table, 78f),
        };

        static List<string> EquipmentLabSet() => new List<string>
        {
            "anesthesia_machine_a01", "lung_ventilation_artificial_a01", "monitor_patient_a01", "pump_infusion_a01", "pump_suction_surgical_a01",
            "unit_electrosurgical_a01", "defibrillator_a01", "ultrasound_b01", "trolley_emergency_a01",
        };

        static SimulationGallery() => EditorApplication.update += Tick;

        [MenuItem(SFPaths.MenuRoot + "Tools/Render Simulation Gallery", priority = 43)]
        public static void RenderAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Gallery] Stop play mode first."); return; }
            RenderEquipment();
            StartPlayShots();
        }

        [MenuItem(SFPaths.MenuRoot + "Tools/Render Equipment Gallery Only", priority = 44)]
        public static void RenderEquipmentOnly() => RenderEquipment();

        // ─────────────────────────────── equipment (edit mode, preview scene) ───────────────────────────────

        static readonly string[] EquipmentShots =
        {
            "anesthesia_machine_a01", "lung_ventilation_artificial_a01", "monitor_patient_a01", "pump_infusion_a01", "pump_system_infusion_a01",
            "pump_suction_surgical_a01", "unit_electrosurgical_a01", "video_system_a01", "table_surgical_a01", "defibrillator_a01",
            "ultrasound_b01", "trolley_emergency_a01", "cart_medical_utility_a01", "warmer_infusion_a01", "CT_injection_system_a01",
            "robot_surgical_c01", "instrument_artery_forceps_b01", "instrument_bone_rongeurs_b01", "instrument_scissors_a01",
            "stapler_surgical_a01", "syringe_a01", "device_otoscope_a01", "container_system_sterilising_a02", "instrument_storage_a01",
        };

        public static void RenderEquipment()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SimReadyLibrary>(SFPaths.Root + "/Resources/SimReadyLibrary.asset");
            if (lib == null) { Debug.LogWarning("[Gallery] Build the SimReady library first."); return; }
            var dir = Path.Combine(Root, "Equipment");
            Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.NewPreviewScene();
            int done = 0;
            try
            {
                Stage(scene);
                foreach (var id in EquipmentShots)
                {
                    var e = lib.Find(id);
                    if (e == null || e.prefab == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab, scene);
                    var b = Bounds(go);
                    float r = Mathf.Max(b.extents.magnitude, 0.08f);
                    var c = b.center;
                    var tiles = new List<Texture2D>
                    {
                        Shoot(scene, c + Dir(-35f, 18f) * r * 2.6f, c, 35f),
                        Shoot(scene, c + Dir(35f, 18f) * r * 2.6f, c, 35f),
                        Shoot(scene, c + Dir(90f, 8f) * r * 2.6f, c, 35f),
                    };
                    var dev = go.GetComponent<ArticulatedDevice>();
                    if (dev != null && OpenAll(dev))
                    {
                        // Look from the side the parts opened towards (drawers slide out of the front).
                        var moved = Vector3.ProjectOnPlane(Bounds(go).center - c, Vector3.up);
                        var from = moved.sqrMagnitude > 1e-4f ? Quaternion.Euler(0f, 30f, 0f) * moved.normalized : Dir(-30f, 0f);
                        from = (from + Vector3.up * 0.45f).normalized;
                        tiles.Add(Shoot(scene, c + from * r * 2.6f, c, 35f));
                    }
                    else tiles.Add(Shoot(scene, c + Dir(180f, 15f) * r * 2.6f, c, 35f));
                    File.WriteAllBytes(Path.Combine(dir, id + ".png"), Sheet(tiles, 2).EncodeToPNG());
                    foreach (var t in tiles) Object.DestroyImmediate(t);
                    Object.DestroyImmediate(go);
                    done++;
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Debug.Log($"[Gallery] {done} equipment sheets (4 angles each) → {dir}");
        }

        /// <summary>Front is −Z (prefabs face the learner along +Z, so the camera stands at −Z). Yaw turns around the model.</summary>
        static Vector3 Dir(float yaw, float pitch) => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * -1f + Vector3.zero;

        static bool OpenAll(ArticulatedDevice dev)
        {
            dev.ManifestAsset = dev.ManifestAsset; // binds the joints in edit mode
            bool any = false;
            foreach (var j in dev.Joints)
            {
                if (j.Continuous) continue;
                var v = j.Open;
                if (j.Rotary) j.node.localRotation = j.restRot * Quaternion.AngleAxis(v, Vector3.up);
                else j.node.localPosition = j.restPos + j.restRot * (Vector3.up * v);
                any = true;
            }
            return any;
        }

        static void Stage(Scene scene)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 40f;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fm.SetColor("_BaseColor", new Color(0.36f, 0.39f, 0.41f));
            fm.SetFloat("_Smoothness", 0.25f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            SceneManager.MoveGameObjectToScene(floor, scene);
            foreach (var (rot, intensity) in new[] { (Quaternion.Euler(45f, -30f, 0f), 1.4f), (Quaternion.Euler(30f, 150f, 0f), 0.6f) })
            {
                var l = new GameObject("Light").AddComponent<Light>();
                l.type = LightType.Directional;
                l.transform.rotation = rot;
                l.intensity = intensity;
                l.shadows = LightShadows.Soft;
                SceneManager.MoveGameObjectToScene(l.gameObject, scene);
            }
        }

        static Texture2D Shoot(Scene scene, Vector3 eye, Vector3 target, float fov)
        {
            var go = new GameObject("GalleryCamera");
            SceneManager.MoveGameObjectToScene(go, scene);
            var cam = go.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.30f, 0.33f, 0.35f);
            var tex = RenderCam(cam, eye, target, fov, 800, 600);
            Object.DestroyImmediate(go);
            return tex;
        }

        static Texture2D Sheet(List<Texture2D> tiles, int cols)
        {
            int w = tiles[0].width, h = tiles[0].height, rows = Mathf.CeilToInt(tiles.Count / (float)cols);
            var sheet = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++)
            {
                int x = (i % cols) * w, y = (rows - 1 - i / cols) * h;
                sheet.SetPixels(x, y, w, h, tiles[i].GetPixels());
            }
            sheet.Apply();
            return sheet;
        }

        // ─────────────────────────────── play-mode areas ───────────────────────────────

        static void StartPlayShots()
        {
            Directory.CreateDirectory(Root);
            SessionState.SetString(KeyOut, Root);
            SessionState.SetInt(KeyShot, 0);
            SessionState.SetInt(KeyPhase, 0);
            SessionState.SetString(KeyStart, EditorSceneManager.playModeStartScene != null ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>($"{SFPaths.Scenes}/Core/{SceneIds.Bootstrap}.unity");
            EditorApplication.EnterPlaymode();
        }

        static bool Running => !string.IsNullOrEmpty(SessionState.GetString(KeyOut, ""));

        static void Tick()
        {
            if (!Running || !EditorApplication.isPlaying) return;
            var loader = SceneLoader.Instance;
            if (loader == null || loader.IsBusy || Time.frameCount < 30) return;
            int i = SessionState.GetInt(KeyShot, 0), phase = SessionState.GetInt(KeyPhase, 0);
            if (i >= Shots.Length) { Finish(); return; }
            var s = Shots[i];
            if (phase == 0)
            {
                // A shot with no load of its own reuses the previous shot's scene (another angle of the same moment).
                s.load?.Invoke(loader);
                SessionState.SetInt(KeyPhase, s.load == null ? Settle : 1);
                return;
            }
            if (phase == 1 || phase == Settle)
            {
                // Characters only animate while a camera that renders every frame sees them; the gallery camera renders
                // once, so without this the staff would stand in their bind (T-) pose.
                foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (phase < Settle + Extra) { SessionState.SetInt(KeyPhase, phase + 1); return; }

            var v = s.view();
            if (v.HasValue)
            {
                var rig = Object.FindAnyObjectByType<XROrigin>();
                var hidden = rig != null ? rig.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray() : new Renderer[0];
                foreach (var r in hidden) r.enabled = false;
                var dir = Path.Combine(SessionState.GetString(KeyOut, ""), s.area);
                Directory.CreateDirectory(dir);
                var go = new GameObject("GalleryCamera");
                var cam = go.AddComponent<Camera>();
                var tex = RenderCam(cam, v.Value.eye, v.Value.target, v.Value.fov, W, H);
                File.WriteAllBytes(Path.Combine(dir, s.file), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(go);
                foreach (var r in hidden) r.enabled = true;
            }
            else Debug.LogWarning($"[Gallery] Skipped {s.file}: its subject was not found.");
            SessionState.SetInt(KeyShot, i + 1);
            SessionState.SetInt(KeyPhase, 0);
        }

        static void Finish()
        {
            var start = SessionState.GetString(KeyStart, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            var outDir = SessionState.GetString(KeyOut, "");
            SessionState.EraseString(KeyOut);
            SessionState.EraseInt(KeyShot);
            SessionState.EraseInt(KeyPhase);
            SessionState.EraseString(KeyStart);
            EditorApplication.ExitPlaymode();
            Debug.Log($"[Gallery] Simulation views rendered to {outDir}");
        }

        // ─────────────────────────────── views found at shot time ───────────────────────────────

        /// <summary>In front of a world-space panel, <paramref name="dist"/> metres back, turned <paramref name="yaw"/> degrees.</summary>
        static (Vector3, Vector3, float)? FacePanel(string name, float dist, float yaw, float fov)
        {
            var t = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.gameObject.activeInHierarchy);
            if (t == null) return null;
            var back = Quaternion.Euler(0f, yaw, 0f) * -Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            return (t.position + back * dist + Vector3.up * 0.05f, t.position, fov);
        }

        static (Vector3, Vector3, float)? LookAtObject(string prefix, Vector3 offset, float fov)
        {
            var t = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(x => x.name.StartsWith(prefix));
            if (t == null) return null;
            var b = Bounds(t.gameObject);
            return (b.center + offset, b.center + Vector3.up * 0.1f, fov);
        }

        static (Vector3, Vector3, float)? Exhibits(float yaw)
        {
            var ex = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(x => x.name.StartsWith("Exhibit_")).ToList();
            if (ex.Count == 0) return null;
            var centre = ex.Aggregate(Vector3.zero, (a, t) => a + t.position) / ex.Count;
            var head = Object.FindAnyObjectByType<XROrigin>()?.Camera?.transform;
            var from = head != null ? Vector3.ProjectOnPlane(head.position - centre, Vector3.up).normalized : Vector3.back;
            var eye = centre + Quaternion.Euler(0f, yaw, 0f) * from * 3.2f + Vector3.up * 1.9f;
            return (eye, centre + Vector3.up * 0.8f, 78f);
        }

        // ─────────────────────────────── shared ───────────────────────────────

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.2f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Texture2D RenderCam(Camera cam, Vector3 eye, Vector3 target, float fov, int width, int height)
        {
            cam.transform.position = eye;
            cam.transform.LookAt(target);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 60f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.allowXRRendering = false;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }
    }
}

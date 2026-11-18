using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using SurgicalFoundations.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Photographs every part of the courses outside Surgical Foundations, in play mode: the course page, each reading
    /// and video, each quiz (first question, answered), and every VR lab, assessment and Explore lesson as the learner
    /// sees it plus an overview of the room and the lab's instruction panel. Writes JPEGs to Docs/Images/Courses and a
    /// gallery page to Docs/CourseGallery.md. Start play mode from 00_Bootstrap first.
    /// </summary>
    public static class CourseGalleryCapture
    {
        const string ImageRoot = "Docs/Images/Courses";
        const string Page = "Docs/CourseGallery.md";
        const int Wide = 1600, Tall = 1000;

        [MenuItem("Tools/Capture Course Gallery (play mode)")]
        public static void Run() => Run(null);

        /// <summary>Captures the given course codes (all non-Foundations courses when null).</summary>
        public static void Run(string[] codes)
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Gallery] Enter play mode from 00_Bootstrap first."); return; }
            var foundations = new HashSet<string>(CourseCatalogStore.Programs.First(p => p.id == "surgical-foundations").courseIds);
            var courses = CourseCatalogStore.Programs.Where(p => p.id != "surgical-foundations").SelectMany(p => p.courseIds).Distinct()
                .Where(id => !foundations.Contains(id)).Select(CourseCatalogStore.Find).Where(c => c != null)
                .Where(c => codes == null || codes.Contains(c.code)).ToList();

            var steps = new List<Func<bool>>();
            var shots = new Dictionary<string, List<(string file, string caption)>>(); // course code -> images
            var labShots = new Dictionary<string, List<(string file, string caption)>>(); // simulation id -> images
            void Add(Dictionary<string, List<(string, string)>> d, string key, string file, string caption)
            {
                if (!d.TryGetValue(key, out var l)) d[key] = l = new List<(string, string)>();
                l.Add((file, caption));
            }

            // ── 1. Course pages, readings, videos and quizzes in the lobby browser ──
            steps.Add(Do(() => SceneLoader.Instance.LoadFrontend(SceneIds.Lobby)));
            steps.Add(Idle());
            steps.Add(Wait(1.5));
            foreach (var c in courses)
            {
                var cc = c;
                string dir = $"{ImageRoot}/{c.code}";
                int n = 0;
                steps.Add(Do(() => Browser()?.ShowCourse(cc)));
                steps.Add(Wait(0.3));
                string f0 = $"{dir}/{++n:00}_course.jpg";
                steps.Add(Do(() => { ShootPanel(Browser()?.PanelRect, 1.5f, f0); Add(shots, cc.code, f0, "Course page in the lobby browser"); }));
                foreach (var l in CourseCatalogStore.Lessons(c).Where(l => l.kind is LessonKind.Reading or LessonKind.Video or LessonKind.Quiz))
                {
                    var ll = l;
                    string f = $"{dir}/{++n:00}_{Slug(l.kind.ToString())}_{Slug(l.title)}.jpg";
                    steps.Add(Do(() => Browser()?.ShowLesson(cc, ll, 0, ll.kind == LessonKind.Quiz)));
                    steps.Add(Wait(0.3));
                    steps.Add(Do(() => { ShootPanel(Browser()?.PanelRect, 1.5f, f); Add(shots, cc.code, f, $"{Kind(ll.kind)}: {ll.title}" + (ll.kind == LessonKind.Quiz ? $" ({ll.questions.Count} questions, first one answered)" : "")); }));
                }
                steps.Add(Do(() => Browser()?.ShowCourse(cc)));
            }

            // ── 2. VR labs, assessments (once per simulation) and Explore lessons ──
            var seenSims = new HashSet<string>();
            foreach (var c in courses)
            {
                foreach (var l in CourseCatalogStore.Lessons(c))
                {
                    bool vr = l.kind is LessonKind.VrLab or LessonKind.VrAssessment;
                    if (!vr && l.kind != LessonKind.Explore) continue;
                    if (vr && !seenSims.Add(l.simulationId)) continue;
                    var cc = c; var ll = l;
                    bool procedure = vr && Procedures.ProcedureRunner.Exists(l.simulationId);
                    string baseName = vr ? $"{ImageRoot}/_labs/{Slug(l.simulationId)}" : $"{ImageRoot}/{c.code}/explore_{Slug(l.title)}";
                    double began = 0;
                    steps.Add(Do(() => SceneLoader.Instance.LoadFrontend(SceneIds.Lobby)));
                    steps.Add(Idle());
                    steps.Add(Wait(1.0));
                    steps.Add(Do(() => { began = EditorApplication.timeSinceStartup; CourseSession.Begin(cc, ll); }));
                    steps.Add(Wait(0.5));
                    steps.Add(Idle());
                    if (procedure) steps.Add(() => GameObject.Find("ProcedurePanel") != null || EditorApplication.timeSinceStartup - began > 40);
                    steps.Add(Wait(3.0)); // let animators, cables and lighting settle
                    steps.Add(Do(() =>
                    {
                        string key = vr ? ll.simulationId : cc.code;
                        var d = vr ? labShots : shots;
                        ShootLearner(baseName + "_learner.jpg");
                        Add(d, key, baseName + "_learner.jpg", vr ? "What the learner sees at the start" : $"Explore in 3D: {ll.title}");
                        if (!vr)
                        {
                            var lab = Object.FindAnyObjectByType<Interaction.EquipmentLab>();
                            if (lab != null && lab.Current != null)
                            {
                                ShootObject(lab.Current, baseName + "_device.jpg");
                                Add(d, key, baseName + "_device.jpg", $"Close-up: {Interaction.SimReadyLibrary.Readable(lab.Current.name)}");
                            }
                            var list = GameObject.Find("Showroom_Panel");
                            if (list != null)
                            {
                                ShootPanel((RectTransform)list.transform, 1f, baseName + "_list.jpg");
                                Add(d, key, baseName + "_list.jpg", "The lesson's device list");
                            }
                        }
                        if (vr)
                        {
                            ShootOverview(baseName + "_room.jpg");
                            Add(d, key, baseName + "_room.jpg", "Overview of the room");
                            var panel = GameObject.Find("ProcedurePanel");
                            if (panel != null)
                            {
                                ShootPanel((RectTransform)panel.transform, 1.25f, baseName + "_panel.jpg");
                                Add(d, key, baseName + "_panel.jpg", "Instruction panel, first step");
                            }
                        }
                    }));
                }
            }

            // ── 3. Gallery page ──
            steps.Add(Do(() => WritePage(courses, shots, labShots)));
            steps.Add(Do(() => SceneLoader.Instance.LoadFrontend(SceneIds.Lobby)));

            int i = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= tick; Debug.LogWarning("[Gallery] Stopped: play mode ended."); return; }
                try
                {
                    while (i < steps.Count && steps[i]()) i++;
                }
                catch (Exception e) { Debug.LogException(e); i++; }
                if (i >= steps.Count) { EditorApplication.update -= tick; Debug.Log($"[Gallery] Done: {Page}"); }
            };
            EditorApplication.update += tick;
            Debug.Log($"[Gallery] Capturing {courses.Count} courses…");
        }

        // ─────────────────────────────── steps ───────────────────────────────

        static Func<bool> Do(Action a) => () => { a(); return true; };

        static Func<bool> Wait(double seconds)
        {
            double until = -1;
            return () =>
            {
                if (until < 0) until = EditorApplication.timeSinceStartup + seconds;
                if (EditorApplication.timeSinceStartup < until) return false;
                until = -1;
                return true;
            };
        }

        static Func<bool> Idle() => () => SceneLoader.Instance != null && !SceneLoader.Instance.IsBusy;

        static CourseBrowser Browser() => Object.FindAnyObjectByType<CourseBrowser>();

        // ─────────────────────────────── cameras ───────────────────────────────

        /// <summary>Frames a world-space panel square-on, filling the image.</summary>
        static void ShootPanel(RectTransform panel, float width, string file)
        {
            if (panel == null) return;
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float w = (corners[3] - corners[0]).magnitude, h = (corners[1] - corners[0]).magnitude;
            const float fov = 50f;
            float aspect = Wide / (float)Tall;
            float dist = Mathf.Max(h / 2f / Mathf.Tan(fov / 2f * Mathf.Deg2Rad), w / 2f / (Mathf.Tan(fov / 2f * Mathf.Deg2Rad) * aspect)) * 1.06f;
            var centre = (corners[0] + corners[2]) / 2f;
            Shoot(centre - panel.forward * dist, Quaternion.LookRotation(panel.forward, panel.up), fov, file);
        }

        static void ShootLearner(string file)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            Shoot(head.position, head.rotation, 80f, file);
        }

        /// <summary>From behind and above the learner, looking at the work (the patient or the display spot).</summary>
        static void ShootOverview(string file)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var target = head.position + fwd * 1.2f + Vector3.down * 0.55f;
            var umbilicus = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Umbilicus");
            if (umbilicus != null && Vector3.Distance(umbilicus.position, head.position) < 4f) target = umbilicus.position;
            var pos = head.position - fwd * 1.3f + Vector3.up * 0.75f;
            Shoot(pos, Quaternion.LookRotation(target - pos, Vector3.up), 80f, file);
        }

        /// <summary>A three-quarter close-up that fits an object's bounds, seen from the learner's side.</summary>
        static void ShootObject(GameObject target, string file)
        {
            var rs = target.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
            if (rs.Count == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var head = Camera.main != null ? Camera.main.transform.position : b.center + Vector3.back;
            var toViewer = Vector3.ProjectOnPlane(head - b.center, Vector3.up).normalized;
            var side = Vector3.Cross(Vector3.up, toViewer);
            var dir = (toViewer + side * 0.45f + Vector3.up * 0.35f).normalized;
            const float fov = 40f;
            float dist = b.extents.magnitude / Mathf.Sin(fov / 2f * Mathf.Deg2Rad) * 0.85f;
            var pos = b.center + dir * Mathf.Max(dist, 0.25f);
            Shoot(pos, Quaternion.LookRotation(b.center - pos, Vector3.up), fov, file);
        }

        static void Shoot(Vector3 pos, Quaternion rot, float fov, string file)
        {
            var src = Camera.main;
            var go = new GameObject("GalleryCamera");
            // Hide the learner's own rig (hands, pointer rays, teleport arcs) so the shot shows the scene.
            var rig = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var hidden = rig != null ? rig.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList() : new List<Renderer>();
            var hands = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("ControllerHand_"))
                .SelectMany(t => t.GetComponentsInChildren<Renderer>()).Where(r => r.enabled);
            hidden.AddRange(hands.Except(hidden));
            foreach (var r in hidden) r.enabled = false;
            try
            {
                var cam = go.AddComponent<Camera>();
                if (src != null) cam.CopyFrom(src);
                cam.transform.SetPositionAndRotation(pos, rot);
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.02f;
                cam.aspect = Wide / (float)Tall;
                var rt = RenderTexture.GetTemporary(Wide, Tall, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Wide, Tall, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Wide, Tall), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, tex.EncodeToJPG(86));
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(go);
                foreach (var r in hidden) if (r != null) r.enabled = true;
            }
        }

        // ─────────────────────────────── page ───────────────────────────────

        static void WritePage(List<Course> courses, Dictionary<string, List<(string file, string caption)>> shots,
                              Dictionary<string, List<(string file, string caption)>> labShots)
        {
            string Rel(string f) => f.Substring("Docs/".Length);
            var sb = new StringBuilder();
            sb.AppendLine("# Course gallery");
            sb.AppendLine();
            sb.AppendLine("Every part of the courses outside Surgical Foundations, captured in play mode by **Tools → Capture Course Gallery**: ");
            sb.AppendLine("the course page, each reading, video and quiz in the lobby course browser, and each VR lab, assessment and Explore lesson ");
            sb.AppendLine("as the learner sees it. Re-run the tool after changing a course to refresh the images.");
            sb.AppendLine();
            foreach (var p in CourseCatalogStore.Programs.Where(p => p.id != "surgical-foundations"))
            {
                var inProgram = courses.Where(c => p.courseIds.Contains(c.id) && shots.ContainsKey(c.code)).ToList();
                if (inProgram.Count == 0) continue;
                sb.AppendLine($"## {p.title}");
                sb.AppendLine();
                foreach (var c in inProgram)
                {
                    sb.AppendLine($"### {c.code} · {c.title}");
                    sb.AppendLine();
                    sb.AppendLine($"{c.subtitle} {c.level} · {c.hours} h · {c.cmeCredits:0.#} CME.");
                    sb.AppendLine();
                    foreach (var (file, caption) in shots[c.code])
                        sb.AppendLine($"![{caption}]({Rel(file)})  \n*{caption}*\n");
                    foreach (var l in CourseCatalogStore.Lessons(c).Where(l => l.kind is LessonKind.VrLab or LessonKind.VrAssessment))
                    {
                        if (!labShots.TryGetValue(l.simulationId, out var imgs)) continue;
                        sb.AppendLine($"**{Kind(l.kind)}: {l.title}** (`{l.simulationId}`)");
                        sb.AppendLine();
                        foreach (var (file, caption) in imgs) sb.AppendLine($"![{caption}]({Rel(file)})  \n*{caption}*\n");
                    }
                }
            }
            File.WriteAllText(Page, sb.ToString());
        }

        static string Kind(LessonKind k) => k switch
        {
            LessonKind.Reading => "Reading", LessonKind.Video => "Video", LessonKind.Quiz => "Quiz", LessonKind.VrLab => "VR lab",
            LessonKind.VrAssessment => "VR assessment", _ => "Explore in 3D",
        };

        static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
            var r = sb.ToString();
            while (r.Contains("--")) r = r.Replace("--", "-");
            r = r.Trim('-');
            return r.Length > 40 ? r.Substring(0, 40).TrimEnd('-') : r;
        }
    }
}

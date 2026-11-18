using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Play-mode smoke test for the course catalogue: launches every VR lab, VR assessment (one per simulation) and
    /// Explore lesson from the lobby the way the course browser does, waits for its scene, and writes what loaded plus
    /// any warnings or errors to Logs/CourseLessonSmokeTest.txt. Start play mode from 00_Bootstrap first.
    /// </summary>
    public static class CourseLessonSmokeTest
    {
        const string Report = "Logs/CourseLessonSmokeTest.txt";

        [MenuItem("Tools/Test Course Lessons (play mode)")]
        static void Run() => Run(false);

        [MenuItem("Tools/Test Course Lessons, all programs (play mode)")]
        static void RunAll() => Run(true);

        /// <summary>Runs the test. <paramref name="only"/> limits it to one simulation id; <paramref name="settle"/> is the wait after each load.</summary>
        public static void Run(bool includeFoundations, string only = null, float settle = 5f)
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[CourseTest] Enter play mode from 00_Bootstrap first."); return; }

            var skip = includeFoundations ? new HashSet<string>()
                : new HashSet<string>(CourseCatalogStore.Programs.First(p => p.id == "surgical-foundations").courseIds);
            var queue = new List<(Course course, Lesson lesson)>();
            var seen = new HashSet<string>();
            foreach (var p in CourseCatalogStore.Programs)
                foreach (var cid in p.courseIds)
                {
                    if (skip.Contains(cid)) continue;
                    var c = CourseCatalogStore.Find(cid);
                    if (c == null) continue;
                    foreach (var l in CourseCatalogStore.Lessons(c))
                    {
                        string key = l.kind == LessonKind.Explore ? "explore:" + l.id
                            : l.kind == LessonKind.VrLab || l.kind == LessonKind.VrAssessment ? "vr:" + l.simulationId : null;
                        if (only != null && l.simulationId != only) continue;
                        if (key != null && seen.Add(key)) queue.Add((c, l));
                    }
                }

            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, $"Course lesson smoke test: {queue.Count} lessons\n");
            var logs = new List<string>();
            void Capture(string msg, string st, LogType type)
            {
                if (type == LogType.Log) return;
                var first = st?.Split('\n').FirstOrDefault(s => s.Contains("SurgicalFoundations"));
                logs.Add(type + ": " + msg.Split('\n')[0] + (type == LogType.Exception && first != null ? "  at " + first.Trim() : ""));
            }
            Application.logMessageReceived += Capture;

            int i = 0, phase = 0, failures = 0;
            double wait = 0, began = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                void Stop() { EditorApplication.update -= tick; Application.logMessageReceived -= Capture; }
                if (!EditorApplication.isPlaying) { Stop(); return; }
                var loader = SceneLoader.Instance;
                if (loader == null || loader.IsBusy || EditorApplication.timeSinceStartup < wait) return;
                if (i >= queue.Count)
                {
                    File.AppendAllText(Report, $"done: {queue.Count} lessons, {failures} with problems\n");
                    Debug.Log($"[CourseTest] {queue.Count} lessons, {failures} with problems. Report: {Report}");
                    Stop();
                    return;
                }
                var (course, lesson) = queue[i];
                switch (phase)
                {
                    case 0: loader.LoadFrontend(SceneIds.Lobby); phase = 1; wait = EditorApplication.timeSinceStartup + 1.5; return;
                    case 1: logs.Clear(); CourseSession.Begin(course, lesson); began = EditorApplication.timeSinceStartup; phase = 2; wait = began + settle; return;
                }
                // A procedure lab builds its equipment, cables and panel after the scene loads: give it up to 30 s.
                bool wantsPanel = Procedures.ProcedureRunner.Exists(lesson.simulationId) && lesson.kind != LessonKind.Explore;
                if (wantsPanel && GameObject.Find("ProcedurePanel") == null && EditorApplication.timeSinceStartup - began < 30) return;

                var scenes = string.Join(" ", Enumerable.Range(0, SceneManager.sceneCount).Select(s => SceneManager.GetSceneAt(s).name));
                string expected = lesson.kind == LessonKind.Explore ? SceneIds.SkillsLab
                    : Procedures.ProcedureRunner.Exists(lesson.simulationId) ? SceneIds.ProcedureLab : null;
                bool sceneOk = expected == null || scenes.Contains(expected);
                string info = "";
                var panel = expected == SceneIds.ProcedureLab ? GameObject.Find("ProcedurePanel") : null;
                if (expected == SceneIds.ProcedureLab)
                {
                    if (panel == null) { sceneOk = false; info = " | no procedure panel"; }
                    else info = $" | ready in {EditorApplication.timeSinceStartup - began:0.0} s | " + string.Join(" / ", panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Take(3)
                        .Select(t => t.text.Replace("\n", " ")).Where(t => t.Length > 0));
                }
                var problems = logs.Distinct().ToList();
                bool ok = sceneOk && !problems.Any(p => p.StartsWith("Error") || p.StartsWith("Exception"));
                if (!ok) failures++;
                File.AppendAllText(Report, $"{(ok ? "OK  " : "FAIL")} {course.code} {lesson.kind} {lesson.simulationId ?? lesson.id} -> {scenes}{info}\n"
                    + string.Concat(problems.Select(p => "      " + p + "\n")));
                i++;
                phase = 0;
            };
            EditorApplication.update += tick;
            Debug.Log($"[CourseTest] Testing {queue.Count} lessons…");
        }
    }
}

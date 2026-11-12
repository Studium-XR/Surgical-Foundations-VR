using System;
using System.Collections.Generic;
using SurgicalFoundations.Backend;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using UnityEngine;

namespace SurgicalFoundations.Curriculum
{
    /// <summary>
    /// The course lesson the learner is working on right now, and the bridge between lessons and simulations: it knows
    /// which simulation ids this build can run, launches them, and when a VR lesson's session is scored it records the
    /// result against the lesson. Read by the palm hub (course line, objectives).
    /// </summary>
    public static class CourseSession
    {
        public static Course Course { get; private set; }
        public static Lesson Lesson { get; private set; }
        public static event Action Changed;

        /// <summary>True while a lesson that is one stage of the theatre scenario runs: the summary follows that stage.</summary>
        public static bool SingleStage => Lesson != null && StageOnly.ContainsKey(Lesson.simulationId ?? "");

        /// <summary>True while the current lesson is a procedure lab (Resources/Procedures/&lt;simulationId&gt;.json).</summary>
        public static bool ProcedureLab => Lesson != null && (Lesson.kind == LessonKind.VrLab || Lesson.kind == LessonKind.VrAssessment)
                                           && Procedures.ProcedureRunner.Exists(Lesson.simulationId);

        // ───────────── simulations this build can run ─────────────

        public const string FullScenario = "lap-foundations";
        public const string SkillsLab = "skills-lab";
        public const string EquipmentLab = "equipment-lab";

        static readonly Dictionary<string, string> StageOnly = new Dictionary<string, string>
        {
            { "lap-prep", SceneIds.StagePrep },
            { "lap-access", SceneIds.StageAccess },
            { "lap-operate", SceneIds.StageOperate },
            { "lap-close", SceneIds.StageClose },
        };

        public static bool IsAvailable(Lesson l)
        {
            if (l == null) return false;
            switch (l.kind)
            {
                case LessonKind.Reading:
                case LessonKind.Quiz:
                case LessonKind.Video:
                    return true;
                case LessonKind.Explore:
                    return true; // the equipment lab lays out whatever SimReady models are installed
                default:
                    var id = l.simulationId ?? "";
                    return id == FullScenario || id == SkillsLab || StageOnly.ContainsKey(id) || Procedures.ProcedureRunner.Exists(id);
            }
        }

        /// <summary>Starts a lesson: reading/quiz/video stay in the lobby (the caller shows them); VR lessons load their scene.</summary>
        public static void Begin(Course course, Lesson lesson)
        {
            Course = course;
            Lesson = lesson;
            CourseProgressStore.Enroll(course.id);
            Changed?.Invoke();
            if (lesson == null) return;

            var loader = SceneLoader.Instance;
            var session = SessionManager.Instance;
            if (lesson.kind == LessonKind.VrAssessment && session != null && !session.IsRunning) session.SetMode(TrainingMode.Assessment);
            else if (lesson.kind == LessonKind.VrLab && session != null && !session.IsRunning) session.SetMode(TrainingMode.Guided);

            switch (lesson.kind)
            {
                case LessonKind.VrLab:
                case LessonKind.VrAssessment:
                    var id = lesson.simulationId ?? "";
                    if (StageOnly.TryGetValue(id, out var stage)) loader?.LoadStage(stage);
                    else if (id == FullScenario) loader?.LoadStage(SceneIds.StagePrep);
                    else if (id == SkillsLab) loader?.LoadFrontend(SceneIds.SkillsLab);
                    else if (Procedures.ProcedureRunner.Exists(id)) loader?.LoadStage(SceneIds.ProcedureLab);
                    break;
                case LessonKind.Explore:
                    EquipmentLabRequest.Equipment = lesson.equipment;
                    loader?.LoadFrontend(SceneIds.SkillsLab);
                    break;
            }
        }

        public static void Clear()
        {
            Course = null;
            Lesson = null;
            Changed?.Invoke();
        }

        /// <summary>Records a finished non-VR lesson (reading, video, quiz) for the current course.</summary>
        public static void Complete(float score, bool completed)
        {
            if (Course == null || Lesson == null) return;
            CourseProgressStore.RecordAttempt(Course.id, Lesson.id, score, completed);
            Changed?.Invoke();
        }

        /// <summary>Hooked to the session recorder: a scored VR session counts for the lesson that launched it.</summary>
        internal static void OnSessionScored(SessionRecorder recorder)
        {
            if (Course == null || Lesson == null || recorder?.LastResult?.request == null) return;
            if (Lesson.kind != LessonKind.VrLab && Lesson.kind != LessonKind.VrAssessment) return;
            var r = recorder.LastResult.request;
            bool pass = r.overallScore >= Lesson.passScore && !recorder.LastResult.criticalFailure;
            CourseProgressStore.RecordAttempt(Course.id, Lesson.id, r.overallScore, pass, recorder.SessionId);
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            // BackendServices lives in 00_Bootstrap and may come up after this runs; poll briefly until it exists.
            var go = new GameObject("CourseSessionHook");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<Hooker>();
        }

        class Hooker : MonoBehaviour
        {
            SessionRecorder hooked;

            void Update()
            {
                var rec = BackendServices.Instance != null ? BackendServices.Instance.Recorder : null;
                if (rec == hooked) return;
                if (hooked != null) hooked.ResultReady -= OnReady;
                hooked = rec;
                if (rec != null) rec.ResultReady += OnReady;
                var b = BackendServices.Instance;
                if (b != null && b.Api != null) _ = CourseCatalogStore.RefreshAsync(b.Api, destroyCancellationToken);
            }

            void OnReady() => OnSessionScored(hooked);
        }
    }

    /// <summary>What an Explore lesson asked the equipment lab to lay out (read by the lab when the scene loads).</summary>
    public static class EquipmentLabRequest
    {
        public static List<string> Equipment;
    }
}

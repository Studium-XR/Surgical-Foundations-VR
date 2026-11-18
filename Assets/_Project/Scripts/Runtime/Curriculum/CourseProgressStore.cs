using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurgicalFoundations.Backend;
using SurgicalFoundations.Contracts;
using UnityEngine;

namespace SurgicalFoundations.Curriculum
{
    /// <summary>
    /// The learner's enrolments and lesson results, kept on the headset per user (guests included) so progress works
    /// offline, and mirrored to the API through the sync queue. The API is the authority for certificates; the headset
    /// shows a course as complete as soon as every lesson is done.
    /// </summary>
    public static class CourseProgressStore
    {
        static EnrollmentList data;
        static string loadedFor;

        public static event Action Changed;

        static string UserKey
        {
            get
            {
                // A guest keeps their own progress even when an account is saved on the headset.
                var auth = BackendServices.Instance != null ? BackendServices.Instance.Auth : null;
                return auth != null && !auth.IsGuest && !string.IsNullOrEmpty(auth.UserId) ? auth.UserId : "guest";
            }
        }

        static string PathFor(string user) => Path.Combine(Application.persistentDataPath, "curriculum", "progress_" + Sanitize(user) + ".json");

        static string Sanitize(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_').ToArray());

        static EnrollmentList Data
        {
            get
            {
                var user = UserKey;
                if (data == null || loadedFor != user)
                {
                    loadedFor = user;
                    data = null;
                    try { if (File.Exists(PathFor(user))) data = ApiJson.Deserialize<EnrollmentList>(File.ReadAllText(PathFor(user))); }
                    catch (Exception e) { Debug.LogWarning("[Curriculum] Progress file unreadable, starting fresh: " + e.Message); }
                    data ??= new EnrollmentList();
                }
                return data;
            }
        }

        public static IReadOnlyList<Enrollment> Enrollments => Data.enrollments;

        public static Enrollment Get(string courseId) => Data.enrollments.FirstOrDefault(e => e.courseId == courseId);

        public static bool IsLessonDone(string courseId, string lessonId) =>
            Get(courseId)?.lessons.Any(l => l.lessonId == lessonId && l.completed) ?? false;

        public static Enrollment Enroll(string courseId)
        {
            var e = Get(courseId);
            if (e != null) return e;
            e = new Enrollment { courseId = courseId, status = EnrollmentStatus.NotStarted, enrolledAtUnixMs = Now };
            Data.enrollments.Add(e);
            Save();
            Queue(SyncQueue.Kinds.Enroll, courseId, null);
            return e;
        }

        /// <summary>Records one finished attempt at a lesson. Returns the updated enrolment.</summary>
        public static Enrollment RecordAttempt(string courseId, string lessonId, float score, bool completed, string sessionId = null)
        {
            var course = CourseCatalogStore.Find(courseId);
            var e = Enroll(courseId);
            var lp = e.lessons.FirstOrDefault(l => l.lessonId == lessonId);
            if (lp == null) { lp = new LessonProgress { courseId = courseId, lessonId = lessonId }; e.lessons.Add(lp); }
            lp.attempts++;
            lp.bestScore = Mathf.Max(lp.bestScore, score);
            if (completed && !lp.completed) { lp.completed = true; lp.completedAtUnixMs = Now; }

            if (course != null)
            {
                int total = CourseCatalogStore.Lessons(course).Count();
                int done = e.lessons.Count(l => l.completed);
                e.progress = total > 0 ? Mathf.Clamp01((float)done / total) : 0f;
                e.status = done >= total && total > 0 ? EnrollmentStatus.Completed : EnrollmentStatus.InProgress;
                if (e.status == EnrollmentStatus.Completed && e.completedAtUnixMs == 0) e.completedAtUnixMs = Now;
            }
            Save();
            Queue(SyncQueue.Kinds.LessonAttempt, courseId, new LessonAttemptRequest
            {
                courseId = courseId, lessonId = lessonId, score = score, completed = completed, sessionId = sessionId, finishedAtUnixMs = Now,
            });
            return e;
        }

        /// <summary>The lesson to continue with: the first one not yet completed, in course order.</summary>
        public static Lesson NextLesson(Course course) =>
            CourseCatalogStore.Lessons(course).FirstOrDefault(l => !IsLessonDone(course.id, l.id));

        /// <summary>Courses whose prerequisites are all completed (or that have none).</summary>
        public static bool PrerequisitesMet(Course course) =>
            course.prerequisites.All(p => Get(p)?.status == EnrollmentStatus.Completed);

        static void Queue(string kind, string courseId, object body)
        {
            var b = BackendServices.Instance;
            if (b == null || b.Queue == null || b.Auth == null || b.Auth.IsGuest || string.IsNullOrEmpty(b.Auth.UserId)) return; // guests stay local
            b.Queue.Enqueue(kind, b.Auth.UserId, courseId, body);
        }

        static void Save()
        {
            try
            {
                var path = PathFor(loadedFor ?? UserKey);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, ApiJson.Serialize(Data));
            }
            catch (IOException e) { Debug.LogWarning("[Curriculum] Could not save progress: " + e.Message); }
            Changed?.Invoke();
        }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SurgicalFoundations.Backend;
using SurgicalFoundations.Contracts;
using UnityEngine;

namespace SurgicalFoundations.Curriculum
{
    /// <summary>
    /// The Studium XR course catalogue. Ships inside the app (Resources/Curriculum/catalog.json) so every course is
    /// browsable offline; when the API has a newer version it is downloaded and cached, and used from then on.
    /// </summary>
    public static class CourseCatalogStore
    {
        static CourseCatalog catalog;
        static Dictionary<string, Course> byId;

        public static event Action Changed;

        static string CachePath => Path.Combine(Application.persistentDataPath, "curriculum", "catalog.json");

        public static CourseCatalog Catalog
        {
            get
            {
                if (catalog == null) Load();
                return catalog;
            }
        }

        public static IReadOnlyList<Course> Courses => Catalog.courses;
        public static IReadOnlyList<LearningProgram> Programs => Catalog.programs;

        public static Course Find(string courseId)
        {
            if (catalog == null) Load();
            return courseId != null && byId.TryGetValue(courseId, out var c) ? c : null;
        }

        public static Lesson FindLesson(string courseId, string lessonId) =>
            Find(courseId)?.modules.SelectMany(m => m.lessons).FirstOrDefault(l => l.id == lessonId);

        public static IEnumerable<Lesson> Lessons(Course c) => c.modules.SelectMany(m => m.lessons);

        static void Load()
        {
            var bundled = Parse(Resources.Load<TextAsset>("Curriculum/catalog")?.text);
            CourseCatalog cached = null;
            try { if (File.Exists(CachePath)) cached = Parse(File.ReadAllText(CachePath)); }
            catch (Exception e) { Debug.LogWarning("[Curriculum] Ignoring unreadable cached catalogue: " + e.Message); }
            Use(cached != null && (bundled == null || cached.version > bundled.version) ? cached : bundled ?? new CourseCatalog());
        }

        static CourseCatalog Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return ApiJson.Deserialize<CourseCatalog>(json); }
            catch (Exception e) { Debug.LogError("[Curriculum] Catalogue JSON is invalid: " + e.Message); return null; }
        }

        static void Use(CourseCatalog c)
        {
            catalog = c;
            byId = c.courses.Where(x => !string.IsNullOrEmpty(x.id)).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First());
            Changed?.Invoke();
        }

        /// <summary>Fetches the API's catalogue and switches to it when it is newer. Silent when offline.</summary>
        public static async Task RefreshAsync(ApiClient api, CancellationToken ct)
        {
            if (api == null) return;
            var r = await api.GetAsync(ApiRoutes.Catalog, null, ct);
            if (!r.Ok) return;
            var remote = Parse(r.Body);
            if (remote == null || remote.version <= Catalog.version) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, r.Body);
            }
            catch (IOException e) { Debug.LogWarning("[Curriculum] Could not cache catalogue: " + e.Message); }
            Use(remote);
        }
    }
}

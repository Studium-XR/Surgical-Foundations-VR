using System;
using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Contracts;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// The Studium XR course catalogue in the lobby: programs down the left, the program's courses in the middle, and the
    /// chosen course on the right with its lessons, progress and a Continue button. Readings and quizzes open in the
    /// panel itself; VR labs, assessments and equipment exploration load their scene. The panel stands in the room to the
    /// learner's right (world-locked, never following the head). Built in code and installed by itself in the lobby.
    /// </summary>
    public class CourseBrowser : MonoBehaviour
    {
        const float W = 1500f, H = 940f, Px = 0.001f;
        const int CoursesPerPage = 7, LessonsPerPage = 8;

        static readonly Color Panel = Hex("17181C", 0.97f), Card = Hex("22242A"), CardOn = Hex("2C3F3B"), Text = Hex("D6D9DE"),
            Muted = Hex("8A9097"), Mint = Hex("5CE0C8"), Amber = Hex("F5B54B"), Red = Hex("F0706A"), Dark = Hex("121316");

        RectTransform root, body;
        LearningProgram program;
        Course course;
        Lesson lesson;
        int coursePage, lessonPage, question, correct;
        bool answered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded += (s, m) => { if (s.name == SceneIds.Lobby) Spawn(); };
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).name == SceneIds.Lobby) Spawn();
        }

        static void Spawn()
        {
            if (FindAnyObjectByType<CourseBrowser>() != null) return;
            var go = new GameObject("CourseBrowser");
            var lobby = SceneManager.GetSceneByName(SceneIds.Lobby);
            if (lobby.IsValid()) SceneManager.MoveGameObjectToScene(go, lobby); // goes away with the lobby
            go.AddComponent<CourseBrowser>();
        }

        void Awake()
        {
            var go = new GameObject("CourseBrowser_Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            root = (RectTransform)go.transform;
            root.sizeDelta = new Vector2(W, H);
            root.localScale = Vector3.one * Px;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            // To the learner's right, turned to face them; placed when the lobby loads and on recenter only. The lobby's
            // own panels stand on an arc about 1.6 m out: Recent attempts at -41°, Main ahead, Settings at +41° (to about
            // +54° at its edge). This 1.5 m wide panel goes next along that arc, at +84°, so its edge (about 24° either
            // side) clears Settings instead of cutting through it.
            const float Radius = 1.65f, Angle = 84f;
            go.AddComponent<WorldLockedPanel>().offset = new Vector3(
                Radius * Mathf.Sin(Angle * Mathf.Deg2Rad), -0.05f, Radius * Mathf.Cos(Angle * Mathf.Deg2Rad));

            var bg = Img(root, UISprites.Rounded, Panel, Vector2.zero, new Vector2(W, H));
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f; // 24 px corners: restrained, not bubbly
            Label(root, "Studium XR · Courses", 38, FontStyles.Bold, Text, new Vector2(-W / 2 + 40, H / 2 - 52), new Vector2(700, 50));
            body = New("Body", root, Vector2.zero, new Vector2(W, H));

            program = CourseCatalogStore.Programs.FirstOrDefault();
            // Reopen where the learner left off.
            if (CourseSession.Course != null) { course = CourseSession.Course; program = CourseCatalogStore.Programs.FirstOrDefault(p => p.courseIds.Contains(course.id)) ?? program; }
            CourseCatalogStore.Changed += Draw;
            CourseProgressStore.Changed += Draw;
            Draw();
        }

        void OnDestroy()
        {
            CourseCatalogStore.Changed -= Draw;
            CourseProgressStore.Changed -= Draw;
        }

        // ─────────────────────────────── drawing ───────────────────────────────

        void Draw()
        {
            if (body == null) return;
            foreach (Transform c in body) Destroy(c.gameObject);
            if (lesson != null && lesson.kind == LessonKind.Quiz) { DrawQuiz(); return; }
            if (lesson != null && (lesson.kind == LessonKind.Reading || lesson.kind == LessonKind.Video)) { DrawReading(); return; }
            DrawPrograms();
            DrawCourses();
            DrawCourse();
        }

        void DrawPrograms()
        {
            float y = H / 2 - 130;
            Label(body, "PROGRAMS", 18, FontStyles.Bold, Muted, new Vector2(-W / 2 + 40, y), new Vector2(300, 26));
            y -= 50;
            foreach (var p in CourseCatalogStore.Programs)
            {
                var pp = p;
                int done = p.courseIds.Count(id => CourseProgressStore.Get(id)?.status == EnrollmentStatus.Completed);
                var b = Button(body, new Vector2(-W / 2 + 190, y), new Vector2(300, 78), p == program ? CardOn : Card, () => { program = pp; course = null; coursePage = 0; Draw(); });
                Label(b, p.title, 22, FontStyles.Bold, Text, new Vector2(-134, 12), new Vector2(270, 30));
                Label(b, $"{p.courseIds.Count} courses · {done} completed", 17, FontStyles.Normal, Muted, new Vector2(-134, -16), new Vector2(270, 24));
                y -= 88;
            }
        }

        void DrawCourses()
        {
            if (program == null) return;
            float x = -W / 2 + 560, y = H / 2 - 130;
            Label(body, program.title.ToUpperInvariant(), 18, FontStyles.Bold, Muted, new Vector2(x - 190, y), new Vector2(380, 26));
            var list = program.courseIds.Select(CourseCatalogStore.Find).Where(c => c != null).ToList();
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)CoursesPerPage));
            coursePage = Mathf.Clamp(coursePage, 0, pages - 1);
            y -= 55;
            foreach (var c in list.Skip(coursePage * CoursesPerPage).Take(CoursesPerPage))
            {
                var cc = c;
                var e = CourseProgressStore.Get(c.id);
                bool ready = CourseProgressStore.PrerequisitesMet(c);
                var b = Button(body, new Vector2(x, y), new Vector2(390, 92), c == course ? CardOn : Card, () => { course = cc; lessonPage = 0; Draw(); });
                Label(b, $"{c.code} · {c.level}", 15, FontStyles.Bold, ready ? Mint : Amber, new Vector2(-180, 28), new Vector2(360, 22));
                Label(b, c.title, 21, FontStyles.Bold, Text, new Vector2(-180, 2), new Vector2(360, 30));
                string state = e == null ? $"{c.hours} h · {c.cmeCredits:0.#} CME" + (ready ? "" : " · see recommended") :
                    e.status == EnrollmentStatus.Completed ? "Completed" : $"{Mathf.RoundToInt(e.progress * 100)}% complete";
                Label(b, state, 16, FontStyles.Normal, Muted, new Vector2(-180, -26), new Vector2(360, 22));
                if (e != null) Bar(b, new Vector2(0, -42), 360, e.progress);
                y -= 102;
            }
            if (pages > 1) Pager(new Vector2(x, -H / 2 + 50), coursePage, pages, d => { coursePage += d; Draw(); });
        }

        void DrawCourse()
        {
            float x0 = -W / 2 + 790; // left edge of the detail column
            float y = H / 2 - 130;
            if (course == null)
            {
                Label(body, "Choose a course to see its lessons.", 24, FontStyles.Normal, Muted, new Vector2(x0, y - 60), new Vector2(640, 40));
                return;
            }
            Label(body, course.title, 30, FontStyles.Bold, Text, new Vector2(x0, y), new Vector2(660, 40));
            var sub = Label(body, course.subtitle, 19, FontStyles.Normal, Muted, new Vector2(x0, y - 44), new Vector2(660, 52));
            sub.textWrappingMode = TextWrappingModes.Normal;
            sub.alignment = TextAlignmentOptions.TopLeft;
            Label(body, string.Join("  ·  ", course.frameworks.Take(2)), 15, FontStyles.Italic, Muted, new Vector2(x0, y - 92), new Vector2(660, 22));

            var lessons = CourseCatalogStore.Lessons(course).ToList();
            int pages = Mathf.Max(1, Mathf.CeilToInt(lessons.Count / (float)LessonsPerPage));
            lessonPage = Mathf.Clamp(lessonPage, 0, pages - 1);
            float ly = y - 140;
            foreach (var l in lessons.Skip(lessonPage * LessonsPerPage).Take(LessonsPerPage))
            {
                var ll = l;
                bool done = CourseProgressStore.IsLessonDone(course.id, l.id);
                bool available = CourseSession.IsAvailable(l);
                var b = Button(body, new Vector2(x0 + 330, ly), new Vector2(660, 62), Card, () => Open(ll));
                Label(b, done ? "OK" : KindGlyph(l.kind), 20, FontStyles.Bold, done ? Mint : Muted, new Vector2(-314, 0), new Vector2(40, 30));
                Label(b, l.title, 20, FontStyles.Bold, available ? Text : Muted, new Vector2(-270, 10), new Vector2(430, 28));
                Label(b, $"{KindName(l.kind)} · {l.minutes} min" + (available ? "" : " · in development"), 15, FontStyles.Normal, available ? Muted : Amber, new Vector2(-270, -14), new Vector2(430, 22));
                ly -= 70;
            }
            if (pages > 1) Pager(new Vector2(x0 + 330, -H / 2 + 50), lessonPage, pages, d => { lessonPage += d; Draw(); });

            // Continue, plus the courses recommended first. Prerequisites advise, they do not lock: every course can be
            // started, as on Coursera, and the certificate rules (all lessons complete) are unchanged.
            var missing = course.prerequisites.Where(p => CourseProgressStore.Get(p)?.status != EnrollmentStatus.Completed).ToList();
            if (missing.Count > 0)
            {
                var need = string.Join(", ", missing.Select(p => CourseCatalogStore.Find(p)?.code ?? p));
                var rec = Label(body, "Recommended first: " + need, 16, FontStyles.Bold, Amber, new Vector2(x0 + 320, -H / 2 + 110), new Vector2(340, 52));
                rec.textWrappingMode = TextWrappingModes.Normal;
            }
            {
                var next = lessons.FirstOrDefault(l => !CourseProgressStore.IsLessonDone(course.id, l.id) && CourseSession.IsAvailable(l));
                if (next != null)
                {
                    var go = Button(body, new Vector2(x0 + 150, -H / 2 + 110), new Vector2(300, 60), Mint, () => Open(next));
                    Label(go, CourseProgressStore.Get(course.id) == null ? "Enrol and start" : "Continue", 22, FontStyles.Bold, Dark, new Vector2(-130, 0), new Vector2(260, 30)).alignment = TextAlignmentOptions.Center;
                }
                else if (CourseProgressStore.Get(course.id)?.status == EnrollmentStatus.Completed)
                    Label(body, $"Course completed · {course.cmeCredits:0.#} CME credits. Certificate issued when synced.", 18, FontStyles.Bold, Mint, new Vector2(x0, -H / 2 + 110), new Vector2(660, 26));
            }
        }

        void Open(Lesson l)
        {
            if (course == null) return;
            if (!CourseSession.IsAvailable(l)) { AudioManager.Instance?.Play(SoundId.UI_Denied); return; }
            lesson = l;
            question = 0; correct = 0; answered = false;
            CourseSession.Begin(course, l); // VR lessons load their scene from here
            if (l.kind == LessonKind.Reading || l.kind == LessonKind.Quiz || l.kind == LessonKind.Video) Draw();
            else lesson = null;
        }

        void DrawReading()
        {
            Label(body, $"{course.code} · {KindName(lesson.kind)} · {lesson.minutes} min", 18, FontStyles.Bold, Mint, new Vector2(-W / 2 + 40, H / 2 - 130), new Vector2(900, 26));
            Label(body, lesson.title, 34, FontStyles.Bold, Text, new Vector2(-W / 2 + 40, H / 2 - 180), new Vector2(1300, 44));
            var t = Label(body, lesson.content ?? lesson.summary, 26, FontStyles.Normal, Text, new Vector2(-W / 2 + 40, 20), new Vector2(W - 80, 520));
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.enableAutoSizing = true; t.fontSizeMin = 18; t.fontSizeMax = 28;
            t.lineSpacing = 12;
            var back = Button(body, new Vector2(-W / 2 + 160, -H / 2 + 70), new Vector2(240, 60), Card, () => { lesson = null; Draw(); });
            Label(back, "Back", 22, FontStyles.Bold, Text, new Vector2(-100, 0), new Vector2(200, 30)).alignment = TextAlignmentOptions.Center;
            var done = Button(body, new Vector2(W / 2 - 200, -H / 2 + 70), new Vector2(320, 60), Mint, () =>
            {
                CourseSession.Complete(100f, true);
                AudioManager.Instance?.Play(SoundId.UI_Confirm);
                lesson = null; Draw();
            });
            Label(done, "Mark as read", 22, FontStyles.Bold, Dark, new Vector2(-140, 0), new Vector2(280, 30)).alignment = TextAlignmentOptions.Center;
        }

        void DrawQuiz()
        {
            var qs = lesson.questions;
            Label(body, $"{course.code} · {lesson.title}", 18, FontStyles.Bold, Mint, new Vector2(-W / 2 + 40, H / 2 - 130), new Vector2(900, 26));
            if (question >= qs.Count)
            {
                float score = qs.Count > 0 ? 100f * correct / qs.Count : 100f;
                bool pass = score >= lesson.passScore;
                Label(body, pass ? "Passed" : "Not yet", 44, FontStyles.Bold, pass ? Mint : Amber, new Vector2(-W / 2 + 40, 120), new Vector2(900, 60));
                Label(body, $"{correct} of {qs.Count} correct · {score:0}% (pass mark {lesson.passScore:0}%)", 26, FontStyles.Normal, Text, new Vector2(-W / 2 + 40, 50), new Vector2(1200, 40));
                var again = Button(body, new Vector2(-W / 2 + 180, -H / 2 + 70), new Vector2(280, 60), Card, () => { question = 0; correct = 0; answered = false; Draw(); });
                Label(again, "Try again", 22, FontStyles.Bold, Text, new Vector2(-120, 0), new Vector2(240, 30)).alignment = TextAlignmentOptions.Center;
                var fin = Button(body, new Vector2(W / 2 - 200, -H / 2 + 70), new Vector2(320, 60), Mint, () => { lesson = null; Draw(); });
                Label(fin, "Back to course", 22, FontStyles.Bold, Dark, new Vector2(-140, 0), new Vector2(280, 30)).alignment = TextAlignmentOptions.Center;
                return;
            }
            var q = qs[question];
            Label(body, $"Question {question + 1} of {qs.Count}", 20, FontStyles.Normal, Muted, new Vector2(-W / 2 + 40, H / 2 - 175), new Vector2(600, 28));
            var prompt = Label(body, q.prompt, 30, FontStyles.Bold, Text, new Vector2(-W / 2 + 40, H / 2 - 260), new Vector2(W - 80, 110));
            prompt.textWrappingMode = TextWrappingModes.Normal;
            prompt.alignment = TextAlignmentOptions.TopLeft;

            // Options in a stable shuffled order (same order each time this question is shown).
            var opts = q.options.OrderBy(o => (o.text.GetHashCode() ^ (q.id ?? "").GetHashCode()) & 0x7fffffff).ToList();
            float y = 70;
            foreach (var o in opts)
            {
                var oo = o;
                var b = Button(body, new Vector2(0, y), new Vector2(W - 80, 84), Card, () => Answer(oo));
                var t = Label(b, o.text, 24, FontStyles.Normal, Text, new Vector2(-(W - 80) / 2 + 30, 0), new Vector2(W - 140, 70));
                t.textWrappingMode = TextWrappingModes.Normal;
                if (answered) { b.GetComponent<Button>().interactable = false; if (o.correct) b.GetComponent<Image>().color = CardOn; }
                y -= 96;
            }
            if (answered)
            {
                var fb = Label(body, feedback, 22, FontStyles.Italic, lastRight ? Mint : Red, new Vector2(-W / 2 + 40, y - 20), new Vector2(W - 400, 80));
                fb.textWrappingMode = TextWrappingModes.Normal;
                fb.alignment = TextAlignmentOptions.TopLeft;
                var next = Button(body, new Vector2(W / 2 - 200, -H / 2 + 70), new Vector2(320, 60), Mint, () =>
                {
                    question++; answered = false;
                    if (question >= qs.Count)
                    {
                        float score = qs.Count > 0 ? 100f * correct / qs.Count : 100f;
                        CourseSession.Complete(score, score >= lesson.passScore);
                    }
                    Draw();
                });
                Label(next, question + 1 >= qs.Count ? "Finish" : "Next", 22, FontStyles.Bold, Dark, new Vector2(-140, 0), new Vector2(280, 30)).alignment = TextAlignmentOptions.Center;
            }
        }

        string feedback;
        bool lastRight;

        // ─────────────────────────────── preview ───────────────────────────────
        // Used by the course gallery capture tool: shows a page exactly as a learner sees it, without launching
        // anything or recording progress.

        /// <summary>Shows a course's page (its program selected, the right page of the course list).</summary>
        public void ShowCourse(Course c)
        {
            if (c == null) return;
            program = CourseCatalogStore.Programs.FirstOrDefault(p => p.courseIds.Contains(c.id) && p.id != "perioperative-practice")
                      ?? CourseCatalogStore.Programs.FirstOrDefault(p => p.courseIds.Contains(c.id)) ?? program;
            coursePage = program != null ? Mathf.Max(0, program.courseIds.IndexOf(c.id)) / CoursesPerPage : 0;
            course = c; lesson = null; lessonPage = 0;
            Draw();
        }

        /// <summary>Shows a reading, video or quiz; for a quiz, question <paramref name="index"/>, optionally answered correctly.</summary>
        public void ShowLesson(Course c, Lesson l, int index = 0, bool answered = false)
        {
            ShowCourse(c);
            lesson = l; question = index; correct = 0; this.answered = false;
            if (answered && l.kind == LessonKind.Quiz && index < l.questions.Count)
            {
                var right = l.questions[index].options.FirstOrDefault(o => o.correct);
                this.answered = true; lastRight = true;
                feedback = "Correct. " + (right?.feedback ?? "");
            }
            Draw();
        }

        /// <summary>The panel's canvas, so a capture camera can frame it.</summary>
        public RectTransform PanelRect => root;

        void Answer(QuizOption o)
        {
            if (answered) return;
            answered = true;
            lastRight = o.correct;
            if (o.correct) correct++;
            feedback = (o.correct ? "Correct. " : "") + (o.feedback ?? "");
            AudioManager.Instance?.Play(o.correct ? SoundId.UI_Confirm : SoundId.UI_Denied);
            Draw();
        }

        static string KindName(LessonKind k) => k switch
        {
            LessonKind.Reading => "Reading", LessonKind.Video => "Video", LessonKind.Quiz => "Quiz", LessonKind.VrLab => "VR lab",
            LessonKind.VrAssessment => "VR assessment", _ => "Explore in 3D",
        };

        static string KindGlyph(LessonKind k) => k switch
        {
            LessonKind.Reading => "R", LessonKind.Video => "V", LessonKind.Quiz => "?", LessonKind.VrLab => "L", LessonKind.VrAssessment => "A", _ => "3D",
        };

        // ─────────────────────────────── helpers ───────────────────────────────

        void Pager(Vector2 pos, int page, int pages, Action<int> step)
        {
            if (page > 0) { var p = Button(body, pos + new Vector2(-90, 0), new Vector2(70, 46), Card, () => step(-1)); Label(p, "<", 22, FontStyles.Bold, Text, new Vector2(-25, 0), new Vector2(50, 30)).alignment = TextAlignmentOptions.Center; }
            Label(body, $"{page + 1}/{pages}", 18, FontStyles.Normal, Muted, pos + new Vector2(-30, 0), new Vector2(60, 26)).alignment = TextAlignmentOptions.Center;
            if (page < pages - 1) { var n = Button(body, pos + new Vector2(90, 0), new Vector2(70, 46), Card, () => step(1)); Label(n, ">", 22, FontStyles.Bold, Text, new Vector2(-25, 0), new Vector2(50, 30)).alignment = TextAlignmentOptions.Center; }
        }

        static void Bar(RectTransform parent, Vector2 pos, float width, float value)
        {
            Img(parent, null, Hex("3A3D44"), pos, new Vector2(width, 4)).raycastTarget = false;
            var fill = Img(parent, null, Mint, pos - new Vector2(width * (1 - value) / 2, 0), new Vector2(width * Mathf.Clamp01(value), 4));
            fill.raycastTarget = false;
        }

        static RectTransform New(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Img", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static RectTransform Button(RectTransform parent, Vector2 pos, Vector2 size, Color color, Action onClick)
        {
            var img = Img(parent, UISprites.Rounded, color, pos, size);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2f; // 12 px corners on cards and buttons
            var b = img.gameObject.AddComponent<Button>();
            var c = b.colors;
            c.highlightedColor = new Color(0.85f, 0.97f, 0.94f); c.pressedColor = new Color(0.75f, 0.9f, 0.86f); c.fadeDuration = 0.06f;
            b.colors = c;
            b.onClick.AddListener(() => { AudioManager.Instance?.Play(SoundId.UI_Click); onClick(); });
            return img.rectTransform;
        }

        /// <summary>Left-aligned label whose left edge is at <paramref name="pos"/>.</summary>
        static TextMeshProUGUI Label(RectTransform parent, string text, float size, FontStyles style, Color color, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Left;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            // Long course names shrink to fit (down to 70 %) before they are cut short.
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.7f;
            t.raycastTarget = false;
            return t;
        }

        static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            c.a = alpha;
            return c;
        }
    }
}

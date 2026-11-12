using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Core;
using SurgicalFoundations.Curriculum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// The equipment showroom for "Explore in 3D" lessons, in the skills lab: one device at a time on a clear display
    /// spot, never a floor full of equipment. The learner is placed facing the spot; a panel beside it names the device,
    /// says which of the lesson's devices it is, and has Previous / Next, Turn (a quarter turn, to see the sides) and
    /// Operate (every moving part once). Drawers, doors, knobs and buttons can also be worked by hand (ArticulatedDevice).
    /// Small items stand on a waist-high plinth so they are at hand height. Installs itself and acts only when a lesson
    /// has set <see cref="EquipmentLabRequest.Equipment"/>.
    /// </summary>
    public class EquipmentLab : MonoBehaviour
    {
        const float SmallItem = 0.45f;   // metres: anything smaller sits on the plinth
        const float PlinthHeight = 0.9f;

        // Skills lab (walls at ±3.1 m, training table ahead of the normal start spot): the open floor behind the start
        // spot is the showroom. The learner stands at LearnerSpot facing −Z; devices stand on DisplaySpot.
        static readonly Vector3 LearnerSpot = new Vector3(0.6f, 0f, -0.45f);
        static readonly Vector3 DisplaySpot = new Vector3(0.6f, 0f, -1.75f);
        static readonly Vector3 PanelSpot = new Vector3(-0.45f, 1.42f, -1.25f);

        readonly List<SimReadyLibrary.Entry> exhibits = new List<SimReadyLibrary.Entry>();
        readonly List<GameObject> spawned = new List<GameObject>();
        GameObject current, plinth;
        int index;
        TextMeshProUGUI nameText, infoText, countText;
        RectTransform listArea;
        SceneLoader subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var go = new GameObject("EquipmentLab");
            DontDestroyOnLoad(go);
            go.AddComponent<EquipmentLab>();
        }

        void Update()
        {
            // SceneLoader lives in 00_Bootstrap; it reports every finished transition, including one that found the
            // skills lab already loaded (an Explore lesson started from inside the skills lab).
            var loader = SceneLoader.Instance;
            if (loader == subscribed) return;
            if (subscribed != null) subscribed.SceneReady -= OnReady;
            subscribed = loader;
            if (loader != null) loader.SceneReady += OnReady;
        }

        void OnDestroy()
        {
            if (subscribed != null) subscribed.SceneReady -= OnReady;
        }

        void OnReady(string sceneName)
        {
            if (sceneName != SceneIds.SkillsLab) { Clear(); return; }
            var want = EquipmentLabRequest.Equipment;
            if (want == null || want.Count == 0) return;
            EquipmentLabRequest.Equipment = null;
            Open(want);
        }

        void Clear()
        {
            foreach (var g in spawned) if (g != null) Destroy(g);
            spawned.Clear();
            exhibits.Clear();
            current = plinth = null;
        }

        /// <summary>Opens the showroom with the given devices (SimReady ids), showing the first.</summary>
        public void Open(List<string> ids)
        {
            Clear();
            var lib = SimReadyLibrary.Instance;
            if (lib == null) { Debug.LogWarning("[EquipmentLab] No SimReady library."); return; }
            foreach (var id in ids) { var e = lib.Find(id); if (e != null) exhibits.Add(e); }
            if (exhibits.Count == 0) { Debug.LogWarning("[EquipmentLab] None of the lesson's equipment is installed: " + string.Join(", ", ids)); return; }

            // Stand the learner facing the display spot.
            var stand = new GameObject("Showroom_LearnerSpot").transform;
            stand.SetPositionAndRotation(LearnerSpot, Quaternion.LookRotation(DisplaySpot - LearnerSpot, Vector3.up));
            spawned.Add(stand.gameObject);
            SceneLoader.Instance?.PlaceRigAt(stand);

            BuildPlinth();
            BuildPanel();
            Show(0);
        }

        // ─────────────────────────────── exhibits ───────────────────────────────

        void Show(int i)
        {
            index = (i % exhibits.Count + exhibits.Count) % exhibits.Count;
            if (current != null) Destroy(current);
            var e = exhibits[index];
            bool small = Mathf.Max(e.size.x, Mathf.Max(e.size.y, e.size.z)) < SmallItem;
            plinth.SetActive(small);

            current = Instantiate(e.prefab);
            current.name = e.prefab.name;
            foreach (var rb in current.GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; rb.useGravity = false; }
            FaceLearner(current);
            var b = Bounds(current);
            float baseY = small ? PlinthHeight : 0f;
            // Its front edge stays at least 0.7 m from the learner, however deep the device is.
            float centreZ = Mathf.Min(DisplaySpot.z, LearnerSpot.z - 0.7f - b.extents.z);
            current.transform.position += new Vector3(DisplaySpot.x - b.center.x, baseY - b.min.y, centreZ - b.center.z);

            var dev = current.GetComponentInChildren<ArticulatedDevice>();
            int moving = dev != null ? dev.Joints.Count : 0;
            nameText.text = e.displayName;
            countText.text = $"{index + 1} of {exhibits.Count}";
            infoText.text = $"{e.category}" + (moving > 0 ? $" · {moving} moving part{(moving == 1 ? "" : "s")}\nGrab a part to move it, or tap it to open and close." : " · no moving parts");
            HighlightList();
            AudioManager.Instance?.Play(SoundId.UI_Click);
        }

        /// <summary>Controls (buttons, knobs, drawers, doors) towards the learner, squared to the room.</summary>
        static void FaceLearner(GameObject go)
        {
            var dir = LearnerSpot - DisplaySpot;
            var dev = go.GetComponentInChildren<ArticulatedDevice>();
            if (dev != null && dev.Joints.Count > 0)
            {
                var controls = dev.Joints.Where(j => j.spec.kind is "press" or "turn" or "slide" or "door" or "lever").Select(j => j.node.position).ToList();
                if (controls.Count > 0)
                {
                    var front = controls.Aggregate(Vector3.zero, (a, c) => a + c) / controls.Count - Bounds(go).center;
                    front.y = 0f;
                    if (front.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.FromToRotation(front.normalized, dir.normalized) * go.transform.rotation;
                }
            }
            go.transform.rotation = Quaternion.Euler(0f, Mathf.Round(go.transform.eulerAngles.y / 90f) * 90f, 0f);
        }

        void Turn()
        {
            if (current == null) return;
            var b = Bounds(current);
            current.transform.RotateAround(new Vector3(b.center.x, 0f, b.center.z), Vector3.up, 90f);
            AudioManager.Instance?.Play(SoundId.UI_Click);
        }

        void Operate()
        {
            var dev = current != null ? current.GetComponentInChildren<ArticulatedDevice>() : null;
            if (dev == null || dev.Joints.Count == 0) { AudioManager.Instance?.Play(SoundId.UI_Denied); return; }
            dev.Showcase();
        }

        void BuildPlinth()
        {
            plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "Showroom_Plinth";
            plinth.transform.position = new Vector3(DisplaySpot.x, PlinthHeight / 2f, DisplaySpot.z);
            plinth.transform.localScale = new Vector3(0.6f, PlinthHeight, 0.45f);
            var r = plinth.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", new Color(0.86f, 0.88f, 0.9f));
                r.sharedMaterial = m;
            }
            spawned.Add(plinth);
        }

        // ─────────────────────────────── panel ───────────────────────────────

        void BuildPanel()
        {
            var go = new GameObject("Showroom_Panel", typeof(RectTransform));
            spawned.Add(go);
            go.transform.position = PanelSpot;
            var look = PanelSpot - LearnerSpot - Vector3.up * 1.4f;
            look.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(620, 760);
            rt.localScale = Vector3.one * 0.001f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var bg = Img(rt, UI.UISprites.Rounded, new Color(0.09f, 0.094f, 0.11f, 0.95f), Vector2.zero, rt.sizeDelta);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 0.3f;

            Text(rt, "Equipment", 20, FontStyles.Bold, new Color(0.36f, 0.88f, 0.78f), new Vector2(0, 340), new Vector2(560, 30));
            nameText = Text(rt, "", 36, FontStyles.Bold, new Color(0.9f, 0.91f, 0.93f), new Vector2(0, 295), new Vector2(560, 48));
            countText = Text(rt, "", 20, FontStyles.Normal, new Color(0.54f, 0.56f, 0.59f), new Vector2(0, 255), new Vector2(560, 28));
            infoText = Text(rt, "", 22, FontStyles.Normal, new Color(0.84f, 0.85f, 0.87f), new Vector2(0, 200), new Vector2(560, 70));
            infoText.textWrappingMode = TextWrappingModes.Normal;

            Button(rt, "‹ Previous", new Vector2(-145, 115), new Vector2(270, 64), () => Show(index - 1));
            Button(rt, "Next ›", new Vector2(145, 115), new Vector2(270, 64), () => Show(index + 1));
            Button(rt, "Turn", new Vector2(-145, 40), new Vector2(270, 64), Turn);
            Button(rt, "Operate", new Vector2(145, 40), new Vector2(270, 64), Operate, primary: true);

            // The lesson's devices, tap one to show it.
            var area = new GameObject("List", typeof(RectTransform));
            listArea = (RectTransform)area.transform;
            listArea.SetParent(rt, false);
            listArea.anchoredPosition = new Vector2(0, -190);
            listArea.sizeDelta = new Vector2(560, 380);
            for (int i = 0; i < exhibits.Count && i < 12; i++)
            {
                int k = i;
                int col = i % 2, row = i / 2;
                Button(listArea, exhibits[i].displayName, new Vector2(col == 0 ? -140 : 140, 160 - row * 58), new Vector2(270, 50), () => Show(k), small: true);
            }
        }

        void HighlightList()
        {
            if (listArea == null) return;
            for (int i = 0; i < listArea.childCount; i++)
            {
                var img = listArea.GetChild(i).GetComponent<Image>();
                if (img != null) img.color = i == index ? new Color(0.19f, 0.27f, 0.25f) : new Color(0.15f, 0.16f, 0.18f);
            }
        }

        // ─────────────────────────────── helpers ───────────────────────────────

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
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

        static TextMeshProUGUI Text(RectTransform parent, string s, float size, FontStyles style, Color color, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = s;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.7f;
            t.raycastTarget = false;
            return t;
        }

        static void Button(RectTransform parent, string label, Vector2 pos, Vector2 size, System.Action onClick, bool primary = false, bool small = false)
        {
            var img = Img(parent, UI.UISprites.Rounded, primary ? new Color(0.36f, 0.88f, 0.78f) : new Color(0.15f, 0.16f, 0.18f), pos, size);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.5f;
            var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            var c = b.colors;
            c.highlightedColor = new Color(0.8f, 0.96f, 0.92f);
            c.pressedColor = new Color(0.65f, 0.88f, 0.82f);
            c.fadeDuration = 0.06f;
            b.colors = c;
            b.onClick.AddListener(() => { AudioManager.Instance?.Play(SoundId.UI_Click); onClick(); });
            Text(img.rectTransform, label, small ? 18 : 24, FontStyles.Bold, primary ? new Color(0.07f, 0.08f, 0.09f) : new Color(0.84f, 0.85f, 0.87f), Vector2.zero, size - new Vector2(24, 6));
        }
    }
}

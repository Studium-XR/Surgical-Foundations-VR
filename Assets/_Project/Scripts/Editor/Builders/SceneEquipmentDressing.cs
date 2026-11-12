using System.Linq;
using SurgicalFoundations.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Puts rigged SimReady equipment into the main scenes, tidily: every device stands with its back to a wall, square to
    /// it, its controls facing into the room, in evenly spaced rows (an equipment bay), so the room reads as organised and
    /// the floor around the table stays clear. Small devices sit on the cart or trolley placed just before them. The
    /// drawers, doors, knobs and buttons work by hand (ArticulatedDevice + Animator), as in the labs.
    /// Placed under one "--- SimReady Equipment ---" root per scene, rebuilt each run; scenes open additively, so the
    /// scene open in the editor is left alone. Procedure labs borrow these devices instead of adding duplicates.
    /// </summary>
    public static class SceneEquipmentDressing
    {
        public const string RootName = "--- SimReady Equipment ---";
        const float WallGap = 0.12f;

        enum Wall { North, South, East, West } // +Z, −Z, +X, −X

        struct Place
        {
            public string id;
            public Wall wall;
            public float along;   // position along the wall (x for North/South, z for East/West)
            public bool onTop;    // on the previous floor item
            public Place(string id, Wall wall, float along, bool onTop = false) { this.id = id; this.wall = wall; this.along = along; this.onTop = onTop; }
        }

        // Operating theatre: walls at ±3.6 m; table at the origin (head −X), learner at (0, 0, −0.85) facing +Z.
        // Equipment bay along the far (north) wall, left of the heart-lung machine; emergency trolley on the east wall.
        // Free bay slots at x = −0.15 and 1.55 are kept for procedure labs.
        static readonly Place[] Theatre =
        {
            new Place("lung_ventilation_artificial_a01", Wall.North, -2.65f),
            new Place("pump_suction_surgical_a01", Wall.North, -1.8f),
            new Place("pump_system_infusion_a01", Wall.North, -1.0f),
            new Place("cart_mobile_b01", Wall.North, 0.75f),
            new Place("unit_electrosurgical_a01", Wall.North, 0.75f, onTop: true),
            new Place("trolley_emergency_a01", Wall.East, -0.9f),
            new Place("defibrillator_a01", Wall.East, -0.9f, onTop: true),
        };

        // Skills lab: walls at ±3.1 m; training table in the middle, learner at (0, 0, −0.2) facing +Z.
        static readonly Place[] SkillsLab =
        {
            new Place("cart_medical_utility_a01", Wall.West, -0.7f),
            new Place("trolley_emergency_a01", Wall.South, -1.45f),
            new Place("wheelChair_c01", Wall.South, -0.4f),
        };

        public static float WallCoord(string sceneName) => sceneName == Core.SceneIds.SkillsLab ? 3.1f : 3.6f;

        [MenuItem(SFPaths.MenuRoot + "Build/1k · Dress Scenes with SimReady Equipment", priority = 5)]
        public static void DressAll()
        {
            Dress($"{SFPaths.Scenes}/Theatre/{Core.SceneIds.OperatingTheatre}.unity", Theatre, 3.6f);
            Dress($"{SFPaths.Scenes}/Frontend/{Core.SceneIds.SkillsLab}.unity", SkillsLab, 3.1f);
        }

        static void Dress(string path, Place[] places, float half)
        {
            var lib = AssetDatabase.LoadAssetAtPath<SimReadyLibrary>(SFPaths.Root + "/Resources/SimReadyLibrary.asset");
            if (lib == null) { Debug.LogWarning("[Dressing] Build the SimReady library first."); return; }
            var already = SceneManager.GetSceneByPath(path);
            bool wasOpen = already.IsValid() && already.isLoaded;
            var scene = wasOpen ? already : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName)) Object.DestroyImmediate(old);
                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                GameObject floorItem = null;
                int placed = 0;
                foreach (var p in places)
                {
                    var e = lib.Find(p.id);
                    if (e == null || e.prefab == null) { Debug.LogWarning($"[Dressing] {p.id} not in the library"); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab, scene);
                    go.transform.SetParent(root.transform, true);
                    var inward = Inward(p.wall);
                    FaceInto(go, inward);
                    if (p.onTop && floorItem != null) PutOnTop(go, floorItem);
                    else
                    {
                        AgainstWall(go, p.wall, p.along, half);
                        floorItem = go;
                    }
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; rb.useGravity = false; }
                    placed++;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[Dressing] {System.IO.Path.GetFileNameWithoutExtension(path)}: {placed} SimReady devices placed against the walls");
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static Vector3 Inward(Wall w) => w switch
        {
            Wall.North => Vector3.back,
            Wall.South => Vector3.forward,
            Wall.East => Vector3.left,
            _ => Vector3.right,
        };

        /// <summary>
        /// Turns the device so its controls (buttons, knobs, drawers, doors) face <paramref name="inward"/>, then squares
        /// it to the nearest 90° so it sits flush with the wall. Devices with no controls keep their modelled heading,
        /// squared.
        /// </summary>
        public static void FaceInto(GameObject go, Vector3 inward)
        {
            var front = ControlsDirection(go);
            if (front.sqrMagnitude > 1e-4f)
                go.transform.rotation = Quaternion.FromToRotation(front, inward) * go.transform.rotation;
            float yaw = Mathf.Round(go.transform.eulerAngles.y / 90f) * 90f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Horizontal direction from the device's centre to the mean of its controls (zero if it has none).</summary>
        public static Vector3 ControlsDirection(GameObject go)
        {
            var dev = go.GetComponent<ArticulatedDevice>();
            if (dev == null) return Vector3.zero;
            if (dev.Joints.Count == 0) dev.ManifestAsset = dev.ManifestAsset; // bind joints in edit mode
            var controls = dev.Joints.Where(j => j.spec.kind is "press" or "turn" or "slide" or "door" or "lever").Select(j => j.node.position).ToList();
            if (controls.Count == 0) return Vector3.zero;
            var front = controls.Aggregate(Vector3.zero, (a, c) => a + c) / controls.Count - Bounds(go).center;
            front.y = 0f;
            return front.sqrMagnitude > 1e-4f ? front.normalized : Vector3.zero;
        }

        static void AgainstWall(GameObject go, Wall wall, float along, float half)
        {
            go.transform.position = Vector3.zero;
            var b = Bounds(go);
            var offset = -new Vector3(b.center.x, 0f, b.center.z); // centre the footprint on the slot
            Vector3 c;
            switch (wall)
            {
                case Wall.North: c = new Vector3(along, 0f, half - WallGap - b.extents.z); break;
                case Wall.South: c = new Vector3(along, 0f, -half + WallGap + b.extents.z); break;
                case Wall.East: c = new Vector3(half - WallGap - b.extents.x, 0f, along); break;
                default: c = new Vector3(-half + WallGap + b.extents.x, 0f, along); break;
            }
            go.transform.position = c + offset + Vector3.up * -b.min.y;
        }

        static void PutOnTop(GameObject go, GameObject under)
        {
            var ub = Bounds(under);
            go.transform.position = Vector3.zero;
            var b = Bounds(go);
            // The top shelf: the highest surface of the cart/trolley body, ignoring tall masts and monitor arms.
            float top = TopSurface(under, ub);
            go.transform.position = new Vector3(ub.center.x - b.center.x, top - b.min.y + 0.002f, ub.center.z - b.center.z);
        }

        static float TopSurface(GameObject under, Bounds ub)
        {
            // The highest renderer whose footprint covers the cart's centre and is wide enough to be a shelf.
            float best = ub.min.y;
            foreach (var r in under.GetComponentsInChildren<Renderer>())
            {
                var rb = r.bounds;
                bool covers = rb.min.x <= ub.center.x && rb.max.x >= ub.center.x && rb.min.z <= ub.center.z && rb.max.z >= ub.center.z;
                bool shelfLike = rb.size.x > ub.size.x * 0.5f && rb.size.z > ub.size.z * 0.5f && rb.size.y < 0.25f;
                if (covers && shelfLike) best = Mathf.Max(best, rb.max.y);
            }
            return best > ub.min.y + 0.05f ? best : ub.max.y;
        }

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}

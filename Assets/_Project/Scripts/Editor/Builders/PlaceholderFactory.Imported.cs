using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Real models from Assets/_Project/Imports placed under a prefab's "Visual" (same contract as the placeholders),
    /// fitted to real-world size, turned to face +Z and, for rigged characters, posed out of the T-pose.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        public enum Fit { Height, Width, None }

        /// <summary>Instantiates <paramref name="modelPath"/> under b.Visual. Returns the model instance or null if missing.</summary>
        static GameObject TryModel(PB b, string modelPath, Fit fit, float size, bool faceForward = false)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(b.Visual, false);
            ImportedAssets.Remap(inst, ImportedAssets.MaterialMap(modelPath));
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) a.enabled = false; // no clips yet; keep the authored pose

            if (faceForward) FaceForward(inst);

            var bounds = WorldBounds(inst);
            float scale = fit switch
            {
                Fit.Height => size / Mathf.Max(bounds.size.y, 1e-4f),
                Fit.Width => size / Mathf.Max(bounds.size.x, 1e-4f),
                _ => 1f
            };
            inst.transform.localScale *= scale;
            bounds = WorldBounds(inst);
            // Centre on X/Z and stand on the floor (y = 0 of the prefab).
            inst.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) + b.Root.transform.position;
            return inst;
        }

        /// <summary>
        /// Keeps only the named meshes of a multi-object FBX (the hospital room holds walls, bed, cabinets… in one file)
        /// under b.Visual, scaled by <paramref name="scale"/>, turned by <paramref name="yaw"/> so the front faces +Z,
        /// centred on X/Z and standing on y = 0. Mesh nodes are matched on "&lt;part&gt;-material". Returns null if missing.
        /// </summary>
        static GameObject TryParts(PB b, string modelPath, float scale, float yaw, params string[] parts)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(b.Visual, false);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                if (!parts.Any(p => r.name == p + "-material")) Object.DestroyImmediate(r.gameObject);
            foreach (var c in inst.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c.gameObject);
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            ImportedAssets.Remap(inst, ImportedAssets.MaterialMap(modelPath));

            inst.transform.localScale *= scale;
            inst.transform.localRotation = Quaternion.Euler(0, yaw, 0) * inst.transform.localRotation;
            var bounds = WorldBounds(inst);
            inst.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) + b.Root.transform.position;
            return inst;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Transform Bone(Transform root, string side, bool fore) =>
            root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.EndsWith(side + (fore ? "ForeArm" : "Arm")));

        /// <summary>Uses the T-pose arm line to find the character's facing and turns it to +Z.</summary>
        static void FaceForward(GameObject inst)
        {
            var l = Bone(inst.transform, "Left", false);
            var r = Bone(inst.transform, "Right", false);
            if (l == null || r == null) return;
            var across = Vector3.ProjectOnPlane(r.position - l.position, Vector3.up);
            if (across.sqrMagnitude < 1e-6f) return;
            var forward = Vector3.Cross(across.normalized, Vector3.up);
            inst.transform.rotation = Quaternion.FromToRotation(forward, Vector3.forward) * inst.transform.rotation;
        }

        static void Aim(Transform bone, Vector3 worldDir)
        {
            if (bone == null || bone.childCount == 0) return;
            var child = bone.GetChild(0);
            var current = (child.position - bone.position).normalized;
            bone.rotation = Quaternion.FromToRotation(current, worldDir.normalized) * bone.rotation;
        }

        /// <summary>Re-poses a T-posed humanoid: upper arms then forearms aimed along world directions (character faces +Z).</summary>
        static void PoseArms(GameObject inst, Vector3 upperL, Vector3 upperR, Vector3 foreL, Vector3 foreR)
        {
            var t = inst.transform;
            Aim(Bone(t, "Left", false), upperL);
            Aim(Bone(t, "Right", false), upperR);
            Aim(Bone(t, "Left", true), foreL);
            Aim(Bone(t, "Right", true), foreR);
        }

        // Character facing +Z: its left side is −X.
        static readonly Vector3[] SterileHands = { new Vector3(-0.22f, -1f, 0.35f), new Vector3(0.22f, -1f, 0.35f), new Vector3(0.4f, 0.35f, 1f), new Vector3(-0.4f, 0.35f, 1f) };
        static readonly Vector3[] RelaxedArms = { new Vector3(-0.16f, -1f, 0.04f), new Vector3(0.16f, -1f, 0.04f), new Vector3(-0.04f, -1f, 0.32f), new Vector3(0.04f, -1f, 0.32f) };

        /// <summary>Character from an imported rig; falls back to false so the caller builds the primitive mannequin.</summary>
        static bool ImportedCharacter(PB b, string model, float height, Vector3[] pose)
        {
            var inst = TryModel(b, model, Fit.Height, height, faceForward: true);
            if (inst == null) return false;
            PoseArms(inst, pose[0], pose[1], pose[2], pose[3]);
            return true;
        }

        // Patient.fbx lies supine along its Z axis, head at −Z; the navel sits 0.10 m head-side of the model origin
        // (just above the modesty towel).
        const float PatientNavelZ = -0.10f;

        /// <summary>
        /// Imported supine patient under b.Visual: head turned to −X, resting on the table top (y = 0) with the navel at
        /// x = −0.02 (SceneBuilder.Umbilicus). Returns the belly-surface height at the navel, or −1 if the model is missing.
        /// </summary>
        static float ImportedPatient(PB b)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedAssets.Patient);
            if (model == null) return -1f;
            ImportedAssets.EnsureReadable(ImportedAssets.Patient);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(b.Visual, false);
            ImportedAssets.Remap(inst, ImportedAssets.MaterialMap(ImportedAssets.Patient));
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            inst.transform.localRotation = Quaternion.Euler(0, 90, 0); // head −Z → −X

            var bounds = WorldBounds(inst);
            var navel = inst.transform.TransformPoint(new Vector3(0, 0, PatientNavelZ));
            inst.transform.position += new Vector3(-0.02f - navel.x, -bounds.min.y, -bounds.center.z) + b.Root.transform.position;

            // Belly height: highest body vertex on the midline within 3 cm of the navel.
            var body = inst.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name == "Body");
            if (body == null || !body.sharedMesh.isReadable) return 0.245f;
            var m = body.transform.localToWorldMatrix;
            var top = body.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v))
                .Where(p => Mathf.Abs(p.x - (-0.02f)) < 0.03f && Mathf.Abs(p.z) < 0.04f)
                .Select(p => p.y).DefaultIfEmpty(0.245f).Max();
            return top - b.Root.transform.position.y;
        }

        /// <summary>
        /// Height of the imported patient's upper surface above the table top, sampled on a <paramref name="cell"/>-metre
        /// grid in the patient prefab's space (x along the body, z across). Null if the model is missing.
        /// </summary>
        static System.Func<float, float, float> PatientHeightField(float cell)
        {
            var tmp = new PB("tmp_PatientSurface");
            try
            {
                if (ImportedPatient(tmp) < 0f) return null;
                var cells = new Dictionary<Vector2Int, float>();
                foreach (var mf in tmp.Visual.GetComponentsInChildren<MeshFilter>())
                {
                    if (!mf.sharedMesh.isReadable) continue;
                    var m = tmp.Root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var p = m.MultiplyPoint3x4(v);
                        var k = new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));
                        if (!cells.TryGetValue(k, out var h) || p.y > h) cells[k] = p.y;
                    }
                }
                return (x, z) => cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(x / cell), Mathf.FloorToInt(z / cell)), out var h) ? h : 0f;
            }
            finally { Object.DestroyImmediate(tmp.Root); }
        }

        /// <summary>
        /// Imported scrub trough under b.Visual, matching the placeholder's contract (origin = floor, centred on X, wall
        /// side at z = −0.3, basin towards +Z). Its three 20k-triangle drain meshes are replaced by flat discs.
        /// Returns the tap outlets (sorted left → right), the basin floor height and the basin's Z.
        /// </summary>
        static bool ImportedSink(PB b, out List<Vector3> spouts, out float basinY, out float basinZ)
        {
            spouts = new List<Vector3>();
            basinY = basinZ = 0f;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedAssets.ScrubSink);
            if (model == null) return false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(b.Visual, false);
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            ImportedAssets.Remap(inst, ImportedAssets.MaterialMap(ImportedAssets.ScrubSink));

            var bounds = WorldBounds(inst);
            inst.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -0.3f - bounds.min.z) + b.Root.transform.position;

            var drains = new List<Vector3>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                if (r.name.StartsWith("Snk_hole"))
                {
                    drains.Add(r.bounds.center - b.Root.transform.position);
                    Object.DestroyImmediate(r.gameObject);
                }
                else if (r.name.StartsWith("Tap"))
                    spouts.Add(Spout(r) - b.Root.transform.position);
            }
            spouts.Sort((p, q) => p.x.CompareTo(q.x));
            if (spouts.Count == 0) { Object.DestroyImmediate(inst); return false; }

            basinY = drains.Count > 0 ? drains.Average(d => d.y) : spouts[0].y - 0.5f;
            basinZ = drains.Count > 0 ? drains.Average(d => d.z) : spouts[0].z;
            foreach (var d in drains)
                b.Cyl("Drain", d + Vector3.up * 0.002f, 0.07f, 0.004f, "Polymer_Dark");
            return true;
        }

        /// <summary>Tap outlet: the tip of the arm furthest from the wall, at its lowest point.</summary>
        static Vector3 Spout(Renderer tap)
        {
            var mf = tap.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
            {
                var m = tap.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                float tip = v.Max(p => p.z);
                var end = v.Where(p => p.z > tip - 0.03f).ToArray();
                return new Vector3(end.Average(p => p.x), end.Min(p => p.y), end.Average(p => p.z));
            }
            var bb = tap.bounds;
            return new Vector3(bb.center.x, bb.min.y, bb.max.z - 0.02f);
        }

        // ───────────── new props from the imports (not in the asset list; background dressing) ─────────────

        static void ImportedProps()
        {
            BuildGloveModels();
            if (ImportedAssets.Exists(ImportedAssets.HeartLung))
            {
                var b = new PB("EQ_HeartLungMachine");
                TryModel(b, ImportedAssets.HeartLung, Fit.None, 0f);
                b.FitColliderFromRenderers();
                b.Info("EQ-X1", "Heart-lung machine", "Equipment", AssetRelease.MVP, 17000, "Imported model (background dressing). Not in the asset list; 17k tris.").isPlaceholder = false;
                b.MakeStatic(false);
                b.Save(Folder("Equipment"));
            }
            if (ImportedAssets.Exists(ImportedAssets.Microscope))
            {
                var b = new PB("EQ_Microscope");
                TryModel(b, ImportedAssets.Microscope, Fit.None, 0f);
                b.FitColliderFromRenderers();
                b.Info("EQ-X2", "Microscope", "Equipment", AssetRelease.MVP, 11600, "Imported model (skills-lab dressing).").isPlaceholder = false;
                b.Save(Folder("Equipment"));
            }
            if (ImportedAssets.Exists(ImportedAssets.SurgeryLamp))
            {
                var b = new PB("EQ_SurgeryLamp");
                TryModel(b, ImportedAssets.SurgeryLamp, Fit.Height, 1.9f);
                b.FitColliderFromRenderers();
                b.Info("EQ-X3", "Surgery lamp", "Equipment", AssetRelease.MVP, 0, "Imported model (floor-standing lamp beside the OR table).").isPlaceholder = false;
                b.Save(Folder("Equipment"));
            }
            if (!SimReadyPatientMonitor() && ImportedAssets.Exists(ImportedAssets.Monitor))
            {
                var b = new PB("EQ_PatientMonitor");
                b.Cyl("Stand_Base", new Vector3(0, 0.02f, 0), 0.5f, 0.04f, "Polymer_Grey");
                b.Cyl("Stand_Pole", new Vector3(0, 0.7f, 0), 0.04f, 1.3f, "Stainless_Satin");
                var head = new GameObject("MonitorHead").transform;
                head.SetParent(b.Visual, false);
                var tmp = new PB("tmp");
                var inst = TryModel(tmp, ImportedAssets.Monitor, Fit.Width, 0.5f);
                if (inst != null)
                {
                    inst.transform.SetParent(head, true);
                    head.localPosition = new Vector3(0, 1.3f, 0);
                }
                Object.DestroyImmediate(tmp.Root);
                var screen = b.Quad("Vitals_Screen", new Vector3(0, 1.3f + WorldBounds(head.gameObject).extents.y, -0.03f), new Vector2(0.44f, 0.27f), "Screen_Vitals");
                screen.gameObject.SetActive(inst != null);
                b.FitColliderFromRenderers();
                b.Info("EQ-X3", "Patient monitor on stand", "Equipment", AssetRelease.MVP, 400, "Imported monitor model + procedural vitals screen (SF/UI/Vitals Monitor).").isPlaceholder = false;
                b.Save(Folder("Equipment"));
            }
            if (ImportedAssets.Exists(ImportedAssets.SurgeryTools))
            {
                var b = new PB("PROP_OpenSurgerySet");
                TryModel(b, ImportedAssets.SurgeryTools, Fit.None, 0f);
                b.FitColliderFromRenderers();
                b.Info("PROP-X1", "Open surgery instrument set", "TrainingProp", AssetRelease.MVP, 164590,
                    "Imported. ⚠ 164k tris — too heavy for the theatre on Quest (400k scene budget). Used as skills-lab display only; decimate before wider use.").isPlaceholder = false;
                b.Save(Folder("TrainingProps"));
            }
            HospitalRoomProps();
        }

        /// <summary>
        /// One glove per hand from the imported pair, for HandAppearance to put on the controllers once gloved:
        /// wrist at the prefab origin, fingers along +Z, palm down, latex material. Saved under Resources so the rig in
        /// 00_Bootstrap can load them without a serialized reference.
        /// </summary>
        [MenuItem(SFPaths.MenuRoot + "Build/1c · Glove Models", priority = 3)]
        public static void BuildGloveModels()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ImportedAssets.Gloves);
            if (model == null) return;
            const string folder = SFPaths.Root + "/Resources/Gloves";
            System.IO.Directory.CreateDirectory(folder);
            var latex = MaterialLibrary.Get("Glove_Latex");

            var tmp = (GameObject)Object.Instantiate(model);
            foreach (var (mesh, name) in new[] { ("glove_left", "Glove_Left"), ("gloves", "Glove_Right") })
            {
                var src = mesh == "gloves" ? tmp.transform : tmp.transform.Find(mesh);
                var srcFilter = src != null ? src.GetComponent<MeshFilter>() : null;
                if (srcFilter == null || srcFilter.sharedMesh == null) { Debug.LogWarning($"[Gloves] No mesh '{mesh}' in {ImportedAssets.Gloves}"); continue; }

                var root = new GameObject(name);
                var visual = new GameObject("Mesh");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = srcFilter.sharedMesh;
                var r = visual.AddComponent<MeshRenderer>();
                r.sharedMaterial = latex;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // Same world orientation and scale as in the file, then the wrist edge (min Z) moved to the origin.
                visual.transform.rotation = src.rotation;
                visual.transform.localScale = src.lossyScale;
                var b = r.bounds;
                visual.transform.position -= new Vector3(b.center.x, b.center.y, b.min.z);

                PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{name}.prefab");
                Object.DestroyImmediate(root);
            }
            Object.DestroyImmediate(tmp);
        }
    }
}

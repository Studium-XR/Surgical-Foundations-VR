using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurgicalFoundations.Interaction;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Turns every rigged SimReady model (Imports/simready_rigged/&lt;Category&gt;/&lt;name&gt;.glb + .joints.json, made by
    /// Tools/SimReady/rig_all.py in Blender) into a prefab under Prefabs/SimReady/&lt;Category&gt;, with its joints driven
    /// by ArticulatedDevice through an Animator Controller (one layer per moving part, posed by a 0-1 parameter, plus a
    /// "Showcase" layer playing the animation authored in Blender), a collider fitted to it, and, for hand-sized items,
    /// a grab interactable and rigidbody. Controllers go to Animation/SimReady/&lt;Category&gt;.
    /// Then writes Resources/SimReadyLibrary.asset, the index the equipment lab and course lessons use.
    /// </summary>
    public static class SimReadyLibraryBuilder
    {
        const string Source = SFPaths.Root + "/Imports/simready_rigged";
        const string PrefabRoot = SFPaths.Prefabs + "/SimReady";
        const string LibraryPath = SFPaths.Root + "/Resources/SimReadyLibrary.asset";
        static readonly HashSet<string> Handheld = new HashSet<string> { "Instruments", "Consumables", "Devices" };

        [MenuItem(SFPaths.MenuRoot + "Build/1i · SimReady Library (rigged)", priority = 4)]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Source)) { Debug.LogWarning("[SimReady] Nothing rigged yet: run Tools/SimReady/rig_all.py first."); return; }
            var lib = AssetDatabase.LoadAssetAtPath<SimReadyLibrary>(LibraryPath);
            if (lib == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                lib = ScriptableObject.CreateInstance<SimReadyLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.entries.Clear();
            int built = 0, failed = 0;
            var guids = AssetDatabase.FindAssets("t:GameObject", new[] { Source });
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!path.EndsWith(".glb")) continue;
                    EditorUtility.DisplayProgressBar("SimReady library", Path.GetFileName(path), (float)i / guids.Length);
                    var e = BuildOne(path);
                    if (e != null) { lib.entries.Add(e); built++; } else failed++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            lib.entries = lib.entries.OrderBy(x => x.category).ThenBy(x => x.displayName).ToList();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SimReady] Library: {built} prefabs, {failed} skipped. {lib.entries.Sum(x => x.joints)} joints in total.");
        }

        const string ControllerRoot = SFPaths.Root + "/Animation/SimReady";

        /// <summary>
        /// Animator Controller for one device. Layer per joint (except free-rolling wheels and swivelling casters): one
        /// state whose motion is a generated 1-second clip sweeping the joint from its minimum to its maximum, with motion
        /// time driven by a float parameter named after the joint. Top layer "Showcase" (weight 0 until played) holds the
        /// Blender animation. Write Defaults is off everywhere, so a layer only touches its own joint.
        /// </summary>
        static AnimatorController BuildController(string id, string category, string glbPath, Transform modelRoot, ArticulatedDevice dev)
        {
            var folder = $"{ControllerRoot}/{category}";
            Directory.CreateDirectory(folder);
            var path = $"{folder}/AC_{id}.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var rest = ctrl.layers[0].stateMachine.AddState("Rest");
            rest.writeDefaultValues = false;

            foreach (var j in dev.Joints)
            {
                if (j.spec.kind == "spin" || j.spec.kind == "swivel") continue;
                var name = j.spec.name;
                var clip = RangeClip(j, AnimationUtility.CalculateTransformPath(j.node, modelRoot));
                clip.name = name + "_range";
                AssetDatabase.AddObjectToAsset(clip, ctrl);

                float span = j.spec.max - j.spec.min;
                ctrl.AddParameter(new AnimatorControllerParameter
                {
                    name = name, type = AnimatorControllerParameterType.Float,
                    defaultFloat = Mathf.Abs(span) < 1e-6f ? 0f : Mathf.Clamp01((j.Rest - j.spec.min) / span),
                });
                var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(sm, ctrl);
                var st = sm.AddState("Pose");
                st.motion = clip;
                st.writeDefaultValues = false;
                st.timeParameterActive = true;
                st.timeParameter = name;
                ctrl.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, stateMachine = sm });
            }

            var showcase = AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Showcase");
            if (showcase != null)
            {
                var sm = new AnimatorStateMachine { name = "Showcase", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(sm, ctrl);
                var off = sm.AddState("Off");
                off.writeDefaultValues = false;
                var play = sm.AddState("Showcase");
                play.motion = showcase;
                play.writeDefaultValues = false;
                var back = play.AddTransition(off);
                back.hasExitTime = true;
                back.exitTime = 1f;
                back.duration = 0.1f;
                sm.defaultState = off;
                ctrl.AddLayer(new AnimatorControllerLayer { name = "Showcase", defaultWeight = 0f, stateMachine = sm });
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>One second, minimum to maximum, linear; rotations keyed every 10 degrees so the sweep stays on its axis.</summary>
        static AnimationClip RangeClip(ArticulatedDevice.Joint j, string path)
        {
            var clip = new AnimationClip { frameRate = 30f };
            float lo = j.spec.min, hi = j.spec.max;
            if (j.Rotary)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(hi - lo) / 10f) + 1);
                var cx = new AnimationCurve(); var cy = new AnimationCurve(); var cz = new AnimationCurve(); var cw = new AnimationCurve();
                for (int k = 0; k < n; k++)
                {
                    float t = k / (n - 1f);
                    var q = j.restRot * Quaternion.AngleAxis(Mathf.Lerp(lo, hi, t), Vector3.up);
                    cx.AddKey(t, q.x); cy.AddKey(t, q.y); cz.AddKey(t, q.z); cw.AddKey(t, q.w);
                }
                foreach (var c in new[] { cx, cy, cz, cw }) Linear(c);
                clip.SetCurve(path, typeof(Transform), "localRotation.x", cx);
                clip.SetCurve(path, typeof(Transform), "localRotation.y", cy);
                clip.SetCurve(path, typeof(Transform), "localRotation.z", cz);
                clip.SetCurve(path, typeof(Transform), "localRotation.w", cw);
                clip.EnsureQuaternionContinuity();
            }
            else
            {
                var a = j.restPos + j.restRot * (Vector3.up * lo);
                var b = j.restPos + j.restRot * (Vector3.up * hi);
                clip.SetCurve(path, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, a.x, 1, b.x));
                clip.SetCurve(path, typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, a.y, 1, b.y));
                clip.SetCurve(path, typeof(Transform), "localPosition.z", AnimationCurve.Linear(0, a.z, 1, b.z));
            }
            return clip;
        }

        static void Linear(AnimationCurve c)
        {
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear);
            }
        }

        static SimReadyLibrary.Entry BuildOne(string glbPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (model == null) return null;
            var id = Path.GetFileNameWithoutExtension(glbPath);
            var category = Path.GetFileName(Path.GetDirectoryName(glbPath));
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.ChangeExtension(glbPath, null) + ".joints.json");

            var root = new GameObject(id);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;

                // Stand it on y = 0, centred on X/Z.
                var rs = root.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return null;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                b.center -= new Vector3(b.center.x, b.min.y, b.center.z);

                int joints = 0;
                if (manifest != null)
                {
                    var dev = root.AddComponent<ArticulatedDevice>();
                    dev.ManifestAsset = manifest;
                    joints = dev.Joints.Count;
                    if (joints > 0)
                    {
                        if (!inst.TryGetComponent<Animator>(out var animator)) animator = inst.AddComponent<Animator>();
                        animator.runtimeAnimatorController = BuildController(id, category, glbPath, inst.transform, dev);
                        animator.applyRootMotion = false;
                        animator.cullingMode = AnimatorCullingMode.CullCompletely; // nothing to pose when out of view
                    }
                }

                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0, b.size.y / 2, 0);
                col.size = b.size;

                bool handheld = Handheld.Contains(category) && Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < 0.45f;
                if (handheld)
                {
                    var rb = root.AddComponent<Rigidbody>();
                    rb.mass = 0.2f;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    root.AddComponent<XRGrabInteractable>();
                }
                else
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
                }

                var folder = $"{PrefabRoot}/{category}";
                Directory.CreateDirectory(folder);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/SR_{id}.prefab");
                return new SimReadyLibrary.Entry
                {
                    id = id, displayName = SimReadyLibrary.Readable(id), category = category, prefab = prefab, joints = joints, size = b.size,
                };
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SimReady] {id}: {ex.Message}");
                return null;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}

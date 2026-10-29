using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Builds the hands shown on the controllers (Resources/Hands/ControllerHand_Left/Right) from the XRI Hands
    /// Interaction Demo visuals (Left/RightHandQuestVisual), the very hands the rig shows with hand tracking. The
    /// tracking and affordance components are stripped (there is no hand data with controllers), the hand is aligned to
    /// the grip pose (fingers along +Z, palm facing the controller), and <see cref="Interaction.ControllerHandAnimator"/>
    /// is added to animate the fingers from the grip, trigger and thumbstick.
    /// </summary>
    public static class ControllerHands
    {
        const string ModelFolder = "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/Prefabs/";
        const string OutFolder = SFPaths.Root + "/Resources/Hands";

        // How far the palm sits beside the grip point.
        const float PalmSideOffset = 0.035f;
        const float PalmBackOffset = -0.03f;

        [MenuItem(SFPaths.MenuRoot + "Build/1d · Controller Hands", priority = 3)]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(OutFolder);
            BuildHand(true);
            BuildHand(false);
            AssetDatabase.SaveAssets();
            Debug.Log("[Surgical Foundations] Controller hands built in " + OutFolder);
        }

        static void BuildHand(bool left)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + (left ? "LeftHandQuestVisual.prefab" : "RightHandQuestVisual.prefab"));
            if (model == null) { Debug.LogWarning("[ControllerHands] XRI hand visuals not found in " + ModelFolder); return; }

            var p = left ? "L_" : "R_";
            var hand = (GameObject)Object.Instantiate(model);
            Strip(hand);
            Transform Bone(string n) => FindDeep(hand.transform, p + n);

            var wrist = Bone("Wrist");
            var indexProx = Bone("IndexProximal");
            var littleProx = Bone("LittleProximal");
            var middleProx = Bone("MiddleProximal");
            if (wrist == null || indexProx == null || littleProx == null || middleProx == null)
            {
                Debug.LogWarning("[ControllerHands] Unexpected bone names in the hand model");
                Object.DestroyImmediate(hand);
                return;
            }

            // Palm normal from the knuckle line, pointed to the side the thumb tip sits on (the palm side of a rest pose).
            var fingers = (middleProx.position - wrist.position).normalized;
            var across = (littleProx.position - indexProx.position).normalized;
            var palm = Vector3.Cross(fingers, across).normalized;
            var thumbTip = Bone("ThumbTip");
            if (thumbTip != null && Vector3.Dot(thumbTip.position - indexProx.position, palm) < 0f) palm = -palm;

            // Align the hand so the knuckle direction is +Z and the palm faces the controller
            // (+X for the left hand, −X for the right), then put the palm centre beside the grip point.
            fingers = (middleProx.position - wrist.position).normalized;
            var targetPalm = left ? Vector3.right : Vector3.left;
            var align = Quaternion.FromToRotation(fingers, Vector3.forward);
            var root = new GameObject(left ? "ControllerHand_Left" : "ControllerHand_Right");
            hand.transform.SetParent(root.transform, true);
            hand.transform.rotation = align * hand.transform.rotation;
            // Spin about +Z so the palm faces the controller.
            var palmNow = Vector3.ProjectOnPlane(align * palm, Vector3.forward).normalized;
            hand.transform.rotation = Quaternion.FromToRotation(palmNow, targetPalm) * hand.transform.rotation;

            var palmCentre = (wrist.position + middleProx.position) * 0.5f;
            hand.transform.position += -palmCentre + (-targetPalm * PalmSideOffset) + Vector3.forward * PalmBackOffset;

            foreach (var r in hand.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            }

            var animator = root.AddComponent<Interaction.ControllerHandAnimator>();
            animator.leftHand = left;

            PrefabUtility.SaveAsPrefabAsset(root, $"{OutFolder}/{root.name}.prefab");
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Keeps only the skinned hand: the hand-tracking driver, mesh controller and interactor affordances need live
        /// hand data and interactors that the controller copy does not have.
        /// </summary>
        static void Strip(GameObject hand)
        {
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name.Contains("Affordances")) Object.DestroyImmediate(t.gameObject);
            // Twice: some components require others and can only go once their dependents are gone.
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in hand.GetComponentsInChildren<Component>(true))
                    if (c != null && !(c is Transform) && !(c is SkinnedMeshRenderer)) Object.DestroyImmediate(c);
            foreach (var r in hand.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.enabled = true; // the mesh controller hides it until tracked
            hand.SetActive(true);
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.name == name) return c;
            return null;
        }
    }
}

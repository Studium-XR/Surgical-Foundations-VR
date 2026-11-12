using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Theatre staff animated with motion from HY-Motion 1.0 (Tencent Hunyuan, text-to-motion; generated through the
    /// tencent/HY-Motion-1.0 Hugging Face Space into Imports/hymotion; see CREDITS.md there). The clips come on an SMPL-X
    /// skeleton; the nurse (Mixamo rig) and the doctor model are made Humanoid so the clips retarget onto them. Each role
    /// gets an animator that plays its idle loop and now and then its action (hand an instrument over, check the monitor),
    /// crossfading between them. Staff stay where the scene puts them: the clips play in place.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        const string HyMotionFolder = SFPaths.Root + "/Imports/hymotion";
        const string StaffAnimationFolder = SFPaths.Root + "/Art/Animation";

        /// <summary>Character prefab → (idle clip file, action clip file).</summary>
        static readonly Dictionary<string, (string idle, string action)> StaffClips = new Dictionary<string, (string, string)>
        {
            { "CHR_ScrubNurse", ("HYM_nurse_idle", "HYM_nurse_handover") },
            { "CHR_Anaesthetist", ("HYM_anaesthetist_idle", "HYM_anaesthetist_monitor") },
        };

        /// <summary>SMPL-X joint → Unity human bone. "Shoulder" in SMPL is the upper arm and "Collar" the clavicle,
        /// which the automatic mapper gets wrong.</summary>
        static readonly (string human, string smpl)[] SmplHumanBones =
        {
            ("Hips", "Pelvis"), ("Spine", "Spine1"), ("Chest", "Spine2"), ("UpperChest", "Spine3"), ("Neck", "Neck"), ("Head", "Head"),
            ("LeftUpperLeg", "L_Hip"), ("LeftLowerLeg", "L_Knee"), ("LeftFoot", "L_Ankle"), ("LeftToes", "L_Foot"),
            ("RightUpperLeg", "R_Hip"), ("RightLowerLeg", "R_Knee"), ("RightFoot", "R_Ankle"), ("RightToes", "R_Foot"),
            ("LeftShoulder", "L_Collar"), ("LeftUpperArm", "L_Shoulder"), ("LeftLowerArm", "L_Elbow"), ("LeftHand", "L_Wrist"),
            ("RightShoulder", "R_Collar"), ("RightUpperArm", "R_Shoulder"), ("RightLowerArm", "R_Elbow"), ("RightHand", "R_Wrist"),
        };

        static readonly (string human, string smpl)[] SmplFingers =
        {
            ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky")
        };

        [MenuItem(SFPaths.MenuRoot + "Build/1i · Staff Animations (HY-Motion)", priority = 3)]
        public static void BuildStaffAnimations()
        {
            MakeHumanoid(ImportedAssets.Nurse);
            MakeHumanoid(ImportedAssets.Doctor);
            foreach (var clips in StaffClips.Values)
            {
                PrepareMotion(clips.idle, loop: true);
                PrepareMotion(clips.action, loop: false);
            }
            // The characters pick up their animators when they are rebuilt (ImportedCharacter → StaffAnimator).
            Mannequins();
            AssetDatabase.SaveAssets();
            Debug.Log("[Surgical Foundations] Staff animations built: " + string.Join(", ", StaffClips.Keys));
        }

        static void MakeHumanoid(string modelPath)
        {
            if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter mi) || mi.animationType == ModelImporterAnimationType.Human) return;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // standard bone names map themselves
            mi.SaveAndReimport();
        }

        /// <summary>An HY-Motion FBX as a Humanoid clip that plays in place (no drift, no turning, feet on the floor).</summary>
        static void PrepareMotion(string file, bool loop)
        {
            var path = $"{HyMotionFolder}/{file}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var bones = model.GetComponentsInChildren<Transform>(true);

            var human = new List<HumanBone>();
            void Map(string humanName, string bone)
            {
                if (bones.Any(t => t.name == bone))
                    human.Add(new HumanBone { humanName = humanName, boneName = bone, limit = new HumanLimit { useDefaultValues = true } });
            }
            foreach (var (h, s) in SmplHumanBones) Map(h, s);
            foreach (var side in new[] { ("Left", "L"), ("Right", "R") })
            foreach (var (h, s) in SmplFingers)
            {
                Map($"{side.Item1} {h} Proximal", $"{side.Item2}_{s}1");
                Map($"{side.Item1} {h} Intermediate", $"{side.Item2}_{s}2");
                Map($"{side.Item1} {h} Distal", $"{side.Item2}_{s}3");
            }
            var description = mi.humanDescription;
            description.human = human.ToArray();
            description.skeleton = BindPoseSkeleton(model, bones);

            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.humanDescription = description;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = file.Replace("HYM_", "");
                c.loopTime = loop;
                c.loopPose = loop;
                // In place: root rotation, height and floor position baked into the pose, from the first frame.
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        /// <summary>
        /// The avatar's reference (T-) pose from the mesh's bind pose. HY-Motion files keep the motion's first frame as
        /// the bones' default pose (hands clasped, say); built from that, the avatar took the clasped hands for a T-pose
        /// and every retargeted frame came out with the arms flung wide. SMPL-X binds its mesh in a T-pose.
        /// </summary>
        static SkeletonBone[] BindPoseSkeleton(GameObject model, Transform[] bones)
        {
            var world = new Dictionary<Transform, Matrix4x4>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                if (bind == null) continue;
                for (int i = 0; i < smr.bones.Length && i < bind.Length; i++)
                    if (smr.bones[i] != null && !world.ContainsKey(smr.bones[i]))
                        world[smr.bones[i]] = smr.transform.localToWorldMatrix * bind[i].inverse;
            }
            Matrix4x4 World(Transform t) => world.TryGetValue(t, out var m) ? m : t.localToWorldMatrix;
            return bones.Select(t =>
            {
                if (!world.ContainsKey(t)) return new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
                // Rotations from the bind pose; positions as the file has them (the bones are the same lengths, and
                // the mesh was bound centred on the origin, which would sink the hips a metre into the floor).
                var local = (t.parent != null ? World(t.parent).inverse : Matrix4x4.identity) * World(t);
                return new SkeletonBone { name = t.name, position = t.localPosition, rotation = local.rotation, scale = t.localScale };
            }).ToArray();
        }

        static AnimationClip MotionClip(string file) =>
            AssetDatabase.LoadAllAssetsAtPath($"{HyMotionFolder}/{file}.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        /// <summary>Idle (looping) and action, crossfading: the action now and then, then back to idle.</summary>
        static RuntimeAnimatorController StaffController(string prefabName)
        {
            if (!StaffClips.TryGetValue(prefabName, out var files)) return null;
            var idle = MotionClip(files.idle);
            var action = MotionClip(files.action);
            if (idle == null) return null;

            Directory.CreateDirectory(StaffAnimationFolder);
            var path = $"{StaffAnimationFolder}/AC_{prefabName.Replace("CHR_", "")}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = controller.layers[0].stateMachine;
            var idleState = sm.AddState("Idle");
            idleState.motion = idle;
            sm.defaultState = idleState;
            if (action != null)
            {
                var actionState = sm.AddState("Action");
                actionState.motion = action;
                var toAction = idleState.AddTransition(actionState);
                toAction.hasExitTime = true;
                toAction.exitTime = 2f; // two idle cycles between actions
                toAction.duration = 0.8f;
                var toIdle = actionState.AddTransition(idleState);
                toIdle.hasExitTime = true;
                toIdle.exitTime = 0.9f;
                toIdle.duration = 0.8f;
            }
            return controller;
        }

        /// <summary>Gives an imported character instance its role's animator (if its motion clips have been imported).</summary>
        static void StaffAnimator(GameObject inst, string prefabName)
        {
            var controller = StaffController(prefabName);
            if (controller == null) return;
            if (!inst.TryGetComponent<Animator>(out var animator)) animator = inst.AddComponent<Animator>();
            if (animator.avatar == null || !animator.avatar.isHuman) return; // model not Humanoid: keep the static pose
            animator.runtimeAnimatorController = controller;
            animator.enabled = true; // TryModel switched it off while the character had no clips; it has them now
            animator.applyRootMotion = false; // staff stay where the scene puts them
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
    }
}

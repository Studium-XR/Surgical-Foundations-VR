using System.IO;
using System.Linq;
using SurgicalFoundations.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// The awake patient: Imports/patient/source/Patient_Rigged.fbx (the patient model given a humanoid skeleton in
    /// Blender by Tools/Characters/rig_patient.py, T-pose rest) made Humanoid, with HY-Motion patient clips (lying:
    /// breathe, talk; sitting: idle, talk, listen, pain, wave) retargeted onto it. Saved as
    /// Resources/Characters/CHR_Patient so procedure labs can bring it in at runtime.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        const string RiggedPatient = ImportedAssets.Root + "/patient/source/Patient_Rigged.fbx";
        const string PatientPrefab = SFPaths.Root + "/Resources/Characters/CHR_Patient.prefab";

        static readonly (string file, bool loop)[] PatientClips =
        {
            ("HYM_patient_lie_breathe", true), ("HYM_patient_lie_talk", true),
            ("HYM_patient_sit_idle", true), ("HYM_patient_sit_talk", true), ("HYM_patient_sit_listen", false),
            ("HYM_patient_sit_pain", false), ("HYM_patient_sit_wave", false),
        };

        [MenuItem(SFPaths.MenuRoot + "Build/1j · Patient Character", priority = 4)]
        public static void BuildPatientCharacter()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RiggedPatient) == null)
            {
                Debug.LogWarning("[Patient] Patient_Rigged.fbx missing: run Tools/Characters/rig_patient.py in Blender first.");
                return;
            }
            MakeHumanoid(RiggedPatient);
            foreach (var (file, loop) in PatientClips) PrepareMotion(file, loop);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RiggedPatient);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(RiggedPatient).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman)
            {
                Debug.LogError("[Patient] The rigged patient did not become a valid Humanoid; check its import settings.");
                return;
            }

            var controller = PatientController();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                inst.name = "CHR_Patient";
                ImportedAssets.Remap(inst, ImportedAssets.MaterialMap(ImportedAssets.Patient));
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                if (!inst.TryGetComponent<Animator>(out var animator)) animator = inst.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                if (!inst.TryGetComponent<PatientCharacter>(out _)) inst.AddComponent<PatientCharacter>();
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = false;
                Directory.CreateDirectory(Path.GetDirectoryName(PatientPrefab));
                PrefabUtility.SaveAsPrefabAsset(inst, PatientPrefab);
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Patient] CHR_Patient built: Humanoid, {controller.animationClips.Length} motions.");
        }

        /// <summary>One state per motion, named after the clip; PatientCharacter crossfades between them. IK pass on
        /// for the head look-at.</summary>
        static AnimatorController PatientController()
        {
            Directory.CreateDirectory(StaffAnimationFolder);
            var path = $"{StaffAnimationFolder}/AC_Patient.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;
            var sm = controller.layers[0].stateMachine;
            foreach (var (file, _) in PatientClips)
            {
                var clip = MotionClip(file);
                if (clip == null) { Debug.LogWarning("[Patient] Missing motion " + file); continue; }
                var state = sm.AddState(file.Replace("HYM_", ""));
                state.motion = clip;
                if (file == "HYM_patient_lie_breathe") sm.defaultState = state;
            }
            return controller;
        }
    }
}

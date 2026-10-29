using SurgicalFoundations.Audio;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Open-surgery foundations kit for the Skills Lab: four new hand instruments (toothed tissue forceps, Mayo scissors,
    /// a ratcheting haemostat, an Army-Navy retractor), a layered practice pad and a vessel trainer, assembled with the
    /// existing scalpel and needle holder into one station (Resources/OpenSurgery/OpenSkillsStation) that
    /// <see cref="Detection.OpenSkillsStation"/> puts on the Skills Lab back table and runs.
    /// Instrument convention as everywhere: long axis +Z, tip at +Z, hinged parts named Jaw_Upper / Jaw_Lower.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        // Practice pad, in its own space: origin at the centre of the base, Y up, incision line along X at z = 0.
        public const float PadLength = 0.22f, PadWidth = 0.15f;
        const float PadBase = 0.008f, PadMuscle = 0.008f, PadFascia = 0.002f, PadFat = 0.008f, PadSkin = 0.004f;
        public const float PadIncisionHalf = 0.08f;

        [MenuItem(SFPaths.MenuRoot + "Build/1g · Open Surgery Station", priority = 3)]
        public static void BuildOpenSurgeryStation()
        {
            SimReadyScalpel();
            SimReadySuction();
            SimReadyProp("tray_sterilization_instrument_c02", "EQ_SterilizationTray", "EQ-X4", "Sterilisation instrument tray", 33900);
            SimReadyProp("needle_surgical_a01", "PROP_NeedleBox", "PROP-X3", "Surgical needle box", 9400, grabbable: true);
            TissueForceps();
            MayoScissors();
            Hemostat();
            Retractor();
            SkinPad();
            VesselTrainer();
            Station();
            AssetDatabase.SaveAssets();
            Debug.Log("[Surgical Foundations] Open surgery station built in Resources/OpenSurgery");
        }

        static void TissueForceps()
        {
            if (SimReadyTissueForceps()) return; // NVIDIA SimReady model when it is imported; primitives otherwise
            var b = new PB("INST_TissueForceps");
            // Two sprung arms joined at the back; they pivot at the joint, so the whole arm opens a few degrees.
            var upper = b.Group("Jaw_Upper", Vector3.zero);
            var lower = b.Group("Jaw_Lower", Vector3.zero);
            foreach (var (arm, sign) in new[] { (upper, 1f), (lower, -1f) })
            {
                b.Box("Arm", new Vector3(0, sign * 0.0035f, 0.06f), new Vector3(0.008f, 0.0016f, 0.12f), "Instrument_Steel", new Vector3(sign * 1.6f, 0, 0), arm);
                b.Box("Grip", new Vector3(0, sign * 0.0042f, 0.045f), new Vector3(0.0085f, 0.0008f, 0.05f), "Stainless_Satin", new Vector3(sign * 1.6f, 0, 0), arm);
                b.Box("Tip", new Vector3(0, sign * 0.0006f, 0.124f), new Vector3(0.003f, 0.0016f, 0.012f), "Instrument_Steel", default, arm);
                b.Box("Tooth", new Vector3(0, -sign * 0.0002f, 0.1298f), new Vector3(0.001f, 0.0016f, 0.001f), "Instrument_Steel", default, arm);
            }
            b.Box("Joint", new Vector3(0, 0, 0.002f), new Vector3(0.008f, 0.0085f, 0.006f), "Instrument_Steel");
            b.Pivot("Pivot_Tip", new Vector3(0, 0, 0.13f));
            b.Info("INST-12", "Tissue forceps (toothed)", "Instrument", AssetRelease.MVP, 600, "Adson-style; arms hinge at the joint. Grasp and lift tissue edges.");
            b.Grabbable(0.03f, new Vector3(0, 0, 0.05f), Vector3.zero, jaws: true);
            Ser.Set(b.Root.GetComponent<InstrumentJaws>(), "openAngle", 7f);
            b.Save(Folder("Instruments"));
        }

        /// <summary>Scissors-type instrument: two arms crossing at a screw, each a blade or jaw ahead of it and a ring behind.</summary>
        static PB CrossedArms(string name, float jawLength, float jawWidth, float handleLength, string jawMat, bool serrated)
        {
            var b = new PB(name);
            float screwZ = handleLength;
            var hinge = b.Group("JawHinge", new Vector3(0, 0, screwZ));
            var upper = b.Group("Jaw_Upper", Vector3.zero, default, hinge);
            var lower = b.Group("Jaw_Lower", Vector3.zero, default, hinge);
            foreach (var (arm, sign) in new[] { (upper, 1f), (lower, -1f) })
            {
                // Ahead of the screw: the blade or jaw on this side. Behind it: the shank and ring on the other side.
                b.Box("Blade", new Vector3(0, sign * 0.0011f, jawLength / 2f), new Vector3(jawWidth, 0.0018f, jawLength), jawMat, default, arm);
                if (serrated)
                    for (int k = 0; k < 6; k++)
                        b.Box("Serration", new Vector3(0, sign * 0.0001f, jawLength * (0.25f + k * 0.12f)), new Vector3(jawWidth * 1.02f, 0.0004f, 0.0012f), "Stainless_Satin", default, arm);
                b.Box("Shank", new Vector3(0, -sign * 0.006f, -handleLength * 0.45f), new Vector3(0.005f, 0.003f, handleLength * 0.8f), "Instrument_Steel", new Vector3(-sign * 5f, 0, 0), arm);
                b.Torus("Ring", new Vector3(0, -sign * 0.018f, -handleLength * 0.92f), 0.024f, 0.004f, "Instrument_Steel", new Vector3(0, 0, 90), arm);
            }
            b.Cyl("Screw", Vector3.zero, 0.006f, 0.008f, "Stainless_Satin", PB.AlongX, hinge);
            b.Pivot("Pivot_Tip", new Vector3(0, 0, screwZ + jawLength));
            return b;
        }

        static void MayoScissors()
        {
            if (SimReadyScissors()) return; // NVIDIA SimReady model when it is imported; primitives otherwise
            var b = CrossedArms("INST_MayoScissors", 0.065f, 0.006f, 0.1f, "Instrument_Steel", false);
            b.Info("INST-13", "Mayo scissors", "Instrument", AssetRelease.MVP, 900, "Blades cut when they close on a target (suture tails).");
            b.Grabbable(0.06f, new Vector3(0, 0, 0.02f), Vector3.zero, jaws: true);
            Ser.Set(b.Root.GetComponent<InstrumentJaws>(), "openAngle", 30f);
            b.Save(Folder("Instruments"));
        }

        static void Hemostat()
        {
            if (SimReadyHemostat()) return; // NVIDIA SimReady model when it is imported; primitives otherwise
            var b = CrossedArms("INST_Hemostat", 0.04f, 0.0045f, 0.095f, "Instrument_Steel", true);
            // Ratchet teeth on the shanks, just ahead of the rings.
            b.Box("Ratchet_A", new Vector3(0, -0.004f, 0.015f), new Vector3(0.004f, 0.003f, 0.012f), "Stainless_Satin");
            b.Box("Ratchet_B", new Vector3(0, 0.004f, 0.015f), new Vector3(0.004f, 0.003f, 0.012f), "Stainless_Satin");
            b.Info("INST-14", "Haemostat (Kelly)", "Instrument", AssetRelease.MVP, 900, "Serrated jaws; locks on a full squeeze (RatchetLock), releases on a second.");
            b.Grabbable(0.05f, new Vector3(0, 0, 0.02f), Vector3.zero, jaws: true);
            Ser.Set(b.Root.GetComponent<InstrumentJaws>(), "openAngle", 22f);
            b.Root.AddComponent<RatchetLock>();
            b.Save(Folder("Instruments"));
        }

        static void Retractor()
        {
            if (SimReadyRetractor()) return; // NVIDIA SimReady model when it is imported; primitives otherwise
            var b = new PB("INST_Retractor");
            b.Box("Handle", new Vector3(0, 0, 0), new Vector3(0.018f, 0.002f, 0.17f), "Instrument_Steel");
            b.Box("Grip", new Vector3(0, 0.0012f, 0), new Vector3(0.016f, 0.0008f, 0.07f), "Stainless_Satin");
            // Working blade: bent down at the far end, lip turned back to hook the wound edge.
            b.Box("Blade", new Vector3(0, -0.019f, 0.085f), new Vector3(0.02f, 0.038f, 0.002f), "Instrument_Steel");
            b.Box("Lip", new Vector3(0, -0.037f, 0.08f), new Vector3(0.02f, 0.002f, 0.01f), "Instrument_Steel");
            // Smaller blade at the other end, bent the other way.
            b.Box("Blade_Small", new Vector3(0, 0.012f, -0.085f), new Vector3(0.015f, 0.024f, 0.002f), "Instrument_Steel");
            b.Pivot("Pivot_Tip", new Vector3(0, -0.037f, 0.078f));
            b.Info("INST-15", "Army-Navy retractor", "Instrument", AssetRelease.MVP, 300, "Hook the wound edge and pull outward to hold it open.");
            b.Grabbable(0.08f, new Vector3(0, 0.002f, -0.01f), Vector3.zero, impact: SoundId.INST_PlaceOnTray);
            b.Save(Folder("Instruments"));
        }

        /// <summary>
        /// Layered practice pad: base, muscle, fascia, fat, and skin in two halves either side of the incision line.
        /// Each skin half hinges at its outer edge (SkinEdge_A at +Z, SkinEdge_B at −Z) so it can be lifted open.
        /// </summary>
        static void SkinPad()
        {
            var b = new PB("PROP_SkinPad");
            float y = 0f;
            Layer(b, "Base", PadBase, "Polymer_Grey", ref y);
            Layer(b, "Muscle", PadMuscle, "Tissue_Wet", ref y);
            Layer(b, "Fascia", PadFascia, "Fascia", ref y);
            Layer(b, "Fat", PadFat, "Fat", ref y);
            float skinBottom = y;
            foreach (var (name, sign) in new[] { ("SkinEdge_A", 1f), ("SkinEdge_B", -1f) })
            {
                var edge = b.Group(name, new Vector3(0, skinBottom, sign * PadWidth / 2f));
                b.Box("Skin", new Vector3(0, PadSkin / 2f, -sign * PadWidth / 4f), new Vector3(PadLength, PadSkin, PadWidth / 2f - 0.0004f), "Skin", default, edge);
            }
            float skinTop = skinBottom + PadSkin;
            // Target line, dashed, drawn just above the skin (a separate group, so it stays put when the edges lift).
            var line = b.Group("TargetLine", new Vector3(0, skinTop + 0.0003f, 0));
            for (float x = -PadIncisionHalf; x < PadIncisionHalf - 0.001f; x += 0.01f)
                b.Box("Dash", new Vector3(x + 0.003f, 0, 0), new Vector3(0.006f, 0.0004f, 0.0012f), "Suture_Violet", default, line);
            b.Pivot("Target_Start", new Vector3(-PadIncisionHalf, skinTop, 0));
            b.Pivot("Target_End", new Vector3(PadIncisionHalf, skinTop, 0));
            // Two stitches, each a bite either side of the line.
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.025f : 0.025f;
                b.Pivot($"Bite_{i}_A", new Vector3(x, skinTop, 0.012f));
                b.Pivot($"Bite_{i}_B", new Vector3(x, skinTop, -0.012f));
            }
            b.Collider(null, new Vector3(0, skinTop / 2f, 0), new Vector3(PadLength, skinTop, PadWidth));
            b.Info("PROP-OS1", "Layered practice pad", "TrainingProp", AssetRelease.MVP, 200,
                "Skin, fat, fascia, muscle. Incision line, two suture bites, liftable skin edges.");
            b.Save(Folder("TrainingProps"));
        }

        static void Layer(PB b, string name, float thickness, string mat, ref float y)
        {
            b.Box(name, new Vector3(0, y + thickness / 2f, 0), new Vector3(PadLength, thickness, PadWidth), mat);
            y += thickness;
        }

        static void VesselTrainer()
        {
            var b = new PB("PROP_VesselTrainer");
            b.Box("Base", new Vector3(0, 0.005f, 0), new Vector3(0.13f, 0.01f, 0.06f), "Polymer_Grey");
            b.Box("Post_L", new Vector3(-0.055f, 0.02f, 0), new Vector3(0.01f, 0.03f, 0.02f), "Polymer_Grey");
            b.Box("Post_R", new Vector3(0.055f, 0.02f, 0), new Vector3(0.01f, 0.03f, 0.02f), "Polymer_Grey");
            b.Cyl("Artery", new Vector3(0, 0.035f, 0), 0.008f, 0.12f, "Vessel_Artery", PB.AlongX);
            b.Cyl("CutEnd", new Vector3(0.061f, 0.035f, 0), 0.0085f, 0.002f, "Tissue_Wet", PB.AlongX);
            b.Pivot("ClampPoint", new Vector3(0.0f, 0.035f, 0));
            b.Pivot("BleedPoint", new Vector3(0.062f, 0.035f, 0));
            b.Collider(null, new Vector3(0, 0.02f, 0), new Vector3(0.13f, 0.04f, 0.06f));
            b.Info("PROP-OS2", "Vessel trainer", "TrainingProp", AssetRelease.MVP, 200, "Cut artery that bleeds until a haemostat is locked on the clamp point.");
            b.Save(Folder("TrainingProps"));
        }

        /// <summary>
        /// The whole station as one prefab, in station space: origin on the table top at the centre of the work area,
        /// the learner standing at −Z. Pad in the middle, vessel trainer to its right, instruments laid out on the left.
        /// </summary>
        static void Station()
        {
            var root = new GameObject("OpenSkillsStation");
            void Place(string rel, string name, Vector3 pos, Vector3 euler)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{SFPaths.Prefabs}/{rel}.prefab");
                if (prefab == null) { Debug.LogWarning($"[OpenSurgery] Missing prefab {rel}"); return; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.name = name;
                go.transform.localPosition = pos;
                go.transform.localEulerAngles = euler;
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true; // stays where it is put down, on the table or in the air
            }
            Place("TrainingProps/PROP_SkinPad", "PROP_SkinPad", new Vector3(0, 0, 0.02f), Vector3.zero);
            Place("TrainingProps/PROP_VesselTrainer", "PROP_VesselTrainer", new Vector3(0.22f, 0, 0.03f), Vector3.zero);
            // Instruments lying flat on the left of the pad, tips away from the learner.
            Place("Instruments/INST_Scalpel", "INST_Scalpel", new Vector3(-0.4f, 0.006f, -0.04f), new Vector3(0, 0, 90)); // on its flat side
            Place("Instruments/INST_TissueForceps", "INST_TissueForceps", new Vector3(-0.35f, 0.005f, -0.04f), new Vector3(0, 0, 90));
            Place("Instruments/INST_MayoScissors", "INST_MayoScissors", new Vector3(-0.29f, 0.004f, -0.1f), new Vector3(0, 0, 90));
            Place("Instruments/INST_Hemostat", "INST_Hemostat", new Vector3(-0.22f, 0.004f, -0.1f), new Vector3(0, 0, 90));
            Place("Instruments/INST_NeedleHolder", "INST_NeedleHolder", new Vector3(-0.16f, 0.004f, -0.08f), Vector3.zero);
            Place("Instruments/INST_Retractor", "INST_Retractor", new Vector3(0.0f, 0.012f, -0.17f), new Vector3(0, 90, 0));
            // SimReady extras (skipped when the models aren't imported): suction tip, needle box, sterilisation tray.
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{SFPaths.Prefabs}/Instruments/INST_Suction.prefab") != null)
                Place("Instruments/INST_Suction", "INST_Suction", new Vector3(0.36f, 0.012f, -0.1f), new Vector3(0, 0, 0));
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{SFPaths.Prefabs}/Equipment/PROP_NeedleBox.prefab") != null)
                Place("Equipment/PROP_NeedleBox", "PROP_NeedleBox", new Vector3(0.24f, 0f, -0.14f), new Vector3(0, 90, 0));
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{SFPaths.Prefabs}/Equipment/EQ_SterilizationTray.prefab") != null)
                Place("Equipment/EQ_SterilizationTray", "EQ_SterilizationTray", new Vector3(-0.28f, 0f, 0.2f), Vector3.zero);
            root.AddComponent<Detection.OpenSkillsStation>();

            const string folder = SFPaths.Root + "/Resources/OpenSurgery";
            System.IO.Directory.CreateDirectory(folder);
            PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/OpenSkillsStation.prefab");
            Object.DestroyImmediate(root);
        }
    }
}

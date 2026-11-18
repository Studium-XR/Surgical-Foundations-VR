using System;
using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// IV and transfusion consumables split out of Imports/iv_supplies.glb, a single sheet of ~30 textured meshes
    /// (bag, giving-set parts, cannulas in their packs, syringe, blood bags, wristband). Each item becomes a grabbable
    /// prefab in Prefabs/Consumables, standing on its lowest point and centred on X/Z, and the saline bag also hangs on
    /// EQ_IVStand in place of the old box placeholder.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        const string IVSuppliesFile = SFPaths.Root + "/Imports/iv_supplies.glb";
        // The sheet is modelled about 1.4x life size (the 1 L bag measures 46 cm); 0.7 brings the bag to ~32 cm with ports.
        const float IVSuppliesScale = 0.7f;

        /// <summary>Every mesh in the sheet, in model space, with its base name ("WaterBag") and model-space centre.</summary>
        static List<(ModelPart part, string key, Vector3 centre)> IVSupplyParts()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(IVSuppliesFile);
            if (asset == null) return null;
            var inst = (GameObject)Object.Instantiate(asset);
            try
            {
                // No de-duplication: "Split" and "Puncture" exist twice as different pieces of different items.
                return inst.GetComponentsInChildren<MeshFilter>().Select(mf =>
                {
                    var r = mf.GetComponent<MeshRenderer>();
                    var part = new ModelPart
                    {
                        name = mf.name.Replace("_All_Tools_0", ""),
                        mesh = mf.sharedMesh,
                        materials = r.sharedMaterials,
                        model = mf.transform.localToWorldMatrix,
                    };
                    return (part, part.name, r.bounds.center);
                }).ToList();
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // prefab, id, display name, mass kg, which sheet parts belong to it
        static readonly (string prefab, string id, string title, float mass, Func<string, Vector3, bool> has)[] IVItems =
        {
            ("CON_IVBag_Saline", "CON-IV1", "IV fluid bag, 1000 ml", 1.05f, (k, c) => k == "WaterBag"),
            ("CON_IVGivingSet", "CON-IV2", "IV giving set (spike, drip chamber, roller clamp, ports)", 0.03f, (k, c) =>
                k is "Stopper1" or "Stopper2" or "Stopper3" or "Filter" or "Rocket_lookin_thing" or "Brace" or "Box_bit" or "Start_Screw"
                    or "Puncture" or "Puncture_Case" // bag spikes; nested under Needle_7x19 in the file but laid out with this set
                || (k == "Split" && c.z > 0f)),
            ("CON_IVCannula_Packed", "CON-IV3", "Peripheral IV cannula in pack", 0.01f, (k, c) =>
                k is "Package_57" or "Case_57" or "Needle_57"),
            ("CON_IVExtensionSet", "CON-IV4", "IV extension set with clamp", 0.02f, (k, c) =>
                k is "Catheter1_1x25mm" or "Cath_case" or "blue_thingy" or "clamp" or "tube" or "package" || (k == "Split" && c.z < 0f)),
            ("CON_InjectionNeedle_Packed", "CON-IV5", "Injection needle in pack, with spikes", 0.01f, (k, c) =>
                k is "Package_7x19" or "Case_7x19" or "Needle_7x19"),
            ("CON_Syringe", "CON-IV6", "Syringe", 0.02f, (k, c) => k is "Tube" or "Pusher"),
            ("CON_PatientWristband", "CON-IV7", "Patient ID wristband", 0.005f, (k, c) => k == "Wristband"),
            ("CON_IVDressingPack", "CON-IV8", "Cannula dressing pack", 0.005f, (k, c) => k == "sticker_package"),
            ("CON_BloodBag_Hanging", "CON-IV9", "Blood unit bag (hanging)", 0.35f, (k, c) => k == "Blood_Bag_Hang"),
            ("CON_BloodBag_Flat", "CON-IV10", "Blood unit bag (flat)", 0.35f, (k, c) => k == "Blood_Bag_flat"),
        };

        /// <summary>Builds every IV consumable prefab. Returns how many were built (0 when the glb is missing).</summary>
        public static int BuildIVSupplies()
        {
            var all = IVSupplyParts();
            if (all == null) return 0;
            int built = 0;
            var unused = new HashSet<string>(all.Select(a => a.key));
            foreach (var it in IVItems)
            {
                var parts = all.Where(a => it.has(a.key, a.centre)).ToList();
                if (parts.Count == 0) { Debug.LogWarning($"[IV supplies] {it.prefab}: no parts found"); continue; }
                foreach (var p in parts) unused.Remove(p.key);
                var list = parts.Select(p => p.part).ToList();
                var b = new PB(it.prefab);
                var m = StandModel(list, 0f, IVSuppliesScale);
                if (!it.prefab.Contains("Hanging") && !it.prefab.Contains("IVBag"))
                    m = LayFlat(list, m); // packs, syringe, tubing rest on a tray: thinnest axis up, longest along Z
                var model = b.Group("Model", Vector3.zero);
                foreach (var p in list) PlacePart(p, model, m);
                int tris = list.Sum(p => p.mesh.triangles.Length / 3);
                b.Info(it.id, it.title, "Consumable", AssetRelease.MVP, tris,
                    $"Imported from Imports/iv_supplies.glb ({list.Count} meshes), scaled x{IVSuppliesScale}.").isPlaceholder = false;
                b.Grabbable(it.mass, PartBounds(list, m).center, Vector3.zero, grabSound: SoundId.None, impact: SoundId.INST_PlaceOnTray);
                b.Save(Folder("Consumables"));
                built++;
            }
            if (unused.Count > 0) Debug.Log("[IV supplies] sheet parts not used by any item: " + string.Join(", ", unused));
            return built;
        }

        /// <summary>Re-orients <paramref name="m"/> so the item's thinnest extent is Y and its longest Z, then re-grounds it.</summary>
        static Matrix4x4 LayFlat(List<ModelPart> parts, Matrix4x4 m)
        {
            var s = PartBounds(parts, m).size;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var order = Enumerable.Range(0, 3).OrderBy(i => s[i]).ToArray(); // thin, mid, long
            var rot = Quaternion.Inverse(Quaternion.LookRotation(axes[order[2]], axes[order[0]])); // long -> +Z, thin -> +Y
            var r = Matrix4x4.Rotate(rot) * m;
            var b = PartBounds(parts, r);
            return Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z)) * r;
        }

        /// <summary>The real 1 L bag hanging from the stand's outer hook, with its giving-set line below. False if the glb is missing.</summary>
        static bool HangIVBag(PB b, Vector3 hook)
        {
            var bag = IVSupplyParts()?.Where(a => a.key == "WaterBag").Select(a => a.part).ToList();
            if (bag == null || bag.Count == 0) return false;
            var r = Matrix4x4.Scale(Vector3.one * IVSuppliesScale);
            var bounds = PartBounds(bag, r);
            // the bag's top (hanger eyelet) just under the hook
            var m = Matrix4x4.Translate(hook - new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)) * r;
            PlacePart(bag[0], b.Group("IVBag", Vector3.zero), m, "IVBag_1000ml");
            float bottom = hook.y - bounds.size.y;
            b.Cyl("IVLine", new Vector3(hook.x, bottom - 0.4f, hook.z), 0.004f, 0.8f, "Hosp_IVBag");
            return true;
        }

        [MenuItem(SFPaths.MenuRoot + "Build/1l · IV Supplies (bags, giving set, cannulas)", priority = 4)]
        public static void BuildIVSuppliesMenu()
        {
            int n = BuildIVSupplies();
            bool stand = SimReadyIVStand();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Surgical Foundations] IV supplies: {n} consumable prefabs in {Folder("Consumables")}; IV stand {(stand ? "rebuilt with the real bag" : "skipped (SimReady pole missing)")}.");
        }
    }
}

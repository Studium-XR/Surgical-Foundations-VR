using System.Collections.Generic;
using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Open-surgery instruments built from NVIDIA SimReady models (CC BY 4.0, via the Hugging Face dataset
    /// codepawl/simready-glb, converted to plain GLB in Imports/simready; see Imports/simready/CREDITS.md).
    /// Same prefab contract as the primitive versions they replace: long axis +Z with the tip at +Z, the back end at
    /// z = 0, Pivot_Tip at the working end, and for scissor-type instruments the two real arms under Jaw_Upper /
    /// Jaw_Lower, hinged at the screw or box lock, so InstrumentJaws opens them. Each builder returns false when its
    /// model is missing, and the caller falls back to the primitive version.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        const string SimReadyFolder = SFPaths.Root + "/Imports/simready";

        struct ModelPart
        {
            public string name;
            public Mesh mesh;
            public Material[] materials;
            public Matrix4x4 model; // part → model space
        }

        /// <summary>The model's meshes in model space, each mesh once (some files carry a duplicate copy).</summary>
        static List<ModelPart> SimReadyParts(string file)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{SimReadyFolder}/{file}.glb");
            if (asset == null) return null;
            // Instantiated as imported: some files keep the mesh on the root node, whose own transform (scale, offset)
            // is part of the model, so model space is the instance's world space, not its local space.
            var inst = (GameObject)Object.Instantiate(asset);
            int Depth(Transform t) { int d = 0; for (; t != inst.transform; t = t.parent) d++; return d; }
            try
            {
                // The duplicates are stray top-level copies at the model origin (USD prototypes left in the export);
                // the placed copy sits under Geometry/<part>_obj_00, so of each mesh the most deeply nested one is kept.
                return inst.GetComponentsInChildren<MeshFilter>()
                    .GroupBy(m => m.sharedMesh.name).Select(g => g.OrderByDescending(m => Depth(m.transform)).First())
                    .Select(m => new ModelPart
                    {
                        name = m.name,
                        mesh = m.sharedMesh,
                        materials = m.GetComponent<MeshRenderer>().sharedMaterials,
                        model = m.transform.localToWorldMatrix,
                    }).ToList();
            }
            finally { Object.DestroyImmediate(inst); }
        }

        /// <summary>
        /// Rotation from model space to instrument space: <paramref name="tipDir"/> (where the working end points in the
        /// model) becomes +Z, and <paramref name="toInstrumentX"/> becomes +X (the hinge axis InstrumentJaws turns about).
        /// </summary>
        static Quaternion ModelToInstrument(Vector3 tipDir, Vector3 toInstrumentX)
        {
            var up = Vector3.Cross(tipDir, toInstrumentX); // the model direction that ends up as +Y
            return Quaternion.Inverse(Quaternion.LookRotation(tipDir, up));
        }

        static IEnumerable<Vector3> Vertices(ModelPart p, Matrix4x4 m)
        {
            var full = m * p.model;
            return p.mesh.vertices.Select(v => full.MultiplyPoint3x4(v));
        }

        static Transform PlacePart(ModelPart p, Transform parent, Matrix4x4 m, string name = null)
        {
            var go = new GameObject(name ?? p.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var full = m * p.model;
            go.transform.localPosition = full.GetColumn(3);
            go.transform.localRotation = full.rotation;
            go.transform.localScale = full.lossyScale;
            go.GetComponent<MeshFilter>().sharedMesh = p.mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = p.materials;
            return go.transform;
        }

        /// <summary>One-piece instrument: placed under Visual with its back end at z = 0, centred on X/Y. Returns the tip (max Z) point.</summary>
        static Vector3 SimReadyStatic(PB b, List<ModelPart> parts, Quaternion q)
        {
            var r = Matrix4x4.Rotate(q);
            var all = parts.SelectMany(p => Vertices(p, r)).ToList();
            var bounds = new Bounds(all[0], Vector3.zero);
            foreach (var v in all) bounds.Encapsulate(v);
            var offset = new Vector3(-bounds.center.x, -bounds.center.y, -bounds.min.z);
            var m = Matrix4x4.Translate(offset) * r;
            var model = b.Group("Model", Vector3.zero);
            foreach (var p in parts) PlacePart(p, model, m);
            // The tip: the furthest forward vertex.
            return all.OrderByDescending(v => v.z).First() + offset;
        }

        /// <summary>
        /// Scissor-type instrument from its two arm meshes: hinged at <paramref name="hingeModel"/>; the arm whose ring
        /// sits at −Y goes under Jaw_Upper (its blade rises as the jaws open), the other under Jaw_Lower.
        /// Returns the tip in instrument space.
        /// </summary>
        static Vector3 SimReadyHinged(PB b, List<ModelPart> arms, Quaternion q, Vector3 hingeModel)
        {
            var r = Matrix4x4.Rotate(q) * Matrix4x4.Translate(-hingeModel); // hinge at the origin
            float back = arms.SelectMany(p => Vertices(p, r)).Min(v => v.z);
            var hinge = b.Group("JawHinge", new Vector3(0, 0, -back));
            var upper = b.Group("Jaw_Upper", Vector3.zero, default, hinge);
            var lower = b.Group("Jaw_Lower", Vector3.zero, default, hinge);
            // Ring end = the back third of each arm; the one lower in Y opens downward, so it is the "upper" jaw.
            var ringY = arms.Select(p => Vertices(p, r).Where(v => v.z < back * 0.4f).DefaultIfEmpty(Vector3.zero).Average(v => v.y)).ToArray();
            int up = ringY[0] < ringY[1] ? 0 : 1;
            PlacePart(arms[up], upper, r, "Arm");
            PlacePart(arms[1 - up], lower, r, "Arm");
            float tip = arms.SelectMany(p => Vertices(p, r)).Max(v => v.z);
            return new Vector3(0, 0, tip - back);
        }

        // ─────────────────────────────── instruments ───────────────────────────────

        static bool SimReadyTissueForceps()
        {
            var parts = SimReadyParts("instrument_forceps_a01");
            if (parts == null) return false;
            var b = new PB("INST_TissueForceps");
            // Lies on its side in the file (arms apart in Y), tips toward −X. One mesh, so the arms don't animate;
            // the grip still reads the trigger for grasping.
            var tip = SimReadyStatic(b, parts, ModelToInstrument(Vector3.left, Vector3.forward));
            b.Group("Jaw_Upper", Vector3.zero);
            b.Group("Jaw_Lower", Vector3.zero);
            b.Pivot("Pivot_Tip", tip);
            b.Info("INST-12", "Tissue forceps", "Instrument", AssetRelease.MVP, 14400, "NVIDIA SimReady instrument_forceps_a01 (CC BY 4.0), simplified to 35%.").isPlaceholder = false;
            b.Grabbable(0.03f, new Vector3(0, 0, tip.z * 0.4f), Vector3.zero, jaws: true);
            Ser.Set(b.Root.GetComponent<InstrumentJaws>(), "openAngle", 7f);
            b.Save(Folder("Instruments"));
            return true;
        }

        static bool SimReadyScissors()
        {
            var parts = SimReadyParts("instrument_scissors_a01");
            if (parts == null || parts.Count < 2) return false;
            var b = new PB("INST_MayoScissors");
            // Flat in the file (thickness along Y), tips toward −X, screw at the origin of the X axis.
            var tip = SimReadyHinged(b, parts.Take(2).ToList(), ModelToInstrument(Vector3.left, Vector3.up), new Vector3(0f, 0f, 0f));
            b.Pivot("Pivot_Tip", tip);
            b.Info("INST-13", "Curved Mayo scissors", "Instrument", AssetRelease.MVP, 11300, "NVIDIA SimReady instrument_scissors_a01 (CC BY 4.0); blades hinge at the screw.").isPlaceholder = false;
            b.Grabbable(0.06f, new Vector3(0, 0, tip.z * 0.25f), Vector3.zero, jaws: true);
            Ser.Set(b.Root.GetComponent<InstrumentJaws>(), "openAngle", 30f);
            b.Save(Folder("Instruments"));
            return true;
        }

        static bool SimReadyHemostat()
        {
            var parts = SimReadyParts("instrument_artery_forceps_b01");
            if (parts == null || parts.Count < 2) return false;
            var b = new PB("INST_Hemostat");
            // Flat, jaws toward +X, box lock where the arms cross (measured: x 0.045, y 0.0025).
            var tip = SimReadyHinged(b, parts.Take(2).ToList(), ModelToInstrument(Vector3.right, Vector3.up), new Vector3(0.0448f, 0.0025f, 0f));
            b.Pivot("Pivot_Tip", tip);
            b.Info("INST-14", "Artery forceps (haemostat)", "Instrument", AssetRelease.MVP, 15300, "NVIDIA SimReady instrument_artery_forceps_b01 (CC BY 4.0); jaws hinge at the box lock; RatchetLock.").isPlaceholder = false;
            b.Grabbable(0.05f, new Vector3(0, 0, tip.z * 0.2f), Vector3.zero, jaws: true);
            Ser.Set(b.Root.GetComponent<InstrumentJaws>(), "openAngle", 22f);
            b.Root.AddComponent<RatchetLock>();
            b.Save(Folder("Instruments"));
            return true;
        }

        static bool SimReadyRetractor()
        {
            var parts = SimReadyParts("instrument_retractor_a01");
            if (parts == null) return false;
            var b = new PB("INST_Retractor");
            // Rake toward +X; its tines curl up (+Y) in the file, turned to curl down so they hook a wound edge.
            var tip = SimReadyStatic(b, parts, ModelToInstrument(Vector3.right, Vector3.forward)); // model +Y → instrument −Y
            b.Pivot("Pivot_Tip", tip);
            b.Info("INST-15", "Rake retractor", "Instrument", AssetRelease.MVP, 17100, "NVIDIA SimReady instrument_retractor_a01 (CC BY 4.0). Hook the wound edge and pull outward.").isPlaceholder = false;
            b.Grabbable(0.08f, new Vector3(0, 0, tip.z * 0.25f), Vector3.zero, impact: SoundId.INST_PlaceOnTray);
            b.Save(Folder("Instruments"));
            return true;
        }

        static bool SimReadySuction()
        {
            var parts = SimReadyParts("instrument_suction_a01");
            if (parts == null) return false;
            var b = new PB("INST_Suction");
            var tip = SimReadyStatic(b, parts, ModelToInstrument(Vector3.left, Vector3.forward));
            b.Pivot("Pivot_Tip", tip);
            b.Info("INST-16", "Suction tip", "Instrument", AssetRelease.MVP, 15800, "NVIDIA SimReady instrument_suction_a01 (CC BY 4.0).").isPlaceholder = false;
            b.Grabbable(0.05f, new Vector3(0, 0, tip.z * 0.75f), Vector3.zero, impact: SoundId.INST_PlaceOnTray);
            b.Save(Folder("Instruments"));
            return true;
        }

        /// <summary>Scalpel: the SimReady model, handle with its blade fitted on the slim end.</summary>
        static bool SimReadyScalpel()
        {
            var parts = SimReadyParts("instrument_scalpel_handle_a01");
            if (parts == null) return false;
            var b = new PB("INST_Scalpel");
            // Flat in the file with the blade toward +X; turned so the blade's flat faces sideways (±X) and its curved
            // cutting edge faces down (−Y), the way it is held for an incision.
            var tip = SimReadyStatic(b, parts, ModelToInstrument(Vector3.right, Vector3.up));
            b.Pivot("Pivot_Tip", tip);
            b.Info("INST-09", "Scalpel", "Instrument", AssetRelease.MVP, 9700, "NVIDIA SimReady instrument_scalpel_handle_a01 (CC BY 4.0), blade fitted.").isPlaceholder = false;
            b.Grabbable(0.03f, new Vector3(0, 0, tip.z * 0.35f), Vector3.zero, impact: SoundId.INST_PlaceOnTray);
            b.Save(Folder("Instruments"));
            return true;
        }

        /// <summary>Static dressing prop from a SimReady model, standing on y = 0, centred on X/Z, front toward +Z.</summary>
        /// <summary>Model space → prefab space for a floor-standing model: turned by <paramref name="yaw"/>, scaled, standing on y = 0, centred on X/Z.</summary>
        static Matrix4x4 StandModel(List<ModelPart> parts, float yaw = 0f, float scale = 1f)
        {
            var r = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);
            var bounds = PartBounds(parts, r);
            return Matrix4x4.Translate(new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z)) * r;
        }

        /// <summary>Bounds, in the space <paramref name="m"/> maps to, of the parts whose name contains <paramref name="name"/> (all parts if null).</summary>
        static Bounds PartBounds(List<ModelPart> parts, Matrix4x4 m, string name = null)
        {
            var all = parts.Where(p => name == null || p.name.Contains(name)).SelectMany(p => Vertices(p, m)).ToList();
            var bounds = new Bounds(all[0], Vector3.zero);
            foreach (var v in all) bounds.Encapsulate(v);
            return bounds;
        }

        static bool SimReadyProp(string file, string prefab, string id, string display, int tris, bool grabbable = false)
        {
            var parts = SimReadyParts(file);
            if (parts == null) return false;
            var b = new PB(prefab);
            var m = StandModel(parts);
            var model = b.Group("Model", Vector3.zero);
            foreach (var p in parts) PlacePart(p, model, m);
            b.Info(id, display, "Equipment", AssetRelease.MVP, tris, $"NVIDIA SimReady {file} (CC BY 4.0).").isPlaceholder = false;
            if (grabbable) b.Grabbable(0.2f, Vector3.zero, Vector3.zero, impact: SoundId.INST_PlaceOnTray);
            else b.FitCollider();
            b.Save(Folder("Equipment"));
            return true;
        }

        // ───────────── Theatre equipment: same prefab names and working parts as the versions they replace ─────────────

        /// <summary>OR table: long axis X, mattress top at 0.92 m (TableTopSurface), the top under TableTop so calibration can move it.</summary>
        static bool SimReadyORTable()
        {
            var parts = SimReadyParts("table_surgical_a01");
            if (parts == null) return false;
            var b = new PB("EQ_ORTable");
            var probe = StandModel(parts);
            var m = StandModel(parts, 0f, 0.92f / PartBounds(parts, probe, "bed_").max.y); // mattress top → 0.92 m
            var model = b.Group("Model", Vector3.zero);
            var top = b.Group("TableTop", Vector3.zero);
            foreach (var p in parts) PlacePart(p, p.name.StartsWith("bed_") || p.name.StartsWith("spring_") ? top : model, m);
            b.Pivot("TableTopSurface", new Vector3(0, 0.92f, 0));
            var bed = PartBounds(parts, m, "bed_");
            b.Collider(null, new Vector3(0, 0.46f, 0), new Vector3(bed.size.x, 0.92f, bed.size.z));
            b.Info("EQ-01", "OR table", "Equipment", AssetRelease.MVP, 32000,
                "NVIDIA SimReady table_surgical_a01 (CC BY 4.0). Height-adjustable top as separate meshes (calibration, seated mode). Move 'TableTop'.").isPlaceholder = false;
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
            return true;
        }

        /// <summary>Anaesthesia workstation, front (drawers, screens) at +Z, with the animated vitals on its arm monitor.</summary>
        static bool SimReadyAnaesthesiaMachine()
        {
            var parts = SimReadyParts("anesthesia_machine_a01");
            if (parts == null) return false;
            var b = new PB("EQ_AnaesthesiaMachine");
            var m = StandModel(parts, 90f); // the model's front faces −X
            var model = b.Group("Model", Vector3.zero);
            foreach (var p in parts)
            {
                var t = PlacePart(p, model, m);
                // The display glass imports as a flat white pane; dark screen glass instead (vitals go on top).
                if (p.name.StartsWith("screenGlass_") || p.name.StartsWith("baseGlass_"))
                {
                    var r = t.GetComponent<MeshRenderer>();
                    r.sharedMaterials = Enumerable.Repeat(MaterialLibrary.Get("Screen_Off"), r.sharedMaterials.Length).ToArray();
                }
            }
            var screen = PartBounds(parts, m, "screenGlass_02");
            b.Quad("Monitor_Screen", screen.center + new Vector3(0, 0, screen.extents.z + 0.002f), new Vector2(screen.size.x * 0.96f, screen.size.y * 0.94f), "Screen_Vitals", new Vector3(0, 180, 0));
            // The main (portrait) screen: vitals across its upper part, landscape so the traces aren't stretched.
            var main = PartBounds(parts, m, "screenGlass_01");
            var mainSize = new Vector2(main.size.x * 0.92f, main.size.x * 0.6f);
            b.Quad("Monitor_Screen_Main", new Vector3(main.center.x, main.max.y - mainSize.y * 0.5f - 0.02f, main.max.z + 0.002f), mainSize, "Screen_Vitals", new Vector3(0, 180, 0));
            var audio = b.Pivot("AudioSocket", screen.center);
            audio.gameObject.AddComponent<AmbientEmitter>().Sound = SoundId.AMB_AnaesthesiaMonitor_72bpm;
            var body = PartBounds(parts, m, "base_01"); // the cart without its swing arms, which stay walk-through
            b.Collider(null, new Vector3(0, 0.85f, body.center.z), new Vector3(Mathf.Min(body.size.x, 0.9f), 1.7f, Mathf.Min(body.size.z, 0.8f)));
            b.Info("EQ-04", "Anaesthesia machine", "Equipment", AssetRelease.MVP, 60700,
                "NVIDIA SimReady anesthesia_machine_a01 (CC BY 4.0). Background only, no interaction. Vitals on the arm monitor; beeps at 72 bpm.").isPlaceholder = false;
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
            return true;
        }

        /// <summary>Patient monitor on a rolling stand, screen facing −Z, showing the animated vitals.</summary>
        static bool SimReadyPatientMonitor()
        {
            var parts = SimReadyParts("monitor_patient_a01");
            if (parts == null) return false;
            var b = new PB("EQ_PatientMonitor");
            var m = StandModel(parts, -90f); // the model's screen faces −X
            var model = b.Group("Model", Vector3.zero);
            foreach (var p in parts) PlacePart(p, model, m);
            var screen = PartBounds(parts, m, "screen_01");
            b.Quad("Vitals_Screen", screen.center - new Vector3(0, 0, screen.extents.z + 0.002f), new Vector2(screen.size.x * 0.98f, screen.size.y * 0.96f), "Screen_Vitals");
            b.FitColliderFromRenderers();
            b.Info("EQ-X3", "Patient monitor on stand", "Equipment", AssetRelease.MVP, 29700,
                "NVIDIA SimReady monitor_patient_a01 (CC BY 4.0) + procedural vitals screen (SF/UI/Vitals Monitor).").isPlaceholder = false;
            b.Save(Folder("Equipment"));
            return true;
        }

        /// <summary>
        /// Laparoscopic tower, front at +Z: the video_system_a01 cart (a robotic vision cart, so its camera boom, column
        /// camera and second monitor are left off) with one monitor moved onto the column and enlarged 1.5× so the feed
        /// still reads from the table. LapTower_Screen on that monitor takes the laparoscope RenderTexture.
        /// </summary>
        static bool SimReadyLapTower()
        {
            var all = SimReadyParts("video_system_a01");
            if (all == null) return false;
            string[] drop = { "fieldArm", "cameraArm", "camera_", "cameraBody", "cameraGlass", "cameraRotator", "extender", "monitor_02", "rotatorMonitor_02" };
            var kept = all.Where(p => !drop.Any(d => p.name.StartsWith(d))).ToList();
            var monitor = kept.Where(p => p.name.StartsWith("monitor_01") || p.name.StartsWith("rotatorMonitor_01")).ToList();
            var cart = kept.Except(monitor).ToList();

            var b = new PB("EQ_LapTower");
            var m = StandModel(cart, 90f); // the monitor side faces −X
            var pivot = PartBounds(monitor, Matrix4x4.identity, "rotatorMonitor_01").center;
            var mount = m * Matrix4x4.Translate(new Vector3(0f, 1.45f, 0f)) * Matrix4x4.Scale(Vector3.one * 1.5f) * Matrix4x4.Translate(-pivot);
            var model = b.Group("Model", Vector3.zero);
            foreach (var p in cart) PlacePart(p, model, m);
            foreach (var p in monitor) PlacePart(p, model, mount);

            var display = PartBounds(monitor, mount, "monitor_01");
            var face = new Vector3(display.center.x, display.center.y, display.max.z);
            var screen = b.Quad("LapTower_Screen", face + new Vector3(0, 0, 0.002f), new Vector2(display.size.x * 0.9f, display.size.y * 0.82f), "Screen_Laparoscope", new Vector3(0, 180, 0));
            screen.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
            b.Pivot("ScreenOverlayAnchor", face + new Vector3(0, 0, 0.01f));
            var audio = b.Pivot("AudioSocket", new Vector3(0, 0.6f, 0.25f));
            audio.gameObject.AddComponent<AmbientEmitter>().Sound = SoundId.AMB_Insufflator;
            b.FitColliderFromRenderers();
            b.Info("EQ-03", "Laparoscopic tower + monitor", "Equipment", AssetRelease.MVP, 30000,
                "NVIDIA SimReady video_system_a01 (CC BY 4.0), one monitor enlarged. Screen mesh 'LapTower_Screen' takes the laparoscope RenderTexture.").isPlaceholder = false;
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
            return true;
        }

        /// <summary>IV pole with an infusion bag on its hook.</summary>
        static bool SimReadyIVStand()
        {
            var parts = SimReadyParts("IV_pole_a01");
            if (parts == null) return false;
            var b = new PB("EQ_IVStand");
            var m = StandModel(parts);
            var model = b.Group("Model", Vector3.zero);
            foreach (var p in parts) PlacePart(p, model, m);
            var hooks = PartBounds(parts, m, "hooks_");
            var bag = new Vector3(hooks.max.x - 0.03f, hooks.min.y - 0.12f, hooks.center.z); // on the outer hook
            b.Box("IVBag", bag, new Vector3(0.11f, 0.19f, 0.035f), "Hosp_IVBag");
            b.Cyl("IVLine", new Vector3(bag.x, bag.y - 0.5f, bag.z), 0.004f, 0.82f, "Hosp_IVBag");
            b.FitColliderFromRenderers();
            b.Info("EQ-X5", "IV stand with infusion bag", "Equipment", AssetRelease.MVP, 20100,
                "NVIDIA SimReady IV_pole_a01 (CC BY 4.0) with a procedural infusion bag.").isPlaceholder = false;
            b.Save(Folder("Equipment"));
            return true;
        }

        /// <summary>Rebuilds the SimReady-based instruments and props only.</summary>
        [MenuItem(SFPaths.MenuRoot + "Build/1h · SimReady Instruments + Props", priority = 3)]
        public static void BuildSimReady()
        {
            var built = new List<string>();
            if (SimReadyTissueForceps()) built.Add("forceps");
            if (SimReadyScissors()) built.Add("scissors");
            if (SimReadyHemostat()) built.Add("haemostat");
            if (SimReadyRetractor()) built.Add("retractor");
            if (SimReadySuction()) built.Add("suction");
            if (SimReadyScalpel()) built.Add("scalpel");
            if (SimReadyProp("tray_sterilization_instrument_c02", "EQ_SterilizationTray", "EQ-X4", "Sterilisation instrument tray", 33900)) built.Add("tray");
            if (SimReadyProp("trolley_instrument_b01", "EQ_InstrumentTrolley", "EQ-X5", "Instrument trolley", 35900)) built.Add("trolley");
            if (SimReadyProp("needle_surgical_a01", "PROP_NeedleBox", "PROP-X3", "Surgical needle box", 9400, grabbable: true)) built.Add("needle box");
            if (SimReadyProp("drape_surgical_a01", "PPE_DrapePack", "PPE-X1", "Folded surgical drape", 18300, grabbable: true)) built.Add("drape pack");
            if (SimReadyProp("light_surgical_ceiling_arm_b01", "EQ_SurgicalLightArm", "EQ-X6", "Surgical ceiling light (arm)", 55100)) built.Add("ceiling light");
            if (SimReadyORTable()) built.Add("OR table");
            if (SimReadyAnaesthesiaMachine()) built.Add("anaesthesia machine");
            if (SimReadyPatientMonitor()) built.Add("patient monitor");
            if (SimReadyIVStand()) built.Add("IV stand");
            if (SimReadyLapTower()) built.Add("laparoscopic tower");
            AssetDatabase.SaveAssets();
            Debug.Log("[Surgical Foundations] SimReady prefabs built: " + string.Join(", ", built));
        }
    }
}

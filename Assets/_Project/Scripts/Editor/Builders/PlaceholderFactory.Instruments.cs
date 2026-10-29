using SurgicalFoundations.Audio;
using SurgicalFoundations.Placeholders;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Instruments (×11). Convention: long axis +Z, tip at +Z, "Attach" at the handle, "Pivot_Tip" at the working end and
    /// "Pivot_Port" where the shaft meets the port (FR-10 fulcrum; updated at runtime). Jaws are separate hinged meshes.
    /// </summary>
    public static partial class PlaceholderFactory
    {
        const float ShaftLength = 0.33f;
        const float ShaftStart = 0.03f;

        enum JawStyle { Grasper, Maryland, Scissors, ClipApplier }

        /// <summary>Rebuilds only the laparoscopic instruments and trocars; the scenes pick them up through the prefabs.</summary>
        [UnityEditor.MenuItem(SFPaths.MenuRoot + "Build/1f · Laparoscopic Instruments", priority = 3)]
        public static void BuildLapInstruments()
        {
            LapInstrumentSet();
            UnityEditor.AssetDatabase.SaveAssets();
        }

        static void LapInstrumentSet()
        {
            InstrumentTray(); // sized for this set
            Trocar("INST_Trocar12", "INST-01", "Trocar 12 mm", 0.012f, 0.04f, "Trocar_Green");
            Trocar("INST_Trocar5", "INST-02", "Trocar 5 mm", 0.0055f, 0.03f, "Trocar_Blue");
            Laparoscope();
            LapInstrument("INST_AtraumaticGrasper", "INST-04", "Atraumatic grasper", JawStyle.Grasper, "Separate jaw meshes, handle ratchet.");
            LapInstrument("INST_MarylandDissector", "INST-05", "Maryland dissector", JawStyle.Maryland, "Curved jaws, separate meshes.");
            LapInstrument("INST_LapScissors", "INST-06", "Laparoscopic scissors", JawStyle.Scissors, "Separate blades.");
            LapInstrument("INST_ClipApplier", "INST-07", "Clip applier", JawStyle.ClipApplier, "Jaws + loaded clip.");
        }

        static void LapInstrument(string prefabName, string id, string display, JawStyle style, string notes)
        {
            var b = new PB(prefabName);
            bool clipApplier = style == JawStyle.ClipApplier;
            float shaftD = clipApplier ? 0.01f : 0.005f;

            // Modern disposable set (reference sheet): white moulded handle, glossy black rotation knob tapering into the
            // shaft. Hand instruments take an in-line scissor grip with two rings and a cautery post; the clip applier
            // takes a pistol grip with a black trigger.
            var handle = b.Group("Handle", Vector3.zero);
            if (clipApplier)
            {
                b.CapE("Housing", new Vector3(0, -0.006f, -0.035f), new Vector3(0.032f, 0.09f, 0.046f), "Instrument_HandleWhite", PB.AlongZ, handle);
                b.CapE("PistolGrip", new Vector3(0, -0.075f, -0.064f), new Vector3(0.03f, 0.13f, 0.04f), "Instrument_HandleWhite", new Vector3(-15, 0, 0), handle);
                b.CapE("Trigger", new Vector3(0, -0.06f, -0.013f), new Vector3(0.011f, 0.085f, 0.015f), "Instrument_KnobBlack", new Vector3(-26, 0, 0), handle);
                b.Box("Window", new Vector3(0.0161f, -0.004f, -0.04f), new Vector3(0.001f, 0.01f, 0.018f), "Instrument_KnobBlack", default, handle);
                b.Sph("KnobBack", new Vector3(0, 0, 0.004f), 0.034f, "Instrument_KnobBlack", handle);
                b.Frustum("RotationKnob", new Vector3(0, 0, 0.021f), 0.034f, 0.013f, 0.034f, "Instrument_KnobBlack", PB.AlongZ, handle);
            }
            else
            {
                b.CapE("Housing", new Vector3(0, -0.004f, -0.024f), new Vector3(0.024f, 0.064f, 0.03f), "Instrument_HandleWhite", PB.AlongZ, handle);
                b.CapE("Grip", new Vector3(0, -0.046f, -0.031f), new Vector3(0.016f, 0.072f, 0.022f), "Instrument_HandleWhite", new Vector3(-18, 0, 0), handle);
                b.Torus("FingerRing", new Vector3(0, -0.088f, -0.045f), 0.036f, 0.008f, "Instrument_HandleWhite", new Vector3(0, 0, 90), handle);
                b.CapE("ThumbLever", new Vector3(0, -0.04f, -0.063f), new Vector3(0.011f, 0.07f, 0.013f), "Instrument_HandleWhite", new Vector3(24, 0, 0), handle);
                b.Torus("ThumbRing", new Vector3(0, -0.079f, -0.084f), 0.032f, 0.008f, "Instrument_HandleWhite", new Vector3(0, 0, 90), handle);
                b.Box("Ratchet", new Vector3(0, -0.03f, -0.006f), new Vector3(0.004f, 0.02f, 0.004f), "Instrument_Steel", default, handle);
                b.Cyl("CauteryPost", new Vector3(0, 0.017f, -0.034f), 0.004f, 0.018f, "Instrument_Steel", default, handle);
                b.Sph("KnobBack", new Vector3(0, 0, 0.004f), 0.028f, "Instrument_KnobBlack", handle);
                b.Frustum("RotationKnob", new Vector3(0, 0, 0.019f), 0.028f, 0.008f, 0.03f, "Instrument_KnobBlack", PB.AlongZ, handle);
            }

            b.Cyl("Shaft", new Vector3(0, 0, ShaftStart + ShaftLength / 2), shaftD, ShaftLength, clipApplier ? "Instrument_Steel" : "Instrument_Insulation", PB.AlongZ);

            float tipZ = ShaftStart + ShaftLength;
            b.Cyl("TipCollar", new Vector3(0, 0, tipZ - 0.005f), shaftD * 1.06f, 0.01f, clipApplier ? "Instrument_KnobBlack" : "Instrument_Steel", PB.AlongZ);
            var hinge = b.Group("JawHinge", new Vector3(0, 0, tipZ));
            var upper = b.Group("Jaw_Upper", Vector3.zero, default, hinge);
            var lower = b.Group("Jaw_Lower", Vector3.zero, default, hinge);
            float jawLen = style == JawStyle.Scissors ? 0.018f : 0.02f;
            string jawMat = "Instrument_Steel";
            switch (style)
            {
                case JawStyle.Maryland:
                    // Curved jaws: two segments bending toward +X
                    b.Box("Seg1", new Vector3(0, 0.0011f, 0.006f), new Vector3(0.0042f, 0.0022f, 0.012f), jawMat, default, upper);
                    b.Box("Seg2", new Vector3(0.0018f, 0.0011f, 0.0155f), new Vector3(0.0036f, 0.0022f, 0.009f), jawMat, new Vector3(0, 25, 0), upper);
                    b.Box("Seg1", new Vector3(0, -0.0011f, 0.006f), new Vector3(0.0042f, 0.0022f, 0.012f), jawMat, default, lower);
                    b.Box("Seg2", new Vector3(0.0018f, -0.0011f, 0.0155f), new Vector3(0.0036f, 0.0022f, 0.009f), jawMat, new Vector3(0, 25, 0), lower);
                    break;
                case JawStyle.Scissors:
                    b.Box("Blade", new Vector3(0, 0.0008f, jawLen / 2), new Vector3(0.0045f, 0.0012f, jawLen), jawMat, new Vector3(0, 0, 8), upper);
                    b.Box("Blade", new Vector3(0, -0.0008f, jawLen / 2), new Vector3(0.0045f, 0.0012f, jawLen), jawMat, new Vector3(0, 0, -8), lower);
                    break;
                case JawStyle.ClipApplier:
                    b.Box("Jaw", new Vector3(0, 0.0022f, 0.011f), new Vector3(0.008f, 0.003f, 0.022f), jawMat, default, upper);
                    b.Box("Jaw", new Vector3(0, -0.0022f, 0.011f), new Vector3(0.008f, 0.003f, 0.022f), jawMat, default, lower);
                    var clip = b.Group("LoadedClip", new Vector3(0, 0, 0.012f), default, hinge);
                    b.Box("Leg_A", new Vector3(0, 0.0012f, 0), new Vector3(0.0012f, 0.0012f, 0.009f), "Instrument_Titanium", default, clip);
                    b.Box("Leg_B", new Vector3(0, -0.0012f, 0), new Vector3(0.0012f, 0.0012f, 0.009f), "Instrument_Titanium", default, clip);
                    break;
                default: // atraumatic grasper: fenestrated flat jaws
                    b.Box("Jaw", new Vector3(0, 0.0011f, jawLen / 2), new Vector3(0.0045f, 0.0022f, jawLen), jawMat, default, upper);
                    b.Box("Jaw", new Vector3(0, -0.0011f, jawLen / 2), new Vector3(0.0045f, 0.0022f, jawLen), jawMat, default, lower);
                    break;
            }

            b.Pivot("Pivot_Tip", new Vector3(0, 0, tipZ + jawLen));
            b.Pivot("Pivot_Port", new Vector3(0, 0, ShaftStart + ShaftLength * 0.55f));
            b.Info(id, display, "Instrument", AssetRelease.MVP, 4000, notes);
            b.Grabbable(0.25f, clipApplier ? new Vector3(0, -0.07f, -0.06f) : new Vector3(0, -0.055f, -0.03f), Vector3.zero, jaws: true);
            b.Save(Folder("Instruments"));
        }

        /// <summary>
        /// Threaded, colour-coded trocar (reference sheet): grey funnel head with a coloured seal ring, side stopcock,
        /// threaded cannula in the size colour, pointed obturator. <paramref name="colour"/> is a Trocar_* material.
        /// </summary>
        static void Trocar(string prefabName, string id, string display, float cannulaD, float housingD, string colour)
        {
            var b = new PB(prefabName);
            b.Frustum("Housing", new Vector3(0, 0, -0.018f), housingD * 1.35f, housingD * 0.5f, 0.04f, "Trocar_Head", PB.AlongZ);
            b.Cyl("Collar", new Vector3(0, 0, 0.008f), housingD * 0.5f, 0.016f, "Trocar_Head", PB.AlongZ);
            b.Torus("ColourRing", new Vector3(0, 0, -0.038f), housingD * 1.42f, 0.006f, colour, new Vector3(90, 0, 0));
            b.Cyl("ValveCap", new Vector3(0, 0, -0.039f), housingD * 1.25f, 0.003f, colour, PB.AlongZ);
            b.Cyl("SidePort", new Vector3(housingD * 0.36f, 0, 0.006f), 0.006f, 0.024f, "Instrument_Steel", PB.AlongX);
            b.Box("Stopcock", new Vector3(housingD * 0.36f + 0.012f, 0.006f, 0.006f), new Vector3(0.004f, 0.014f, 0.004f), "Instrument_Steel");
            b.Cyl("Cannula", new Vector3(0, 0, 0.0725f), cannulaD, 0.1f, colour, PB.AlongZ);
            for (int k = 0; k < 8; k++)
                b.Torus($"Thread_{k}", new Vector3(0, 0, 0.035f + k * 0.0085f), cannulaD * 1.4f, cannulaD * 0.3f, colour, new Vector3(90, 0, 0));
            var obt = b.Group("Obturator", Vector3.zero);
            b.Sph("Knob", new Vector3(0, 0, -0.043f), housingD * 0.5f, colour, obt);
            b.Cyl("Shaft", new Vector3(0, 0, 0.06f), cannulaD * 0.85f, 0.12f, "Instrument_Steel", PB.AlongZ, obt);
            b.Frustum("Tip", new Vector3(0, 0, 0.1325f), cannulaD * 0.9f, 0f, 0.025f, "Instrument_Steel", PB.AlongZ, obt);
            b.Pivot("Pivot_Tip", new Vector3(0, 0, 0.13f));
            b.Pivot("Pivot_Port", new Vector3(0, 0, 0.09f));
            b.Info(id, display, "Instrument", AssetRelease.MVP, 3000, "Cannula + obturator as 2 parts; valve cap. Obturator withdraws on port placement.");
            b.Grabbable(0.08f, new Vector3(0, 0, -0.03f), Vector3.zero, grabSound: SoundId.INST_TrocarValve);
            b.Save(Folder("Instruments"));
        }

        static void Laparoscope()
        {
            var b = new PB("INST_Laparoscope30");
            // Rigid scope (reference sheet): steel body with turned rings, black eyepiece cup, light post under the body.
            b.Frustum("Eyepiece", new Vector3(0, 0, -0.046f), 0.052f, 0.022f, 0.028f, "Instrument_KnobBlack", PB.AlongZ);
            b.Torus("EyepieceRim", new Vector3(0, 0, -0.06f), 0.054f, 0.005f, "Instrument_KnobBlack", new Vector3(90, 0, 0));
            b.Cyl("Coupler", new Vector3(0, 0, -0.022f), 0.026f, 0.022f, "Instrument_Steel", PB.AlongZ);
            b.Cyl("Body", new Vector3(0, 0, 0.0f), 0.02f, 0.03f, "Stainless_Satin", PB.AlongZ);
            b.Cyl("BodyRing", new Vector3(0, 0, 0.012f), 0.024f, 0.005f, "Instrument_Steel", PB.AlongZ);
            b.Cyl("LightPost", new Vector3(0, -0.02f, 0.002f), 0.008f, 0.03f, "Instrument_Steel");
            b.Cyl("LightConnector", new Vector3(0, -0.038f, 0.002f), 0.011f, 0.012f, "Instrument_Steel");
            b.Cyl("Shaft", new Vector3(0, 0, 0.018f + ShaftLength / 2), 0.01f, ShaftLength, "Instrument_Steel", PB.AlongZ);
            float tip = 0.018f + ShaftLength;
            b.Cyl("Lens", new Vector3(0, 0, tip), 0.008f, 0.001f, "Screen_Off", new Vector3(120, 0, 0));
            b.Pivot("Pivot_Tip", new Vector3(0, 0, tip));
            b.Pivot("Pivot_Port", new Vector3(0, 0, 0.2f));
            b.Pivot("CameraSocket", new Vector3(0, 0, tip), new Vector3(30, 0, 0));
            b.Info("INST-03", "Laparoscope 30°", "Instrument", AssetRelease.MVP, 4000, "Scope, camera head, light cable; camera socket at tip, 30° offset.");
            b.Grabbable(0.3f, new Vector3(0, 0, -0.01f), Vector3.zero);
            b.Save(Folder("Instruments"));
        }

        static void SurgicalClip()
        {
            var b = new PB("INST_SurgicalClip");
            b.Box("Crown", new Vector3(0, 0, 0), new Vector3(0.0012f, 0.004f, 0.0012f), "Instrument_Titanium");
            b.Box("Leg_A", new Vector3(0, 0.0015f, 0.0045f), new Vector3(0.0012f, 0.0012f, 0.009f), "Instrument_Titanium", new Vector3(-6, 0, 0));
            b.Box("Leg_B", new Vector3(0, -0.0015f, 0.0045f), new Vector3(0.0012f, 0.0012f, 0.009f), "Instrument_Titanium", new Vector3(6, 0, 0));
            b.Info("INST-08", "Surgical clip", "Instrument", AssetRelease.MVP, 200, "Tiny; GPU-instanced.");
            b.Save(Folder("Instruments"));
        }

        static void Scalpel()
        {
            if (SimReadyScalpel()) return; // NVIDIA SimReady handle when it is imported
            var b = new PB("INST_Scalpel");
            b.Box("Handle", new Vector3(0, 0, 0.065f), new Vector3(0.009f, 0.003f, 0.13f), "Instrument_Steel");
            b.Box("Grip", new Vector3(0, 0, 0.05f), new Vector3(0.0092f, 0.0032f, 0.05f), "Stainless_Satin");
            b.Box("Blade", new Vector3(0, 0.001f, 0.147f), new Vector3(0.0005f, 0.007f, 0.034f), "Instrument_Steel", new Vector3(0, 0, 90));
            b.Pivot("Pivot_Tip", new Vector3(0, -0.002f, 0.165f));
            b.Info("INST-09", "Scalpel", "Instrument", AssetRelease.MVP, 1000, "Skin incision at port sites.");
            b.Grabbable(0.03f, new Vector3(0, 0, 0.05f), Vector3.zero, impact: SoundId.INST_PlaceOnTray);
            b.Save(Folder("Instruments"));
        }

        static void NeedleHolder()
        {
            var b = new PB("INST_NeedleHolder");
            b.Cyl("Ring_L", new Vector3(-0.018f, 0, -0.01f), 0.026f, 0.006f, "Instrument_Steel");
            b.Cyl("Ring_R", new Vector3(0.018f, 0, -0.01f), 0.026f, 0.006f, "Instrument_Steel");
            b.Box("Shank_L", new Vector3(-0.007f, 0, 0.05f), new Vector3(0.004f, 0.005f, 0.11f), "Instrument_Steel", new Vector3(0, 10, 0));
            b.Box("Shank_R", new Vector3(0.007f, 0, 0.05f), new Vector3(0.004f, 0.005f, 0.11f), "Instrument_Steel", new Vector3(0, -10, 0));
            b.Box("BoxLock", new Vector3(0, 0, 0.105f), new Vector3(0.009f, 0.007f, 0.012f), "Instrument_Steel");
            var jaws = b.Group("JawHinge", new Vector3(0, 0, 0.11f));
            var up = b.Group("Jaw_Upper", Vector3.zero, default, jaws);
            var lo = b.Group("Jaw_Lower", Vector3.zero, default, jaws);
            b.Box("Jaw", new Vector3(0, 0.001f, 0.014f), new Vector3(0.005f, 0.002f, 0.028f), "Instrument_Titanium", default, up);
            b.Box("Jaw", new Vector3(0, -0.001f, 0.014f), new Vector3(0.005f, 0.002f, 0.028f), "Instrument_Titanium", default, lo);
            var needle = b.Group("Needle", new Vector3(0, 0, 0.135f));
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Lerp(-80f, 80f, i / 5f) * Mathf.Deg2Rad;
                b.Cyl($"Seg_{i}", new Vector3(Mathf.Sin(a) * 0.009f, -Mathf.Cos(a) * 0.009f + 0.009f, 0), 0.0011f, 0.0055f, "Instrument_Steel", new Vector3(0, 0, a * Mathf.Rad2Deg + 90), needle);
            }
            b.Cyl("Suture", new Vector3(0.009f, -0.14f, 0.135f), 0.0008f, 0.28f, "Suture_Violet");
            b.Info("INST-10", "Needle holder + suture", "Instrument", AssetRelease.MVP, 3000, "Port-site closure; suture as spline mesh.");
            b.Grabbable(0.06f, new Vector3(0, 0, -0.005f), Vector3.zero, jaws: true);
            b.Save(Folder("Instruments"));
        }

        static void Swab()
        {
            var b = new PB("INST_Swab");
            b.Box("Gauze", Vector3.zero, new Vector3(0.1f, 0.006f, 0.1f), "Swab_White");
            b.Box("XRayStripe", new Vector3(0.03f, 0, 0), new Vector3(0.008f, 0.0064f, 0.1f), "Swab_Stripe");
            b.Info("INST-11", "Swab / gauze", "Instrument", AssetRelease.MVP, 1000, "Cloth-sim version plus a folded static version; hideable (US-CLS-05). This is the folded static version.");
            b.Grabbable(0.01f, Vector3.zero, Vector3.zero, grabSound: SoundId.ENV_DrapeRustle, impact: SoundId.ENV_SwabDrop);
            b.Save(Folder("Instruments"));
        }
    }
}

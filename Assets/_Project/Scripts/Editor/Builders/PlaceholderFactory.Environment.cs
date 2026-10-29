using System.Linq;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Lighting;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>Environment and equipment placeholders (asset list: Environment ×4, Equipment ×9).</summary>
    public static partial class PlaceholderFactory
    {
        static string Folder(string category) => $"{SFPaths.Prefabs}/{category}";

        // ───────────────────────────── Environment ─────────────────────────────

        /// <summary>7 × 3 × 7 m theatre. Origin = floor centre (table goes here). Door + window in the south wall lead to the scrub alcove.</summary>
        static void OperatingTheatre()
        {
            const float W = 7f, H = 3f, D = 7f, T = 0.12f;
            var b = new PB("ENV_OperatingTheatre");
            var shell = b.Group("Shell", Vector3.zero);
            b.Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(W + 2 * T, 0.1f, D + 2 * T), "Floor_Epoxy", default, shell);
            b.Box("Ceiling", new Vector3(0, H + 0.05f, 0), new Vector3(W + 2 * T, 0.1f, D + 2 * T), "Ceiling", default, shell);
            b.Box("Wall_East", new Vector3(W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), "Wall_OR", default, shell);
            b.Box("Wall_West", new Vector3(-W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), "Wall_OR", default, shell);
            b.Box("Wall_North", new Vector3(0, H / 2, D / 2 + T / 2), new Vector3(W + 2 * T, H, T), "Wall_OR", default, shell);

            // South wall: solid | door to scrub alcove | pier | observation window | solid
            float zS = -D / 2 - T / 2;
            void South(string n, float x0, float x1, float y0, float y1) =>
                b.Box(n, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, zS), new Vector3(x1 - x0, y1 - y0, T), "Wall_OR", default, shell);
            South("Wall_South_A", -W / 2 - T, -2.9f, 0, H);
            South("Wall_South_DoorHead", -2.9f, -1.6f, 2.2f, H);
            South("Wall_South_Pier", -1.6f, -1.4f, 0, H);
            South("Wall_South_Sill", -1.4f, -0.6f, 0, 1.0f);
            South("Wall_South_WindowHead", -1.4f, -0.6f, 2.0f, H);
            South("Wall_South_B", -0.6f, W / 2 + T, 0, H);
            b.Box("ObservationGlass", new Vector3(-1.0f, 1.5f, zS), new Vector3(0.8f, 1.0f, 0.012f), "Glass", default, shell);

            // Skirting (hygienic coved base)
            b.Box("Skirting_N", new Vector3(0, 0.05f, D / 2 - 0.01f), new Vector3(W, 0.1f, 0.02f), "Wall_Skirting", default, shell);
            b.Box("Skirting_E", new Vector3(W / 2 - 0.01f, 0.05f, 0), new Vector3(0.02f, 0.1f, D), "Wall_Skirting", default, shell);
            b.Box("Skirting_W", new Vector3(-W / 2 + 0.01f, 0.05f, 0), new Vector3(0.02f, 0.1f, D), "Wall_Skirting", default, shell);

            // Laminar-flow ceiling canopy over the table + perimeter panels (all baked emissive sources)
            var ceiling = b.Group("CeilingLights", Vector3.zero);
            b.Box("LaminarFrame", new Vector3(0, H - 0.02f, 0), new Vector3(3.3f, 0.04f, 3.3f), "Polymer_White", default, ceiling);
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                b.Box($"LaminarPanel_{(ix < 0 ? "W" : "E")}{(iz < 0 ? "S" : "N")}", new Vector3(ix * 0.78f, H - 0.045f, iz * 0.78f), new Vector3(1.45f, 0.012f, 1.45f), "Light_CeilingPanel", default, ceiling);
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
                b.Box($"Panel_{(ix < 0 ? "W" : "E")}{(iz < 0 ? "S" : "N")}", new Vector3(ix * 2.5f, H - 0.012f, iz * 2.5f), new Vector3(1.2f, 0.012f, 0.6f), "Light_CeilingPanel", default, ceiling);

            // Fixtures
            var fx = b.Group("Fixtures", Vector3.zero);
            b.Box("Cabinet_Body", new Vector3(1.4f, 1.15f, D / 2 - 0.2f), new Vector3(2.4f, 2.0f, 0.4f), "Cabinet_White", default, fx);
            b.Box("Cabinet_Glass", new Vector3(1.4f, 1.35f, D / 2 - 0.405f), new Vector3(2.3f, 1.3f, 0.01f), "Glass", default, fx);
            b.Box("Cabinet_Plinth", new Vector3(1.4f, 0.075f, D / 2 - 0.2f), new Vector3(2.4f, 0.15f, 0.36f), "Wall_Skirting", default, fx);
            b.Box("Door_Main_Frame", new Vector3(-W / 2 + 0.02f, 1.15f, 1.8f), new Vector3(0.04f, 2.3f, 1.8f), "Stainless_Satin", default, fx);
            b.Box("Door_Main_Leaf", new Vector3(-W / 2 + 0.05f, 1.1f, 1.8f), new Vector3(0.04f, 2.2f, 1.6f), "Door_Steel", default, fx);
            b.Box("Door_Main_Window", new Vector3(-W / 2 + 0.075f, 1.55f, 1.8f), new Vector3(0.01f, 0.4f, 0.3f), "Glass", default, fx);
            b.Box("WallMonitor_PACS", new Vector3(W / 2 - 0.04f, 1.7f, -0.8f), new Vector3(0.06f, 0.7f, 1.2f), "Polymer_Dark", default, fx);
            b.Box("WallMonitor_Screen", new Vector3(W / 2 - 0.075f, 1.7f, -0.8f), new Vector3(0.01f, 0.62f, 1.1f), "Screen_Off", default, fx);
            b.Box("GasPanel", new Vector3(-W / 2 + 0.03f, 1.4f, -1.2f), new Vector3(0.06f, 0.35f, 0.6f), "Polymer_White", default, fx);
            for (int i = 0; i < 4; i++)
                b.Cyl($"GasOutlet_{i}", new Vector3(-W / 2 + 0.07f, 1.4f, -1.4f + i * 0.13f), 0.045f, 0.03f, i % 2 == 0 ? "Indicator_Mint" : "Polymer_Grey", PB.AlongX, fx);

            // Gameplay colliders (floor carries the teleport area)
            var col = b.Pivot("Colliders", Vector3.zero);
            b.Collider("Floor", new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), false, col);
            b.Collider("Wall_East", new Vector3(W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), false, col);
            b.Collider("Wall_West", new Vector3(-W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), false, col);
            b.Collider("Wall_North", new Vector3(0, H / 2, D / 2 + T / 2), new Vector3(W, H, T), false, col);
            b.Collider("Wall_South_A", new Vector3((-W / 2 - 2.9f) / 2, H / 2, zS), new Vector3(W / 2 - 2.9f, H, T), false, col);
            b.Collider("Wall_South_B", new Vector3((W / 2 - 1.6f) / 2, H / 2, zS), new Vector3(W / 2 + 1.6f, H, T), false, col);
            b.Collider("Cabinet", new Vector3(1.4f, 1.15f, D / 2 - 0.2f), new Vector3(2.4f, 2.0f, 0.4f), false, col);

            b.Info("ENV-01", "Operating theatre room", "Environment", AssetRelease.MVP, 30000,
                "Walls, floor, ceiling, doors; baked lighting; 2 LODs. South wall opens to the scrub alcove (door + observation window).");
            b.MakeStatic(true);
            b.Save(Folder("Environment"));
        }

        /// <summary>3 × 3 × 2 m alcove behind the south wall. Origin = floor centre; open side faces +Z (into the theatre).</summary>
        static void ScrubAlcove()
        {
            const float W = 3f, H = 3f, D = 2f, T = 0.12f;
            var b = new PB("ENV_ScrubAlcove");
            b.Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), "Floor_Epoxy");
            b.Box("Ceiling", new Vector3(0, H + 0.05f, 0), new Vector3(W + 2 * T, 0.1f, D), "Ceiling");
            b.Box("Wall_Back", new Vector3(0, H / 2, -D / 2 - T / 2), new Vector3(W + 2 * T, H, T), "Wall_OR");
            b.Box("Wall_Left", new Vector3(-W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), "Wall_OR");
            b.Box("Wall_Right", new Vector3(W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), "Wall_OR");
            b.Box("Skirting_Back", new Vector3(0, 0.05f, -D / 2 + 0.01f), new Vector3(W, 0.1f, 0.02f), "Wall_Skirting");
            b.Box("CeilingPanel", new Vector3(0, H - 0.012f, -0.2f), new Vector3(1.2f, 0.012f, 0.6f), "Light_CeilingPanel");
            b.Box("TowelDispenser", new Vector3(-W / 2 + 0.06f, 1.3f, -0.3f), new Vector3(0.1f, 0.35f, 0.3f), "Polymer_White");

            var col = b.Pivot("Colliders", Vector3.zero);
            b.Collider("Floor", new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), false, col);
            b.Collider("Wall_Back", new Vector3(0, H / 2, -D / 2 - T / 2), new Vector3(W, H, T), false, col);
            b.Collider("Wall_Left", new Vector3(-W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), false, col);
            b.Collider("Wall_Right", new Vector3(W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), false, col);

            b.Info("ENV-02", "Scrub room alcove", "Environment", AssetRelease.MVP, 10000, "Attached to theatre, window into OR.");
            b.MakeStatic(true);
            b.Save(Folder("Environment"));
        }

        /// <summary>Calm octagonal lobby (r = 5 m) in the UI palette: dark teal walls, grid floor, mint cove light.</summary>
        static void Lobby()
        {
            const float R = 5f, H = 4f;
            var b = new PB("ENV_Lobby");
            b.Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(2 * R + 1, 0.1f, 2 * R + 1), "Lobby_Floor");
            b.Box("Ceiling", new Vector3(0, H + 0.05f, 0), new Vector3(2 * R + 1, 0.1f, 2 * R + 1), "Lobby_Wall");
            b.CylE("Rug", new Vector3(0, 0.004f, 0), new Vector3(3.4f, 0.008f, 3.4f), "Lobby_Rug");
            float seg = 2f * R * Mathf.Tan(Mathf.PI / 8f) + 0.02f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var wall = b.Group($"Wall_{i}", dir * R, new Vector3(0, a, 0));
                b.Box("Panel", new Vector3(0, H / 2, 0.06f), new Vector3(seg, H, 0.12f), "Lobby_Wall", default, wall);
                b.Box("Cove_Top", new Vector3(0, H - 0.35f, -0.03f), new Vector3(seg - 0.2f, 0.03f, 0.03f), "Light_CovePanel", default, wall);
                b.Box("Cove_Floor", new Vector3(0, 0.08f, -0.03f), new Vector3(seg - 0.2f, 0.02f, 0.03f), "Light_CovePanel", default, wall);
            }
            b.Box("FeatureWall", new Vector3(0, 1.9f, R - 0.05f), new Vector3(3.8f, 2.4f, 0.04f), "Lobby_Rug");

            var col = b.Pivot("Colliders", Vector3.zero);
            b.Collider("Floor", new Vector3(0, -0.05f, 0), new Vector3(2 * R, 0.1f, 2 * R), false, col);
            for (int i = 0; i < 8; i++)
            {
                var p = b.Pivot($"Wall_{i}", Quaternion.Euler(0, i * 45f, 0) * Vector3.forward * R, new Vector3(0, i * 45f, 0), col);
                var c = p.gameObject.AddComponent<BoxCollider>();
                c.center = new Vector3(0, H / 2, 0.06f);
                c.size = new Vector3(seg, H, 0.12f);
            }

            b.Info("ENV-03", "Lobby room", "Environment", AssetRelease.MVP, 15000, "Calm neutral space, one wall for panels.");
            b.MakeStatic(true);
            b.Save(Folder("Environment"));
        }

        static void SkillsLab()
        {
            const float W = 6f, H = 3f, D = 6f, T = 0.12f;
            var b = new PB("ENV_SkillsLab");
            b.Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(W + 2 * T, 0.1f, D + 2 * T), "Floor_Epoxy");
            b.Box("Ceiling", new Vector3(0, H + 0.05f, 0), new Vector3(W + 2 * T, 0.1f, D + 2 * T), "Ceiling");
            b.Box("Wall_North", new Vector3(0, H / 2, D / 2 + T / 2), new Vector3(W + 2 * T, H, T), "Lab_Wall");
            b.Box("Wall_South", new Vector3(0, H / 2, -D / 2 - T / 2), new Vector3(W + 2 * T, H, T), "Lab_Wall");
            b.Box("Wall_East", new Vector3(W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), "Lab_Wall");
            b.Box("Wall_West", new Vector3(-W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), "Lab_Wall");
            b.Box("CeilingPanel_A", new Vector3(0, H - 0.012f, 1.0f), new Vector3(1.2f, 0.012f, 0.6f), "Light_CeilingPanel");
            b.Box("CeilingPanel_B", new Vector3(0, H - 0.012f, -1.2f), new Vector3(1.2f, 0.012f, 0.6f), "Light_CeilingPanel");
            b.Box("Pegboard", new Vector3(0, 1.65f, D / 2 - 0.02f), new Vector3(2.6f, 1.0f, 0.02f), "Lab_Pegboard");
            b.Box("Shelf", new Vector3(0, 2.25f, D / 2 - 0.15f), new Vector3(2.6f, 0.03f, 0.3f), "Lab_Bench");
            b.Box("Door", new Vector3(-W / 2 + 0.03f, 1.1f, -1.5f), new Vector3(0.04f, 2.2f, 1.0f), "Door_Steel");
            // Height-adjustable training table (calibration moves "TableTop"; 92 cm default like the OR table)
            var table = b.Group("TrainingTable", new Vector3(0, 0, 0.6f));
            b.Box("Base", new Vector3(0, 0.03f, 0), new Vector3(1.3f, 0.06f, 0.6f), "Stainless_Satin", default, table);
            b.Box("Column_L", new Vector3(-0.55f, 0.44f, 0), new Vector3(0.08f, 0.8f, 0.08f), "Stainless_Satin", default, table);
            b.Box("Column_R", new Vector3(0.55f, 0.44f, 0), new Vector3(0.08f, 0.8f, 0.08f), "Stainless_Satin", default, table);
            var top = b.Group("TableTop", Vector3.zero, default, table);
            b.Box("Top", new Vector3(0, 0.9f, 0), new Vector3(1.4f, 0.04f, 0.7f), "Lab_Bench", default, top);
            b.Box("Edge", new Vector3(0, 0.9f, -0.352f), new Vector3(1.4f, 0.045f, 0.01f), "Stainless_Brushed", default, top);

            var col = b.Pivot("Colliders", Vector3.zero);
            b.Collider("Floor", new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), false, col);
            b.Collider("Wall_North", new Vector3(0, H / 2, D / 2 + T / 2), new Vector3(W, H, T), false, col);
            b.Collider("Wall_South", new Vector3(0, H / 2, -D / 2 - T / 2), new Vector3(W, H, T), false, col);
            b.Collider("Wall_East", new Vector3(W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), false, col);
            b.Collider("Wall_West", new Vector3(-W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), false, col);
            b.Collider("TrainingTable", new Vector3(0, 0.46f, 0.6f), new Vector3(1.4f, 0.92f, 0.7f), false, col);

            b.Info("ENV-04", "Skills lab room", "Environment", AssetRelease.MVP, 12000, "Simple bench room for calibration and tutorial.");
            b.MakeStatic(true);
            b.Save(Folder("Environment"));
        }

        // ───────────────────────────── Equipment ─────────────────────────────

        /// <summary>Origin = floor under table centre; long axis X, head end at −X. Mattress top at 0.92 m (calibration default).</summary>
        static void ORTable()
        {
            if (SimReadyORTable()) return;
            var b = new PB("EQ_ORTable");
            b.Box("Base", new Vector3(0, 0.05f, 0), new Vector3(0.75f, 0.1f, 0.48f), "Stainless_Satin");
            b.Box("Column", new Vector3(0, 0.42f, 0), new Vector3(0.3f, 0.64f, 0.22f), "Polymer_White");
            var top = b.Group("TableTop", Vector3.zero);
            b.Box("Frame", new Vector3(0, 0.82f, 0), new Vector3(2.0f, 0.05f, 0.5f), "Stainless_Brushed", default, top);
            b.Box("Mattress", new Vector3(0, 0.88f, 0), new Vector3(1.98f, 0.08f, 0.52f), "Mattress_Black", default, top);
            b.Box("Headrest", new Vector3(-1.13f, 0.87f, 0), new Vector3(0.26f, 0.06f, 0.3f), "Mattress_Black", default, top);
            for (int s = -1; s <= 1; s += 2)
            {
                b.Box($"Rail_{(s < 0 ? "S" : "N")}", new Vector3(0, 0.83f, s * 0.27f), new Vector3(1.8f, 0.03f, 0.015f), "Stainless_Brushed", default, top);
                b.Box($"ArmBoard_{(s < 0 ? "S" : "N")}", new Vector3(-0.55f, 0.88f, s * 0.58f), new Vector3(0.16f, 0.05f, 0.62f), "Mattress_Black", default, top);
            }
            b.Pivot("TableTopSurface", new Vector3(0, 0.92f, 0));
            b.Collider(null, new Vector3(0, 0.5f, 0), new Vector3(2.0f, 0.92f, 0.52f));
            b.Info("EQ-01", "OR table", "Equipment", AssetRelease.MVP, 8000, "Height-adjustable top as separate mesh (calibration, seated mode). Move 'TableTop'.");
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
        }

        /// <summary>Ceiling-mounted twin heads. Origin = floor below the mount, placed at the table centre. Each head carries a mixed spot light.</summary>
        static void SurgicalLights()
        {
            var b = new PB("EQ_SurgicalLights");
            b.Cyl("Canopy", new Vector3(0, 2.97f, 0), 0.36f, 0.06f, "Polymer_White");
            b.Cyl("Drop", new Vector3(0, 2.72f, 0), 0.08f, 0.46f, "Polymer_White");
            var target = new Vector3(0, 0.95f, 0);

            void Head(string id, float side, float z, bool shadows)
            {
                float armEnd = side * 0.85f;
                b.Cyl($"Arm_{id}", new Vector3(armEnd / 2, 2.5f, z / 2), 0.06f, Mathf.Sqrt(armEnd * armEnd + z * z), "Polymer_White",
                    new Vector3(0, -Mathf.Atan2(z, armEnd) * Mathf.Rad2Deg, 90));
                b.Cyl($"Yoke_{id}", new Vector3(armEnd, 2.42f, z), 0.05f, 0.16f, "Polymer_White");
                var headPos = new Vector3(armEnd * 0.9f, 2.3f, z * 0.9f); // ~1.1 m above the field, clear of the learner's head
                var look = Quaternion.LookRotation(target - headPos, Vector3.up);
                // Head disc faces its local −Y toward the table: build a rotation whose −Y = look direction.
                var headRot = Quaternion.FromToRotation(Vector3.down, look * Vector3.forward);
                var head = b.Group($"Head_{id}", headPos, headRot.eulerAngles);
                b.CylE("Housing", Vector3.zero, new Vector3(0.64f, 0.12f, 0.64f), "Polymer_White", default, head);
                var lens = b.Cyl("Lens", new Vector3(0, -0.062f, 0), 0.52f, 0.008f, "Light_Lens", default, head);
                lens.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                b.Cyl("SterileHandle", new Vector3(0, -0.12f, 0), 0.04f, 0.11f, "Polymer_Grey", default, head);

                var lightPivot = b.Pivot($"Light_{id}", headPos + (look * Vector3.forward) * 0.08f, look.eulerAngles);
                var l = lightPivot.gameObject.AddComponent<Light>();
                l.type = LightType.Spot;
                l.lightmapBakeType = LightmapBakeType.Mixed;
                l.spotAngle = 38f;
                l.innerSpotAngle = 20f;
                l.range = 3.5f;
                l.intensity = 5f; // 7 washed the drapes out on the headset, which runs without tonemapping
                l.useColorTemperature = true;
                l.colorTemperature = 4500f;
                l.color = Color.white;
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                l.shadowStrength = 0.85f;
                l.shadowBias = 0.02f;
                l.shadowNormalBias = 0.3f;
                var sl = lightPivot.gameObject.AddComponent<SurgicalLight>();
                Ser.Set(sl, "spot", l);
                Ser.Set(sl, "lens", lens.GetComponent<MeshRenderer>());
            }

            Head("A", -1f, -0.25f, true);
            Head("B", 1f, 0.3f, false);
            var hum = b.Pivot("AudioSocket", new Vector3(0, 2.3f, 0));
            var em = hum.gameObject.AddComponent<AmbientEmitter>();
            em.Sound = SoundId.AMB_SurgicalLight_Hum;

            b.Info("EQ-02", "Surgical ceiling lights", "Equipment", AssetRelease.MVP, 6000,
                "2 heads on articulated arms. Head A casts soft shadows (one shadowed spot keeps Quest cost bounded).");
            b.Save(Folder("Equipment"));
        }

        static void LapTower()
        {
            if (SimReadyLapTower()) return;
            var b = new PB("EQ_LapTower");
            b.Box("Base", new Vector3(0, 0.1f, 0), new Vector3(0.62f, 0.06f, 0.56f), "Polymer_Grey");
            for (int i = 0; i < 4; i++)
                b.Sph($"Caster_{i}", new Vector3((i % 2 == 0 ? -1 : 1) * 0.26f, 0.035f, (i < 2 ? -1 : 1) * 0.23f), 0.07f, "Rubber_Black");
            b.Box("Column", new Vector3(0, 0.72f, 0), new Vector3(0.5f, 1.15f, 0.46f), "Polymer_Grey");
            string[] units = { "Insufflator", "LightSource", "CameraUnit" };
            for (int i = 0; i < units.Length; i++)
            {
                float y = 0.45f + i * 0.22f;
                b.Box($"{units[i]}_Front", new Vector3(0, y, 0.235f), new Vector3(0.46f, 0.16f, 0.02f), "Polymer_Dark");
                b.Box($"{units[i]}_Display", new Vector3(-0.12f, y + 0.02f, 0.247f), new Vector3(0.12f, 0.05f, 0.004f), i == 0 ? "Indicator_Amber" : "Indicator_Mint");
                b.Cyl($"{units[i]}_Knob", new Vector3(0.14f, y, 0.25f), 0.03f, 0.015f, "Stainless_Satin", PB.AlongZ);
            }
            b.Cyl("MonitorPole", new Vector3(0, 1.43f, -0.05f), 0.05f, 0.3f, "Stainless_Satin");
            b.Box("Monitor", new Vector3(0, 1.72f, 0), new Vector3(0.74f, 0.46f, 0.06f), "Polymer_Dark");
            var screen = b.Quad("LapTower_Screen", new Vector3(0, 1.72f, 0.031f), new Vector2(0.68f, 0.4f), "Screen_Laparoscope", new Vector3(0, 180, 0));
            screen.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
            b.Pivot("ScreenOverlayAnchor", new Vector3(0, 1.72f, 0.04f));
            var audio = b.Pivot("AudioSocket", new Vector3(0, 0.6f, 0.2f));
            audio.gameObject.AddComponent<AmbientEmitter>().Sound = SoundId.AMB_Insufflator;
            b.Collider(null, new Vector3(0, 0.95f, 0), new Vector3(0.74f, 1.9f, 0.56f));
            b.Info("EQ-03", "Laparoscopic tower + monitor", "Equipment", AssetRelease.MVP, 6000,
                "Screen mesh 'LapTower_Screen' takes the laparoscope RenderTexture; insufflator and light-source boxes.");
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
        }

        static void AnaesthesiaMachine()
        {
            if (SimReadyAnaesthesiaMachine()) return;
            var b = new PB("EQ_AnaesthesiaMachine");
            b.Box("Body", new Vector3(0, 0.6f, 0), new Vector3(0.75f, 1.0f, 0.6f), "Polymer_White");
            b.Box("Worktop", new Vector3(0, 1.12f, 0.05f), new Vector3(0.8f, 0.03f, 0.7f), "Polymer_Grey");
            for (int i = 0; i < 3; i++)
                b.Box($"Drawer_{i}", new Vector3(0, 0.3f + i * 0.2f, 0.305f), new Vector3(0.6f, 0.16f, 0.01f), "Cabinet_White");
            b.Box("Monitor", new Vector3(0, 1.5f, 0.05f), new Vector3(0.44f, 0.32f, 0.05f), "Polymer_Dark");
            b.Quad("Monitor_Screen", new Vector3(0, 1.5f, 0.076f), new Vector2(0.4f, 0.27f), "Screen_Vitals", new Vector3(0, 180, 0));
            b.Cyl("Vaporizer_A", new Vector3(-0.2f, 1.25f, 0.2f), 0.08f, 0.2f, "Indicator_Amber");
            b.Cyl("Vaporizer_B", new Vector3(-0.08f, 1.25f, 0.2f), 0.08f, 0.2f, "Polymer_Grey");
            b.Cyl("Bellows", new Vector3(0.26f, 1.26f, 0.2f), 0.15f, 0.24f, "Glass");
            b.Cyl("GasCylinder_O2", new Vector3(-0.25f, 0.55f, -0.36f), 0.1f, 0.7f, "Polymer_White");
            for (int i = 0; i < 4; i++)
                b.Sph($"Caster_{i}", new Vector3((i % 2 == 0 ? -1 : 1) * 0.32f, 0.04f, (i < 2 ? -1 : 1) * 0.26f), 0.08f, "Rubber_Black");
            var audio = b.Pivot("AudioSocket", new Vector3(0, 1.5f, 0.1f));
            audio.gameObject.AddComponent<AmbientEmitter>().Sound = SoundId.AMB_AnaesthesiaMonitor_72bpm;
            b.Collider(null, new Vector3(0, 0.85f, 0), new Vector3(0.8f, 1.7f, 0.7f));
            b.Info("EQ-04", "Anaesthesia machine", "Equipment", AssetRelease.MVP, 6000, "Background only, no interaction. Monitor beeps at 72 bpm.");
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
        }

        /// <summary>Wall-mounted twin scrub trough. Origin = floor under the front lip centre; wall at −Z.</summary>
        static void ScrubSink()
        {
            var b = new PB("EQ_ScrubSink");
            if (ImportedSink(b, out var spouts, out float basinY, out float basinZ))
            {
                string[] names = spouts.Count == 3 ? new[] { "L", "C", "R" } : spouts.Select((_, i) => i.ToString()).ToArray();
                for (int i = 0; i < spouts.Count; i++)
                {
                    var p = spouts[i];
                    b.Box($"Sensor_{names[i]}", new Vector3(p.x, 1.47f, -0.285f), new Vector3(0.05f, 0.035f, 0.02f), "Polymer_Dark");
                    b.Box($"Sensor_{names[i]}_LED", new Vector3(p.x, 1.47f, -0.274f), new Vector3(0.01f, 0.01f, 0.002f), "Indicator_Mint");
                    WaterSocket(b, names[i], p, p.y - basinY);
                }
                b.Box("SoapDispenser", new Vector3(-0.25f, 1.62f, -0.26f), new Vector3(0.1f, 0.2f, 0.08f), "Polymer_White");
                b.Box("Brushes", new Vector3(0.25f, 1.62f, -0.27f), new Vector3(0.2f, 0.25f, 0.06f), "Polymer_White");
                b.Quad("SoapLather", new Vector3(spouts[0].x, basinY + 0.012f, basinZ), new Vector2(0.3f, 0.26f), "FX_SoapLather", new Vector3(90, 0, 0));
                b.FitColliderFromRenderers();
                b.Info("EQ-05", "Scrub sink + sensor taps", "Equipment", AssetRelease.MVP, 17000,
                    $"Imported (Imports/scrubbing-sink, glTF); {spouts.Count} sensor taps with water sockets at the spouts (WaterSocket_*); soap dispenser.").isPlaceholder = false;
                b.MakeStatic(false);
                KeepFxOutOfBatching(b);
                b.Save(Folder("Equipment"));
                return;
            }
            b.Box("Splashback", new Vector3(0, 1.25f, -0.3f), new Vector3(1.7f, 1.1f, 0.03f), "Stainless_Satin");
            b.Box("Trough_Bottom", new Vector3(0, 0.8f, -0.05f), new Vector3(1.6f, 0.03f, 0.5f), "Stainless_Brushed");
            b.Box("Trough_Front", new Vector3(0, 0.93f, 0.19f), new Vector3(1.6f, 0.26f, 0.03f), "Stainless_Brushed");
            b.Box("Trough_Left", new Vector3(-0.79f, 0.93f, -0.05f), new Vector3(0.03f, 0.26f, 0.5f), "Stainless_Brushed");
            b.Box("Trough_Right", new Vector3(0.79f, 0.93f, -0.05f), new Vector3(0.03f, 0.26f, 0.5f), "Stainless_Brushed");
            b.Box("Pedestal", new Vector3(0, 0.4f, -0.2f), new Vector3(1.4f, 0.8f, 0.18f), "Stainless_Satin");
            for (int s = -1; s <= 1; s += 2)
            {
                string n = s < 0 ? "L" : "R";
                float x = s * 0.4f;
                b.Cyl($"Tap_{n}_Riser", new Vector3(x, 1.36f, -0.26f), 0.025f, 0.34f, "Stainless_Brushed");
                b.Cyl($"Tap_{n}_Neck", new Vector3(x, 1.53f, -0.17f), 0.025f, 0.2f, "Stainless_Brushed", PB.AlongZ);
                b.Cyl($"Tap_{n}_Spout", new Vector3(x, 1.5f, -0.07f), 0.02f, 0.06f, "Stainless_Brushed");
                b.Box($"Sensor_{n}", new Vector3(x, 1.15f, -0.28f), new Vector3(0.05f, 0.035f, 0.02f), "Polymer_Dark");
                b.Box($"Sensor_{n}_LED", new Vector3(x, 1.15f, -0.269f), new Vector3(0.01f, 0.01f, 0.002f), "Indicator_Mint");
                WaterSocket(b, n, new Vector3(x, 1.47f, -0.07f), 0.32f);
            }
            b.Box("SoapDispenser", new Vector3(0, 1.45f, -0.26f), new Vector3(0.1f, 0.2f, 0.08f), "Polymer_White");
            b.Quad("SoapLather", new Vector3(-0.4f, 0.818f, -0.05f), new Vector2(0.32f, 0.28f), "FX_SoapLather", new Vector3(90, 0, 0));
            b.Box("Brushes", new Vector3(0.62f, 1.4f, -0.27f), new Vector3(0.2f, 0.25f, 0.06f), "Polymer_White");
            b.Collider(null, new Vector3(0, 0.95f, -0.1f), new Vector3(1.7f, 1.9f, 0.6f));
            b.Info("EQ-05", "Scrub sink + sensor tap", "Equipment", AssetRelease.MVP, 5000, "Water VFX socket at spout (WaterSocket_*); soap dispenser.");
            b.MakeStatic(false);
            KeepFxOutOfBatching(b);
            b.Save(Folder("Equipment"));
        }

        /// <summary>
        /// Static batching bakes meshes into world space, which breaks shaders that animate in object space (the water
        /// stream's wobble scales around the world origin, the lather drifts). Those effects stay out of the batch.
        /// </summary>
        static void KeepFxOutOfBatching(PB b)
        {
            foreach (var t in b.Root.GetComponentsInChildren<Transform>(true))
                if (t.name == "WaterStream" || t.name == "SoapLather")
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                        GameObjectUtility.GetStaticEditorFlags(t.gameObject) & ~StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Tap outlet: water sound + visible stream of <paramref name="fall"/> metres, off until the sensor fires.</summary>
        static void WaterSocket(PB b, string n, Vector3 spout, float fall)
        {
            var w = b.Pivot($"WaterSocket_{n}", spout, new Vector3(90, 0, 0));
            var em = w.gameObject.AddComponent<AmbientEmitter>();
            em.Sound = SoundId.AMB_TapWater;
            // Visible stream (SF/FX/Water Stream): socket +Z points down, so offset along +Z and undo the socket tilt.
            var stream = b.Cyl("WaterStream", new Vector3(0, 0, fall / 2f), 0.012f, fall, "FX_WaterStream", new Vector3(-90, 0, 0), w);
            stream.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            w.gameObject.SetActive(false); // switched on by the sensor
        }

        /// <summary>Draped instrument back table with sterile-zone and non-sterile-edge triggers (FR-07).</summary>
        static void BackTable()
        {
            var b = new PB("EQ_BackTable");
            b.Box("Top", new Vector3(0, 0.9f, 0), new Vector3(1.2f, 0.03f, 0.6f), "Stainless_Brushed");
            b.Box("Shelf", new Vector3(0, 0.3f, 0), new Vector3(1.1f, 0.02f, 0.5f), "Stainless_Brushed");
            for (int i = 0; i < 4; i++)
                b.Cyl($"Leg_{i}", new Vector3((i % 2 == 0 ? -1 : 1) * 0.56f, 0.45f, (i < 2 ? -1 : 1) * 0.26f), 0.03f, 0.88f, "Stainless_Satin");
            var drape = b.Group("SterileDrape", Vector3.zero);
            b.Box("Drape_Top", new Vector3(0, 0.921f, 0), new Vector3(1.26f, 0.008f, 0.66f), "Drape_Teal", default, drape);
            b.Box("Drape_Front", new Vector3(0, 0.77f, 0.331f), new Vector3(1.26f, 0.3f, 0.004f), "Drape_Teal", default, drape);
            b.Box("Drape_Back", new Vector3(0, 0.77f, -0.331f), new Vector3(1.26f, 0.3f, 0.004f), "Drape_Teal", default, drape);
            b.Box("Drape_Left", new Vector3(-0.631f, 0.77f, 0), new Vector3(0.004f, 0.3f, 0.66f), "Drape_Teal", default, drape);
            b.Box("Drape_Right", new Vector3(0.631f, 0.77f, 0), new Vector3(0.004f, 0.3f, 0.66f), "Drape_Teal", default, drape);

            b.Collider(null, new Vector3(0, 0.46f, 0), new Vector3(1.26f, 0.92f, 0.66f));
            b.Collider("SterileZone", new Vector3(0, 0.99f, 0), new Vector3(1.1f, 0.12f, 0.5f), true);
            b.Collider("NonSterileEdge_Front", new Vector3(0, 0.7f, 0.35f), new Vector3(1.3f, 0.3f, 0.04f), true);
            b.Collider("NonSterileEdge_Back", new Vector3(0, 0.7f, -0.35f), new Vector3(1.3f, 0.3f, 0.04f), true);
            b.Collider("NonSterileEdge_Left", new Vector3(-0.65f, 0.7f, 0), new Vector3(0.04f, 0.3f, 0.7f), true);
            b.Collider("NonSterileEdge_Right", new Vector3(0.65f, 0.7f, 0), new Vector3(0.04f, 0.3f, 0.7f), true);
            b.Info("EQ-06", "Back table", "Equipment", AssetRelease.MVP, 3000, "Sterile zone collider on top, non-sterile edge colliders (FR-07).");
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
        }

        static void MayoStand()
        {
            var b = new PB("EQ_MayoStand");
            b.Box("Base_L", new Vector3(-0.16f, 0.02f, 0), new Vector3(0.05f, 0.04f, 0.62f), "Stainless_Satin");
            b.Box("Base_R", new Vector3(0.16f, 0.02f, 0), new Vector3(0.05f, 0.04f, 0.62f), "Stainless_Satin");
            b.Box("Base_Cross", new Vector3(0, 0.02f, -0.28f), new Vector3(0.37f, 0.04f, 0.05f), "Stainless_Satin");
            b.Cyl("Post", new Vector3(0, 0.53f, -0.28f), 0.032f, 1.0f, "Stainless_Brushed");
            b.Box("Tray", new Vector3(0, 1.05f, 0), new Vector3(0.5f, 0.015f, 0.36f), "Stainless_Brushed");
            b.Box("Tray_Rim_F", new Vector3(0, 1.065f, 0.18f), new Vector3(0.5f, 0.02f, 0.006f), "Stainless_Brushed");
            b.Box("Tray_Rim_B", new Vector3(0, 1.065f, -0.18f), new Vector3(0.5f, 0.02f, 0.006f), "Stainless_Brushed");
            b.Box("Tray_Drape", new Vector3(0, 1.06f, 0), new Vector3(0.52f, 0.004f, 0.38f), "Drape_Teal");
            for (int i = 0; i < 4; i++) b.Pivot($"Socket_{i + 1}", new Vector3(-0.15f + i * 0.1f, 1.075f, 0), new Vector3(0, 0, 90));
            b.Collider(null, new Vector3(0, 1.05f, 0), new Vector3(0.52f, 0.03f, 0.38f));
            b.Collider("PostCollider", new Vector3(0, 0.53f, -0.28f), new Vector3(0.04f, 1.0f, 0.04f));
            b.Info("EQ-07", "Mayo stand", "Equipment", AssetRelease.MVP, 2000, "Instrument sockets.");
            b.MakeStatic(false);
            b.Save(Folder("Equipment"));
        }

        /// <summary>One named XR socket per item for the opening count (screen 07).</summary>
        static void InstrumentTray()
        {
            var b = new PB("EQ_InstrumentTray");
            // Sized for the laparoscopic set: the instruments lie on their sides with their handles towards −Z and
            // behind their sockets towards −X, so the tray runs further those ways (rims at z +0.148 / −0.24 and
            // x −0.30 / +0.238), and the rims are low enough (2.4 cm) that the shafts, resting 3.2 cm up, pass over them.
            const float front = 0.148f, back = -0.24f, left = -0.30f, right = 0.238f, rimH = 0.024f;
            float depth = front - back, mid = (front + back) * 0.5f, length = right - left, midX = (right + left) * 0.5f;
            b.Box("Base", new Vector3(midX, 0.003f, mid), new Vector3(length, 0.006f, depth), "Stainless_Brushed");
            b.Box("Rim_F", new Vector3(midX, rimH / 2, front), new Vector3(length, rimH, 0.004f), "Stainless_Brushed");
            b.Box("Rim_B", new Vector3(midX, rimH / 2, back), new Vector3(length, rimH, 0.004f), "Stainless_Brushed");
            b.Box("Rim_L", new Vector3(left, rimH / 2, mid), new Vector3(0.004f, rimH, depth), "Stainless_Brushed");
            b.Box("Rim_R", new Vector3(right, rimH / 2, mid), new Vector3(0.004f, rimH, depth), "Stainless_Brushed");
            b.Box("Mat", new Vector3(midX, 0.0065f, mid), new Vector3(length - 0.02f, 0.002f, depth - 0.02f), "Drape_Teal");
            string[] sockets = { "Trocar12", "Trocar5_A", "Trocar5_B", "Laparoscope", "Grasper_A", "Grasper_B", "Scissors", "ClipApplier" };
            for (int i = 0; i < sockets.Length; i++)
                b.Pivot($"Socket_{sockets[i]}", new Vector3(-0.19f + (i % 4) * 0.125f, 0.02f, i < 4 ? 0.07f : -0.07f), new Vector3(0, 90, 0));
            b.Collider(null, new Vector3(midX, rimH / 2, mid), new Vector3(length, rimH, depth));
            b.Info("EQ-08", "Instrument tray", "Equipment", AssetRelease.MVP, 2000, "One XR socket per item for the tray check.");
            b.Save(Folder("Equipment"));
        }

        static void KickBucket()
        {
            var b = new PB("EQ_KickBucket");
            b.Cyl("BaseRing", new Vector3(0, 0.08f, 0), 0.46f, 0.025f, "Stainless_Satin");
            for (int i = 0; i < 3; i++)
            {
                var d = Quaternion.Euler(0, i * 120f, 0) * Vector3.forward * 0.2f;
                b.Sph($"Caster_{i}", d + Vector3.up * 0.035f, 0.07f, "Rubber_Black");
            }
            b.Cyl("Bucket", new Vector3(0, 0.27f, 0), 0.38f, 0.34f, "Stainless_Satin");
            b.Cyl("Bucket_Inner", new Vector3(0, 0.439f, 0), 0.35f, 0.004f, "Polymer_Dark");
            b.Collider(null, new Vector3(0, 0.22f, 0), new Vector3(0.44f, 0.44f, 0.44f));
            b.Collider("DropTarget", new Vector3(0, 0.5f, 0), new Vector3(0.36f, 0.14f, 0.36f), true);
            b.Info("EQ-09", "Kick bucket", "Equipment", AssetRelease.MVP, 1000, "Swab drop target for the count.");
            b.Save(Folder("Equipment"));
        }
    }
}

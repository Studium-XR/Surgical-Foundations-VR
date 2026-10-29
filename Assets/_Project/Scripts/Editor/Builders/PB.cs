using System.IO;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Interaction;
using SurgicalFoundations.Placeholders;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Tiny prefab-building DSL for placeholder art. Convention: Root (gameplay components, colliders, named pivots)
    /// → "Visual" (primitives only, no colliders). Artists replace the contents of Visual with the real mesh.
    /// Sizes are in metres; cylinders and capsules take (diameter, length) along their local Y axis.
    /// </summary>
    public class PB
    {
        public readonly GameObject Root;
        public readonly Transform Visual;

        public static readonly Vector3 AlongX = new Vector3(0, 0, 90);
        public static readonly Vector3 AlongZ = new Vector3(90, 0, 0);

        public PB(string name)
        {
            Root = new GameObject(name);
            Visual = new GameObject("Visual").transform;
            Visual.SetParent(Root.transform, false);
        }

        public Transform Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, string mat, Vector3 euler = default, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : Visual, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Get(mat);
            return go.transform;
        }

        public Transform Box(string name, Vector3 center, Vector3 size, string mat, Vector3 euler = default, Transform parent = null) =>
            Prim(PrimitiveType.Cube, name, center, size, mat, euler, parent);

        public Transform Cyl(string name, Vector3 center, float diameter, float length, string mat, Vector3 euler = default, Transform parent = null) =>
            Prim(PrimitiveType.Cylinder, name, center, new Vector3(diameter, length * 0.5f, diameter), mat, euler, parent);

        /// <summary>Elliptic cylinder: (diameterX, length, diameterZ).</summary>
        public Transform CylE(string name, Vector3 center, Vector3 dims, string mat, Vector3 euler = default, Transform parent = null) =>
            Prim(PrimitiveType.Cylinder, name, center, new Vector3(dims.x, dims.y * 0.5f, dims.z), mat, euler, parent);

        public Transform Cap(string name, Vector3 center, float diameter, float length, string mat, Vector3 euler = default, Transform parent = null) =>
            Prim(PrimitiveType.Capsule, name, center, new Vector3(diameter, length * 0.5f, diameter), mat, euler, parent);

        /// <summary>Elliptic capsule: (diameterX, length, diameterZ).</summary>
        public Transform CapE(string name, Vector3 center, Vector3 dims, string mat, Vector3 euler = default, Transform parent = null) =>
            Prim(PrimitiveType.Capsule, name, center, new Vector3(dims.x, dims.y * 0.5f, dims.z), mat, euler, parent);

        public Transform Sph(string name, Vector3 center, Vector3 size, string mat, Vector3 euler = default, Transform parent = null) =>
            Prim(PrimitiveType.Sphere, name, center, size, mat, euler, parent);

        public Transform Sph(string name, Vector3 center, float d, string mat, Transform parent = null) =>
            Prim(PrimitiveType.Sphere, name, center, Vector3.one * d, mat, default, parent);

        public Transform Quad(string name, Vector3 center, Vector2 size, string mat, Vector3 euler = default, Transform parent = null)
        {
            var t = Prim(PrimitiveType.Quad, name, center, new Vector3(size.x, size.y, 1f), mat, euler, parent);
            t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        /// <summary>
        /// Cone frustum along local Y, like <see cref="Cyl"/>: <paramref name="d0"/> across at −Y (start), <paramref name="d1"/>
        /// at +Y (end). Use d1 = 0 for a point.
        /// </summary>
        public Transform Frustum(string name, Vector3 center, float d0, float d1, float length, string mat, Vector3 euler = default, Transform parent = null)
        {
            var big = Mathf.Max(d0, d1, 1e-5f);
            float k = Mathf.Round(Mathf.Min(d0, d1) / big * 20f) / 20f; // ratio, quantised so meshes are shared
            var t = Mesh(name, ProcMesh.Frustum(k, d0 < d1), center, new Vector3(big, length, big), mat, euler, parent);
            return t;
        }

        /// <summary>Ring in the local XZ plane (axis along Y): <paramref name="outerD"/> across, tube <paramref name="tubeD"/> thick.</summary>
        public Transform Torus(string name, Vector3 center, float outerD, float tubeD, string mat, Vector3 euler = default, Transform parent = null)
        {
            float major = (outerD - tubeD) * 0.5f;
            float m = Mathf.Round(tubeD * 0.5f / Mathf.Max(major, 1e-5f) * 50f) / 50f; // tube radius / ring radius
            return Mesh(name, ProcMesh.Torus(m), center, Vector3.one * (major * 2f), mat, euler, parent);
        }

        Transform Mesh(string name, Mesh mesh, Vector3 pos, Vector3 scale, string mat, Vector3 euler, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent != null ? parent : Visual, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Get(mat);
            return go.transform;
        }

        /// <summary>Empty transform under Visual (a hinge or sub-assembly that artists keep by name).</summary>
        public Transform Group(string name, Vector3 pos, Vector3 euler = default, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent != null ? parent : Visual, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            return t;
        }

        /// <summary>Gameplay pivot under Root (survives replacing Visual).</summary>
        public Transform Pivot(string name, Vector3 pos, Vector3 euler = default, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent != null ? parent : Root.transform, false);
            t.localPosition = pos;
            t.localEulerAngles = euler;
            return t;
        }

        public BoxCollider Collider(string name, Vector3 center, Vector3 size, bool trigger = false, Transform parent = null)
        {
            var host = name == null ? Root.transform : Pivot(name, Vector3.zero, default, parent);
            var c = host.gameObject.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            c.isTrigger = trigger;
            return c;
        }

        /// <summary>Box collider on Root enclosing everything under <paramref name="of"/> (default: Visual).</summary>
        public BoxCollider FitCollider(Transform of = null, float pad = 0.002f)
        {
            of ??= Visual;
            var b = LocalBounds(Root.transform, of);
            var c = Root.AddComponent<BoxCollider>();
            c.center = b.center;
            c.size = b.size + Vector3.one * pad;
            return c;
        }

        /// <summary>Box collider from renderer bounds (works for imported and skinned meshes). Root must be at identity.</summary>
        public BoxCollider FitColliderFromRenderers()
        {
            var rs = Visual.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return null;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var c = Root.AddComponent<BoxCollider>();
            c.center = Root.transform.InverseTransformPoint(b.center);
            c.size = b.size;
            return c;
        }

        public static Bounds LocalBounds(Transform space, Transform of)
        {
            bool any = false;
            var result = new Bounds();
            foreach (var mf in of.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds;
                var m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        public PlaceholderInfo Info(string id, string displayName, string category, AssetRelease release, int tris, string notes)
        {
            var info = Root.AddComponent<PlaceholderInfo>();
            info.assetId = id;
            info.displayName = displayName;
            info.category = category;
            info.release = release;
            info.triangleBudget = tris;
            info.buildNotes = notes;
            return info;
        }

        /// <summary>Static for GI/batching/occlusion. Large surfaces take lightmaps; small parts light from probes.</summary>
        public void MakeStatic(bool lightmapped)
        {
            foreach (var t in Root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ReflectionProbeStatic | (lightmapped ? StaticEditorFlags.OccluderStatic : 0));
                var r = t.GetComponent<MeshRenderer>();
                if (r != null) r.receiveGI = lightmapped ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            }
        }

        /// <summary>Rigidbody + XR grab + sounds. Instruments with jaws get InstrumentJaws instead of GrabSound.</summary>
        public XRGrabInteractable Grabbable(float mass, Vector3 attachPos, Vector3 attachEuler, bool jaws = false,
            SoundId grabSound = SoundId.INST_PickupMetal, SoundId impact = SoundId.None)
        {
            var rb = Root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (Root.GetComponent<Collider>() == null) FitCollider();

            var attach = Pivot("Attach", attachPos, attachEuler);
            var grab = Root.AddComponent<XRGrabInteractable>();
            grab.attachTransform = attach;
            grab.useDynamicAttach = false;
            grab.throwOnDetach = false;

            if (jaws) Root.AddComponent<InstrumentJaws>();
            else if (grabSound != SoundId.None)
            {
                var gs = Root.AddComponent<GrabSound>();
                Ser.Set(gs, "onGrab", (int)grabSound);
            }
            if (impact != SoundId.None)
            {
                var imp = Root.AddComponent<ImpactSound>();
                Ser.Set(imp, "sound", (int)impact);
            }
            return grab;
        }

        public GameObject Save(string folder)
        {
            Directory.CreateDirectory(folder);
            var path = $"{folder}/{Root.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(Root, path);
            Object.DestroyImmediate(Root);
            return prefab;
        }
    }
}

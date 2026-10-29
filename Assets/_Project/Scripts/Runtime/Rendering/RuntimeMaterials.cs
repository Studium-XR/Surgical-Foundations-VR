using UnityEngine;

namespace SurgicalFoundations.Rendering
{
    /// <summary>
    /// Materials for objects made at runtime with GameObject.CreatePrimitive. The primitive's default material shows
    /// magenta in a Quest build (its shader is stripped), so anything created in code gets a URP Lit material instead.
    /// </summary>
    public static class RuntimeMaterials
    {
        static Shader lit, unlit;

        public static Material Lit(Color color)
        {
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            var m = new Material(lit);
            m.SetColor("_BaseColor", color);
            return m;
        }

        /// <summary>Flat colour for lines and markers, which have no normals to light (Lit draws them black). Falls back
        /// to Lit when the Unlit shader isn't in the build.</summary>
        public static Material Unlit(Color color)
        {
            if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) return Lit(color);
            var m = new Material(unlit);
            m.SetColor("_BaseColor", color);
            return m;
        }

        /// <summary>Destroy that also works outside play mode (editor tools and tests build these objects too).</summary>
        public static void Remove(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Creates a primitive without its collider, with a URP Lit material in the given colour.</summary>
        public static GameObject Primitive(PrimitiveType type, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Remove(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Lit(color);
            return go;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Every rigged SimReady model in the build, by asset id (e.g. "pump_infusion_a01"), with a readable name and its
    /// category. Built by Surgical Foundations → Build → SimReady Library; loaded from Resources at runtime by the
    /// equipment lab and anything a course lesson asks to lay out.
    /// </summary>
    [CreateAssetMenu(menuName = "Surgical Foundations/SimReady Library")]
    public class SimReadyLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string id;
            public string displayName;
            public string category;
            public GameObject prefab;
            public int joints;
            public Vector3 size;
        }

        public List<Entry> entries = new List<Entry>();

        static SimReadyLibrary instance;
        Dictionary<string, Entry> index;

        public static SimReadyLibrary Instance => instance != null ? instance : instance = Resources.Load<SimReadyLibrary>("SimReadyLibrary");

        public Entry Find(string id)
        {
            if (index == null)
            {
                index = new Dictionary<string, Entry>();
                foreach (var e in entries) if (e.prefab != null) index[e.id] = e;
            }
            return id != null && index.TryGetValue(id, out var x) ? x : null;
        }

        /// <summary>"pump_infusion_a01" → "Infusion pump".</summary>
        public static string Readable(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var parts = new List<string>(id.Split('_'));
            if (parts.Count > 1 && System.Text.RegularExpressions.Regex.IsMatch(parts[parts.Count - 1], @"^[a-z]\d\d$")) parts.RemoveAt(parts.Count - 1);
            // SimReady names put the noun first ("pump_infusion"): read it back to front for two-word names.
            if (parts.Count == 2) parts.Reverse();
            var s = string.Join(" ", parts).Replace("instrument ", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}

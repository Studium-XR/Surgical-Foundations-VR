using UnityEngine;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// White, anti-aliased shapes generated at runtime for UI built in code (tinted through Image.color): a disc, a ring,
    /// a 9-sliced rounded box and an arrow head. Generated once and cached; nothing depends on editor-only assets.
    /// </summary>
    public static class UISprites
    {
        static Sprite circle, rounded, triangle;
        static readonly System.Collections.Generic.Dictionary<int, Sprite> rings = new System.Collections.Generic.Dictionary<int, Sprite>();

        public static Sprite Circle => circle != null ? circle : circle = Make(256, (x, y) => Disc(x, y, 1f));

        /// <summary>Ring whose hole is <paramref name="innerRatio"/> of its diameter.</summary>
        public static Sprite Ring(float innerRatio)
        {
            int key = Mathf.RoundToInt(innerRatio * 100f);
            if (rings.TryGetValue(key, out var s) && s != null) return s;
            return rings[key] = Make(512, (x, y) => Mathf.Min(Disc(x, y, 1f), 1f - Disc(x, y, innerRatio)));
        }

        /// <summary>Rounded box, 9-sliced: corners stay round at any size.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int size = 64, radius = 24;
                var tex = Texture(size, (px, py) =>
                {
                    float cx = Mathf.Clamp(px, radius, size - radius), cy = Mathf.Clamp(py, radius, size - radius);
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    return Mathf.Clamp01(radius - d + 0.5f);
                });
                return rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            }
        }

        /// <summary>Arrow head pointing up.</summary>
        public static Sprite Triangle => triangle != null ? triangle : triangle = Make(128, (x, y) =>
        {
            // x, y in −1..1; apex at the top, base at the bottom.
            float half = (1f - y) * 0.5f * 0.9f;
            float edge = Mathf.Min(half - Mathf.Abs(x), y + 0.8f, 0.9f - y);
            return Mathf.Clamp01(edge * 64f);
        });

        static float Disc(float x, float y, float r) => Mathf.Clamp01((r - Mathf.Sqrt(x * x + y * y)) * 128f + 0.5f);

        /// <summary>Square sprite from a shape function over −1..1 (alpha 0..1).</summary>
        static Sprite Make(int size, System.Func<float, float, float> alpha)
        {
            var tex = Texture(size, (px, py) => alpha((px + 0.5f) / size * 2f - 1f, (py + 0.5f) / size * 2f - 1f));
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D Texture(int size, System.Func<int, int, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}

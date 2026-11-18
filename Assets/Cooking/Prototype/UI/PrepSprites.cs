using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// A picture of an ingredient as it's been prepped, made from its own sprite the first time it's
    /// needed: <b>peeled</b> is paler (the skin's off), <b>sliced</b> is cut into slices with gaps
    /// between them, <b>grated</b> is a little heap of shreds. They combine: peeled and sliced is
    /// pale slices, peeled and grated pale shreds. Raw ingredients keep their own sprite.
    /// </summary>
    public static class PrepSprites
    {
        private static readonly Dictionary<(IngredientDefinition, PrepSteps), Sprite> cache = new Dictionary<(IngredientDefinition, PrepSteps), Sprite>();

        public static Sprite For(IngredientDefinition ingredient, PrepSteps prep)
        {
            if(ingredient == null) return null;
            Sprite raw = ingredient.Icon;
            prep &= PrepSteps.Peeled | PrepSteps.Sliced | PrepSteps.Grated;
            if(raw == null || prep == PrepSteps.None) return raw;

            (IngredientDefinition, PrepSteps) key = (ingredient, prep);
            if(cache.TryGetValue(key, out Sprite made) && made != null) return made;

            made = Make(raw, prep, ingredient.DisplayName.GetHashCode());
            cache[key] = made ?? raw;
            return cache[key];
        }

        private static Sprite Make(Sprite raw, PrepSteps prep, int seed)
        {
            Color[] source = ReadPixels(raw, out int w, out int h);
            if(source == null) return null;

            if((prep & PrepSteps.Peeled) != 0) source = Peel(source);

            Color[] result = (prep & PrepSteps.Grated) != 0 ? Grate(source, w, h, seed)
                : (prep & PrepSteps.Sliced) != 0 ? Slice(source, w, h)
                : source;

            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(result);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), raw.pixelsPerUnit);
        }

        /// <summary>The sprite's pixels, read back through the GPU so the texture needn't be readable.</summary>
        private static Color[] ReadPixels(Sprite sprite, out int w, out int h)
        {
            Rect rect = sprite.textureRect;
            w = Mathf.RoundToInt(rect.width);
            h = Mathf.RoundToInt(rect.height);
            Texture2D texture = sprite.texture;
            if(texture == null || w <= 0 || h <= 0) return null;

            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = copy.GetPixels();
            Object.Destroy(copy);
            return pixels;
        }

        /// <summary>The skin's off: paler and less saturated, the outline kept.</summary>
        private static Color[] Peel(Color[] pixels)
        {
            Color[] result = new Color[pixels.Length];

            for(int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                if(c.a <= 0.01f || c.grayscale < 0.22f)
                {
                    result[i] = c;
                    continue;
                }

                Color.RGBToHSV(c, out float hue, out float sat, out float val);
                Color pale = Color.HSVToRGB(hue, sat * 0.45f, Mathf.Lerp(val, 0.97f, 0.45f));
                pale = Color.Lerp(pale, new Color(1f, 0.95f, 0.8f), 0.2f);
                pale.a = c.a;
                result[i] = pale;
            }

            return result;
        }

        /// <summary>Three slices side by side with gaps, each outlined, with a pale cut face on its inner edge.</summary>
        private static Color[] Slice(Color[] pixels, int w, int h)
        {
            Color[] result = new Color[pixels.Length];
            const int slices = 3;
            const float squeeze = 0.72f;
            float gap = w * (1f - squeeze) / (slices - 1);
            int edge = Mathf.Max(2, w / 70);
            int face = Mathf.Max(3, w / 28);
            Color outline = new Color(0.22f, 0.15f, 0.1f, 1f);

            for(int s = 0; s < slices; s++)
            {
                int fromX = s * w / slices;
                int toX = (s + 1) * w / slices;
                // Every other slice sits a little higher and tilts the other way.
                int lift = s % 2 == 1 ? Mathf.RoundToInt(h * 0.05f) : 0;

                for(int x = fromX; x < toX; x++)
                {
                    int targetX = Mathf.RoundToInt(x * squeeze + s * gap);
                    if(targetX < 0 || targetX >= w) continue;
                    bool leftCut = s > 0 && x - fromX < edge;
                    bool rightCut = s < slices - 1 && toX - 1 - x < edge;
                    bool inFace = s > 0 && x - fromX >= edge && x - fromX < edge + face;

                    for(int y = 0; y < h; y++)
                    {
                        Color c = pixels[y * w + x];
                        if(c.a <= 0.01f) continue;
                        int targetY = Mathf.Clamp(y + lift, 0, h - 1);
                        if(leftCut || rightCut) c = new Color(outline.r, outline.g, outline.b, c.a);
                        else if(inFace && c.grayscale > 0.22f) c = Color.Lerp(c, new Color(1f, 0.97f, 0.88f, c.a), 0.6f);
                        result[targetY * w + targetX] = c;
                    }
                }
            }

            return result;
        }

        /// <summary>A heap of thin shreds in the ingredient's own colours.</summary>
        private static Color[] Grate(Color[] pixels, int w, int h, int seed)
        {
            Color[] result = new Color[pixels.Length];
            System.Random random = new System.Random(seed);

            // Colours to shred: the opaque, non-outline pixels.
            List<Color> colours = new List<Color>();
            for(int i = 0; i < pixels.Length; i += 7)
            {
                if(pixels[i].a > 0.5f && pixels[i].grayscale > 0.2f) colours.Add(pixels[i]);
            }

            if(colours.Count == 0) return pixels;

            int shredLong = Mathf.Max(4, w / 14);
            int shredThin = Mathf.Max(2, w / 48);
            int count = 170;

            for(int n = 0; n < count; n++)
            {
                // A mound: higher in the middle, wider at the bottom.
                float u = (float)random.NextDouble();
                float mound = 0.62f * (1f - Mathf.Pow(Mathf.Abs(2f * u - 1f), 1.6f));
                int cx = Mathf.RoundToInt(Mathf.Lerp(0.08f, 0.92f, u) * w);
                int cy = Mathf.RoundToInt(((float)random.NextDouble() * mound + 0.08f) * h);
                bool upright = random.NextDouble() < 0.5;
                int sw = upright ? shredThin : shredLong;
                int sh = upright ? shredLong : shredThin;
                Color colour = colours[random.Next(colours.Count)];
                colour.a = 1f;
                Color edge = Color.Lerp(colour, Color.black, 0.55f);

                for(int dx = -1; dx <= sw; dx++)
                {
                    for(int dy = -1; dy <= sh; dy++)
                    {
                        int x = cx - sw / 2 + dx;
                        int y = cy - sh / 2 + dy;
                        if(x < 0 || x >= w || y < 0 || y >= h) continue;
                        bool border = dx < 0 || dy < 0 || dx == sw || dy == sh;
                        int i = y * w + x;
                        if(border && result[i].a > 0.5f) continue;
                        result[i] = border ? edge : colour;
                    }
                }
            }

            return result;
        }
    }
}

using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// Paints the Bright Market in the distance behind the customer: a station skyline with lit
    /// windows, a row of food stalls with glowing awnings and lamps, strings of lanterns and neon,
    /// then blurs it so it reads as far away and out of focus. Made once at startup, no asset needed.
    /// </summary>
    public static class MarketBackdrop
    {
        private const int Width = 480;
        private const int Height = 150;

        public static Sprite Create(int seed = 7)
        {
            System.Random random = new System.Random(seed);
            Color[] pixels = new Color[Width * Height];

            // Night sky: deep violet at the top to a warm haze over the market.
            for(int y = 0; y < Height; y++)
            {
                float t = y / (float)(Height - 1);
                Color sky = Color.Lerp(new Color(0.24f, 0.13f, 0.2f), new Color(0.07f, 0.06f, 0.14f), t);
                for(int x = 0; x < Width; x++) pixels[y * Width + x] = sky;
            }

            // The station's towers, far back, with a few lit windows.
            int towerX = 0;
            while(towerX < Width)
            {
                int w = random.Next(18, 46);
                int h = random.Next(50, 120);
                Color body = new Color(0.1f, 0.08f, 0.15f);
                Fill(pixels, towerX, 30, w, h, body);

                for(int wy = 36; wy < 30 + h - 4; wy += 6)
                {
                    for(int wx = towerX + 3; wx < towerX + w - 3; wx += 5)
                    {
                        if(random.NextDouble() < 0.28)
                        {
                            Color lit = random.NextDouble() < 0.7 ? new Color(0.95f, 0.75f, 0.4f) : new Color(0.45f, 0.8f, 0.95f);
                            Fill(pixels, wx, wy, 2, 3, lit * 0.8f);
                        }
                    }
                }

                towerX += w + random.Next(0, 6);
            }

            // Neon signs on the towers.
            Color[] neon = { new Color(1f, 0.3f, 0.6f), new Color(0.3f, 0.95f, 0.9f), new Color(1f, 0.65f, 0.2f), new Color(0.6f, 0.4f, 1f) };
            for(int i = 0; i < 9; i++)
            {
                Color c = neon[random.Next(neon.Length)];
                int x = random.Next(0, Width - 30);
                int y = random.Next(70, 135);
                bool tall = random.NextDouble() < 0.4;
                Glow(pixels, x, y, tall ? 5 : random.Next(16, 30), tall ? random.Next(14, 24) : 5, c);
            }

            // The street level: a row of stalls with awnings and lamps under them.
            Fill(pixels, 0, 0, Width, 34, new Color(0.08f, 0.06f, 0.08f));
            int stallX = -random.Next(0, 20);
            while(stallX < Width)
            {
                int w = random.Next(36, 60);
                Color awning = Color.HSVToRGB((float)random.NextDouble(), 0.55f, 0.55f);
                Color inside = new Color(0.95f, 0.62f, 0.3f);
                Fill(pixels, stallX + 2, 4, w - 4, 22, inside * 0.55f);
                Fill(pixels, stallX, 26, w, 7, awning);
                for(int s = 0; s < w; s += 8) Fill(pixels, stallX + s, 26, 4, 7, awning * 0.75f);
                Glow(pixels, stallX + w / 2 - 2, 20, 4, 3, new Color(1f, 0.85f, 0.55f));
                // People in front of the stalls, as dark shapes.
                for(int p = 0; p < random.Next(1, 4); p++)
                {
                    int px = stallX + random.Next(2, Mathf.Max(3, w - 8));
                    Fill(pixels, px, 0, 5, random.Next(14, 20), new Color(0.05f, 0.04f, 0.06f));
                    Fill(pixels, px + 1, 18, 3, 4, new Color(0.05f, 0.04f, 0.06f));
                }

                stallX += w + random.Next(2, 8);
            }

            // Strings of lanterns across the street.
            for(int line = 0; line < 2; line++)
            {
                int baseY = 42 + line * 16;
                for(int x = 0; x < Width; x += 9)
                {
                    int sag = (int)(Mathf.Sin(x / (float)Width * Mathf.PI * (3 + line)) * 4f);
                    Color bulb = line == 0 ? new Color(1f, 0.8f, 0.45f) : neon[(x / 9) % neon.Length];
                    Glow(pixels, x, baseY + sag, 2, 2, bulb * 0.9f);
                }
            }

            Blur(pixels, 2);
            Blur(pixels, 2);

            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, Width, Height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void Fill(Color[] pixels, int x0, int y0, int w, int h, Color color)
        {
            for(int y = Mathf.Max(0, y0); y < Mathf.Min(Height, y0 + h); y++)
            {
                for(int x = Mathf.Max(0, x0); x < Mathf.Min(Width, x0 + w); x++)
                {
                    pixels[y * Width + x] = new Color(color.r, color.g, color.b, 1f);
                }
            }
        }

        /// <summary>A bright shape with a soft halo around it.</summary>
        private static void Glow(Color[] pixels, int x0, int y0, int w, int h, Color color)
        {
            const int halo = 5;

            for(int y = Mathf.Max(0, y0 - halo); y < Mathf.Min(Height, y0 + h + halo); y++)
            {
                for(int x = Mathf.Max(0, x0 - halo); x < Mathf.Min(Width, x0 + w + halo); x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(x0 - x, x - (x0 + w - 1)));
                    float dy = Mathf.Max(0, Mathf.Max(y0 - y, y - (y0 + h - 1)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= 0f ? 1f : Mathf.Clamp01(1f - d / halo) * 0.35f;
                    Color p = pixels[y * Width + x];
                    pixels[y * Width + x] = Color.Lerp(p, color, a);
                }
            }
        }

        /// <summary>A box blur, horizontal then vertical.</summary>
        private static void Blur(Color[] pixels, int radius)
        {
            Color[] temp = new Color[pixels.Length];
            int span = radius * 2 + 1;

            for(int y = 0; y < Height; y++)
            {
                for(int x = 0; x < Width; x++)
                {
                    Color sum = Color.clear;
                    for(int k = -radius; k <= radius; k++) sum += pixels[y * Width + Mathf.Clamp(x + k, 0, Width - 1)];
                    temp[y * Width + x] = sum / span;
                }
            }

            for(int y = 0; y < Height; y++)
            {
                for(int x = 0; x < Width; x++)
                {
                    Color sum = Color.clear;
                    for(int k = -radius; k <= radius; k++) sum += temp[Mathf.Clamp(y + k, 0, Height - 1) * Width + x];
                    pixels[y * Width + x] = sum / span;
                }
            }
        }
    }
}

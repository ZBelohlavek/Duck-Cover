using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Hand-painted-looking surface textures made in code: stone courses, rock slabs, dirt, ribbed
    /// metal, planks, concrete, deck plate, shutter slats, bark and leaves. Each one tiles, takes its
    /// colours from the theme, and is made once per theme. The colour is in the pixels and the
    /// material's tint stays white, the same way <see cref="Paint"/> colours everything (runtime
    /// colour properties collapsed to one colour in real captures).
    ///
    /// The look aims at painted rather than plastic: broad tonal variation per stone or plank, light
    /// worn edges on top, darker grime at the bottom of each piece, and fine grain.
    /// </summary>
    public static class PaintedTextures
    {
        public const int Size = 256;

        public enum Kind { Stone, Rock, Ground, Ribbed, Planks, Concrete, Deck, Shutter, Bark, Leaf, Paving, Plain, Patch, Glyph }

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>The texture of a kind in the given colours (cached by kind, colours and seed).</summary>
        public static Texture2D Get(Kind kind, Color a, Color b, int seed)
        {
            string key = kind + " " + (Color32)a + " " + (Color32)b + " " + seed;
            if (cache.TryGetValue(key, out var found) && found != null) return found;
            var pixels = Make(kind, a, b, seed);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            tex.name = "Painted " + key;
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.SetPixels(pixels);
            tex.Apply(true);
            cache[key] = tex;
            return tex;
        }

        /// <summary>The pixels for a kind, row by row from the bottom.</summary>
        public static Color[] Make(Kind kind, Color a, Color b, int seed)
        {
            var px = new Color[Size * Size];
            var cells = new CellRows(seed);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                float grain = Noise.Fbm(u * 32f, v * 32f, 32, seed + 3, 3);
                Color c;
                switch (kind)
                {
                    case Kind.Stone: c = Courses(u, v, a, b, seed, 5, 2.2f, 0.012f, cells); break;
                    case Kind.Paving: c = Courses(u, v, a, b, seed, 4, 1.0f, 0.016f, cells); break;
                    case Kind.Rock: c = Slabs(u, v, a, b, seed); break;
                    case Kind.Ground: c = Dirt(u, v, a, b, seed); break;
                    case Kind.Ribbed: c = Ribbed(u, v, a, seed); break;
                    case Kind.Planks: c = Planks(u, v, a, b, seed); break;
                    case Kind.Concrete: c = Concrete(u, v, a, seed); break;
                    case Kind.Deck: c = DeckPlate(u, v, a, b, seed); break;
                    case Kind.Shutter: c = Shutter(u, v, a, b); break;
                    case Kind.Bark: c = Bark(u, v, a, b, seed); break;
                    case Kind.Leaf: c = Leaf(u, v, a, seed); break;
                    case Kind.Glyph: c = Glyph(u, v, a, b, seed); break;
                    case Kind.Patch: c = Shade(a, 0.85f + 0.3f * Noise.Fbm(u * 8f, v * 8f, 8, seed, 4)); break;
                    default: c = Shade(a, 0.9f + 0.2f * Noise.Fbm(u * 6f, v * 6f, 6, seed, 3)); break;
                }
                c = Shade(c, 0.94f + 0.12f * grain);
                c.a = 1f;
                px[y * Size + x] = c;
            }
            return px;
        }

        static Color Shade(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
        static Color Mix(Color a, Color b, float t) { t = Mathf.Clamp01(t); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f); }
        static Color Lighten(Color c, float t) => Mix(c, new Color(1f, 0.97f, 0.9f), t);

        /// <summary>Random brick widths per row, fixed per seed, so stones line up across the tile edge.</summary>
        class CellRows
        {
            public readonly float[][] cuts = new float[8][];
            public CellRows(int seed)
            {
                var r = new System.Random(seed * 31 + 5);
                for (int row = 0; row < cuts.Length; row++)
                {
                    var list = new List<float> { 0f };
                    float at = 0.15f + (float)r.NextDouble() * 0.2f;
                    while (at < 0.88f) { list.Add(at); at += 0.22f + (float)r.NextDouble() * 0.2f; }
                    list.Add(1f);
                    cuts[row] = list.ToArray();
                }
            }
        }

        /// <summary>Stone courses: rows of blocks of varied width, each with its own tint, lit top edge, darker foot and mortar lines.</summary>
        static Color Courses(float u, float v, Color stone, Color mortar, int seed, int rows, float jitter, float mortarWidth, CellRows cells)
        {
            int row = Mathf.FloorToInt(v * rows) % rows;
            float rv = v * rows - Mathf.Floor(v * rows); // 0..1 inside the row
            float[] cut = cells.cuts[row % cells.cuts.Length];
            int idx = 0;
            while (idx < cut.Length - 2 && u >= cut[idx + 1]) idx++;
            float left = cut[idx], right = cut[idx + 1];
            float ru = (u - left) / Mathf.Max(0.001f, right - left);

            float m = mortarWidth * rows;
            float mu = mortarWidth / Mathf.Max(0.001f, right - left);
            if (rv < m || ru < mu * 0.6f || ru > 1f - mu * 0.6f) return Shade(mortar, 0.9f + 0.2f * Noise.Fbm(u * 20f, v * 20f, 20, seed, 2));

            float tint = Hash(row * 97 + idx * 13 + seed) - 0.5f;
            var c = Shade(stone, 1f + tint * 0.16f * jitter);
            // painted shape: light worn top edge, shadowed foot, a soft broad gradient across the stone
            float top = Mathf.Clamp01((rv - 0.82f) / 0.18f);
            float foot = Mathf.Clamp01((0.22f - rv) / 0.22f);
            c = Lighten(c, top * 0.28f);
            c = Shade(c, 1f - foot * 0.22f);
            c = Shade(c, 0.93f + 0.14f * Noise.Fbm(u * 6f + idx, v * 6f, 6, seed + row, 3));
            // chips
            float chip = Noise.Fbm(u * 48f, v * 48f, 48, seed + 9, 2);
            if (chip > 0.8f) c = Shade(c, 0.9f);
            return c;
        }

        /// <summary>Big rock slabs: cells with their own tint, dark cracks between them and light ledges.</summary>
        static Color Slabs(float u, float v, Color rock, Color crack, int seed)
        {
            Noise.Cells(u * 5f, v * 3f, 5, 3, seed, out float d1, out float d2, out int id);
            float edge = d2 - d1;
            if (edge < 0.06f) return Shade(crack, 0.9f + edge * 2f);
            var c = Shade(rock, 0.86f + 0.28f * Hash(id + seed));
            c = Lighten(c, Mathf.Clamp01((edge - 0.06f) * 1.2f) * 0.12f);
            c = Shade(c, 0.88f + 0.24f * Noise.Fbm(u * 10f, v * 10f, 10, seed + 2, 4));
            return c;
        }

        static Color Dirt(float u, float v, Color a, Color b, int seed)
        {
            float n = Noise.Fbm(u * 4f, v * 4f, 4, seed, 5);
            var c = Mix(b, a, n * 1.4f - 0.2f);
            float speck = Noise.Fbm(u * 64f, v * 64f, 64, seed + 5, 2);
            if (speck > 0.68f) c = Shade(c, 0.78f);
            else if (speck < 0.25f) c = Lighten(c, 0.08f);
            float stones = Noise.Fbm(u * 24f, v * 24f, 24, seed + 8, 2);
            if (stones > 0.74f) c = Lighten(c, 0.22f);
            return c;
        }

        static Color Ribbed(float u, float v, Color a, int seed)
        {
            // vertical ribs every 1/10 of the tile, shaded like a bent sheet
            float rib = Mathf.Sin(u * 10f * Mathf.PI * 2f);
            var c = Shade(a, 1f + rib * 0.12f);
            float wear = Noise.Fbm(u * 6f, v * 6f, 6, seed, 4);
            c = Shade(c, 0.85f + 0.25f * wear);
            if (rib > 0.85f && Noise.Fbm(u * 30f, v * 30f, 30, seed + 1, 2) > 0.6f) c = Lighten(c, 0.35f);
            // rust/grime streaks running down
            float streak = Noise.Fbm(u * 40f, v * 2f, 40, seed + 4, 3);
            if (streak > 0.7f) c = Shade(c, 0.88f);
            return c;
        }

        static Color Planks(float u, float v, Color a, Color dark, int seed)
        {
            int plank = Mathf.FloorToInt(v * 6f);
            float pv = v * 6f - plank;
            if (pv < 0.06f) return dark;
            float grain = Noise.Fbm(u * 3f, v * 40f, 3, seed + plank, 3);
            var c = Mix(a, dark, grain * 0.5f);
            c = Shade(c, 0.9f + 0.2f * Hash(plank + seed));
            c = Lighten(c, Mathf.Clamp01((pv - 0.85f) / 0.15f) * 0.2f);
            return c;
        }

        static Color Concrete(float u, float v, Color a, int seed)
        {
            var c = Shade(a, 0.9f + 0.18f * Noise.Fbm(u * 5f, v * 5f, 5, seed, 4));
            if (Noise.Fbm(u * 80f, v * 80f, 80, seed + 1, 1) > 0.82f) c = Shade(c, 0.88f);
            return c;
        }

        static Color DeckPlate(float u, float v, Color a, Color dark, int seed)
        {
            float gu = u * 8f - Mathf.Floor(u * 8f), gv = v * 8f - Mathf.Floor(v * 8f);
            var c = Shade(a, 0.92f + 0.16f * Noise.Fbm(u * 4f, v * 4f, 4, seed, 3));
            if (gu < 0.05f || gv < 0.05f) return dark;
            // little raised studs
            float du = gu - 0.5f, dv = gv - 0.5f;
            if (du * du + dv * dv < 0.03f) c = Lighten(c, 0.15f);
            return c;
        }

        static Color Shutter(float u, float v, Color a, Color band)
        {
            float slat = v * 24f - Mathf.Floor(v * 24f);
            if (v > 0.06f && v < 0.12f) return (Mathf.FloorToInt(u * 10f) % 2 == 0) ? band : Shade(band, 0.25f);
            var c = Shade(a, 0.85f + 0.3f * Mathf.Sin(slat * Mathf.PI));
            if (slat < 0.08f) c = Shade(c, 0.6f);
            return c;
        }

        /// <summary>
        /// One carved glyph per tile, for a band of stone: a framed square key of stepped grooves, cut
        /// darker into the stone with a light lip above each groove.
        /// </summary>
        static Color Glyph(float u, float v, Color stone, Color groove, int seed)
        {
            var c = Shade(stone, 0.92f + 0.14f * Noise.Fbm(u * 4f, v * 4f, 4, seed, 3));
            if (Cut(u, v)) return Shade(groove, 0.85f + 0.2f * Noise.Fbm(u * 16f, v * 16f, 16, seed + 1, 2));
            if (Cut(u, v - 0.025f)) return Lighten(c, 0.25f); // the lit lip just above a groove
            return c;
        }

        static bool Cut(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            if (d > 0.4f && d < 0.44f) return true;                          // frame
            if (d > 0.26f && d < 0.3f) return !(x > 0f && Mathf.Abs(y) < 0.06f);  // middle ring, open on the right
            if (d > 0.12f && d < 0.16f) return !(x < 0f && Mathf.Abs(y) < 0.05f); // inner ring, open on the left
            if (Mathf.Abs(y) < 0.025f && x > 0.12f && x < 0.3f) return true;   // step joining the rings
            return d < 0.05f;                                                    // centre stud
        }

        static Color Bark(float u, float v, Color a, Color dark, int seed)
        {
            float s = Noise.Fbm(u * 16f, v * 2f, 16, seed, 4);
            return Mix(dark, a, s * 1.5f - 0.1f);
        }

        static Color Leaf(float u, float v, Color a, int seed)
        {
            float n = Noise.Fbm(u * 6f, v * 6f, 6, seed, 4);
            var c = Shade(a, 0.75f + 0.5f * n);
            float blob = Noise.Fbm(u * 14f, v * 14f, 14, seed + 6, 2);
            if (blob > 0.66f) c = Lighten(c, 0.2f);
            return c;
        }

        static float Hash(int n)
        {
            unchecked
            {
                uint x = (uint)n * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                x = (x >> 22) ^ x;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Tileable value noise and cells.</summary>
        public static class Noise
        {
            static float Lattice(int x, int y, int period, int seed)
            {
                x = ((x % period) + period) % period;
                y = ((y % period) + period) % period;
                return Hash(x * 73856093 ^ y * 19349663 ^ seed * 83492791);
            }

            static float Value(float x, float y, int period, int seed)
            {
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
                float a = Lattice(x0, y0, period, seed), b = Lattice(x0 + 1, y0, period, seed);
                float c = Lattice(x0, y0 + 1, period, seed), d = Lattice(x0 + 1, y0 + 1, period, seed);
                return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
            }

            /// <summary>Fractal noise in 0..1 that repeats every <paramref name="period"/> units of x and y.</summary>
            public static float Fbm(float x, float y, int period, int seed, int octaves)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                int p = Math.Max(1, period);
                for (int i = 0; i < octaves; i++)
                {
                    sum += Value(x, y, p, seed + i * 17) * amp;
                    norm += amp;
                    x *= 2f; y *= 2f; p *= 2; amp *= 0.5f;
                }
                return sum / norm;
            }

            /// <summary>Tileable cell noise: distances to the nearest two jittered points and the nearest one's id.</summary>
            public static void Cells(float x, float y, int px, int py, int seed, out float d1, out float d2, out int id)
            {
                int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
                d1 = 9f; d2 = 9f; id = 0;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = ((gx % px) + px) % px, wy = ((gy % py) + py) % py;
                    int h = wx * 92837111 ^ wy * 689287499 ^ seed;
                    float ox = gx + 0.15f + 0.7f * Hash(h), oy = gy + 0.15f + 0.7f * Hash(h + 1);
                    float dx = ox - x, dy = oy - y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; id = wx + wy * 131; }
                    else if (d < d2) d2 = d;
                }
            }
        }
    }
}

using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Paints a theme's sky into a 360° panorama (longitude across, latitude up): a gradient from
    /// the zenith through a bright band to a warm horizon, a ring of soft painted clouds low on the
    /// horizon, a big banded planet with a ring, and a small moon. Used with Unity's panoramic skybox.
    /// </summary>
    public static class SkyPainter
    {
        public const int Width = 1024, Height = 512;

        public static Texture2D Paint(EnvTheme t)
        {
            var px = new Color[Width * Height];
            float pYaw = t.planetYaw * Mathf.Deg2Rad, pEl = t.planetElevation * Mathf.Deg2Rad, pR = t.planetSize * Mathf.Deg2Rad;
            var planetDir = Dir(pYaw, pEl);
            var moonDir = Dir(pYaw + 1.9f, pEl + 0.25f);
            float moonR = pR * 0.28f;
            // The planet sits 1 unit away; its ring is tilted toward us so it shows as an ellipse.
            var planetPos = planetDir;
            float planetRadius = Mathf.Tan(pR);
            var side = Normalize(new Vector3(planetDir.z, 0f, -planetDir.x));
            var ringAxis = Normalize(new Vector3(0f, 1f, 0f) - Scale(planetDir, 0.9f) + Scale(side, 0.3f));
            var planetLight = Normalize(Scale(side, -0.8f) + new Vector3(0f, 0.5f, 0f) - Scale(planetDir, 0.4f));

            for (int y = 0; y < Height; y++)
            {
                float el = (y / (float)(Height - 1) - 0.5f) * Mathf.PI; // -90..90 degrees
                for (int x = 0; x < Width; x++)
                {
                    float yaw = x / (float)Width * Mathf.PI * 2f;
                    var d = Dir(yaw, el);
                    Color c = Gradient(t, el);

                    // Clouds: a broken band of soft heaps along the horizon, lit on top, and thin high streaks.
                    float u = x / (float)Width;
                    if (el > -0.02f && el < 0.75f)
                    {
                        float heap = PaintedTextures.Noise.Fbm(u * 9f, el * 3.2f, 9, t.seed + 40, 5);
                        float band = Mathf.Clamp01(1f - Mathf.Abs(el - 0.2f) / 0.3f);
                        float k = Mathf.Clamp01((heap * (0.55f + band) - 0.62f) * 5f);
                        if (k > 0f)
                        {
                            float lit = Mathf.Clamp01((el - 0.05f) / 0.3f + (heap - 0.5f) * 1.5f);
                            var cc = Color.Lerp(t.cloudShade, t.cloud, lit);
                            c = Color.Lerp(c, cc, k * 0.95f);
                        }
                    }
                    if (el > 0.45f && el < 1.2f)
                    {
                        float streak = PaintedTextures.Noise.Fbm(u * 6f, el * 14f, 6, t.seed + 77, 4);
                        float k = Mathf.Clamp01((streak - 0.58f) * 4f) * Mathf.Clamp01(1f - Mathf.Abs(el - 0.8f) / 0.35f);
                        c = Color.Lerp(c, t.cloud, k * 0.45f);
                    }

                    // Planet: a sphere far away with bands, lit from one side, and a tilted ring around it.
                    float tPlanet = RaySphere(d, planetPos, planetRadius);
                    if (tPlanet > 0f)
                    {
                        var n = Scale(Sub(Scale(d, tPlanet), planetPos), 1f / planetRadius);
                        float lat = Dot(n, ringAxis);
                        float wobble = PaintedTextures.Noise.Fbm(x / 48f, y / 48f, 21, t.seed, 3);
                        float bands = 0.5f + 0.5f * Mathf.Sin(lat * 11f + wobble * 2.5f);
                        var pc = Color.Lerp(t.planet, t.planetBand, bands * 0.75f);
                        float light = Mathf.Clamp01(0.3f + 0.85f * Dot(n, planetLight));
                        pc = Color.Lerp(Color.Lerp(t.skyMid, t.planet, 0.4f), pc, light);
                        float rim = Mathf.Clamp01(-Dot(n, d) * 6f);
                        c = Color.Lerp(c, pc, rim);
                    }
                    float tRing = Dot(planetPos, ringAxis) / Dot(d, ringAxis);
                    if (tRing > 0f && (tPlanet <= 0f || tRing < tPlanet))
                    {
                        float r = Length(Sub(Scale(d, tRing), planetPos)) / planetRadius;
                        if (r > 1.3f && r < 2.3f)
                        {
                            float band = 0.7f + 0.3f * Mathf.Sin((r - 1.3f) * 22f);
                            if (r > 1.75f && r < 1.82f) band = 0.15f; // a dark gap between the inner and outer ring
                            float fade = Mathf.Clamp01(Mathf.Min(r - 1.3f, 2.3f - r) * 10f);
                            c = Color.Lerp(c, t.planetRing, band * fade * 0.9f);
                        }
                    }

                    float ma = Angle(d, moonDir);
                    if (ma < moonR)
                    {
                        float crater = PaintedTextures.Noise.Fbm(x / 6f, y / 6f, 64, t.seed + 9, 2);
                        var mc = t.moon * (crater > 0.62f ? 0.85f : 1f);
                        c = Color.Lerp(c, mc, Mathf.Clamp01((moonR - ma) / (moonR * 0.08f)));
                    }
                    c.a = 1f;
                    px[y * Width + x] = c;
                }
            }
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.name = "Sky " + t.id;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        static Color Gradient(EnvTheme t, float el)
        {
            if (el < 0f) return Color.Lerp(t.skyHorizon, t.skyBelow, Mathf.Clamp01(-el / 0.25f));
            float k = el / (Mathf.PI * 0.5f);
            if (k < 0.12f) return Color.Lerp(t.skyHorizon, t.skyMid, k / 0.12f);
            return Color.Lerp(t.skyMid, t.skyZenith, Mathf.Pow((k - 0.12f) / 0.88f, 0.7f));
        }

        /// <summary>Distance along the unit ray d to the near side of a sphere, or -1 if it misses.</summary>
        static float RaySphere(Vector3 d, Vector3 centre, float radius)
        {
            float b = Dot(d, centre);
            float c = Dot(centre, centre) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return -1f;
            float t = b - Mathf.Sqrt(disc);
            return t > 0f ? t : -1f;
        }

        static Vector3 Dir(float yaw, float el) =>
            new Vector3(Mathf.Cos(el) * Mathf.Sin(yaw), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(yaw));

        static float Angle(Vector3 a, Vector3 b) => Mathf.Acos(Mathf.Clamp(Dot(a, b), -1f, 1f));
        static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        static Vector3 Scale(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        static float Length(Vector3 a) => Mathf.Sqrt(Dot(a, a));
        static Vector3 Normalize(Vector3 a) { float l = Length(a); return l < 1e-6f ? a : Scale(a, 1f / l); }
    }
}

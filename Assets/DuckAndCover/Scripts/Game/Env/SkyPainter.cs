using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Paints a theme's sky into a 360° panorama for Unity's panoramic skybox (longitude across,
    /// latitude up, laid out the way Skybox/Panoramic reads it, so things land where the world expects:
    /// the sun's glow sits where the sun light comes from).
    ///
    /// Back to front: a graded sky with a hazy glow along the horizon and a halo round the sun; a big
    /// banded planet with a tilted ring and a soft atmosphere, and a small moon; a deck of fair-weather
    /// clouds seen in perspective (big overhead, packed into thin rows toward the horizon), lit from the
    /// sun's side; high streaks; a ring of towering cloud heaps on the horizon; then two ranges of far
    /// mountains, the far one faded into the haze, so the world doesn't end at a flat line.
    /// </summary>
    public static class SkyPainter
    {
        public const int Width = 2048, Height = 1024;

        /// <summary>The direction the sun light comes from (towards the sun), for a light turned by <paramref name="euler"/>.</summary>
        public static Vector3 SunDirection(Vector3 euler)
        {
            // Quaternion.Euler(x, y, z) * forward: pitch x down, then yaw y round the vertical. Roll doesn't move it.
            float x = euler.x * Mathf.Deg2Rad, y = euler.y * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(y) * Mathf.Cos(x), -Mathf.Sin(x), Mathf.Cos(y) * Mathf.Cos(x));
            return new Vector3(-forward.x, -forward.y, -forward.z);
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> cache = new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>The theme's sky texture, painted once per theme (about a second's work) and kept.</summary>
        public static Texture2D Paint(EnvTheme t)
        {
            if (cache.TryGetValue(t.id, out var found) && found != null) return found;
            var px = Pixels(t);
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.name = "Sky " + t.id;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(px);
            tex.Apply(false);
            cache[t.id] = tex;
            return tex;
        }

        /// <summary>The panorama's pixels, row by row from the bottom (the nadir) to the top (the zenith).</summary>
        public static Color[] Pixels(EnvTheme t)
        {
            var px = new Color[Width * Height];
            var sun = SunDirection(t.sunEuler);
            float sunAzimuth = Mathf.Atan2(sun.x, sun.z);

            float pYaw = t.planetYaw * Mathf.Deg2Rad, pEl = t.planetElevation * Mathf.Deg2Rad, pR = t.planetSize * Mathf.Deg2Rad;
            var planetPos = Compass(pYaw, pEl);
            var moonDir = Compass(pYaw + 1.15f, pEl + 0.2f);
            float moonR = pR * 0.24f;
            // The planet sits 1 unit away; its ring is tilted toward us so it shows as an ellipse.
            float planetRadius = Mathf.Tan(pR);
            var side = Normalize(new Vector3(planetPos.z, 0f, -planetPos.x));
            var ringAxis = Normalize(new Vector3(0f, 1f, 0f) - Scale(planetPos, 0.9f) + Scale(side, 0.3f));
            // The planet is lit from the sun's side.
            var planetLight = Normalize(Sub(Scale(sun, 1f), Scale(planetPos, 0.6f)));

            // Mountain ridges per column, far range and near range (radians above the horizon).
            var far = new float[Width];
            var near = new float[Width];
            for (int x = 0; x < Width; x++)
            {
                float u = x / (float)Width;
                far[x] = 0.03f + 0.11f * Ridge(PaintedTextures.Noise.Fbm(u * 7f, 3.3f, 7, t.seed + 301, 5));
                float n = PaintedTextures.Noise.Fbm(u * 13f, 8.7f, 13, t.seed + 517, 5);
                // Near range: flat-topped mesas with steep sides, the jungle-moon look.
                near[x] = 0.01f + 0.1f * Mathf.Clamp01((n - 0.4f) * 3.4f) + 0.01f * PaintedTextures.Noise.Fbm(u * 60f, 1.5f, 60, t.seed + 11, 2);
            }

            for (int y = 0; y < Height; y++)
            {
                float el = ((y + 0.5f) / Height - 0.5f) * Mathf.PI; // -90..90 degrees
                for (int x = 0; x < Width; x++)
                {
                    float u = (x + 0.5f) / Width;
                    var d = Panorama(u, el);
                    float s = Dot(d, sun);
                    Color c = Gradient(t, el, s);

                    // ---- Planet, ring, atmosphere and moon (far behind the clouds) ----
                    float tPlanet = RaySphere(d, planetPos, planetRadius);
                    if (tPlanet > 0f)
                    {
                        var n = Scale(Sub(Scale(d, tPlanet), planetPos), 1f / planetRadius);
                        float lat = Dot(n, ringAxis);
                        float wobble = PaintedTextures.Noise.Fbm(u * 64f, el * 40f, 64, t.seed, 3);
                        float bands = 0.5f + 0.5f * Mathf.Sin(lat * 11f + wobble * 2.5f);
                        var pc = Color.Lerp(t.planet, t.planetBand, bands * 0.75f);
                        float light = Mathf.Clamp01(0.25f + 0.9f * Dot(n, planetLight));
                        pc = Color.Lerp(Color.Lerp(t.skyMid, t.planet, 0.35f), pc, light);
                        // A thin bright rim of atmosphere on the lit limb.
                        float limb = 1f - Mathf.Clamp01(-Dot(n, d));
                        pc = Color.Lerp(pc, t.planetGlow, Mathf.Pow(limb, 3f) * light * 0.6f);
                        float edge = Mathf.Clamp01(-Dot(n, d) * 6f);
                        c = Color.Lerp(c, pc, edge * 0.97f);
                    }
                    else
                    {
                        // Atmosphere glow just outside the disc.
                        float ang = Angle(d, planetPos);
                        float outside = (ang - pR) / (pR * 0.18f);
                        if (outside > 0f && outside < 6f) c = Add(c, t.planetGlow, Mathf.Exp(-outside) * 0.28f);
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
                            c = Color.Lerp(c, t.planetRing, band * fade * 0.85f);
                        }
                    }
                    float ma = Angle(d, moonDir);
                    if (ma < moonR)
                    {
                        float crater = PaintedTextures.Noise.Fbm(u * 512f, el * 160f, 512, t.seed + 9, 2);
                        var mc = t.moon * (crater > 0.6f ? 0.86f : 1f);
                        c = Color.Lerp(c, mc, Mathf.Clamp01((moonR - ma) / (moonR * 0.1f)));
                    }

                    // ---- The sun disc: a hot core with a soft edge ----
                    float sunAng = Mathf.Acos(Mathf.Clamp(s, -1f, 1f));
                    if (sunAng < 0.05f) c = Color.Lerp(c, Color.white, Mathf.Clamp01((0.03f - sunAng) / 0.008f));

                    if (el > 0f)
                    {
                        // ---- High streaks on a plane far up ----
                        float hy = Mathf.Max(d.y, 0.015f);
                        float cx = d.x / hy * 0.35f, cz = d.z / hy * 0.35f;
                        float streak = PaintedTextures.Noise.Fbm(cx * 0.45f + 50f, cz * 2.4f + 50f, 1024, t.seed + 77, 4);
                        float ka = Mathf.Clamp01((streak - 0.56f) * 3.5f) * Mathf.Clamp01((d.y - 0.05f) / 0.25f);
                        c = Color.Lerp(c, Color.Lerp(t.cloud, t.sun, 0.25f), ka * 0.4f);

                        // ---- Fair-weather cloud deck in perspective ----
                        float px0 = d.x / hy * 0.9f, pz0 = d.z / hy * 0.9f;
                        float dens = CloudDensity(t, px0, pz0);
                        if (dens > 0.001f)
                        {
                            // Lit from the sun's side: thinner toward the sun means more light gets through.
                            float toward = CloudDensity(t, px0 + sun.x * 0.25f, pz0 + sun.z * 0.25f);
                            float lit = Mathf.Clamp01(0.55f + (dens - toward) * 2.2f + d.y * 0.4f);
                            var cc = Color.Lerp(t.cloudShade, t.cloud, lit);
                            // Silver lining where a thin edge stands in front of the sun.
                            cc = Add(cc, t.sun, Mathf.Pow(Mathf.Max(0f, s), 10f) * (1f - dens) * 0.9f);
                            // Distant clouds melt into the horizon haze.
                            float near01 = Mathf.Clamp01((d.y - 0.02f) / 0.22f);
                            cc = Color.Lerp(Color.Lerp(t.skyHorizon, t.haze, 0.5f), cc, 0.35f + 0.65f * near01);
                            float alpha = dens * Mathf.Clamp01((d.y - 0.012f) / 0.05f);
                            c = Color.Lerp(c, cc, alpha);
                        }
                    }

                    // ---- Towering heaps on the horizon, behind the mountains ----
                    if (el > -0.02f && el < 0.5f)
                    {
                        float heap = PaintedTextures.Noise.Fbm(u * 11f, el * 3.6f, 11, t.seed + 40, 5);
                        float top = 0.07f + 0.22f * Mathf.Clamp01((PaintedTextures.Noise.Fbm(u * 5f, 0.5f, 5, t.seed + 41, 3) - 0.45f) * 2.2f);
                        float body = Mathf.Clamp01((top - el) / 0.06f);
                        float k = Mathf.Clamp01((heap * (0.6f + body) - 0.66f) * 5f);
                        if (k > 0f)
                        {
                            float lit = Mathf.Clamp01((el - 0.02f) / Mathf.Max(0.05f, top) + (heap - 0.5f) * 1.2f);
                            float sunSide = 0.5f + 0.5f * Mathf.Cos(Mathf.Atan2(d.x, d.z) - sunAzimuth);
                            var cc = Color.Lerp(t.cloudShade, t.cloud, lit * (0.65f + 0.35f * sunSide));
                            cc = Color.Lerp(t.haze, cc, 0.55f);
                            c = Color.Lerp(c, cc, k * 0.9f);
                        }
                    }

                    // ---- Far mountains, then the near mesas ----
                    // Below the horizon the ranges' feet sink into haze (the floor of the world hides them anyway).
                    float hf = far[x];
                    if (el < hf && el > -0.06f)
                    {
                        float shade = SlopeLight(far, x, u, sunAzimuth, 0.6f);
                        var mc = Color.Lerp(Color.Lerp(t.mountainFar, t.skyMid, 0.1f) * (0.85f + 0.3f * shade), t.haze, 0.42f);
                        // Haze thickens toward the foot of the range.
                        mc = Color.Lerp(mc, t.haze, Mathf.Clamp01((hf - el) / 0.12f) * 0.35f);
                        c = Color.Lerp(c, mc, Mathf.Clamp01((hf - el) / 0.0025f) * Mathf.Clamp01((el + 0.06f) / 0.05f));
                    }
                    float hn = near[x];
                    if (el < hn && el > -0.06f)
                    {
                        float shade = SlopeLight(near, x, u, sunAzimuth, 1f);
                        // A lit band of jungle along the mesa tops, darker cliffs below.
                        float topBand = Mathf.Clamp01(1.4f - (hn - el) / 0.016f);
                        var mc = Color.Lerp(t.mountainNear, Color.Lerp(t.mountainNear, t.canopyFar, 0.8f), topBand);
                        mc = mc * (0.85f + 0.3f * shade);
                        // Rock strata: faint horizontal bands down the cliffs.
                        float strata = PaintedTextures.Noise.Fbm(u * 24f, el * 260f, 24, t.seed + 5, 3);
                        mc = mc * (0.92f + 0.16f * strata * (1f - topBand));
                        mc = Color.Lerp(mc, t.haze, 0.18f + Mathf.Clamp01((hn - el) / 0.1f) * 0.3f);
                        c = Color.Lerp(c, mc, Mathf.Clamp01((hn - el) / 0.0025f) * Mathf.Clamp01((el + 0.06f) / 0.05f));
                    }

                    c.a = 1f;
                    px[y * Width + x] = c;
                }
            }
            return px;
        }

        /// <summary>How much cloud covers a point on the cloud deck, 0..1.</summary>
        static float CloudDensity(EnvTheme t, float x, float z)
        {
            float n = PaintedTextures.Noise.Fbm(x * 0.55f + 100f, z * 0.55f + 100f, 1024, t.seed + 23, 5);
            return Mathf.Clamp01((n - (1f - t.cloudCover)) * 4.5f);
        }

        /// <summary>Faces of a ridge that turn toward the sun's side of the sky are lighter (0..1).</summary>
        static float SlopeLight(float[] ridge, int x, float u, float sunAzimuth, float gain)
        {
            int w = ridge.Length;
            float slope = (ridge[(x + 6) % w] - ridge[(x - 6 + w) % w]) * w * (0.05f / 6f);
            // The azimuth of this column and which way along the panorama the sun is.
            float phi = (0.5f - u) * Mathf.PI * 2f;
            float az = Mathf.Atan2(Mathf.Cos(phi), Mathf.Sin(phi)); // compass azimuth of the column (see Panorama)
            float toward = Mathf.Sin(sunAzimuth - az);
            return Mathf.Clamp01(0.5f + slope * toward * gain * 1.5f);
        }

        static float Ridge(float n) { float r = 1f - Mathf.Abs(n * 2f - 1f); return r * r; }

        static Color Gradient(EnvTheme t, float el, float sunDot)
        {
            Color c;
            if (el < 0f) c = Color.Lerp(t.haze, t.skyBelow, Mathf.Clamp01(-el / 0.3f));
            else
            {
                float k = el / (Mathf.PI * 0.5f);
                if (k < 0.16f) c = Color.Lerp(t.skyHorizon, t.skyMid, Mathf.SmoothStep(0f, 1f, k / 0.16f));
                else c = Color.Lerp(t.skyMid, t.skyZenith, Mathf.Pow((k - 0.16f) / 0.84f, 0.75f));
                // A bright band of haze hugging the horizon.
                c = Color.Lerp(c, t.haze, Mathf.Exp(-el * 14f) * 0.55f);
            }
            // The sun's halo, wide and faint, then tight and bright; the horizon glows warmer on its side.
            float sd = Mathf.Max(0f, sunDot);
            c = Add(c, t.sun, Mathf.Pow(sd, 6f) * 0.18f + Mathf.Pow(sd, 200f) * 0.45f);
            c = Add(c, t.skyHorizon, Mathf.Pow(sd, 2f) * Mathf.Exp(-Mathf.Abs(el) * 8f) * 0.2f);
            return c;
        }

        static Color Add(Color c, Color light, float k) =>
            new Color(Mathf.Min(1f, c.r + light.r * k), Mathf.Min(1f, c.g + light.g * k), Mathf.Min(1f, c.b + light.b * k), 1f);

        /// <summary>
        /// The world direction a panorama pixel shows. Skybox/Panoramic maps a direction to
        /// u = 0.5 - atan2(z, x) / 2π and v = 1 - acos(y) / π; this is the inverse.
        /// </summary>
        static Vector3 Panorama(float u, float el)
        {
            float phi = (0.5f - u) * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(phi), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(phi));
        }

        /// <summary>A direction from a compass yaw (0 = +z, 90° = +x) and an elevation.</summary>
        static Vector3 Compass(float yaw, float el) =>
            new Vector3(Mathf.Cos(el) * Mathf.Sin(yaw), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(yaw));

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

        static float Angle(Vector3 a, Vector3 b) => Mathf.Acos(Mathf.Clamp(Dot(a, b) / Mathf.Max(1e-6f, Length(a) * Length(b)), -1f, 1f));
        static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        static Vector3 Scale(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        static float Length(Vector3 a) => Mathf.Sqrt(Dot(a, a));
        static Vector3 Normalize(Vector3 a) { float l = Length(a); return l < 1e-6f ? a : Scale(a, 1f / l); }
    }
}

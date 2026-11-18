using System;
using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Lays an <see cref="EnvTheme"/> over a built greybox: painted textures and chamfered meshes on the
    /// blocks, trims and plants on them, cliffs, giant trees and overhead gantries around the map, and
    /// the sky, sun, ambient light and haze.
    ///
    /// Fair-play rules for dressing, so the art can never change how the map plays:
    /// - A block keeps its transform and collider; only its mesh and material change.
    /// - Nothing added has a collider (the rules and shots only see the blocks).
    /// - Anything added inside the play area is flat on the ground, flush with a block (at most a few
    ///   centimetres proud of it), or high overhead. Tall things (cliffs, trees, gantry legs) stand
    ///   outside the outer walls.
    /// Dressing works from block kinds and the map's bounds, never from block names, so it fits any layout.
    /// </summary>
    public static class MapDresser
    {
        const float GlyphSize = 0.5f;

        static EnvTheme ThemeAt(Context c, float x, float z)
        {
            var t = EnvTheme.Find(c.map.ThemeAt(x, z));
            return t == null ? c.theme : t;
        }

        /// <summary>How far trims may stand proud of a block's faces, in metres.</summary>
        public const float MaxProud = 0.07f;

        public class Context
        {
            public EnvTheme theme;
            /// <summary>The theme of the piece being dressed: the map's own, or an art region's.</summary>
            public EnvTheme active;
            public MapDef map;
            public Transform root;
            public System.Random rng;
            /// <summary>False for a second, preview map in the same scene: keep the sky and light already set.</summary>
            public bool lighting = true;
            public readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        }

        public static Context Begin(MapDef map, Transform root, EnvTheme theme)
        {
            if (theme == null) return null;
            var ctx = new Context { theme = theme, active = theme, map = map, root = root, rng = new System.Random(theme.seed * 7919 + StableHash(map.id)) };
            return ctx;
        }

        // ---------------- materials ----------------

        static Material Mat(Context c, PaintedTextures.Kind kind, Color a, Color b, bool glow = false)
        {
            string key = kind + "|" + (Color32)a + "|" + (Color32)b + "|" + glow;
            if (c.materials.TryGetValue(key, out var m) && m != null) return m;
            m = Paint.Textured(PaintedTextures.Get(kind, a, b, c.active.seed), glow);
            c.materials[key] = m;
            return m;
        }

        static Material Plain(Context c, Color a, bool glow = false) => Mat(c, PaintedTextures.Kind.Plain, a, a, glow);

        // ---------------- blocks ----------------

        /// <summary>Dresses one block that <see cref="MapScene"/> built (same transform, same collider).</summary>
        public static void DressBlock(Context c, GameObject go, MapBlock b)
        {
            if (c == null || go == null) return;
            var size = b.size.ToVector3();
            var centre = b.center.ToVector3();
            bool outer = IsOuter(c.map, b);
            // The floor and the outer walls span the whole map, so they always wear the map's own theme.
            var t = b.kind == BlockKind.Ground || outer ? c.theme : ThemeAt(c, b.center.x, b.center.z);
            c.active = t;
            bool bigCrate = b.kind == BlockKind.Crate && Math.Max(b.size.x, b.size.z) > 3.5f;

            if (!string.IsNullOrEmpty(b.style) && b.kind != BlockKind.Ground && DressStyled(c, go, b, t)) return;

            Material mat;
            float bevel, tile;
            switch (b.kind)
            {
                case BlockKind.Ground: mat = Mat(c, PaintedTextures.Kind.Ground, t.groundA, t.groundB); bevel = 0f; tile = 9f; break;
                case BlockKind.Wall when outer: mat = Mat(c, PaintedTextures.Kind.Rock, t.rock, t.rockCrack); bevel = 0.25f; tile = 9f; break;
                case BlockKind.Wall: mat = Mat(c, PaintedTextures.Kind.Stone, t.stone, t.stoneMortar); bevel = 0.07f; tile = 4f; break;
                case BlockKind.LowCover: mat = Mat(c, PaintedTextures.Kind.Concrete, t.cover, t.cover); bevel = 0.1f; tile = 3f; break;
                case BlockKind.Crate when bigCrate:
                    mat = Mat(c, PaintedTextures.Kind.Ribbed, Pick(t.containers, b), Pick(t.containers, b)); bevel = 0.06f; tile = 3f; break;
                case BlockKind.Crate: mat = Mat(c, PaintedTextures.Kind.Planks, t.wood, t.woodDark); bevel = 0.06f; tile = 1.5f; break;
                case BlockKind.Platform: mat = Mat(c, PaintedTextures.Kind.Deck, t.deck, t.deckDark); bevel = 0.08f; tile = 2.5f; break;
                case BlockKind.Ramp: mat = Mat(c, PaintedTextures.Kind.Deck, t.deck, t.deckDark); bevel = 0.05f; tile = 2.5f; break;
                case BlockKind.Shutter: mat = Mat(c, PaintedTextures.Kind.Shutter, t.shutter, t.shutterBand); bevel = 0.03f; tile = Math.Max(1f, b.size.y); break;
                default: return; // decals (spawn pads) keep their team colour
            }

            var filter = go.GetComponent<MeshFilter>();
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = mat;
            if (filter != null)
            {
                // Ramps are tilted slabs: their transform scale is (width, thickness, length), not the block's box.
                var meshSize = b.kind == BlockKind.Ramp ? go.transform.localScale : size;
                filter.sharedMesh = EnvMesh.ChamferBox(meshSize, bevel, centre, tile);
            }

            switch (b.kind)
            {
                case BlockKind.Wall when !outer: WallTrims(c, b); break;
                case BlockKind.Wall: WallTopPlants(c, b); break;
                case BlockKind.LowCover: Band(c, b, 0.6f, 0.16f, Plain(c, t.coverStripe)); break;
                case BlockKind.Crate when bigCrate: CornerPosts(c, b); break;
                case BlockKind.Crate:
                    Band(c, b, 0.08f, 0.1f, Plain(c, t.trimBase));
                    Band(c, b, 0.92f, 0.1f, Plain(c, t.trimBase));
                    break;
                case BlockKind.Platform: Band(c, b, 1f - 0.12f / Math.Max(0.2f, b.size.y), 0.07f, Plain(c, t.glow, true)); break;
            }
        }

        // ---------------- natural walls: tree lines and rocks ----------------

        /// <summary>
        /// Draws a block by its art style instead of its kind. The block keeps its box collider, so the
        /// drawing fills the box closely: a tree line is a dense hedge with trunks rising out of it and
        /// canopies high overhead; rocks are a chunky outcrop with boulders inside its footprint.
        /// </summary>
        static bool DressStyled(Context c, GameObject go, MapBlock b, EnvTheme t)
        {
            var size = b.size.ToVector3();
            var centre = b.center.ToVector3();
            var filter = go.GetComponent<MeshFilter>();
            var renderer = go.GetComponent<Renderer>();
            switch (b.style)
            {
                case "treeline":
                {
                    var hedge = Mat(c, PaintedTextures.Kind.Leaf, Shade(Pick(t.canopy, b), 0.72f), Shade(Pick(t.canopy, b), 0.72f));
                    if (renderer != null) renderer.sharedMaterial = hedge;
                    if (filter != null) filter.sharedMesh = EnvMesh.ChamferBox(size, Math.Min(0.7f, Math.Min(size.x, size.z) * 0.3f), centre, 2.5f);
                    // Leafy lumps on the hedge, inside its box, so it reads as bushes rather than a block.
                    var leaf = Mat(c, PaintedTextures.Kind.Leaf, Pick(t.canopy, b), Pick(t.canopy, b));
                    int lumps = 2 + c.rng.Next(2);
                    for (int i = 0; i < lumps; i++)
                    {
                        float sx = size.x * Range(c, 0.6f, 0.95f), sz = size.z * Range(c, 0.6f, 0.95f), sy = size.y * Range(c, 0.35f, 0.5f);
                        float px = b.center.x + Range(c, -1f, 1f) * (size.x - sx) * 0.5f;
                        float pz = b.center.z + Range(c, -1f, 1f) * (size.z - sz) * 0.5f;
                        float py = b.Min.y + Range(c, sy * 0.5f, size.y - sy * 0.5f);
                        Shape(c, PrimitiveType.Sphere, "Bush", new Vector3(px, py, pz), new Vector3(sx, sy, sz), leaf);
                    }
                    // Now and then a tree grows out of the hedge: trunk through the top, canopy well overhead.
                    if (c.rng.NextDouble() < 0.45) Tree(c, new Vector3(b.center.x, b.Max.y - 1f, b.center.z), Range(c, 3.5f, 6f), Range(c, 2.4f, 3.6f));
                    return true;
                }
                case "rocks":
                {
                    var rock = Mat(c, PaintedTextures.Kind.Rock, t.rock, t.rockCrack);
                    if (renderer != null) renderer.sharedMaterial = rock;
                    if (filter != null) filter.sharedMesh = EnvMesh.ChamferBox(size, Math.Min(0.6f, Math.Min(size.x, Math.Min(size.y, size.z)) * 0.3f), centre, 6f);
                    // A boulder on top of the outcrop, turned a little, kept inside the footprint and below the top.
                    float bs = Math.Min(size.x, size.z) * Range(c, 0.5f, 0.65f), bh = size.y * Range(c, 0.3f, 0.45f);
                    float yaw = Range(c, -20f, 20f);
                    var boulder = Box(c, "Boulder", new Vector3(b.center.x, b.Max.y - bh * 0.5f + 0.02f, b.center.z), new Vector3(bs, bh, bs), rock, bs * 0.25f, 6f);
                    boulder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    // Moss on top.
                    var moss = Mat(c, PaintedTextures.Kind.Patch, t.patch, t.patch);
                    Shape(c, PrimitiveType.Sphere, "Moss", new Vector3(b.center.x, b.Max.y - 0.05f, b.center.z), new Vector3(size.x * 0.8f, 0.12f, size.z * 0.8f), moss);
                    return true;
                }
            }
            return false;
        }

        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        /// <summary>A building wall: dark base course, a cap course and a glowing strip near the top, vines down its faces.</summary>
        static void WallTrims(Context c, MapBlock b)
        {
            var t = c.active;
            if (b.Min.y < 0.05f) BandAbs(c, b, b.Min.y, Math.Min(0.5f, b.size.y * 0.3f), Plain(c, t.trimBase), 0.06f);
            if (b.size.y >= 2.5f || b.Min.y > 0.05f)
            {
                BandAbs(c, b, b.Max.y - 0.3f, 0.3f, Plain(c, t.trimBase), 0.06f);
                if (b.size.y >= 2.5f) BandAbs(c, b, b.Max.y - 0.62f, 0.09f, Plain(c, t.glow, true), 0.02f);
            }
            if (t.glyphs && b.size.y >= 3f && b.Min.y < 0.05f)
            {
                // Carved glyphs, one per half metre. The band starts on a half-metre line so each glyph
                // lands whole in the world-projected texture.
                float bottom = Mathf.Floor((b.Max.y - 1.25f) / GlyphSize) * GlyphSize;
                var glyphs = Mat(c, PaintedTextures.Kind.Glyph, t.stone, t.glyph);
                var size = new Vector3(b.size.x + 0.06f, GlyphSize, b.size.z + 0.06f);
                var centre = new Vector3(b.center.x, bottom + GlyphSize * 0.5f, b.center.z);
                var go = Shape(c, PrimitiveType.Cube, "Glyphs", centre, size, glyphs);
                var f = go.GetComponent<MeshFilter>();
                if (f != null) f.sharedMesh = EnvMesh.ChamferBox(size, 0.02f, centre, GlyphSize);
            }
            if (t.vines && b.size.y >= 3.5f && c.rng.NextDouble() < 0.55) Vines(c, b);
        }

        static void Vines(Context c, MapBlock b)
        {
            bool alongX = b.size.x >= b.size.z;
            float length = alongX ? b.size.x : b.size.z;
            float half = (alongX ? b.size.z : b.size.x) * 0.5f;
            int count = Math.Max(1, (int)(length / 3.5f));
            var leaf = Mat(c, PaintedTextures.Kind.Leaf, c.active.leaf, c.active.leaf);
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < count; i++)
            {
                if (c.rng.NextDouble() < 0.4) continue;
                float along = ((float)c.rng.NextDouble() - 0.5f) * (length - 1f);
                float drop = 0.8f + (float)c.rng.NextDouble() * Math.Min(2.6f, b.size.y * 0.55f);
                float width = 0.5f + (float)c.rng.NextDouble() * 0.7f;
                float off = half + 0.03f;
                var pos = alongX
                    ? new Vector3(b.center.x + along, b.Max.y - 0.3f - drop * 0.5f, b.center.z + side * off)
                    : new Vector3(b.center.x + side * off, b.Max.y - 0.3f - drop * 0.5f, b.center.z + along);
                var scale = alongX ? new Vector3(width, drop, 0.06f) : new Vector3(0.06f, drop, width);
                Shape(c, PrimitiveType.Sphere, "Vine", pos, scale, leaf);
            }
        }

        static void WallTopPlants(Context c, MapBlock b)
        {
            var leaf = Mat(c, PaintedTextures.Kind.Leaf, c.active.leaf, c.active.leaf);
            bool alongX = b.size.x >= b.size.z;
            float length = alongX ? b.size.x : b.size.z;
            int count = (int)(length / 5f);
            for (int i = 0; i < count; i++)
            {
                float along = ((float)c.rng.NextDouble() - 0.5f) * length;
                var p = alongX ? new Vector3(b.center.x + along, b.Max.y, b.center.z) : new Vector3(b.center.x, b.Max.y, b.center.z + along);
                Fern(c, p, 1.2f + (float)c.rng.NextDouble() * 1.2f, leaf);
            }
        }

        static void CornerPosts(Context c, MapBlock b)
        {
            var m = Plain(c, c.active.trimBase);
            const float w = 0.2f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var p = new Vector3(b.center.x + sx * (b.size.x * 0.5f - w * 0.5f + 0.02f), b.center.y, b.center.z + sz * (b.size.z * 0.5f - w * 0.5f + 0.02f));
                Box(c, "Post", p, new Vector3(w, b.size.y + 0.02f, w), m, 0.03f);
            }
        }

        /// <summary>A band around a block at a fraction of its height.</summary>
        static void Band(Context c, MapBlock b, float at, float height, Material m) =>
            BandAbs(c, b, b.Min.y + b.size.y * at - height * 0.5f, height, m, 0.03f);

        /// <summary>A band around a block from height <paramref name="bottom"/>, standing <paramref name="proud"/> out from its faces.</summary>
        static void BandAbs(Context c, MapBlock b, float bottom, float height, Material m, float proud)
        {
            proud = Math.Min(proud, MaxProud);
            var size = new Vector3(b.size.x + proud * 2f, height, b.size.z + proud * 2f);
            Box(c, "Trim", new Vector3(b.center.x, bottom + height * 0.5f, b.center.z), size, m, Math.Min(0.04f, height * 0.3f));
        }

        // ---------------- around the map ----------------

        /// <summary>Everything outside the blocks: sky and light, outer ground, cliffs, trees, gantries, ground patches and zone paving.</summary>
        public static void DressWorld(Context c, Light sun)
        {
            if (c == null) return;
            var t = c.theme;
            c.active = t;
            var map = c.map;
            float hx = map.halfX, hz = map.halfZ;

            if (c.lighting) Lighting(c, sun);

            // Ground beyond the walls, just under the map's own floor.
            var groundMat = Mat(c, PaintedTextures.Kind.Ground, t.groundA, t.groundB);
            var outerSize = new Vector3(hx * 2f + 260f, 1f, hz * 2f + 260f);
            var outerGround = Shape(c, PrimitiveType.Cube, "OuterGround", new Vector3(0f, -0.53f, 0f), outerSize, groundMat);
            var f = outerGround.GetComponent<MeshFilter>();
            if (f != null) f.sharedMesh = EnvMesh.ChamferBox(outerSize, 0f, new Vector3(0f, -0.53f, 0f), 9f);

            // Patches on the floor (moss, frost...) and paving under each zone: flat, 1 cm high.
            var patch = Mat(c, PaintedTextures.Kind.Patch, t.patch, t.patch);
            int patches = (int)(hx * hz / 55f);
            for (int i = 0; i < patches; i++)
            {
                float x = Range(c, -hx + 2f, hx - 2f), z = Range(c, -hz + 2f, hz - 2f);
                float r = Range(c, 1.2f, 3.2f);
                if (ThemeAt(c, x, z).courtyard) continue; // courtyards are paved
                // Each patch a hair thicker than the last, so overlapping patches never flicker.
                float th = 0.003f + (i % 16) * 0.0003f;
                Shape(c, PrimitiveType.Cylinder, "Patch", new Vector3(x, th, z), new Vector3(r * 2f, th, r * Range(c, 1.0f, 1.8f)), patch);
            }
            // Courtyards: flagstones over a region's whole floor, 6 mm high (under the zone paving).
            foreach (var region in map.artRegions)
            {
                var rt = EnvTheme.Find(region.theme);
                if (rt == null || !rt.courtyard) continue;
                c.active = rt;
                float x0 = Math.Max(region.minX, -hx), x1 = Math.Min(region.maxX, hx);
                float z0 = Math.Max(region.minZ, -hz), z1 = Math.Min(region.maxZ, hz);
                if (x1 <= x0 || z1 <= z0) continue;
                var size = new Vector3(x1 - x0, 0.006f, z1 - z0);
                var centre = new Vector3((x0 + x1) * 0.5f, 0.003f, (z0 + z1) * 0.5f);
                var floor = Shape(c, PrimitiveType.Cube, "Courtyard", centre, size, Mat(c, PaintedTextures.Kind.Paving, rt.paving, rt.pavingMortar));
                var ff = floor.GetComponent<MeshFilter>();
                if (ff != null) ff.sharedMesh = EnvMesh.ChamferBox(size, 0f, centre, 3f);
            }
            foreach (var zone in map.zones)
            {
                c.active = ThemeAt(c, zone.center.x, zone.center.z);
                var paving = Mat(c, PaintedTextures.Kind.Paving, c.active.paving, c.active.pavingMortar);
                float r = zone.radius + 1.5f;
                Shape(c, PrimitiveType.Cylinder, "Paving " + zone.label, zone.center.ToVector3() + new Vector3(0f, 0.008f, 0f), new Vector3(r * 2f, 0.008f, r * 2f), paving);
            }

            c.active = t;
            Paths(c);
            Cliffs(c, hx, hz);
            if (t.mushroomTrees) Trees(c, hx, hz);
            if (t.overheadGantries) Gantries(c, hx, hz);
        }

        static void Lighting(Context c, Light sun)
        {
            var t = c.theme;
            if (sun != null)
            {
                sun.color = t.sun;
                sun.intensity = t.sunIntensity;
                sun.transform.rotation = Quaternion.Euler(t.sunEuler.x, t.sunEuler.y, t.sunEuler.z);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = t.ambientSky;
            RenderSettings.ambientEquatorColor = t.ambientEquator;
            RenderSettings.ambientGroundColor = t.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = t.fog;
            RenderSettings.fogStartDistance = t.fogStart;
            RenderSettings.fogEndDistance = t.fogEnd;

            var skyShader = Shader.Find("Skybox/Panoramic");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "Sky " + t.id };
                sky.SetTexture("_MainTex", SkyPainter.Paint(t));
                if (sky.HasProperty("_Mapping")) sky.SetFloat("_Mapping", 1f);
                if (sky.HasProperty("_ImageType")) sky.SetFloat("_ImageType", 0f);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1f);
                sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
                RenderSettings.skybox = sky;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                if (skyShader != null) cam.clearFlags = CameraClearFlags.Skybox;
                else { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = t.skyHorizon; }
                cam.farClipPlane = Math.Max(cam.farClipPlane, 700f);
            }
        }

        /// <summary>
        /// Paints each art path flat on the ground: a lane of packed dirt with darker verges, or a narrow
        /// mossy trail with a leafy arch overhead where it leaves the lane (a jungle entrance).
        /// Strips sit 1 to 1.2 cm up: over floor patches, under zone paving.
        /// </summary>
        static void Paths(Context c)
        {
            foreach (var path in c.map.paths)
            {
                if (path.points.Count < 2) continue;
                c.active = ThemeAt(c, path.points[0].x, path.points[0].z);
                var t = c.active;
                bool lane = path.style == "lane";
                var top = lane ? Mat(c, PaintedTextures.Kind.Ground, t.laneA, t.laneB) : Mat(c, PaintedTextures.Kind.Patch, t.trail, t.trail);
                var edge = Mat(c, PaintedTextures.Kind.Patch, t.verge, t.verge);
                for (int i = 0; i + 1 < path.points.Count; i++)
                {
                    var a = path.points[i].ToVector3();
                    var b = path.points[i + 1].ToVector3();
                    if (lane) Strip(c, a, b, path.width + 1.6f, 0.010f, edge);
                    Strip(c, a, b, path.width, lane ? 0.012f : 0.011f, top);
                }
                // Round joints so bends read as one curving path.
                foreach (var p in path.points)
                {
                    var at = p.ToVector3();
                    if (lane) Shape(c, PrimitiveType.Cylinder, "Verge", new Vector3(at.x, 0.005f, at.z), new Vector3(path.width + 1.6f, 0.005f, path.width + 1.6f), edge);
                    Shape(c, PrimitiveType.Cylinder, "Joint", new Vector3(at.x, lane ? 0.006f : 0.0055f, at.z), new Vector3(path.width, lane ? 0.006f : 0.0055f, path.width), top);
                }
                if (path.style == "trail") Gateway(c, path);
            }
            c.active = c.theme;
        }

        /// <summary>A flat strip from a to b on the ground, its top at <paramref name="height"/>.</summary>
        static void Strip(Context c, Vector3 a, Vector3 b, float width, float height, Material m)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) return;
            var size = new Vector3(width, height, len);
            var centre = new Vector3((a.x + b.x) * 0.5f, height * 0.5f, (a.z + b.z) * 0.5f);
            var go = Box(c, "Path", centre, size, m, 0f, 6f);
            go.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 0f);
        }

        /// <summary>
        /// Marks a way into the jungle: a leafy arch over the trail's first point, its underside 4.6 m up so
        /// nobody can touch it, with vines and two glowing fruit hanging from it.
        /// </summary>
        static void Gateway(Context c, ArtPath path)
        {
            var t = c.active;
            var a = path.points[0].ToVector3();
            var b = path.points[1].ToVector3();
            float dx = b.x - a.x, dz = b.z - a.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) return;
            // Across the trail: perpendicular to its first leg.
            float px = -dz / len, pz = dx / len;
            float span = path.width + 3.2f;
            var leaf = Mat(c, PaintedTextures.Kind.Leaf, Pick(t.canopy, c), Pick(t.canopy, c));
            var fruit = Plain(c, t.bulb, true);
            var mid = new Vector3(a.x, 5.1f, a.z);
            int lumps = 4;
            for (int i = 0; i < lumps; i++)
            {
                float f = (i / (float)(lumps - 1) - 0.5f) * span;
                float rise = 0.6f * (1f - Mathf.Abs(f) / (span * 0.5f));
                Shape(c, PrimitiveType.Sphere, "Arch", mid + new Vector3(px * f, rise, pz * f), new Vector3(2.2f, 1.0f, 2.2f), leaf);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var hang = mid + new Vector3(px * side * span * 0.28f, -0.8f, pz * side * span * 0.28f);
                Shape(c, PrimitiveType.Sphere, "ArchFruit", hang, new Vector3(0.45f, 0.55f, 0.45f), fruit);
            }
        }

        /// <summary>A ragged ring of rock outcrops just outside the outer walls.</summary>
        static void Cliffs(Context c, float hx, float hz)
        {
            var rock = Mat(c, PaintedTextures.Kind.Rock, c.theme.rock, c.theme.rockCrack);
            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = -hz - 8f; z <= hz + 8f; z += Range(c, 7f, 11f))
                {
                    float h = Range(c, 9f, 20f), w = Range(c, 6f, 11f);
                    // Inner face at least 1.5 m outside the outer wall, so nothing tall stands in the play area.
                    Outcrop(c, new Vector3(side * (hx + 1.5f + w * 0.5f + Range(c, 0f, 4f)), h * 0.5f - 1f, z), new Vector3(w, h, Range(c, 7f, 12f)), rock);
                }
                for (float x = -hx - 12f; x <= hx + 12f; x += Range(c, 8f, 12f))
                {
                    float h = Range(c, 12f, 26f), d = Range(c, 7f, 12f);
                    Outcrop(c, new Vector3(x, h * 0.5f - 1f, side * (hz + 1.5f + d * 0.5f + Range(c, 0f, 5f))), new Vector3(Range(c, 8f, 14f), h, d), rock);
                }
            }
        }

        static void Outcrop(Context c, Vector3 centre, Vector3 size, Material rock)
        {
            // A main mass and a smaller ledge on top, both chunky.
            Box(c, "Cliff", centre, size, rock, 0.9f, 9f);
            var top = new Vector3(centre.x + Range(c, -0.2f, 0.2f) * size.x, centre.y + size.y * 0.5f + size.y * 0.12f, centre.z + Range(c, -0.2f, 0.2f) * size.z);
            Box(c, "CliffTop", top, new Vector3(size.x * 0.6f, size.y * 0.24f, size.z * 0.6f), rock, 0.7f, 9f);
        }

        /// <summary>Giant capped trees behind the cliffs: crooked trunks, lumpy caps, glowing fruit underneath.</summary>
        static void Trees(Context c, float hx, float hz)
        {
            var t = c.theme;
            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = -hz; z <= hz; z += Range(c, 11f, 16f))
                    Tree(c, new Vector3(side * (hx + Range(c, 6f, 14f)), 0f, z), Range(c, 15f, 25f), Range(c, 6f, 10f));
                for (float x = -hx; x <= hx; x += Range(c, 11f, 16f))
                    Tree(c, new Vector3(x, 0f, side * (hz + Range(c, 8f, 16f))), Range(c, 16f, 27f), Range(c, 7f, 11f));
            }
        }

        static void Tree(Context c, Vector3 basePos, float height, float capRadius)
        {
            var t = c.active;
            var bark = Mat(c, PaintedTextures.Kind.Bark, t.trunk, t.trunkDark);
            var cap = Mat(c, PaintedTextures.Kind.Leaf, Pick(t.canopy, c), Pick(t.canopy, c));
            var fruit = Plain(c, t.bulb, true);

            var p = basePos;
            var lean = new Vector3(Range(c, -0.25f, 0.25f), 1f, Range(c, -0.25f, 0.25f));
            float r = capRadius * 0.16f;
            const int segments = 5;
            for (int i = 0; i < segments; i++)
            {
                float seg = height / segments;
                var dir = Normalize(lean);
                var mid = p + dir * (seg * 0.5f);
                var go = Shape(c, PrimitiveType.Cylinder, "Trunk", mid, new Vector3(r * 2f, seg * 0.55f, r * 2f), bark);
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                p = p + dir * seg;
                r *= 0.88f;
                lean = lean + new Vector3(Range(c, -0.3f, 0.3f), 0f, Range(c, -0.3f, 0.3f));
            }
            Shape(c, PrimitiveType.Sphere, "Cap", p + new Vector3(0f, capRadius * 0.12f, 0f), new Vector3(capRadius * 2f, capRadius * 0.65f, capRadius * 2f), cap);
            int lobes = 3 + c.rng.Next(3);
            for (int k = 0; k < lobes; k++)
            {
                float a = Range(c, 0f, Mathf.PI * 2f);
                var off = new Vector3(Mathf.Cos(a) * capRadius * 0.55f, Range(c, -0.15f, 0.1f) * capRadius, Mathf.Sin(a) * capRadius * 0.55f);
                float s = capRadius * Range(c, 0.5f, 0.7f);
                Shape(c, PrimitiveType.Sphere, "CapLobe", p + off, new Vector3(s * 2f, s * 0.7f, s * 2f), cap);
            }
            int fruits = 3 + c.rng.Next(4);
            for (int k = 0; k < fruits; k++)
            {
                float a = Range(c, 0f, Mathf.PI * 2f), d = Range(c, 0.2f, 0.8f) * capRadius;
                float s = capRadius * Range(c, 0.08f, 0.14f) * 2f;
                Shape(c, PrimitiveType.Sphere, "Fruit", p + new Vector3(Mathf.Cos(a) * d, -capRadius * 0.28f, Mathf.Sin(a) * d), new Vector3(s, s * 1.15f, s), fruit);
            }
        }

        /// <summary>Freight gantries spanning the map overhead: legs outside the outer walls, twin beams and a trolley 12 m up.</summary>
        static void Gantries(Context c, float hx, float hz)
        {
            var steel = Mat(c, PaintedTextures.Kind.Ribbed, c.theme.steel, c.theme.steel);
            var dark = Plain(c, c.theme.trimBase);
            int count = Math.Max(1, (int)Math.Round(hz * 2f / 45f));
            for (int i = 0; i < count; i++)
            {
                float z = count == 1 ? 0f : -hz * 0.42f + i * (hz * 0.84f / (count - 1));
                const float top = 12.6f;
                for (int side = -1; side <= 1; side += 2)
                for (int pair = -1; pair <= 1; pair += 2)
                    Box(c, "GantryLeg", new Vector3(side * (hx + 1.4f), top * 0.5f, z + pair * 1.6f), new Vector3(0.9f, top + 0.6f, 0.9f), steel, 0.1f, 3f);
                for (int pair = -1; pair <= 1; pair += 2)
                    Box(c, "GantryBeam", new Vector3(0f, top, z + pair * 1.6f), new Vector3(hx * 2f + 4f, 0.9f, 1.1f), steel, 0.12f, 3f);
                Box(c, "GantryTrolley", new Vector3(Range(c, -hx * 0.5f, hx * 0.5f), top - 0.5f, z), new Vector3(4f, 1.6f, 4.4f), dark, 0.15f, 3f);
            }
        }

        static void Fern(Context c, Vector3 at, float size, Material leaf)
        {
            int n = 6;
            for (int k = 0; k < n; k++)
            {
                float a = k * (360f / n) + Range(c, -15f, 15f);
                var go = Shape(c, PrimitiveType.Sphere, "Frond", at, new Vector3(size * 0.32f, 0.06f, size), leaf);
                go.transform.rotation = Quaternion.Euler(-30f, a, 0f);
                go.transform.localPosition = at + go.transform.rotation * new Vector3(0f, 0f, size * 0.42f);
            }
        }

        // ---------------- helpers ----------------

        static GameObject Shape(Context c, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(c.root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
            return go;
        }

        /// <summary>A chamfered decoration box with world-scale texture (no collider).</summary>
        static GameObject Box(Context c, string name, Vector3 centre, Vector3 size, Material m, float bevel, float tile = 2f)
        {
            var go = Shape(c, PrimitiveType.Cube, name, centre, size, m);
            var f = go.GetComponent<MeshFilter>();
            if (f != null) f.sharedMesh = EnvMesh.ChamferBox(size, bevel, centre, tile);
            return go;
        }

        /// <summary>Is the block part of the outer wall (touching the map's edge)?</summary>
        static bool IsOuter(MapDef map, MapBlock b) =>
            b.Max.x >= map.halfX - 0.01f || b.Min.x <= -map.halfX + 0.01f || b.Max.z >= map.halfZ - 0.01f || b.Min.z <= -map.halfZ + 0.01f;

        /// <summary>A colour for a block, chosen by where it stands (so a layout gets a fixed, varied mix).</summary>
        static Color Pick(Color[] colors, MapBlock b) =>
            colors == null || colors.Length == 0 ? Color.white
                : colors[(StableHash(Mathf.RoundToInt(b.center.x * 10f) + "," + Mathf.RoundToInt(b.center.z * 10f)) & 0x7fffffff) % colors.Length];

        static Color Pick(Color[] colors, Context c) =>
            colors == null || colors.Length == 0 ? Color.white : colors[c.rng.Next(colors.Length)];

        static float Range(Context c, float lo, float hi) => lo + (float)c.rng.NextDouble() * (hi - lo);

        static Vector3 Normalize(Vector3 v)
        {
            float l = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return l < 1e-6f ? Vector3.up : new Vector3(v.x / l, v.y / l, v.z / l);
        }

        /// <summary>A string hash that is the same on every machine and every run (string.GetHashCode is not).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) foreach (char ch in s) h = h * 31 + ch;
                return h;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// Line work for the helmet display, drawn as a UI mesh: thin glowing lines, slanted bars, rings and
    /// arcs, with no textures. Each shape has a soft edge that fades out (the glow), so the HUD looks
    /// projected onto the visor rather than printed on boxes.
    /// Shapes are in the graphic's own layout pixels, measured from its pivot. Build them between
    /// <see cref="Clear"/> and <see cref="Commit"/>; <see cref="Graphic.color"/> tints the whole graphic.
    /// Keep moving parts (a bar's fill) on a graphic of their own, so a change rebuilds only a few shapes.
    /// </summary>
    public class VisorGraphic : MaskableGraphic
    {
        /// <summary>How far the glow reaches past a line's edge, in layout pixels, and how strong it starts.</summary>
        public const float DefaultGlow = 5f;
        const float GlowStrength = 0.38f;

        readonly List<Vector3> points = new List<Vector3>();
        readonly List<Color32> colors = new List<Color32>();
        readonly List<int> triangles = new List<int>();

        /// <summary>Corners drawn now, and how many times Unity has asked for the mesh (for the capture's HUD check).</summary>
        public int VertexCount => points.Count;
        public int MeshBuilds { get; private set; }

        protected VisorGraphic()
        {
            // The mesh path Image and Text use (VertexHelper straight to the renderer), not the old Mesh one.
            useLegacyMeshGeneration = false;
        }

        public void Clear()
        {
            points.Clear();
            colors.Clear();
            triangles.Clear();
        }

        /// <summary>Show what was drawn since <see cref="Clear"/>.</summary>
        public void Commit() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            MeshBuilds++;
            Color tint = color;
            for (int i = 0; i < points.Count; i++)
            {
                Color c = colors[i];
                vh.AddVert(points[i], new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * tint.a), Vector4.zero);
            }
            for (int i = 0; i + 2 < triangles.Count; i += 3) vh.AddTriangle(triangles[i], triangles[i + 1], triangles[i + 2]);
        }

        int Vert(float x, float y, Color c)
        {
            points.Add(new Vector3(x, y, 0f));
            colors.Add(c);
            return points.Count - 1;
        }

        /// <summary>
        /// One triangle, always wound clockwise on screen as Unity's own Image and Text meshes are, so a
        /// pipeline that culls back faces still draws it.
        /// </summary>
        void Tri(int a, int b, int c)
        {
            Vector3 pa = points[a], pb = points[b], pc = points[c];
            float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
            triangles.Add(a);
            if (cross > 0f) { triangles.Add(c); triangles.Add(b); }
            else { triangles.Add(b); triangles.Add(c); }
        }

        static Color Faded(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        /// <summary>A four-cornered patch, corners in order around it, each with its own colour (for gradients).</summary>
        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int i0 = Vert(a.x, a.y, ca), i1 = Vert(b.x, b.y, cb), i2 = Vert(c.x, c.y, cc), i3 = Vert(d.x, d.y, cd);
            Tri(i0, i1, i2);
            Tri(i0, i2, i3);
        }

        /// <summary>A filled convex shape, corners in order around it, with a glow fading out past its edges.</summary>
        public void Fill(IList<Vector2> corners, Color c, float glow = 0f)
        {
            if (corners.Count < 3) return;
            float cx = 0f, cy = 0f;
            foreach (var p in corners) { cx += p.x; cy += p.y; }
            cx /= corners.Count;
            cy /= corners.Count;
            int centre = Vert(cx, cy, c);
            int first = points.Count;
            foreach (var p in corners) Vert(p.x, p.y, c);
            for (int i = 0; i < corners.Count; i++)
            {
                Tri(centre, first + i, first + (i + 1) % corners.Count);
            }
            if (glow <= 0f) return;
            for (int i = 0; i < corners.Count; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % corners.Count];
                Outward(a, b, cx, cy, out float nx, out float ny);
                Quad(a, b, new Vector2(b.x + nx * glow, b.y + ny * glow), new Vector2(a.x + nx * glow, a.y + ny * glow),
                     Faded(c, GlowStrength), Faded(c, GlowStrength), Faded(c, 0f), Faded(c, 0f));
            }
        }

        static void Outward(Vector2 a, Vector2 b, float cx, float cy, out float nx, out float ny)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) { nx = ny = 0f; return; }
            nx = dy / len;
            ny = -dx / len;
            // Point away from the middle of the shape.
            if ((a.x - cx) * nx + (a.y - cy) * ny < 0f) { nx = -nx; ny = -ny; }
        }

        /// <summary>A line through <paramref name="path"/>, <paramref name="width"/> wide, with a glow on both sides.</summary>
        public void Line(IList<Vector2> path, float width, Color c, bool closed = false, float glow = DefaultGlow)
        {
            int count = path.Count;
            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++) Segment(path[i], path[(i + 1) % count], width, c, glow);
        }

        public void Segment(Vector2 a, Vector2 b, float width, Color c, float glow = DefaultGlow)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            dx /= len;
            dy /= len;
            // Run each end on by half the width so corners close up.
            float h = width * 0.5f;
            var a0 = new Vector2(a.x - dx * h, a.y - dy * h);
            var b0 = new Vector2(b.x + dx * h, b.y + dy * h);
            float nx = -dy, ny = dx;
            var aL = new Vector2(a0.x + nx * h, a0.y + ny * h);
            var aR = new Vector2(a0.x - nx * h, a0.y - ny * h);
            var bL = new Vector2(b0.x + nx * h, b0.y + ny * h);
            var bR = new Vector2(b0.x - nx * h, b0.y - ny * h);
            Quad(aR, bR, bL, aL, c, c, c, c);
            if (glow <= 0f) return;
            Color inner = Faded(c, GlowStrength), outer = Faded(c, 0f);
            float g = glow;
            Quad(aL, bL, new Vector2(bL.x + nx * g, bL.y + ny * g), new Vector2(aL.x + nx * g, aL.y + ny * g), inner, inner, outer, outer);
            Quad(bR, aR, new Vector2(aR.x - nx * g, aR.y - ny * g), new Vector2(bR.x - nx * g, bR.y - ny * g), inner, inner, outer, outer);
        }

        /// <summary>
        /// Part of a ring around (<paramref name="cx"/>, <paramref name="cy"/>), from <paramref name="fromDegrees"/>
        /// to <paramref name="toDegrees"/> (0 is right, counter-clockwise is positive).
        /// </summary>
        public void Arc(float cx, float cy, float radius, float width, float fromDegrees, float toDegrees, Color c, float glow = DefaultGlow)
        {
            float span = toDegrees - fromDegrees;
            if (Mathf.Abs(span) < 0.01f) return;
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(span) / 6f));
            var path = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float a = (fromDegrees + span * i / steps) * Mathf.Deg2Rad;
                path.Add(new Vector2(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius));
            }
            Line(path, width, c, false, glow);
        }

        /// <summary>The corners of a slanted bar (a parallelogram) whose bottom edge starts at (x, y): the top edge is shifted right by <paramref name="slant"/>.</summary>
        public static Vector2[] Slanted(float x, float y, float width, float height, float slant) => new[]
        {
            new Vector2(x, y), new Vector2(x + width, y), new Vector2(x + width + slant, y + height), new Vector2(x + slant, y + height),
        };

        /// <summary>The corners of a regular shape with <paramref name="sides"/> sides around a point, the first corner at <paramref name="startDegrees"/>.</summary>
        public static Vector2[] Regular(float cx, float cy, float radius, int sides, float startDegrees)
        {
            var corners = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (startDegrees + 360f * i / sides) * Mathf.Deg2Rad;
                corners[i] = new Vector2(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius);
            }
            return corners;
        }

        /// <summary>
        /// A slanted bar cut into <paramref name="segments"/> cells with gaps between them, filled to
        /// <paramref name="share"/> (0 to 1) from the left, or from the right with <paramref name="fromRight"/>.
        /// Filled cells glow; empty cells show as a faint outline.
        /// </summary>
        public void SegmentedBar(float x, float y, float width, float height, float slant, int segments, float gap, float share,
                                 Color fill, Color empty, bool fromRight = false)
        {
            segments = Mathf.Max(1, segments);
            float cell = (width - gap * (segments - 1)) / segments;
            float filled = Mathf.Clamp01(share) * segments;
            for (int i = 0; i < segments; i++)
            {
                float left = x + i * (cell + gap);
                float part = Mathf.Clamp01(filled - (fromRight ? segments - 1 - i : i));
                if (part < 1f) Line(Slanted(left, y, cell, height, slant), 1f, empty, true, 0f);
                if (part <= 0f) continue;
                float w = cell * part;
                // A part-filled cell keeps the bar's slant: its filled piece hugs the side the fill comes from.
                float start = fromRight ? left + cell - w : left;
                Fill(Slanted(start, y, w, height, slant), fill, part >= 1f ? 4f : 0f);
            }
        }
    }
}

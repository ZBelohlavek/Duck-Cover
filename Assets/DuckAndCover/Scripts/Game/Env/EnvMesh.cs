using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Meshes for the dressed map. The main one is a chamfered box: a box with its edges and corners
    /// cut off at a fixed width in metres, so blocks read as chunky carved or cast pieces and catch a
    /// line of light along each edge, instead of razor-sharp greybox cubes. Its UVs come from world
    /// position (box projection), so neighbouring blocks share one continuous texture at the same scale.
    /// </summary>
    public static class EnvMesh
    {
        /// <summary>
        /// A chamfered box built in unit space for a transform scaled to <paramref name="size"/> (as
        /// <see cref="Paint.Block"/> makes them), so the block's transform and collider stay exactly as the
        /// rules expect. <paramref name="worldCenter"/> offsets the UVs; <paramref name="tile"/> is how many
        /// metres one repeat of the texture covers.
        /// </summary>
        public static Mesh ChamferBox(Vector3 size, float bevel, Vector3 worldCenter, float tile)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            float b = Mathf.Min(bevel, 0.45f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
            bool chamfer = b > 0.002f;
            if (!chamfer) b = 0f;

            var polys = new List<V3[]>();
            float ix = hx - b, iy = hy - b, iz = hz - b;
            // Six faces.
            for (int s = -1; s <= 1; s += 2)
            {
                polys.Add(new[] { new V3(s * hx, -iy, -iz), new V3(s * hx, iy, -iz), new V3(s * hx, iy, iz), new V3(s * hx, -iy, iz) });
                polys.Add(new[] { new V3(-ix, s * hy, -iz), new V3(ix, s * hy, -iz), new V3(ix, s * hy, iz), new V3(-ix, s * hy, iz) });
                polys.Add(new[] { new V3(-ix, -iy, s * hz), new V3(ix, -iy, s * hz), new V3(ix, iy, s * hz), new V3(-ix, iy, s * hz) });
            }
            if (chamfer)
            {
                // Twelve edge strips and eight corner triangles.
                for (int s = -1; s <= 1; s += 2)
                for (int t = -1; t <= 1; t += 2)
                {
                    // edges along z (between x and y faces)
                    polys.Add(new[] { new V3(s * hx, t * iy, -iz), new V3(s * hx, t * iy, iz), new V3(s * ix, t * hy, iz), new V3(s * ix, t * hy, -iz) });
                    // edges along x (between y and z faces)
                    polys.Add(new[] { new V3(-ix, s * hy, t * iz), new V3(ix, s * hy, t * iz), new V3(ix, s * iy, t * hz), new V3(-ix, s * iy, t * hz) });
                    // edges along y (between x and z faces)
                    polys.Add(new[] { new V3(s * hx, -iy, t * iz), new V3(s * hx, iy, t * iz), new V3(s * ix, iy, t * hz), new V3(s * ix, -iy, t * hz) });
                    for (int u = -1; u <= 1; u += 2)
                        polys.Add(new[] { new V3(s * hx, t * iy, u * iz), new V3(s * ix, t * hy, u * iz), new V3(s * ix, t * iy, u * hz) });
                }
            }
            return Build(polys, size, new V3(worldCenter.x, worldCenter.y, worldCenter.z), tile);
        }

        /// <summary>
        /// A lumpy box for natural walls (hedges, outcrops, cliffs): each face is cut into a grid of about
        /// <paramref name="cell"/> metres and every corner is pushed toward the box's middle by up to
        /// <paramref name="depth"/> metres, more along the edges so the silhouette rounds off. Pushes are
        /// only ever inward, so the drawing never pokes out of the block's collider; the push depends only
        /// on the corner's position, so faces meet without cracks. Flat shaded, world-projected UVs like
        /// <see cref="ChamferBox"/>, built in unit space for a transform scaled to <paramref name="size"/>.
        /// The bottom face is left out (it sits on the ground).
        ///
        /// <paramref name="sealedFaces"/> marks faces pressed against a neighbouring block (bits <see cref="PlusX"/> and
        /// so on): those stay flat and every corner on them stays put, so two lumpy blocks side by side still meet
        /// with no gap to see through.
        /// </summary>
        public static Mesh LumpyBox(Vector3 size, Vector3 worldCenter, float tile, float depth, float cell, int seed, int sealedFaces = 0)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            depth = Mathf.Min(depth, 0.35f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
            var wc = new V3(worldCenter.x, worldCenter.y, worldCenter.z);
            float r = Math.Min(hx, hz);
            float Clamp(float v, float lim) => lim <= 0f ? 0f : Math.Max(-lim, Math.Min(lim, v));
            bool Sealed(int bit) => (sealedFaces & bit) != 0;
            const float on = 0.001f;
            V3 Push(V3 p)
            {
                if ((Sealed(PlusX) && p.x >= hx - on) || (Sealed(MinusX) && p.x <= -hx + on) || (Sealed(PlusY) && p.y >= hy - on)
                    || (Sealed(PlusZ) && p.z >= hz - on) || (Sealed(MinusZ) && p.z <= -hz + on)) return p;
                // How close the corner is to an open edge of the box (0 on an edge). Sealed sides and the foot,
                // which stays square on the ground, don't count.
                const float far = 1e4f;
                float ex = Math.Min(Sealed(PlusX) ? far : hx - p.x, Sealed(MinusX) ? far : hx + p.x);
                float ey = Sealed(PlusY) ? far : hy - p.y;
                float ez = Math.Min(Sealed(PlusZ) ? far : hz - p.z, Sealed(MinusZ) ? far : hz + p.z);
                float a = Math.Min(ex, ey), b = Math.Min(ey, ez), c = Math.Min(ex, ez);
                float edge = Math.Min(a, Math.Min(b, c));
                float round = Mathf.Clamp01(1f - edge / Math.Max(0.01f, depth * 2.5f));
                var w = p + wc;
                float n = PaintedTextures.Noise.Fbm(w.x * 0.45f + w.y * 0.13f + 300f, w.z * 0.45f - w.y * 0.21f + 300f, 1024, seed, 3);
                float d = depth * Mathf.Clamp01(0.15f + 0.85f * Mathf.Clamp01((n - 0.3f) * 2.5f) + round * 0.6f);
                // Push toward the box's core (the box shrunk by its smallest half width), which is straight in
                // from the middle of each face and diagonally in at the edges.
                var core = new V3(Clamp(p.x, hx - r), Math.Min(p.y, hy - r), Clamp(p.z, hz - r));
                var toward = core - p;
                if (toward.Length() < 1e-4f) return p;
                return p + toward.Normalized() * d;
            }
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float sx = Mathf.Max(size.x, 0.0001f), sy = Mathf.Max(size.y, 0.0001f), sz = Mathf.Max(size.z, 0.0001f);
            void Tri(V3 a, V3 b, V3 c, V3 faceNormal)
            {
                var pa = Push(a); var pb = Push(b); var pc = Push(c);
                var n = (pb - pa).Cross(pc - pa);
                if (n.Dot(faceNormal) < 0f) { var t = pb; pb = pc; pc = t; n = n * -1f; }
                n = n.Normalized();
                var un = new V3(n.x * sx, n.y * sy, n.z * sz).Normalized();
                float ax = Math.Abs(faceNormal.x), ay = Math.Abs(faceNormal.y), az = Math.Abs(faceNormal.z);
                foreach (var p in new[] { pa, pb, pc })
                {
                    verts.Add(new Vector3(p.x / sx, p.y / sy, p.z / sz));
                    normals.Add(new Vector3(un.x, un.y, un.z));
                    var w = p + wc;
                    if (ay >= ax && ay >= az) uvs.Add(new Vector2(w.x / tile, w.z / tile));
                    else if (ax >= az) uvs.Add(new Vector2(w.z / tile, w.y / tile));
                    else uvs.Add(new Vector2(w.x / tile, w.y / tile));
                    tris.Add(verts.Count - 1);
                }
            }
            // One face: corner o, edge vectors e1 and e2 spanning it, outward normal n.
            void Face(V3 o, V3 e1, V3 e2, V3 n)
            {
                int nu = Math.Max(1, (int)Math.Round(e1.Length() / cell)), nv = Math.Max(1, (int)Math.Round(e2.Length() / cell));
                for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    V3 P(int a, int b) => o + e1 * (a / (float)nu) + e2 * (b / (float)nv);
                    var p00 = P(i, j); var p10 = P(i + 1, j); var p01 = P(i, j + 1); var p11 = P(i + 1, j + 1);
                    Tri(p00, p10, p11, n);
                    Tri(p00, p11, p01, n);
                }
            }
            Face(new V3(-hx, hy, -hz), new V3(2f * hx, 0f, 0f), new V3(0f, 0f, 2f * hz), new V3(0f, 1f, 0f));
            Face(new V3(hx, -hy, -hz), new V3(0f, 2f * hy, 0f), new V3(0f, 0f, 2f * hz), new V3(1f, 0f, 0f));
            Face(new V3(-hx, -hy, -hz), new V3(0f, 2f * hy, 0f), new V3(0f, 0f, 2f * hz), new V3(-1f, 0f, 0f));
            Face(new V3(-hx, -hy, hz), new V3(2f * hx, 0f, 0f), new V3(0f, 2f * hy, 0f), new V3(0f, 0f, 1f));
            Face(new V3(-hx, -hy, -hz), new V3(2f * hx, 0f, 0f), new V3(0f, 2f * hy, 0f), new V3(0f, 0f, -1f));
            var mesh = new Mesh { name = "LumpyBox" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public const int PlusX = 1, MinusX = 2, PlusY = 4, PlusZ = 8, MinusZ = 16;

        /// <summary>Turns convex polygons around the origin (in metres) into a flat-shaded unit-space mesh.</summary>
        static Mesh Build(List<V3[]> polys, Vector3 size, V3 worldCenter, float tile)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float sx = Mathf.Max(size.x, 0.0001f), sy = Mathf.Max(size.y, 0.0001f), sz = Mathf.Max(size.z, 0.0001f);
            foreach (var poly in polys)
            {
                var ordered = Order(poly);
                if (ordered == null) continue;
                V3 n = (ordered[1] - ordered[0]).Cross(ordered[2] - ordered[0]).Normalized();
                // Normal in unit space that comes back to n once Unity scales the transform by size.
                var un = new V3(n.x * sx, n.y * sy, n.z * sz).Normalized();
                int start = verts.Count;
                float ax = Math.Abs(n.x), ay = Math.Abs(n.y), az = Math.Abs(n.z);
                foreach (var p in ordered)
                {
                    verts.Add(new Vector3(p.x / sx, p.y / sy, p.z / sz));
                    normals.Add(new Vector3(un.x, un.y, un.z));
                    var w = p + worldCenter;
                    Vector2 uv;
                    if (ay >= ax && ay >= az) uv = new Vector2(w.x / tile, w.z / tile);
                    else if (ax >= az) uv = new Vector2(w.z / tile, w.y / tile);
                    else uv = new Vector2(w.x / tile, w.y / tile);
                    uvs.Add(uv);
                }
                for (int i = 1; i + 1 < ordered.Length; i++) { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
            }
            var mesh = new Mesh { name = "ChamferBox" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Puts a convex polygon's corners in order around it, wound so Unity sees its front from
        /// outside the box (Unity's front face normal is Cross(b - a, c - a)).
        /// </summary>
        static V3[] Order(V3[] poly)
        {
            var c = new V3(0f, 0f, 0f);
            foreach (var p in poly) c = c + p;
            c = c * (1f / poly.Length);
            var axis = c.Normalized();
            if (axis.Length() < 0.5f) return null;
            var any = Math.Abs(axis.y) < 0.9f ? new V3(0f, 1f, 0f) : new V3(1f, 0f, 0f);
            var e1 = any.Cross(axis).Normalized();
            var e2 = axis.Cross(e1);
            var keyed = new List<(float a, V3 p)>();
            foreach (var p in poly) { var d = p - c; keyed.Add(((float)Math.Atan2(d.Dot(e2), d.Dot(e1)), p)); }
            keyed.Sort((l, r) => l.a.CompareTo(r.a));
            var result = new V3[keyed.Count];
            for (int i = 0; i < result.Length; i++) result[i] = keyed[i].p;
            var n = (result[1] - result[0]).Cross(result[2] - result[0]);
            if (n.Dot(c) < 0f) Array.Reverse(result);
            return result;
        }

        /// <summary>Plain float vector maths, so the mesh code doesn't lean on engine maths helpers.</summary>
        struct V3
        {
            public float x, y, z;
            public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
            public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
            public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
            public static V3 operator *(V3 a, float k) => new V3(a.x * k, a.y * k, a.z * k);
            public float Dot(V3 o) => x * o.x + y * o.y + z * o.z;
            public V3 Cross(V3 o) => new V3(y * o.z - z * o.y, z * o.x - x * o.z, x * o.y - y * o.x);
            public float Length() => (float)Math.Sqrt(x * x + y * y + z * z);
            public V3 Normalized() { float l = Length(); return l < 1e-6f ? this : this * (1f / l); }
        }
    }
}

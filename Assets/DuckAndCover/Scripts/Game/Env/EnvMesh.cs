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

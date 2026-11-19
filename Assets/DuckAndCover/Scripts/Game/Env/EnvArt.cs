using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Modelled environment pieces (Breakwater's jungle walls, rocks, low walls, crates, temple pieces,
    /// Bastions and pads), made in Blender from the scripts in the project's art/env-assets-v1 folder.
    ///
    /// Each piece is a small binary mesh in Data/Resources/EnvArt (&lt;name&gt;.bytes, format DCM2) with its
    /// painted look baked into &lt;name&gt;_c.jpg and, for pieces that were lightened, a normal map
    /// &lt;name&gt;_n.jpg. Binary meshes and JPEGs need no Git LFS. Glowing parts (trim lights, crystals) are
    /// flat colours that light themselves. Every piece is drawn only: nothing here has a collider.
    ///
    /// Pieces are modelled in metres on their gameplay box, origin at the floor centre, length along x,
    /// depth along z. <see cref="Fit"/> stretches modules over a block's box so the drawing never leaves
    /// the collider. When a piece is missing, <see cref="Has"/> is false and callers keep the code-made look.
    /// </summary>
    public static class EnvArt
    {
        public const string Folder = "EnvArt/";

        class PartData
        {
            public string key;
            public Mesh mesh;
            public Material material;
        }

        class Piece
        {
            public PartData[] parts;
            public Bounds bounds;
        }

        static readonly Dictionary<string, Piece> cache = new Dictionary<string, Piece>();

        /// <summary>Is the piece in the build?</summary>
        public static bool Has(string name) => Get(name) != null;

        /// <summary>The piece's size in metres (Unity axes).</summary>
        public static Vector3 Size(string name)
        {
            var p = Get(name);
            return p == null ? Vector3.one : p.bounds.size;
        }

        static Piece Get(string name)
        {
            if (cache.TryGetValue(name, out var found)) return found;
            Piece piece = null;
            try
            {
                var data = Resources.Load<TextAsset>(Folder + name);
                if (data != null) piece = Read(name, data.bytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning("EnvArt: could not read " + name + ": " + e.Message);
                piece = null;
            }
            cache[name] = piece;
            return piece;
        }

        static Piece Read(string name, byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                var magic = Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic != "DCM2") throw new InvalidDataException("not a DCM2 mesh");
                int subs = r.ReadInt32(), count = r.ReadInt32();
                var pos = new Vector3[count];
                var nrm = new Vector3[count];
                var uv = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                }
                var albedo = Resources.Load<Texture2D>(Folder + name + "_c");
                var normal = Resources.Load<Texture2D>(Folder + name + "_n");
                if (albedo == null)
                    Debug.LogError("Duck and Cover: environment piece " + name + " has no colour texture: Resources/" + Folder + name + "_c did not load as a Texture2D.");
                var parts = new List<PartData>();
                var bounds = new Bounds();
                bool first = true;
                for (int s = 0; s < subs; s++)
                {
                    int keyLength = r.ReadUInt16();
                    string key = Encoding.UTF8.GetString(r.ReadBytes(keyLength));
                    var glow = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1f);
                    float strength = r.ReadSingle();
                    int n = r.ReadInt32();
                    var idx = new int[n];
                    for (int i = 0; i < n; i++) idx[i] = r.ReadInt32();
                    var mesh = SubMesh(name + " " + key, pos, nrm, uv, idx);
                    if (mesh == null) continue;
                    if (first) { bounds = mesh.bounds; first = false; } else bounds.Encapsulate(mesh.bounds);
                    var material = key == "body" ? Body(albedo, normal) : Glow(glow, strength);
                    parts.Add(new PartData { key = key, mesh = mesh, material = material });
                }
                return new Piece { parts = parts.ToArray(), bounds = bounds };
            }
        }

        /// <summary>One submesh as its own mesh (only the vertices it uses), so each part can be recoloured on its own.</summary>
        static Mesh SubMesh(string name, Vector3[] pos, Vector3[] nrm, Vector2[] uv, int[] idx)
        {
            if (idx.Length == 0) return null;
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector2>();
            var tris = new int[idx.Length];
            for (int i = 0; i < idx.Length; i++)
            {
                int k = idx[i];
                if (!map.TryGetValue(k, out int j))
                {
                    j = v.Count; map[k] = j;
                    v.Add(pos[k]); n.Add(nrm[k]); t.Add(uv[k]);
                }
                tris[i] = j;
            }
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, t);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static readonly Dictionary<Texture2D, Material> bodies = new Dictionary<Texture2D, Material>();

        static Material Body(Texture2D albedo, Texture2D normal)
        {
            if (albedo == null) return Paint.For(Color.white);
            if (bodies.TryGetValue(albedo, out var m) && m != null) return m;
            // A copy of the shared painted material, so adding the normal map stays on this piece.
            m = new Material(Paint.Textured(albedo));
            m.name = "EnvArt " + albedo.name;
            if (normal != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            bodies[albedo] = m;
            return m;
        }

        /// <summary>A glowing flat colour (trim light, crystal). The colour lives in a one-pixel texture, as with <see cref="Paint"/>.</summary>
        public static Material Glow(Color color, float strength = 1f)
        {
            Color32 key = color;
            var tex = GlowTexture(key, strength > 0.05f);
            return Paint.Textured(tex, strength > 0.05f);
        }

        static readonly Dictionary<string, Texture2D> glowTextures = new Dictionary<string, Texture2D>();

        static Texture2D GlowTexture(Color32 c, bool lit)
        {
            string key = c + "|" + lit;
            if (glowTextures.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.name = "EnvArt glow " + key;
            t.SetPixel(0, 0, c);
            t.Apply();
            glowTextures[key] = t;
            return t;
        }

        /// <summary>
        /// Puts a piece in the scene under <paramref name="parent"/>: one child per part, named by its material
        /// key ("body", "crystal_n", "team_a", ...). Returns null if the piece is missing.
        /// </summary>
        public static GameObject Place(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var p = Get(name);
            if (p == null) return null;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            root.transform.localScale = scale;
            foreach (var part in p.parts)
            {
                var go = new GameObject(part.key);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = part.material;
            }
            return root;
        }

        /// <summary>A part of a placed piece by its key, or null.</summary>
        public static GameObject Part(GameObject placed, string key)
        {
            if (placed == null) return null;
            var t = placed.transform.Find(key);
            return t == null ? null : t.gameObject;
        }

        /// <summary>
        /// Covers a box with a run of modules picked from several variants of a piece, so a long wall doesn't
        /// read as one object copied over and over: module lengths vary, neighbours overlap by
        /// <paramref name="overlap"/> metres so their rounded ends melt together, and each module is turned end
        /// for end or mirrored at random, set back a little from the faces (<paramref name="depthJitter"/>) and
        /// a little lower (<paramref name="heightJitter"/>). Nothing ever leaves the box. <paramref name="each"/>
        /// gets every module with its index and its centre on the floor. Returns how many modules were placed.
        /// </summary>
        public static int FitRun(Transform parent, string[] names, Vector3 centre, Vector3 size, float module, System.Random rng,
            float overlap = 0f, float heightJitter = 0f, float depthJitter = 0f, Action<GameObject, int, Vector3> each = null)
        {
            var have = new List<string>();
            foreach (var n in names) if (Get(n) != null) have.Add(n);
            if (have.Count == 0) return 0;
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, depth = alongX ? size.z : size.x;
            int count = Math.Max(1, (int)Math.Round(length / module));
            // Uneven module lengths that still add up to the box.
            var share = new float[count];
            float total = 0f;
            for (int i = 0; i < count; i++) { share[i] = count == 1 ? 1f : 0.75f + 0.5f * (float)rng.NextDouble(); total += share[i]; }
            var axis = alongX ? Vector3.right : Vector3.forward;
            var side = alongX ? Vector3.forward : Vector3.right;
            var bottom = centre - new Vector3(0f, size.y * 0.5f, 0f);
            float start = -length * 0.5f;
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                float step = length * share[i] / total;
                float lo = start - (i > 0 ? overlap : 0f), hi = start + step + (i < count - 1 ? overlap : 0f);
                start += step;
                int pick = rng.Next(have.Count);
                if (pick == last && have.Count > 1) pick = (pick + 1 + rng.Next(have.Count - 1)) % have.Count;
                last = pick;
                var model = Get(have[pick]).bounds.size;
                float d = depth * (1f - depthJitter * (float)rng.NextDouble());
                float shift = (depth - d) * 0.5f * (float)(rng.NextDouble() * 2.0 - 1.0);
                float h = size.y * (1f - heightJitter * (float)rng.NextDouble());
                var scale = new Vector3((hi - lo) / Math.Max(0.01f, model.x), h / Math.Max(0.01f, model.y), d / Math.Max(0.01f, model.z));
                if (rng.NextDouble() < 0.5) scale.x = -scale.x; // mirrored end to end
                float yaw = (alongX ? 0f : 90f) + (rng.NextDouble() < 0.5 ? 0f : 180f);
                var at = bottom + axis * ((lo + hi) * 0.5f) + side * shift;
                var go = Place(parent, have[pick], at, Quaternion.Euler(0f, yaw, 0f), scale);
                if (each != null) each(go, i, at);
            }
            return count;
        }

        /// <summary>
        /// Covers a block's box with a piece: the piece's length runs along the box's longer side, repeated as
        /// modules of about <paramref name="module"/> metres, each stretched to fill its share of the box exactly.
        /// Returns how many modules were placed (0 if the piece is missing).
        /// </summary>
        public static int Fit(Transform parent, string name, Vector3 centre, Vector3 size, float module, Action<GameObject, int> each = null)
        {
            var p = Get(name);
            if (p == null) return 0;
            var model = p.bounds.size;
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, depth = alongX ? size.z : size.x;
            int n = Math.Max(1, (int)Math.Round(length / module));
            float step = length / n;
            var rotation = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            var scale = new Vector3(step / Math.Max(0.01f, model.x), size.y / Math.Max(0.01f, model.y), depth / Math.Max(0.01f, model.z));
            var axis = alongX ? Vector3.right : Vector3.forward;
            var bottom = centre - new Vector3(0f, size.y * 0.5f, 0f);
            for (int i = 0; i < n; i++)
            {
                var at = bottom + axis * (-length * 0.5f + step * (i + 0.5f));
                var go = Place(parent, name, at, rotation, scale);
                if (each != null) each(go, i);
            }
            return n;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Gives placeholder objects a flat colour. Every coloured object in the game goes through here.
    ///
    /// The first real captures showed every runtime-coloured block in the last colour set, however
    /// the colour property was written. So a colour is not a material property here: each colour is
    /// a one-pixel texture on its own material, shared by everything of that colour, and the tint
    /// stays white. <see cref="SelfCheck"/> renders three blocks and reads the pixels back, so a
    /// capture run fails loudly if colours ever collapse again.
    /// </summary>
    public static class Paint
    {
        static readonly Dictionary<Color32, Material> materials = new Dictionary<Color32, Material>();
        static Shader shader;

        /// <summary>The shared material for a colour.</summary>
        public static Material For(Color color)
        {
            Color32 key = color;
            if (materials.TryGetValue(key, out var found) && found != null) return found;

            if (shader == null) shader = FindShader();
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = "Paint " + key;
            texture.SetPixel(0, 0, color);
            texture.Apply();

            var material = new Material(shader);
            material.name = "Paint " + key;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            materials[key] = material;
            return material;
        }

        public static void Apply(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = For(color);
        }

        /// <summary>The shader primitives are drawn with in this project (URP Lit, or the built-in one without URP).</summary>
        static Shader FindShader()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = probe.GetComponent<Renderer>();
            Shader found = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.shader : null;
            Object.Destroy(probe);
            if (found == null) found = Shader.Find("Universal Render Pipeline/Lit");
            if (found == null) found = Shader.Find("Standard");
            return found;
        }

        /// <summary>A cube with a colour. <paramref name="solid"/> false removes its collider (decoration, bodies).</summary>
        public static GameObject Block(Transform parent, string name, Vector3 localPosition, Vector3 size, Color color, bool solid = true)
            => Shape(PrimitiveType.Cube, parent, name, localPosition, size, color, solid);

        public static GameObject Shape(PrimitiveType type, Transform parent, string name, Vector3 localPosition, Vector3 size, Color color, bool solid)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            if (!solid)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null) Object.Destroy(collider);
            }
            Apply(go, color);
            return go;
        }

        /// <summary>
        /// Renders a red, a green and a blue block in front of a camera of its own and reads the
        /// middle pixel of each. Returns null when each block shows its own colour, otherwise what
        /// was seen. Used by the screenshot run.
        /// </summary>
        public static string SelfCheck()
        {
            var root = new GameObject("PaintSelfCheck");
            root.transform.position = new Vector3(0f, -500f, 0f);
            var colors = new[] { new Color(0.9f, 0.1f, 0.1f), new Color(0.1f, 0.8f, 0.1f), new Color(0.1f, 0.2f, 0.9f) };
            for (int i = 0; i < 3; i++) Block(root.transform, "Check" + i, new Vector3(-2f + 2f * i, 0f, 4f), Vector3.one * 1.5f, colors[i], false);

            var lightObject = new GameObject("CheckLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;

            var camObject = new GameObject("CheckCamera");
            camObject.transform.SetParent(root.transform, false);
            var cam = camObject.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.enabled = false;

            const int w = 300, h = 100;
            var target = new RenderTexture(w, h, 24);
            var read = new Texture2D(w, h, TextureFormat.RGB24, false);
            string problem = null;
            try
            {
                cam.targetTexture = target;
                cam.Render();
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                var seen = new Color[3];
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = cam.WorldToScreenPoint(root.transform.position + new Vector3(-2f + 2f * i, 0f, 4f));
                    seen[i] = read.GetPixel(Mathf.Clamp((int)p.x, 0, w - 1), Mathf.Clamp((int)p.y, 0, h - 1));
                }
                bool red = seen[0].r > seen[0].g * 1.5f && seen[0].r > seen[0].b * 1.5f;
                bool green = seen[1].g > seen[1].r * 1.5f && seen[1].g > seen[1].b * 1.5f;
                bool blue = seen[2].b > seen[2].r * 1.5f && seen[2].b > seen[2].g * 1.5f;
                if (!(red && green && blue))
                    problem = "red block shows " + seen[0] + ", green block shows " + seen[1] + ", blue block shows " + seen[2] +
                              " (shader " + (shader == null ? "none" : shader.name) + ")";
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = null;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(read);
                Object.Destroy(root);
            }
            return problem;
        }
    }
}

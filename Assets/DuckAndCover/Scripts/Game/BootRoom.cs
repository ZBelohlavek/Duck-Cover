using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// A placeholder greybox built in code: a floor, one 4 m x 3 m wall (the size the plan uses for
    /// a breakable surface), a doorway, a crate and a sun. It only exists so the Boot scene shows
    /// something; the first real map arrives in WP9.
    /// </summary>
    public static class BootRoom
    {
        public static GameObject Build()
        {
            var root = new GameObject("BootRoom");

            var sun = new GameObject("Sun");
            sun.transform.SetParent(root.transform, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;

            var floor = Block(root, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(20f, 0.1f, 20f), new Color(0.42f, 0.43f, 0.45f));
            floor.isStatic = true;

            var wallColor = new Color(0.72f, 0.66f, 0.55f);
            // A wall with a door-size gap: two 4 m x 3 m panels either side of a 1.2 m doorway.
            Block(root, "WallLeft", new Vector3(-2.6f, 1.5f, 3f), new Vector3(4f, 3f, 0.2f), wallColor);
            Block(root, "WallRight", new Vector3(2.6f, 1.5f, 3f), new Vector3(4f, 3f, 0.2f), wallColor);
            Block(root, "Lintel", new Vector3(0f, 2.6f, 3f), new Vector3(1.2f, 0.8f, 0.2f), wallColor);
            Block(root, "Crate", new Vector3(-1.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f), new Color(0.35f, 0.45f, 0.3f));

            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = false;
                cam.fieldOfView = 70f;
                cam.transform.SetPositionAndRotation(new Vector3(1.2f, 1.7f, -4f), Quaternion.Euler(8f, -8f, 0f));
            }
            return root;
        }

        static GameObject Block(GameObject parent, string name, Vector3 position, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
            return go;
        }
    }
}

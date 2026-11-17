using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The placeholder test arena, built in code: a 30 m x 52 m greybox with a spawn area at each
    /// end, cover, low walls to vault, a doorway wall in the middle and two platforms. It matches
    /// <see cref="ArenaLayout.Placeholder"/> (spawn and dummy spots) and is mirrored end to end. It
    /// only exists so players can move and fight before the first real map (WP9).
    /// </summary>
    public static class BootRoom
    {
        static readonly Color Floor = new Color(0.36f, 0.38f, 0.42f);
        static readonly Color Wall = new Color(0.80f, 0.76f, 0.66f);
        static readonly Color Outer = new Color(0.22f, 0.24f, 0.30f);
        static readonly Color Cover = new Color(0.93f, 0.78f, 0.25f);
        static readonly Color Crate = new Color(0.30f, 0.62f, 0.36f);
        static readonly Color Platform = new Color(0.55f, 0.42f, 0.70f);

        /// <summary>Where the camera sits when nobody is playing: above team A's end, looking down the arena.</summary>
        public static readonly Vector3 OverviewPosition = new Vector3(0f, 15f, -31f);
        public static readonly Quaternion OverviewRotation = Quaternion.Euler(32f, 0f, 0f);

        public static GameObject Build()
        {
            var root = new GameObject("BootRoom");
            var t = root.transform;

            var sun = new GameObject("Sun");
            sun.transform.SetParent(t, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;

            Paint.Block(t, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 52f), Floor);
            Paint.Block(t, "WallWest", new Vector3(-15.25f, 2f, 0f), new Vector3(0.5f, 4f, 52f), Outer);
            Paint.Block(t, "WallEast", new Vector3(15.25f, 2f, 0f), new Vector3(0.5f, 4f, 52f), Outer);
            Paint.Block(t, "WallSouth", new Vector3(0f, 2f, -26.25f), new Vector3(31f, 4f, 0.5f), Outer);
            Paint.Block(t, "WallNorth", new Vector3(0f, 2f, 26.25f), new Vector3(31f, 4f, 0.5f), Outer);

            // Spawn pads in the team colours (the same blue and orange the UI uses).
            Paint.Block(t, "SpawnPadA", new Vector3(0f, 0.01f, -21f), new Vector3(12f, 0.02f, 3f), new Color(0.24f, 0.60f, 0.95f), false);
            Paint.Block(t, "SpawnPadB", new Vector3(0f, 0.01f, 21f), new Vector3(12f, 0.02f, 3f), new Color(0.95f, 0.55f, 0.20f), false);

            // The middle: a wall across with a doorway in the centre and a gap at each side.
            Paint.Block(t, "MidWallLeft", new Vector3(-5.6f, 1.5f, 0f), new Vector3(10f, 3f, 0.3f), Wall);
            Paint.Block(t, "MidWallRight", new Vector3(5.6f, 1.5f, 0f), new Vector3(10f, 3f, 0.3f), Wall);
            Paint.Block(t, "MidLintel", new Vector3(0f, 2.6f, 0f), new Vector3(1.2f, 0.8f, 0.3f), Wall);

            // Each half, mirrored: sign -1 is team A's side, +1 team B's.
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0f ? "A" : "B";
                // A spawn screen, so the two spawns can't see each other.
                Paint.Block(t, "SpawnScreen" + side, new Vector3(0f, 1.5f, 16f * s), new Vector3(9f, 3f, 0.3f), Wall);
                // Low walls: crouch behind them or vault over (1 m).
                Paint.Block(t, "LowWallLeft" + side, new Vector3(-7f, 0.5f, 10f * s), new Vector3(5f, 1f, 0.4f), Cover);
                Paint.Block(t, "LowWallRight" + side, new Vector3(7f, 0.5f, 10f * s), new Vector3(5f, 1f, 0.4f), Cover);
                // Crates: one to stand behind, one stacked too high to vault.
                Paint.Block(t, "Crate" + side, new Vector3(-2.5f * s, 0.6f, 5f * s), new Vector3(1.2f, 1.2f, 1.2f), Crate);
                Paint.Block(t, "CrateStackLow" + side, new Vector3(10f * s, 0.6f, 5f * s), new Vector3(1.2f, 1.2f, 1.2f), Crate);
                Paint.Block(t, "CrateStackHigh" + side, new Vector3(10f * s, 1.8f, 5f * s), new Vector3(1.2f, 1.2f, 1.2f), Crate);
                // A platform one metre up with a step to it.
                Paint.Block(t, "Platform" + side, new Vector3(-11.5f * s, 0.5f, 3.5f * s), new Vector3(5f, 1f, 5f), Platform);
                Paint.Block(t, "PlatformStep" + side, new Vector3(-8.4f * s, 0.25f, 3.5f * s), new Vector3(1.2f, 0.5f, 3f), Platform);
                // Pillars.
                Paint.Block(t, "Pillar" + side, new Vector3(4f * s, 1.5f, 12.5f * s), new Vector3(0.8f, 3f, 0.8f), Wall);
            }

            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = false;
                cam.fieldOfView = 78f;
                cam.nearClipPlane = 0.05f;
                cam.transform.SetPositionAndRotation(OverviewPosition, OverviewRotation);
            }
            return root;
        }
    }
}

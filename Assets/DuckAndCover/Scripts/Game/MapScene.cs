using System;
using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Builds a map's greybox from its <see cref="MapDef"/>: every block as a coloured box (ramps as
    /// tilted slabs), a ring and a beacon for each zone, and a sun. The same data feeds the rules
    /// (zones, spawn spots) and the validator, so the scene, the rules and the checks can't drift
    /// apart. <see cref="MapView"/> on the root keeps the zones' colours and the shift up to date.
    /// Real art replaces the boxes later; the blocks keep their names so it can find them.
    /// </summary>
    public static class MapScene
    {
        /// <summary>The map built in this scene (every machine builds the same one).</summary>
        public static MapDef Current { get; private set; }

        static readonly Color GroundColor = new Color(0.36f, 0.38f, 0.42f);
        static readonly Color OuterWall = new Color(0.22f, 0.24f, 0.30f);
        static readonly Color BuildingWall = new Color(0.80f, 0.76f, 0.66f);
        static readonly Color LowCover = new Color(0.93f, 0.78f, 0.25f);
        static readonly Color CrateColor = new Color(0.45f, 0.70f, 0.40f);
        static readonly Color Container = new Color(0.27f, 0.52f, 0.33f);
        static readonly Color PlatformColor = new Color(0.55f, 0.42f, 0.70f);
        static readonly Color RampColor = new Color(0.68f, 0.58f, 0.80f);
        static readonly Color ShutterColor = new Color(0.85f, 0.20f, 0.25f);
        public static readonly Color TeamA = new Color(0.24f, 0.60f, 0.95f);
        public static readonly Color TeamB = new Color(0.95f, 0.55f, 0.20f);

        const float RampThickness = 0.3f;

        /// <summary>Where the camera sits when nobody is playing.</summary>
        public static Vector3 OverviewPosition => Current == null ? new Vector3(0f, 40f, -40f) : Current.overviewPosition.ToVector3();
        public static Quaternion OverviewRotation => Current == null
            ? Quaternion.Euler(45f, 0f, 0f)
            : Quaternion.LookRotation((Current.overviewTarget - Current.overviewPosition).ToVector3());

        public static GameObject Build(MapDef map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Current = map;
            var root = new GameObject("Map " + map.displayName);
            var t = root.transform;

            var sun = new GameObject("Sun");
            sun.transform.SetParent(t, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;

            var view = root.AddComponent<MapView>();
            foreach (var b in map.blocks)
            {
                var go = b.kind == BlockKind.Ramp ? Ramp(t, b) : Paint.Block(t, b.name, b.center.ToVector3(), b.size.ToVector3(), ColorOf(b), b.Solid);
                if (b.shift != ShiftRole.None) view.AddShiftBlock(go, b);
            }
            foreach (var z in map.zones) view.AddZone(z, ZoneMarker(t, z));

            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = false;
                cam.fieldOfView = 78f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 400f;
                cam.transform.SetPositionAndRotation(OverviewPosition, OverviewRotation);
            }
            return root;
        }

        static Color ColorOf(MapBlock b)
        {
            switch (b.kind)
            {
                case BlockKind.Ground: return GroundColor;
                case BlockKind.Wall: return b.name.StartsWith("Outer") ? OuterWall : BuildingWall;
                case BlockKind.LowCover: return LowCover;
                case BlockKind.Crate: return Math.Max(b.size.x, b.size.z) > 3.5f ? Container : CrateColor;
                case BlockKind.Platform: return PlatformColor;
                case BlockKind.Ramp: return RampColor;
                case BlockKind.Shutter: return ShutterColor;
                default: return b.center.z < 0f ? TeamA : TeamB;
            }
        }

        /// <summary>
        /// A ramp is a slab tilted so its top surface runs from the floor at the low end to the top
        /// of its box at the high end (the surface MapBlock.TopAt and the validator walk).
        /// </summary>
        static GameObject Ramp(Transform parent, MapBlock b)
        {
            int yaw = ((b.rampRisesToward % 360) + 360) % 360;
            bool alongZ = yaw == 0 || yaw == 180;
            float run = alongZ ? b.size.z : b.size.x;
            float width = alongZ ? b.size.x : b.size.z;
            float rise = b.size.y;
            float angle = Mathf.Atan2(rise, run);
            float length = Mathf.Sqrt(run * run + rise * rise);

            float yawRad = yaw * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            // The slab's up after tilting its front end up by angle, then turning it to face dir.
            var normal = new Vector3(-Mathf.Sin(angle) * dir.x, Mathf.Cos(angle), -Mathf.Sin(angle) * dir.z);
            var surfaceMid = new Vector3(b.center.x, b.Min.y + rise * 0.5f, b.center.z);
            var center = surfaceMid - normal * (RampThickness * 0.5f);

            var go = Paint.Block(parent, b.name, center, new Vector3(width, RampThickness, length), RampColor);
            go.transform.localRotation = Quaternion.Euler(-angle * Mathf.Rad2Deg, yaw, 0f);
            return go;
        }

        /// <summary>A flat ring on the floor and a thin beacon in the middle, both see-through to shots and bodies.</summary>
        static GameObject[] ZoneMarker(Transform parent, MapZone z)
        {
            var c = z.center.ToVector3();
            var disc = Paint.Shape(PrimitiveType.Cylinder, parent, "Zone " + z.label, c + new Vector3(0f, 0.02f, 0f),
                new Vector3(z.radius * 2f, 0.01f, z.radius * 2f), MapView.Locked, false);
            // A cylinder primitive is 2 m tall, so a y scale of 3 stands 6 m high.
            var beacon = Paint.Shape(PrimitiveType.Cylinder, parent, "Beacon " + z.label, c + new Vector3(0f, 6f, 0f),
                new Vector3(0.25f, 3f, 0.25f), MapView.Locked, false);
            return new[] { disc, beacon };
        }
    }
}

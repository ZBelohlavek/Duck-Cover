using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A minion lane as map data (WP15): a line of points from team A's base end to team B's. Team A's
    /// waves walk it forward from the first point, team B's backward from the last. Minions keep to
    /// within <see cref="leash"/> metres of the line, even when they chase something.
    /// </summary>
    [Serializable]
    public class LaneDef
    {
        public string id = "lane";
        /// <summary>The line the waves walk, team A's end first. At least two points.</summary>
        public List<Vec3> points = new List<Vec3>();
        /// <summary>How far from the line, in metres, a minion may go to fight.</summary>
        public float leash = 8f;

        public LaneDef Clone()
        {
            var c = (LaneDef)MemberwiseClone();
            c.points = new List<Vec3>(points);
            return c;
        }

        /// <summary>Length of the line in metres (on the ground plane).</summary>
        public float Length
        {
            get
            {
                float len = 0f;
                for (int i = 1; i < points.Count; i++) len += Flat(points[i] - points[i - 1]).Magnitude;
                return len;
            }
        }

        /// <summary>The point <paramref name="distance"/> metres along the line from team A's end (clamped to the ends).</summary>
        public Vec3 PointAt(float distance)
        {
            if (points.Count == 0) return Vec3.Zero;
            if (distance <= 0f) return points[0];
            for (int i = 1; i < points.Count; i++)
            {
                float seg = Flat(points[i] - points[i - 1]).Magnitude;
                if (distance <= seg && seg > 1e-5f) return Vec3.Lerp(points[i - 1], points[i], distance / seg);
                distance -= seg;
            }
            return points[points.Count - 1];
        }

        /// <summary>Unit direction of the line at a distance, pointing from team A's end toward team B's.</summary>
        public Vec3 DirectionAt(float distance)
        {
            if (points.Count < 2) return Vec3.Forward;
            float d = distance;
            for (int i = 1; i < points.Count; i++)
            {
                var step = Flat(points[i] - points[i - 1]);
                float seg = step.Magnitude;
                if ((d <= seg || i == points.Count - 1) && seg > 1e-5f) return step / seg;
                d -= seg;
            }
            return Vec3.Forward;
        }

        /// <summary>
        /// Where along the line (metres from team A's end) the closest point to <paramref name="p"/> is,
        /// looking only between <paramref name="from"/> and <paramref name="to"/> metres, so a winding
        /// lane whose loops pass near each other is read at the right loop. Also gives the distance off the line.
        /// </summary>
        public float Project(Vec3 p, float from, float to, out float offLine)
        {
            offLine = float.MaxValue;
            float best = 0f, start = 0f;
            var fp = Flat(p);
            for (int i = 1; i < points.Count; i++)
            {
                var a = Flat(points[i - 1]);
                var ab = Flat(points[i]) - a;
                float seg = ab.Magnitude;
                float end = start + seg;
                if (seg > 1e-5f && end >= from && start <= to)
                {
                    float t = Vec3.Dot(fp - a, ab) / (seg * seg);
                    float lo = Math.Max(0f, (from - start) / seg), hi = Math.Min(1f, (to - start) / seg);
                    if (t < lo) t = lo; else if (t > hi) t = hi;
                    float d = Vec3.Distance(fp, a + ab * t);
                    if (d < offLine) { offLine = d; best = start + seg * t; }
                }
                start = end;
            }
            if (offLine == float.MaxValue) offLine = Vec3.Distance(fp, Flat(PointAt(best)));
            return best;
        }

        /// <summary>Problems with the lane, empty when usable.</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            if (points == null || points.Count < 2) e.Add("lane '" + id + "' needs at least two points");
            else if (Length < 1f) e.Add("lane '" + id + "' is too short");
            if (leash < 0f) e.Add("lane '" + id + "': leash can't be negative");
            return e;
        }

        public static Vec3 Flat(Vec3 v) => new Vec3(v.x, 0f, v.z);
    }

    /// <summary>The four kinds of neutral camp (Overrun's jungle).</summary>
    public enum CampKind
    {
        /// <summary>Sends a Juggernaut down the lane with the taking team's next wave.</summary>
        Juggernaut = 0,
        /// <summary>A timed damage boost for every hero on the taking team.</summary>
        Damage = 1,
        /// <summary>Experience split between the heroes who helped take it.</summary>
        Experience = 2,
        /// <summary>A timed speed boost for the heroes standing on the pad when it is taken.</summary>
        Speed = 3,
    }

    /// <summary>
    /// A neutral camp on the map: guards stand at <see cref="guardSpots"/>; once they are all down,
    /// heroes take the camp by holding its pad, a standing cylinder like a zone's.
    /// </summary>
    [Serializable]
    public class CampDef
    {
        public string id = "";
        public CampKind kind;
        /// <summary>Centre of the pad's floor.</summary>
        public Vec3 pad;
        public float padRadius = 4f;
        /// <summary>Feet from 1 m below the pad to this far above it count as on it.</summary>
        public float padHeight = 3f;
        /// <summary>The half of the map it sits in, or None for a camp in the middle (the Juggernaut camp).</summary>
        public TeamId half = TeamId.None;
        /// <summary>Where its guards stand (feet). Empty: the camp kind's number of guards around the pad.</summary>
        public List<Vec3> guardSpots = new List<Vec3>();

        public bool OnPad(Vec3 feet)
        {
            float dx = feet.x - pad.x, dz = feet.z - pad.z;
            if (dx * dx + dz * dz > padRadius * padRadius) return false;
            return feet.y >= pad.y - 1f && feet.y <= pad.y + padHeight;
        }

        public CampDef Clone()
        {
            var c = (CampDef)MemberwiseClone();
            c.guardSpots = new List<Vec3>(guardSpots);
            return c;
        }
    }
}

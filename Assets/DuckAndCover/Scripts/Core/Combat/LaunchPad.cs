using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A launch pad (Overrun maps): step on it and it throws you along a fixed arc to its landing spot.
    /// The arc is the same on every machine: the player's own machine flies the body along it, and the
    /// server's movement check (<see cref="MovementRules"/>) accepts moves that follow it instead of
    /// flagging them as too fast. One way only: the landing spot has no pad back.
    /// </summary>
    [Serializable]
    public class LaunchPad
    {
        public string name = "";
        /// <summary>The team whose base it stands in. Anyone may use it; the validator checks where it is.</summary>
        public TeamId team = TeamId.None;
        /// <summary>Centre of the pad's floor (feet height).</summary>
        public Vec3 center;
        public float radius = 1.5f;
        /// <summary>Where the arc comes down (feet height).</summary>
        public Vec3 landing;
        /// <summary>How high the arc rises above the higher of its two ends, in metres.</summary>
        public float apexHeight = 16f;
        /// <summary>Seconds in the air.</summary>
        public float seconds = 3f;

        public LaunchPad() { }
        public LaunchPad(string name, TeamId team, Vec3 center, Vec3 landing, float apexHeight, float seconds, float radius = 1.5f)
        {
            this.name = name; this.team = team; this.center = center; this.landing = landing;
            this.apexHeight = apexHeight; this.seconds = seconds; this.radius = radius;
        }

        /// <summary>Are these feet standing on the pad (inside its ring, up to half a metre above its floor)?</summary>
        public bool Contains(Vec3 feet)
        {
            float dx = feet.x - center.x, dz = feet.z - center.z;
            return dx * dx + dz * dz <= radius * radius && feet.y >= center.y - 0.3f && feet.y <= center.y + 0.5f;
        }

        /// <summary>
        /// Where the body is <paramref name="t"/> seconds after leaving the pad (clamped to the flight):
        /// a straight line from the pad to the landing on the ground, lifted by a parabola that peaks
        /// <see cref="apexHeight"/> above the higher end halfway along.
        /// </summary>
        public Vec3 PointAt(float t)
        {
            float s = seconds <= 0f ? 1f : t / seconds;
            if (s < 0f) s = 0f; else if (s > 1f) s = 1f;
            var p = Vec3.Lerp(center, landing, s);
            float top = Math.Max(center.y, landing.y) + apexHeight;
            // Peak of the straight line plus lift at s = 0.5 is (center.y + landing.y) / 2 + lift(0.5).
            float lift = (top - (center.y + landing.y) * 0.5f) * 4f * s * (1f - s);
            return new Vec3(p.x, p.y + lift, p.z);
        }

        /// <summary>Velocity along the arc at <paramref name="t"/> seconds (for the client's body when the flight ends).</summary>
        public Vec3 VelocityAt(float t)
        {
            const float h = 0.01f;
            return (PointAt(t + h) - PointAt(t - h)) / (2f * h);
        }

        /// <summary>Ground distance from the pad to the landing.</summary>
        public float FlatLength => LaneDef.Flat(landing - center).Magnitude;

        /// <summary>
        /// The closest the arc comes to <paramref name="p"/> between <paramref name="from"/> and
        /// <paramref name="to"/> seconds into the flight, sampled every twentieth of a second.
        /// </summary>
        public float DistanceTo(Vec3 p, float from, float to)
        {
            from = Math.Max(0f, from);
            to = Math.Min(seconds, to);
            float best = float.MaxValue;
            for (float t = from; ; t += 0.05f)
            {
                if (t > to) t = to;
                float d = Vec3.Distance(PointAt(t), p);
                if (d < best) best = d;
                if (t >= to) break;
            }
            return best;
        }

        public LaunchPad Clone() => (LaunchPad)MemberwiseClone();
    }

    /// <summary>A player stepped on a launch pad and is flying its arc (clients play the throw for others).</summary>
    public sealed class LaunchedEvent : GameEvent
    {
        public int playerId;
        /// <summary>The pad's name in the map data.</summary>
        public string pad = "";
    }
}

using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Where a player's body is at one moment: feet position and whether they crouch. What the
    /// hitboxes are built from, live and in the lag compensation history.
    /// </summary>
    [Serializable]
    public struct BodyPose
    {
        public Vec3 position;
        public bool crouched;
        public bool alive;

        public BodyPose(Vec3 position, bool crouched, bool alive = true)
        {
            this.position = position;
            this.crouched = crouched;
            this.alive = alive;
        }
    }

    /// <summary>
    /// The three hitboxes of a hero, from the ruleset: a head sphere, a torso capsule and a legs
    /// capsule, all upright. Crouching squashes the heights by crouchHeight / standHeight. The Game
    /// layer builds the placeholder body from the same numbers, so what you see is what you hit.
    /// </summary>
    public static class Hitboxes
    {
        public static float HeightScale(Ruleset r, bool crouched) => crouched ? r.crouchHeight / r.standHeight : 1f;

        public static Vec3 HeadCenter(Ruleset r, BodyPose p) => p.position + Vec3.Up * (r.headHeight * HeightScale(r, p.crouched));

        /// <summary>The middle of the torso: what area damage and line of sight aim at.</summary>
        public static Vec3 Chest(Ruleset r, BodyPose p) =>
            p.position + Vec3.Up * ((r.torsoBottom + r.torsoTop) * 0.5f * HeightScale(r, p.crouched));

        public static float EyeHeight(Ruleset r, bool crouched) => crouched ? r.crouchEyeHeight : r.eyeHeight;

        /// <summary>
        /// The hitbox a ray strikes, or false. The distance is where it first enters the body. The
        /// head wins whenever the ray passes through it: the shoulders' rounded top overlaps the head,
        /// and a shot through the head is a head shot. A ray that starts inside a hitbox doesn't hit
        /// that hitbox (a player's own eyes are inside their head).
        /// </summary>
        public static bool Raycast(Ruleset r, BodyPose p, Vec3 origin, Vec3 dir, float max, out float distance, out int bodyPart)
        {
            distance = float.MaxValue;
            bodyPart = BodyParts.None;
            if (!p.alive) return false;
            float s = HeightScale(r, p.crouched);
            Vec3 feet = p.position;
            float t;
            if (RayCapsule(origin, dir, feet + Vec3.Up * (r.torsoBottom * s), feet + Vec3.Up * (r.torsoTop * s), r.torsoRadius, out t) && t <= max && t < distance) { distance = t; bodyPart = BodyParts.Body; }
            if (RayCapsule(origin, dir, feet + Vec3.Up * (r.legsBottom * s), feet + Vec3.Up * (r.legsTop * s), r.legsRadius, out t) && t <= max && t < distance) { distance = t; bodyPart = BodyParts.Legs; }
            if (RaySphere(origin, dir, HeadCenter(r, p), r.headRadius, out t) && t <= max) { distance = Math.Min(distance, t); bodyPart = BodyParts.Head; }
            return bodyPart != BodyParts.None;
        }

        /// <summary>Entry distance of a ray into a sphere. False if missed, behind, or starting inside.</summary>
        public static bool RaySphere(Vec3 origin, Vec3 dir, Vec3 center, float radius, out float t)
        {
            t = 0f;
            Vec3 oc = origin - center;
            float b = Vec3.Dot(oc, dir);
            float c = Vec3.Dot(oc, oc) - radius * radius;
            if (c < 0f) return false; // inside
            float h = b * b - c;
            if (h < 0f) return false;
            t = -b - (float)Math.Sqrt(h);
            return t >= 0f;
        }

        /// <summary>
        /// Entry distance of a ray (unit <paramref name="dir"/>) into a capsule from a to b. False if
        /// missed, behind, or starting inside.
        /// </summary>
        public static bool RayCapsule(Vec3 origin, Vec3 dir, Vec3 a, Vec3 b, float radius, out float t)
        {
            t = 0f;
            if (DistanceToSegment(origin, a, b) < radius) return false; // inside
            Vec3 ba = b - a, oa = origin - a;
            float baba = Vec3.Dot(ba, ba), bard = Vec3.Dot(ba, dir), baoa = Vec3.Dot(ba, oa);
            float rdoa = Vec3.Dot(dir, oa), oaoa = Vec3.Dot(oa, oa);
            float qa = baba - bard * bard;
            float best = float.MaxValue;
            if (qa > 1e-8f)
            {
                float qb = baba * rdoa - baoa * bard;
                float qc = baba * oaoa - baoa * baoa - radius * radius * baba;
                float h = qb * qb - qa * qc;
                if (h >= 0f)
                {
                    float tt = (-qb - (float)Math.Sqrt(h)) / qa;
                    float y = baoa + tt * bard;
                    if (tt >= 0f && y > 0f && y < baba) best = tt;
                }
            }
            // The round ends.
            if (RaySphere(origin, dir, a, radius, out float ta) && ta < best) best = ta;
            if (RaySphere(origin, dir, b, radius, out float tb) && tb < best) best = tb;
            if (best == float.MaxValue) return false;
            t = best;
            return true;
        }

        public static float DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a;
            float len2 = Vec3.Dot(ab, ab);
            float k = len2 <= 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, Vec3.Dot(p - a, ab) / len2));
            return Vec3.Distance(p, a + ab * k);
        }
    }

    /// <summary>
    /// The fixed pellet pattern of multi-pellet weapons: pellet 0 straight ahead, the rest evenly
    /// round a ring at the full spread (an inner ring at half spread once there are more than 8).
    /// Fixed rather than random, so the shooter's screen and the server agree pellet for pellet.
    /// </summary>
    public static class SpreadPattern
    {
        public static Vec3 Direction(Vec3 forward, int pellet, int pellets, float spreadDegrees)
        {
            forward = forward.Normalized;
            if (pellets <= 1 || pellet <= 0 || spreadDegrees <= 0f) return forward;
            int ringCount = pellets - 1;
            int inner = ringCount > 8 ? ringCount / 3 : 0;
            int outer = ringCount - inner;
            int i = pellet - 1;
            bool onInner = i < inner;
            int index = onInner ? i : i - inner;
            int count = onInner ? inner : outer;
            float cone = spreadDegrees * (onInner ? 0.5f : 1f) * (float)(Math.PI / 180.0);
            float around = (float)(2.0 * Math.PI * index / count) + (onInner ? 0.4f : 0f);

            Vec3 helper = Math.Abs(forward.y) > 0.95f ? Vec3.Forward : Vec3.Up;
            Vec3 right = Vec3.Cross(helper, forward).Normalized;
            Vec3 up = Vec3.Cross(forward, right);
            float tan = (float)Math.Tan(cone);
            Vec3 d = forward + right * ((float)Math.Cos(around) * tan) + up * ((float)Math.Sin(around) * tan);
            return d.Normalized;
        }
    }
}

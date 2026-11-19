using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Barriers and gates that abilities place (objects whose definition has a wall size and
    /// blocksShots) as the rules see them: upright boxes standing on their spot, facing the way the
    /// caster aimed. <see cref="CombatWorld"/> asks this so shots, ability rays and line of sight stop on
    /// them the same way on every machine; the Game layer only draws them and gives them a collider.
    /// </summary>
    public static class SolidObjects
    {
        /// <summary>
        /// The nearest solid object along the ray within <paramref name="max"/>, skipping those
        /// <paramref name="skip"/> names. Balls that shots can hit (drones) count unless
        /// <paramref name="wallsOnly"/>, which line of sight uses.
        /// </summary>
        public static bool Raycast(IReadOnlyList<ObjectState> objects, Vec3 origin, Vec3 dir, float max, Func<ObjectState, bool> skip, out HitInfo hit, bool wallsOnly = false)
        {
            hit = HitInfo.Miss;
            if (objects == null) return false;
            float best = max;
            bool found = false;
            foreach (var o in objects)
            {
                if (o == null || o.def == null) continue;
                bool ball = !wallsOnly && o.def.IsBall;
                if (!o.def.IsSolid && !ball) continue;
                if (skip != null && skip(o)) continue;
                float t;
                Vec3 normal;
                if (o.def.IsSolid ? !Intersect(o, origin, dir, best, out t, out normal) : !IntersectBall(o, origin, dir, best, out t, out normal)) continue;
                best = t;
                found = true;
                hit = new HitInfo
                {
                    kind = HitKind.Device, point = origin + dir * t, normal = normal, distance = t,
                    playerId = -1, deviceId = o.id, cell = SurfaceCell.None,
                };
            }
            return found;
        }

        /// <summary>Where the ray enters the object's ball (<see cref="ObjectDef.hitRadius"/> around its spot). A ray starting inside doesn't count.</summary>
        public static bool IntersectBall(ObjectState o, Vec3 origin, Vec3 dir, float max, out float distance, out Vec3 normal)
        {
            distance = 0f;
            normal = Vec3.Zero;
            float r = o.def.hitRadius;
            Vec3 rel = origin - o.position;
            float b = Vec3.Dot(rel, dir);
            float c = Vec3.Dot(rel, rel) - r * r;
            if (c <= 0f) return false;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float t = -b - (float)Math.Sqrt(disc);
            if (t < 0f || t > max) return false;
            distance = t;
            normal = (origin + dir * t - o.position).Normalized;
            return true;
        }

        /// <summary>Where the ray enters the object's box, if it does within <paramref name="max"/>. A ray starting inside doesn't count.</summary>
        public static bool Intersect(ObjectState o, Vec3 origin, Vec3 dir, float max, out float distance, out Vec3 normal)
        {
            distance = 0f;
            normal = Vec3.Zero;
            Vec3 forward = o.facing.Magnitude < 1e-4f ? Vec3.Forward : o.facing.Normalized;
            Vec3 up = Vec3.Up;
            Vec3 right = Vec3.Cross(up, forward).Normalized;
            Vec3 center = o.position + up * (o.def.height * 0.5f);
            Vec3 half = new Vec3(o.Width * 0.5f, o.def.height * 0.5f, ObjectDef.SolidDepth * 0.5f);

            // Into the box's own axes: x across, y up, z through.
            Vec3 rel = origin - center;
            Vec3 p = new Vec3(Vec3.Dot(rel, right), Vec3.Dot(rel, up), Vec3.Dot(rel, forward));
            Vec3 d = new Vec3(Vec3.Dot(dir, right), Vec3.Dot(dir, up), Vec3.Dot(dir, forward));

            float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
            int axis = -1;
            float sign = 0f;
            for (int i = 0; i < 3; i++)
            {
                float pi = i == 0 ? p.x : i == 1 ? p.y : p.z;
                float di = i == 0 ? d.x : i == 1 ? d.y : d.z;
                float hi = i == 0 ? half.x : i == 1 ? half.y : half.z;
                if (Math.Abs(di) < 1e-8f)
                {
                    if (pi < -hi || pi > hi) return false;
                    continue;
                }
                float t1 = (-hi - pi) / di, t2 = (hi - pi) / di;
                float s = -1f;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; s = 1f; }
                if (t1 > tMin) { tMin = t1; axis = i; sign = s; }
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }
            if (axis < 0 || tMin < 0f || tMin > max) return false;
            distance = tMin;
            normal = (axis == 0 ? right : axis == 1 ? up : forward) * sign;
            return true;
        }
    }
}

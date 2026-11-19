using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Targeting shapes, running effects on what they caught, and effects waiting on a delay.
    public sealed partial class AbilityRunner
    {
        /// <summary>
        /// Whether <paramref name="targetId"/> (a player or unit) is someone an "affects" rule reaches,
        /// seen from the source. Neutral units are everyone's enemy and nobody's ally.
        /// </summary>
        public bool Matches(string affects, int sourceId, TeamId sourceTeam, int targetId)
        {
            if (!Combat.IsKnown(targetId)) return false;
            var team = Combat.TeamOf(targetId);
            bool ally = team != TeamId.None && team == sourceTeam;
            switch (affects)
            {
                case Affects.Enemies: return !ally && targetId != sourceId;
                case Affects.Allies: return ally && targetId != sourceId;
                case Affects.AlliesAndSelf: return ally || targetId == sourceId;
                case Affects.Self: return targetId == sourceId;
                case Affects.All: return true;
                default: return false;
            }
        }

        /// <summary>A caster's ray: through their own team's barriers when the world can tell (see <see cref="ICasterQuery"/>).</summary>
        bool CastRay(int casterId, Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit)
        {
            if (world is ICasterQuery q) return q.RaycastFor(casterId, origin, dir, max, tick, out hit);
            return world.Raycast(origin, dir, max, tick, out hit);
        }

        List<EffectArea> FindAreas(int casterId, TeamId team, TargetingDef t, Vec3 origin, Vec3 dir, int tick)
        {
            var areas = new List<EffectArea>();
            int count = Math.Max(1, t.count);
            switch (t.shape)
            {
                case Shapes.Ray:
                    for (int i = 0; i < count; i++)
                    {
                        float angle = count == 1 ? 0f : -t.spreadDegrees / 2f + t.spreadDegrees * i / (count - 1);
                        Vec3 d = Yaw(dir, angle);
                        var area = new EffectArea { point = origin + d * t.range };
                        if (CastRay(casterId, origin, d, t.range, tick, out var hit) && hit.kind != HitKind.Nothing)
                        {
                            area.point = hit.point;
                            if (hit.kind == HitKind.Player && Combat.IsAlive(hit.playerId) && Matches(t.affects, casterId, team, hit.playerId))
                                area.targets = new[] { hit.playerId };
                            else if (hit.kind == HitKind.Device && CanHitObject(t.affects, team, State.Object(hit.deviceId)))
                                area.objects = new[] { hit.deviceId };
                        }
                        areas.Add(area);
                    }
                    break;

                case Shapes.Sphere:
                    float radius = t.radius * Math.Max(0f, Combat.Stat(casterId, StatIds.AbilityRadius));
                    Vec3 center = origin;
                    if (t.origin == Origins.AimPoint)
                    {
                        center = CastRay(casterId, origin, dir, t.range, tick, out var land) && land.kind != HitKind.Nothing ? land.point : origin + dir * t.range;
                        if (t.ground) center = Ground(casterId, origin, center, tick);
                    }
                    var first = Query(center, radius, t.affects, casterId, team, t.maxTargets, tick, t.origin == Origins.Caster ? t.arcDegrees : 0f, dir);
                    areas.Add(first);
                    for (int i = 1; i < count; i++)
                    {
                        // Extra areas sit in a ring, one radius out from the first.
                        double a = 2.0 * Math.PI * (i - 1) / (count - 1);
                        var c = center + new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a)) * radius;
                        areas.Add(Query(c, radius, t.affects, casterId, team, t.maxTargets, tick));
                    }
                    break;

                default: // self
                    areas.Add(new EffectArea { point = origin, targets = new[] { casterId } });
                    break;
            }
            return areas;
        }

        /// <summary>
        /// The floor below an aimed point: a little back toward the caster (off a wall it hit), then straight
        /// down, up to <see cref="Ruleset.groundProbeMetres"/>. Nothing below: the point as it is.
        /// </summary>
        Vec3 Ground(int casterId, Vec3 origin, Vec3 point, int tick)
        {
            Vec3 back = new Vec3(origin.x - point.x, 0f, origin.z - point.z);
            float len = back.Magnitude;
            if (len > 1e-4f) point += back / len * Math.Min(len, Rules.groundProbeBack);
            Vec3 start = point + Vec3.Up * 0.05f;
            if (CastRay(casterId, start, -Vec3.Up, Rules.groundProbeMetres, tick, out var floor) && floor.kind != HitKind.Nothing && floor.kind != HitKind.Player)
                return floor.point;
            return point;
        }

        /// <summary>
        /// Who an area catches: living players and units the rule reaches and enemy objects. With a limit,
        /// the ones nearest the centre are kept (ties to the lower id), so a single-target grenade takes
        /// whoever is closest to where it lands.
        /// </summary>
        EffectArea Query(Vec3 center, float radius, string affects, int sourceId, TeamId team, int maxTargets, int tick, float arcDegrees = 0f, Vec3 aim = default)
        {
            var ids = new List<int>();
            foreach (int id in world.PlayersInSphere(center, radius, tick))
                if (!ids.Contains(id) && Combat.IsAlive(id) && Matches(affects, sourceId, team, id)
                    && (arcDegrees <= 0f || id == sourceId || !(world is IPositionQuery wq) || !wq.TryGetPosition(id, tick, out var spot) || InArc(center, aim, arcDegrees, spot)))
                    ids.Add(id);
            ids.Sort();
            if (maxTargets > 0 && ids.Count > maxTargets)
            {
                var dist = new Dictionary<int, float>();
                foreach (int id in ids) dist[id] = world is IPositionQuery where && where.TryGetPosition(id, tick, out var at) ? Vec3.Distance(at, center) : float.MaxValue;
                ids.Sort((a, b) => { int c = dist[a].CompareTo(dist[b]); return c != 0 ? c : a.CompareTo(b); });
                ids.RemoveRange(maxTargets, ids.Count - maxTargets);
            }

            var objects = new List<int>();
            foreach (var o in State.objects)
                if (CanHitObject(affects, team, o) && Vec3.Distance(o.position, center) <= radius + Epsilon
                    && (arcDegrees <= 0f || InArc(center, aim, arcDegrees, o.position))) objects.Add(o.id);
            return new EffectArea { point = center, targets = ids.ToArray(), objects = objects.ToArray() };
        }

        /// <summary>
        /// Whether a spot is inside a sweep: within half of <paramref name="arcDegrees"/> either side of the level
        /// aim, seen from <paramref name="center"/>. A spot right on the centre counts. 360 or more is all round.
        /// </summary>
        static bool InArc(Vec3 center, Vec3 aim, float arcDegrees, Vec3 spot)
        {
            if (arcDegrees >= 360f) return true;
            var level = new Vec3(aim.x, 0f, aim.z);
            var to = new Vec3(spot.x - center.x, 0f, spot.z - center.z);
            if (level.Magnitude < 1e-4f || to.Magnitude < 1e-3f) return true;
            float cos = Vec3.Dot(level.Normalized, to.Normalized);
            double angle = Math.Acos(Math.Max(-1f, Math.Min(1f, cos))) * 180.0 / Math.PI;
            return angle <= arcDegrees * 0.5f + 1e-3f;
        }

        bool CanHitObject(string affects, TeamId team, ObjectState o)
        {
            if (o == null || o.def.health <= 0f) return false;
            if (affects != Affects.Enemies && affects != Affects.All) return false;
            return o.team != team || Rules.friendlyFire;
        }

        /// <summary>Turn a direction left or right around the up axis (ray fans).</summary>
        static Vec3 Yaw(Vec3 dir, float degrees)
        {
            if (degrees == 0f) return dir;
            double r = degrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(r), sin = (float)Math.Sin(r);
            return new Vec3(dir.x * cos + dir.z * sin, dir.y, -dir.x * sin + dir.z * cos);
        }

        /// <summary>Apply one effect to what the areas caught, credited to the source.</summary>
        void RunEffect(EffectDef e, int sourceId, TeamId team, string abilityId, int objectId, Vec3 aim, List<EffectArea> areas, float scale, ResolvedAbility ability)
        {
            if (e == null || !effects.TryGetValue(e.kind, out var fx)) return;
            var ctx = new EffectContext
            {
                runner = this, sourceId = sourceId, sourceTeam = team, abilityId = abilityId ?? "", objectId = objectId,
                effect = e, aim = aim, scale = scale, ability = ability,
            };

            if (e.kind == EffectKinds.Spawn)
            {
                foreach (var area in areas) { ctx.point = area.point; fx.Apply(ctx, -1); }
                return;
            }
            if (e.affects == Affects.Self)
            {
                if (areas.Count > 0) ctx.point = areas[0].point;
                if (Combat.IsAlive(sourceId)) fx.Apply(ctx, sourceId);
                return;
            }
            foreach (var area in areas)
            {
                ctx.point = area.point;
                foreach (int target in area.targets)
                {
                    if (!Combat.IsAlive(target)) continue;
                    if (!string.IsNullOrEmpty(e.affects) && !Matches(e.affects, sourceId, team, target)) continue;
                    fx.Apply(ctx, target);
                }
                if (e.kind == EffectKinds.Damage)
                {
                    float dealt = e.amount * scale * (Combat.IsKnown(sourceId) ? Math.Max(0f, Combat.Stat(sourceId, StatIds.DamageDealt)) : 1f);
                    foreach (int obj in area.objects) DamageObject(obj, dealt, sourceId);
                }
            }
        }

        void TickPending(float dt)
        {
            foreach (var p in State.pending.ToArray())
            {
                p.timeLeft -= dt;
                if (p.timeLeft > Epsilon) continue;
                State.pending.Remove(p);
                var areas = p.areas;
                if (p.requery)
                {
                    areas = new List<EffectArea>();
                    foreach (var a in p.areas) areas.Add(Query(a.point, p.radius, p.affects, p.sourceId, p.team, p.maxTargets, Match.tick));
                }
                var unit = State.Unit(p.sourceId);
                var ability = unit != null && Library.Ability(p.abilityId) != null ? Resolved(unit, p.abilityId) : null;
                RunEffect(p.effect, p.sourceId, p.team, p.abilityId, 0, p.aim, areas, 1f, ability);
            }
        }
    }
}

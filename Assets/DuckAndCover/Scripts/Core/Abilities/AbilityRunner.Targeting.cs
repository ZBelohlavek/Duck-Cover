using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Targeting shapes, running effects on what they caught, and effects waiting on a delay.
    public sealed partial class AbilityRunner
    {
        /// <summary>Whether <paramref name="targetId"/> is someone an "affects" rule reaches, seen from the source.</summary>
        public bool Matches(string affects, int sourceId, TeamId sourceTeam, int targetId)
        {
            var target = Match.Player(targetId);
            if (target == null) return false;
            switch (affects)
            {
                case Affects.Enemies: return target.team != sourceTeam;
                case Affects.Allies: return target.team == sourceTeam && targetId != sourceId;
                case Affects.AlliesAndSelf: return target.team == sourceTeam;
                case Affects.Self: return targetId == sourceId;
                case Affects.All: return true;
                default: return false;
            }
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
                        if (world.Raycast(origin, d, t.range, tick, out var hit) && hit.kind != HitKind.Nothing)
                        {
                            area.point = hit.point;
                            if (hit.kind == HitKind.Player && Match.IsAlive(hit.playerId) && Matches(t.affects, casterId, team, hit.playerId))
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
                        center = world.Raycast(origin, dir, t.range, tick, out var land) && land.kind != HitKind.Nothing ? land.point : origin + dir * t.range;
                    areas.Add(Query(center, radius, t.affects, casterId, team, t.maxTargets, tick));
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

        /// <summary>Who an area catches: living players the rule reaches (lowest ids first, up to the limit) and enemy objects.</summary>
        EffectArea Query(Vec3 center, float radius, string affects, int sourceId, TeamId team, int maxTargets, int tick)
        {
            var ids = new List<int>();
            foreach (int id in world.PlayersInSphere(center, radius, tick))
                if (!ids.Contains(id) && Match.IsAlive(id) && Matches(affects, sourceId, team, id)) ids.Add(id);
            ids.Sort();
            if (maxTargets > 0 && ids.Count > maxTargets) ids.RemoveRange(maxTargets, ids.Count - maxTargets);

            var objects = new List<int>();
            foreach (var o in State.objects)
                if (CanHitObject(affects, team, o) && Vec3.Distance(o.position, center) <= radius + Epsilon) objects.Add(o.id);
            return new EffectArea { point = center, targets = ids.ToArray(), objects = objects.ToArray() };
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
                if (Match.IsAlive(sourceId)) fx.Apply(ctx, sourceId);
                return;
            }
            foreach (var area in areas)
            {
                ctx.point = area.point;
                foreach (int target in area.targets)
                {
                    if (!Match.IsAlive(target)) continue;
                    if (!string.IsNullOrEmpty(e.affects) && !Matches(e.affects, sourceId, team, target)) continue;
                    fx.Apply(ctx, target);
                }
                if (e.kind == EffectKinds.Damage)
                {
                    float dealt = e.amount * scale * (Match.Player(sourceId) != null ? Math.Max(0f, Combat.Stat(sourceId, StatIds.DamageDealt)) : 1f);
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

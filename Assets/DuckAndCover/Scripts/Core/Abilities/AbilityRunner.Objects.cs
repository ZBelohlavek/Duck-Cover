using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Objects that abilities place: barriers, mines, turrets, fields.
    public sealed partial class AbilityRunner
    {
        /// <summary>
        /// Place an object for a player. If they already have the most allowed of this kind, the
        /// oldest goes. The Game layer builds the model and colliders from ObjectSpawnedEvent.
        /// </summary>
        public ObjectState SpawnObject(ObjectDef def, int ownerId, Vec3 position, string abilityId = "")
        {
            if (def == null || !Match.IsPlaying) return null;
            var owner = Match.Player(ownerId);
            if (def.maxPerOwner > 0)
            {
                var mine = State.objects.FindAll(o => o.ownerId == ownerId && o.defId == def.id);
                for (int i = 0; i <= mine.Count - def.maxPerOwner; i++) RemoveObject(mine[i], "replaced", -1);
            }
            var obj = new ObjectState
            {
                id = State.nextObjectId++,
                defId = def.id,
                ownerId = ownerId,
                team = owner != null ? owner.team : TeamId.None,
                abilityId = abilityId ?? "",
                position = position,
                health = def.health,
                timeLeft = def.lifetime,
                armLeft = def.armTime,
                pulseIn = def.trigger == Triggers.Pulse ? def.interval : 0f,
                def = def.Clone(),
                radius = def.radius * (owner != null ? Math.Max(0f, Combat.Stat(ownerId, StatIds.AbilityRadius)) : 1f),
            };
            State.objects.Add(obj);
            Publish(new ObjectSpawnedEvent
            {
                objectId = obj.id, defId = obj.defId, ownerId = ownerId, team = obj.team, abilityId = obj.abilityId,
                position = position, radius = obj.radius, lifetime = def.lifetime, health = def.health,
                blocksMovement = def.blocksMovement, blocksShots = def.blocksShots,
            });
            return obj;
        }

        /// <summary>
        /// Hurt an object (weapons call this when a shot hits its deviceId). The amount is final:
        /// the caller applies the attacker's damage stats. Teammates can't hurt it unless friendly
        /// fire is on. Returns the damage taken.
        /// </summary>
        public float DamageObject(int objectId, float amount, int attackerId = -1)
        {
            var obj = State.Object(objectId);
            if (obj == null || obj.def.health <= 0f || amount <= 0f) return 0f;
            var attacker = Match.Player(attackerId);
            if (attacker != null && attacker.team == obj.team && !Rules.friendlyFire) return 0f;
            float taken = Math.Min(obj.health, amount);
            obj.health -= taken;
            Publish(new ObjectDamagedEvent { objectId = obj.id, attackerId = attackerId, amount = taken, healthLeft = obj.health });
            if (obj.health <= 0f) RemoveObject(obj, "destroyed", attackerId);
            return taken;
        }

        /// <summary>Take an object out of the world (a script picking its turret back up, for example).</summary>
        public bool RemoveObject(int objectId, string reason = "removed")
        {
            var obj = State.Object(objectId);
            if (obj == null) return false;
            RemoveObject(obj, reason, -1);
            return true;
        }

        void RemoveObject(ObjectState obj, string reason, int byPlayerId)
        {
            if (!State.objects.Remove(obj)) return;
            Publish(new ObjectRemovedEvent { objectId = obj.id, defId = obj.defId, ownerId = obj.ownerId, reason = reason, byPlayerId = byPlayerId });
        }

        void RemoveObjectsOf(int ownerId, string reason)
        {
            foreach (var o in State.objects.FindAll(x => x.ownerId == ownerId)) RemoveObject(o, reason, -1);
        }

        void TickObjects(float dt)
        {
            foreach (var obj in State.objects.ToArray())
            {
                if (!State.objects.Contains(obj)) continue;
                var def = obj.def;
                var owner = Match.Player(obj.ownerId);
                if (owner != null && !owner.connected) { RemoveObject(obj, "ownerLeft", -1); continue; } // left while waiting to respawn
                if (def.lifetime > 0f)
                {
                    obj.timeLeft -= dt;
                    if (obj.timeLeft <= Epsilon) { RemoveObject(obj, "expired", -1); continue; }
                }
                if (def.trigger != Triggers.Pulse && def.trigger != Triggers.Proximity) continue;
                if (obj.armLeft > 0f)
                {
                    obj.armLeft -= dt;
                    if (obj.armLeft > Epsilon) continue;
                    obj.armLeft = 0f;
                }

                if (obj.pulseIn > 0f)
                {
                    obj.pulseIn -= dt;
                    if (obj.pulseIn > Epsilon) continue;
                }
                // A pulse fires on its beat whether or not anyone is there; a mine waits for someone.
                var area = ObjectTargets(obj);
                if (def.trigger == Triggers.Pulse) obj.pulseIn += def.interval;
                if (area.targets.Length == 0) { if (def.trigger == Triggers.Proximity) obj.pulseIn = 0f; continue; }
                if (def.trigger == Triggers.Proximity) obj.pulseIn = def.interval;

                Publish(new ObjectTriggeredEvent { objectId = obj.id, defId = obj.defId, position = obj.position, targets = (int[])area.targets.Clone() });
                var areas = new List<EffectArea> { area };
                foreach (var e in def.effects)
                {
                    if (e == null || e.kind == EffectKinds.Spawn || !string.IsNullOrEmpty(e.tag) && !ObjectTagOn(obj, e.tag)) continue;
                    RunEffect(e, obj.ownerId, obj.team, obj.abilityId, obj.id, Vec3.Zero, areas, 1f, null);
                }
                if (def.trigger == Triggers.Proximity && def.consumeOnTrigger) RemoveObject(obj, "triggered", -1);
            }
        }

        bool ObjectTagOn(ObjectState obj, string tag)
        {
            var unit = State.Unit(obj.ownerId);
            return unit != null && Library.Ability(obj.abilityId) != null && Resolved(unit, obj.abilityId).enabled.Contains(tag);
        }

        EffectArea ObjectTargets(ObjectState obj)
        {
            var area = Query(obj.position, obj.radius, obj.def.affects, obj.ownerId, obj.team, 0, Match.tick);
            var ids = new List<int>(area.targets);
            if (obj.def.needsLineOfSight && world is ILineOfSightQuery los)
                ids.RemoveAll(id => !los.HasLineOfSight(obj.position, id, Match.tick));
            if (obj.def.maxTargets > 0 && ids.Count > obj.def.maxTargets) ids.RemoveRange(obj.def.maxTargets, ids.Count - obj.def.maxTargets);
            area.targets = ids.ToArray();
            area.objects = new int[0];
            return area;
        }
    }
}

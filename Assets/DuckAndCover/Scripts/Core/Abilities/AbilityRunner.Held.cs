using System;

namespace DuckAndCover.Core
{
    /// <summary>Where a hero stands and the level way they face, for objects that move with them.</summary>
    public delegate bool OwnerPoseQuery(int playerId, out Vec3 feet, out Vec3 facing);

    // Held abilities (a shield raised and lowered) and objects that move with their owner (the
    // shield itself, a drone at the shoulder).
    public sealed partial class AbilityRunner
    {
        /// <summary>
        /// Where heroes stand and face, for objects that follow someone (<see cref="ObjectState.followId"/>);
        /// CombatServer answers from the movement rules. When it has no answer (no hook, or a unit), they
        /// follow the position query and keep the way they faced.
        /// </summary>
        public OwnerPoseQuery OwnerPose;

        /// <summary>Why the hero can't fire their weapon because of an ability that is up, or null.</summary>
        public string WeaponBlocked(int playerId)
        {
            var unit = Combat.Unit(playerId);
            if (unit == null) return null;
            foreach (var s in unit.slots)
                if (s.raised && Resolved(unit, s.abilityId).def.blocksWeapon) return Resolved(unit, s.abilityId).def.displayName + " is up";
            return null;
        }

        /// <summary>Whether the hero's held ability in this slot is up.</summary>
        public bool IsRaised(int playerId, int slot)
        {
            var unit = Combat.Unit(playerId);
            return unit != null && slot >= 0 && slot < unit.slots.Count && unit.slots[slot].raised;
        }

        static string HeldSource(string abilityId) => "held:" + abilityId;

        /// <summary>The held ability's object as this hero has it (talents applied).</summary>
        ObjectDef HeldObjectOf(ResolvedAbility ability)
        {
            foreach (var e in ability.def.effects)
            {
                if (e == null || e.kind != EffectKinds.Spawn) continue;
                if (ability.objects.TryGetValue(e.objectId, out var def)) return def;
                return Library.Object(e.objectId);
            }
            return null;
        }

        /// <summary>Put a held ability up: its object comes out with the health it kept, and its stats apply.</summary>
        CommandResult Raise(HeroUnit unit, int slotIndex, UseAbilityCommand use)
        {
            var s = unit.slots[slotIndex];
            var ability = Resolved(unit, s.abilityId);
            Vec3 dir = use.aimDirection.Normalized;
            if (dir == Vec3.Zero) dir = Vec3.Forward;
            s.aimOrigin = use.aimOrigin;
            s.aimDirection = dir;
            s.castTick = use.tick;

            int before = State.nextObjectId;
            Land(unit, slotIndex);
            ObjectState obj = null;
            foreach (var o in State.objects)
                if (o.id >= before && o.ownerId == unit.playerId && o.abilityId == ability.def.id) { obj = o; break; }
            if (obj == null) return CommandResult.Accepted; // the match ended as it went up
            obj.health = Math.Max(1f, obj.def.health * Math.Max(0f, Math.Min(1f, s.reserve)));
            s.raised = true;
            s.heldObjectId = obj.id;
            s.downFor = 0f;
            if (ability.def.stats != null && ability.def.stats.Length > 0)
                Combat.AddModifiers(unit.playerId, HeldSource(ability.def.id), ability.def.stats);
            return CommandResult.Accepted;
        }

        /// <summary>Take a held ability down; its object keeps its health share for next time.</summary>
        void Lower(HeroUnit unit, int slotIndex, string reason)
        {
            var s = unit.slots[slotIndex];
            if (!s.raised) return;
            var obj = State.Object(s.heldObjectId);
            if (obj != null) RemoveObject(obj, reason, -1); // ObjectGone keeps the share and clears the slot
            else EndHeld(unit, s, s.reserve);
        }

        void EndHeld(HeroUnit unit, AbilitySlotState s, float reserve)
        {
            s.raised = false;
            s.heldObjectId = 0;
            s.reserve = Math.Max(0f, Math.Min(1f, reserve));
            s.downFor = 0f;
            Combat.RemoveModifiers(unit.playerId, HeldSource(s.abilityId));
        }

        /// <summary>An object left the world: if it was a held ability's, the ability is down now.</summary>
        void ObjectGone(ObjectState obj, string reason)
        {
            var unit = State.Unit(obj.ownerId);
            if (unit == null) return;
            foreach (var s in unit.slots)
            {
                if (!s.raised || s.heldObjectId != obj.id) continue;
                if (reason == "destroyed")
                {
                    // Broken: it comes back from nothing, after the cooldown.
                    EndHeld(unit, s, 0f);
                    s.charges = 0;
                    s.rechargeLeft = Resolved(unit, s.abilityId).def.cooldown;
                }
                else EndHeld(unit, s, obj.def.health > 0f ? obj.health / obj.def.health : 1f);
            }
        }

        /// <summary>Held abilities each tick: down when stunned or out of the fight, and their health coming back while down.</summary>
        void TickHeld(HeroUnit unit, int slotIndex, ResolvedAbility ability, float dt)
        {
            var s = unit.slots[slotIndex];
            if (s.raised)
            {
                if (!Match.IsAlive(unit.playerId) || IsStunned(unit.playerId)) Lower(unit, slotIndex, "lowered");
                return;
            }
            if (s.reserve >= 1f) return;
            var obj = HeldObjectOf(ability);
            if (obj == null || obj.health <= 0f) { s.reserve = 1f; return; }
            s.downFor += dt;
            if (s.downFor < obj.regenDelay) return;
            s.reserve = Math.Min(1f, s.reserve + obj.regen / obj.health * dt);
        }

        /// <summary>Move an object that follows someone (<see cref="ObjectState.followId"/>) to where they stand now.</summary>
        void Follow(ObjectState obj)
        {
            if (obj.followId < 0 || obj.def == null || !Combat.IsAlive(obj.followId)) return;
            Vec3 feet, facing;
            if (OwnerPose != null && OwnerPose(obj.followId, out feet, out facing)) { }
            else if (world is IPositionQuery q && q.TryGetPosition(obj.followId, Match.tick, out feet)) facing = obj.facing;
            else return;
            facing = Level(facing);
            obj.facing = facing;
            obj.position = feet + facing * obj.def.followAhead + Vec3.Up * obj.def.followRise;
        }

        /// <summary>Send a share of what an object took back to whoever dealt it (a shield that deflects).</summary>
        void Reflect(ObjectState obj, float taken, int attackerId)
        {
            if (obj.def.reflect <= 0f || taken <= 0f || attackerId < 0 || attackerId == obj.ownerId) return;
            if (!Combat.IsKnown(attackerId) || !Combat.IsAlive(attackerId)) return;
            if (obj.team != TeamId.None && Combat.TeamOf(attackerId) == obj.team) return;
            Combat.Damage(obj.ownerId, attackerId, taken * obj.def.reflect, obj.abilityId, obj.id);
        }
    }
}

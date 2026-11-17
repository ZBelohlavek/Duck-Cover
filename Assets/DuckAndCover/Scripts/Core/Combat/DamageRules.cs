using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Health, shields and stats for every hero in the match: the one place damage and healing are
    /// decided. Abilities use it through the effects; weapons (WP4) call <see cref="Damage"/> with
    /// their own numbers. A hero at zero health is eliminated through MatchRules, and comes back at
    /// full health when the Spawn rules return them.
    /// </summary>
    public sealed class DamageRules
    {
        readonly MatchRules match;
        readonly IWorldQuery world;
        readonly IGameEvents events;
        readonly AbilityState state;

        Ruleset Rules => match.Rules;

        public DamageRules(MatchRules match, IWorldQuery world, IGameEvents events, AbilityState state)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        // ---- Units and stats ----

        /// <summary>The hero state of a player in the match (made on first use, at full health), or null.</summary>
        public HeroUnit Unit(int playerId)
        {
            foreach (var u in state.units) if (u.playerId == playerId) return u;
            if (match.State.Player(playerId) == null) return null;
            var unit = new HeroUnit { playerId = playerId };
            unit.stats.SetBase(StatIds.MaxHealth, Rules.defaultMaxHealth);
            unit.health = Rules.defaultMaxHealth;
            state.units.Add(unit);
            return unit;
        }

        /// <summary>A stat's value now, with conditional modifiers judged against the live match.</summary>
        public float Stat(int playerId, string stat)
        {
            var u = Unit(playerId);
            if (u == null) return StatIds.DefaultBase(stat);
            return u.stats.Get(stat, when => Holds(u, when));
        }

        public float MaxHealth(int playerId) => Math.Max(1f, Stat(playerId, StatIds.MaxHealth));

        /// <summary>The hero's speed multiplier for the movement code, never below Ruleset.minMoveSpeed.</summary>
        public float MoveSpeed(int playerId) => Math.Max(Rules.minMoveSpeed, Stat(playerId, StatIds.MoveSpeed));

        /// <summary>Whether a modifier condition (StatConditions) holds for the player right now.</summary>
        public bool Holds(int playerId, string when)
        {
            var u = Unit(playerId);
            return u != null && Holds(u, when);
        }

        bool Holds(HeroUnit u, string when)
        {
            switch (when)
            {
                case null:
                case "":
                    return true;
                case StatConditions.Shielded:
                    return u.Shield > 0f;
                case StatConditions.LowHealth:
                    // Unconditional max health here, so a low-health bonus to max health can't loop.
                    return u.health <= Rules.lowHealthShare * Math.Max(1f, u.stats.Get(StatIds.MaxHealth));
                case StatConditions.OnPoint:
                case StatConditions.OnHeldPoint:
                    var p = match.State.Player(u.playerId);
                    if (p == null || !p.alive) return false;
                    foreach (var z in match.State.zones)
                    {
                        if (!z.active) continue;
                        if (when == StatConditions.OnHeldPoint && z.owner != p.team) continue;
                        if (Array.IndexOf(world.PlayersInZone(z.id), u.playerId) >= 0) return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>The hero's own max health before modifiers (from hero data). Refills to the new maximum.</summary>
        public void SetBaseMaxHealth(int playerId, float value)
        {
            var u = Unit(playerId);
            if (u == null || value <= 0f) return;
            u.stats.SetBase(StatIds.MaxHealth, value);
            u.health = MaxHealth(playerId);
        }

        /// <summary>
        /// Add stat modifiers from a source (an upgrade id, a passive). A second call with the same
        /// source replaces the first. Extra max health is given as health straight away.
        /// </summary>
        public void AddModifiers(int playerId, string source, IEnumerable<StatModifier> mods)
        {
            var u = Unit(playerId);
            if (u == null) return;
            float before = MaxHealth(playerId);
            u.stats.Remove(source);
            u.stats.Add(source, mods);
            SettleHealth(u, before);
        }

        public void RemoveModifiers(int playerId, string source)
        {
            var u = Unit(playerId);
            if (u == null) return;
            float before = MaxHealth(playerId);
            u.stats.Remove(source);
            SettleHealth(u, before);
        }

        void SettleHealth(HeroUnit u, float maxBefore)
        {
            float max = MaxHealth(u.playerId);
            if (max > maxBefore && match.State.IsAlive(u.playerId)) u.health += max - maxBefore;
            if (u.health > max) u.health = max;
        }

        // ---- Damage, healing, shields ----

        /// <summary>
        /// Hurt a hero. The amount is scaled by the attacker's damageDealt and the target's
        /// damageTaken, shields absorb first, and zero health eliminates. Returns what was taken
        /// (health plus shield), 0 when nothing happened. <paramref name="attackerId"/> -1 = the world.
        /// </summary>
        public float Damage(int attackerId, int targetId, float amount, string abilityId = "", int objectId = 0)
        {
            if (amount <= 0f || !match.State.IsPlaying || !match.State.IsAlive(targetId)) return 0f;
            var target = Unit(targetId);
            var attacker = match.State.Player(attackerId);
            var victim = match.State.Player(targetId);
            if (attacker != null && attackerId != targetId && attacker.team == victim.team && !Rules.friendlyFire) return 0f;

            float scaled = amount * Math.Max(0f, Stat(targetId, StatIds.DamageTaken));
            if (attacker != null) scaled *= Math.Max(0f, Stat(attackerId, StatIds.DamageDealt));
            if (scaled <= 0f) return 0f;

            float left = scaled, absorbed = 0f;
            target.shields.Sort((a, b) => a.timeLeft.CompareTo(b.timeLeft)); // the shield about to run out goes first
            foreach (var s in target.shields)
            {
                float take = Math.Min(s.amount, left);
                s.amount -= take;
                left -= take;
                absorbed += take;
                if (left <= 0f) break;
            }
            target.shields.RemoveAll(s => s.amount <= 0f);

            float lost = Math.Min(target.health, left);
            target.health -= lost;
            target.sinceDamaged = 0f;
            bool down = target.health <= 0f;
            Publish(new DamagedEvent
            {
                attackerId = attacker != null ? attackerId : -1,
                targetId = targetId,
                amount = lost + absorbed,
                absorbed = absorbed,
                healthLeft = target.health,
                abilityId = abilityId ?? "",
                objectId = objectId,
                eliminated = down,
            });
            if (down) match.EliminatePlayer(targetId, attacker != null && attackerId != targetId ? attackerId : -1);
            return lost + absorbed;
        }

        /// <summary>
        /// Restore health, scaled by the healer's healingDone and the target's healingTaken. Returns
        /// the health actually restored. Anyone can be healed by anyone: who is reached is the
        /// ability's targeting.
        /// </summary>
        public float Heal(int healerId, int targetId, float amount, string abilityId = "", int objectId = 0)
        {
            if (amount <= 0f || !match.State.IsPlaying || !match.State.IsAlive(targetId)) return 0f;
            var target = Unit(targetId);
            float scaled = amount * Math.Max(0f, Stat(targetId, StatIds.HealingTaken));
            if (match.State.Player(healerId) != null) scaled *= Math.Max(0f, Stat(healerId, StatIds.HealingDone));
            float restored = Math.Min(scaled, MaxHealth(targetId) - target.health);
            if (restored <= 0f) return 0f;
            target.health += restored;
            Publish(new HealedEvent { healerId = healerId, targetId = targetId, amount = restored, healthNow = target.health, abilityId = abilityId ?? "", objectId = objectId });
            return restored;
        }

        /// <summary>
        /// Give a shield for some seconds, scaled by the source's shieldStrength. A new shield from
        /// the same source and ability replaces the old one; all shields together stop at
        /// Ruleset.maxShield. Returns the shield actually added.
        /// </summary>
        public float AddShield(int sourceId, int targetId, float amount, float seconds, string abilityId = "", int objectId = 0)
        {
            if (amount <= 0f || !match.State.IsPlaying || !match.State.IsAlive(targetId)) return 0f;
            var target = Unit(targetId);
            abilityId = abilityId ?? "";
            if (seconds <= 0f) seconds = Rules.defaultShieldSeconds;
            if (match.State.Player(sourceId) != null) amount *= Math.Max(0f, Stat(sourceId, StatIds.ShieldStrength));
            target.shields.RemoveAll(s => s.sourceId == sourceId && s.abilityId == abilityId);
            float added = Math.Min(amount, Rules.maxShield - target.Shield);
            if (added <= 0f) return 0f;
            target.shields.Add(new ShieldState { sourceId = sourceId, abilityId = abilityId, amount = added, timeLeft = seconds });
            Publish(new ShieldedEvent { sourceId = sourceId, targetId = targetId, amount = added, seconds = seconds, shieldNow = target.Shield, abilityId = abilityId, objectId = objectId });
            return added;
        }

        // ---- Time and respawns ----

        /// <summary>Shield timers, passive regen and the max-health clamp. Called by AbilityRunner.Tick.</summary>
        public void Tick(float dt)
        {
            foreach (var u in state.units)
            {
                if (!match.State.IsAlive(u.playerId)) continue;
                u.sinceDamaged += dt;
                foreach (var s in u.shields) s.timeLeft -= dt;
                u.shields.RemoveAll(s => s.timeLeft <= 0f);
                float max = MaxHealth(u.playerId);
                float regen = Stat(u.playerId, StatIds.HealthRegen);
                if (regen > 0f && u.sinceDamaged >= Rules.regenDelaySeconds && u.health < max) u.health = Math.Min(max, u.health + regen * dt);
                if (u.health > max) u.health = max;
            }
        }

        /// <summary>Back in the world: full health, no shields.</summary>
        public void OnSpawned(int playerId)
        {
            var u = Unit(playerId);
            if (u == null) return;
            u.shields.Clear();
            u.sinceDamaged = Rules.regenDelaySeconds;
            u.health = MaxHealth(playerId);
        }

        public void OnEliminated(int playerId)
        {
            var u = Unit(playerId);
            if (u == null) return;
            u.shields.Clear();
            u.health = 0f;
        }

        void Publish(GameEvent e)
        {
            e.tick = match.State.tick;
            events.Publish(e);
        }
    }
}

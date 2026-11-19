using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Status effects: timed stat changes, control flags (stun, silence, root, reveal) and ticking effects.
    public sealed partial class AbilityRunner
    {
        /// <summary>
        /// Put a status on a living hero or unit. <paramref name="seconds"/> 0 uses the status's own duration;
        /// the source's statusDuration stat scales it and stuns stop at Ruleset.maxStunSeconds.
        /// Returns false when nothing changed (unknown status, target down, or a weaker repeat).
        /// </summary>
        public bool ApplyStatus(int targetId, string statusId, int sourceId = -1, float strength = 1f, float seconds = 0f, float interval = 0f, string abilityId = "")
        {
            var def = Library.Status(statusId);
            if (def == null || !Match.IsPlaying || !Combat.IsAlive(targetId)) return false;
            var unit = Combat.Unit(targetId);
            // Untouchable: no harmful status from an enemy (or the world) lands.
            if (def.negative && unit.invulnerable && !(sourceId >= 0 && Combat.IsKnown(sourceId) && Combat.SameTeam(sourceId, targetId))) return false;
            if (def.negative && BlockedByWard(unit, statusId, sourceId, abilityId)) return false;
            if (seconds <= 0f) seconds = def.duration;
            if (Combat.IsKnown(sourceId)) seconds *= Math.Max(0f, Combat.Stat(sourceId, StatIds.StatusDuration));
            if (def.stun) seconds = Math.Min(seconds, Rules.maxStunSeconds);
            if (seconds <= 0f) return false;
            float every = interval > 0f ? interval : def.interval;

            var s = unit.Status(statusId);
            if (s == null)
            {
                s = new StatusInstance { statusId = statusId, sourceId = sourceId, strength = strength, stacks = 1, timeLeft = seconds, interval = every, tickIn = every };
                unit.statuses.Add(s);
            }
            else
            {
                switch (def.stacking)
                {
                    case Stacking.Ignore:
                        return false;
                    case Stacking.Stack:
                        s.stacks = Math.Min(Math.Max(1, def.maxStacks), s.stacks + 1);
                        s.strength = Math.Max(s.strength, strength);
                        s.timeLeft = seconds;
                        break;
                    case Stacking.Strongest:
                        if (strength < s.strength - Epsilon) return false;
                        s.strength = strength;
                        s.timeLeft = Math.Max(s.timeLeft, seconds);
                        break;
                    default: // refresh
                        s.strength = strength;
                        s.timeLeft = seconds;
                        break;
                }
                s.sourceId = sourceId;
                s.interval = every;
            }
            s.duration = s.timeLeft;
            WriteStatusStats(unit, s, def);
            if (def.stun) CancelCasts(unit, "stunned");
            UpdateStealth(unit);
            Publish(new StatusAppliedEvent { targetId = targetId, sourceId = sourceId, statusId = statusId, strength = s.strength, stacks = s.stacks, seconds = s.timeLeft, abilityId = abilityId ?? "" });
            return true;
        }

        /// <summary>
        /// A ward on the target stops a harmful status from an enemy (or from the world) and is used up.
        /// Harm from a teammate (friendly fire, a hero's own drawback) passes the ward.
        /// </summary>
        bool BlockedByWard(HeroUnit unit, string statusId, int sourceId, string abilityId)
        {
            if (sourceId >= 0 && Combat.IsKnown(sourceId) && Combat.SameTeam(sourceId, unit.playerId)) return false;
            foreach (var w in unit.statuses)
            {
                var wardDef = Library.Status(w.statusId);
                if (wardDef == null || !wardDef.ward) continue;
                Publish(new StatusBlockedEvent { targetId = unit.playerId, sourceId = sourceId, statusId = statusId, wardId = w.statusId, abilityId = abilityId ?? "" });
                EndStatus(unit, w, "used");
                return true;
            }
            return false;
        }

        /// <summary>Take a status off early. False if the hero doesn't have it.</summary>
        public bool RemoveStatus(int targetId, string statusId, string reason = "removed")
        {
            var unit = State.Unit(targetId);
            var s = unit?.Status(statusId);
            if (s == null) return false;
            EndStatus(unit, s, reason);
            return true;
        }

        /// <summary>Remove every harmful status from a hero. Returns how many.</summary>
        public int Cleanse(int targetId)
        {
            var unit = State.Unit(targetId);
            if (unit == null) return 0;
            int n = 0;
            foreach (var s in unit.statuses.ToArray())
            {
                var def = Library.Status(s.statusId);
                if (def == null || !def.negative) continue;
                EndStatus(unit, s, "cleansed");
                n++;
            }
            return n;
        }

        static string StatusSource(string statusId) => "status:" + statusId;

        void WriteStatusStats(HeroUnit unit, StatusInstance s, StatusDef def)
        {
            if (def.stats == null || def.stats.Length == 0) return;
            var mods = new List<StatModifier>();
            foreach (var m in def.stats)
            {
                if (m == null) continue;
                var c = m.Clone();
                if (c.op == ModOp.Multiply) c.value = (float)Math.Pow(c.value, s.stacks);
                else c.value *= s.stacks * (def.scaleWithStrength ? s.strength : 1f);
                mods.Add(c);
            }
            Combat.AddModifiers(unit.playerId, StatusSource(def.id), mods);
        }

        void EndStatus(HeroUnit unit, StatusInstance s, string reason)
        {
            if (!unit.statuses.Remove(s)) return;
            Combat.RemoveModifiers(unit.playerId, StatusSource(s.statusId));
            UpdateStealth(unit);
            Publish(new StatusEndedEvent { targetId = unit.playerId, statusId = s.statusId, reason = reason });
        }

        /// <summary>
        /// Keep <see cref="HeroUnit.stealthed"/> (a stealth status and no reveal) and
        /// <see cref="HeroUnit.invulnerable"/> (an invulnerable status) in step with the statuses.
        /// </summary>
        void UpdateStealth(HeroUnit unit)
        {
            bool stealth = false, reveal = false, untouchable = false;
            foreach (var s in unit.statuses)
            {
                var def = Library.Status(s.statusId);
                if (def == null) continue;
                stealth |= def.stealth;
                reveal |= def.reveal;
                untouchable |= def.invulnerable;
            }
            unit.invulnerable = untouchable;
            bool now = stealth && !reveal;
            if (now == unit.stealthed) return;
            unit.stealthed = now;
            Publish(new StealthChangedEvent { playerId = unit.playerId, stealthed = now });
        }

        /// <summary>
        /// The hero attacked (fired, or used an ability that reaches past themselves): end their stealth
        /// statuses and, if one was up, open the ambush window. Returns whether stealth was up.
        /// The combat server calls it before each shot, and ability use calls it before the ability lands.
        /// </summary>
        public bool BreakStealth(int playerId, string reason = "attacked")
        {
            var unit = State.Unit(playerId);
            if (unit == null) return false;
            bool broke = false;
            foreach (var s in unit.statuses.ToArray())
            {
                var def = Library.Status(s.statusId);
                if (def == null || !def.stealth) continue;
                EndStatus(unit, s, reason);
                broke = true;
            }
            if (!broke) return false;
            float window = Rules.ambushSeconds + Combat.Stat(playerId, StatIds.AmbushSeconds);
            if (window > 0f)
            {
                unit.ambushLeft = window;
                Publish(new AmbushEvent { playerId = playerId, seconds = window });
            }
            return true;
        }

        void ClearStatuses(HeroUnit unit, string reason)
        {
            foreach (var s in unit.statuses.ToArray()) EndStatus(unit, s, reason);
        }

        void TickStatuses(HeroUnit unit, float dt)
        {
            foreach (var s in unit.statuses.ToArray())
            {
                if (!unit.statuses.Contains(s)) continue; // ended by something earlier this tick
                var def = Library.Status(s.statusId);
                if (def == null) { EndStatus(unit, s, "expired"); continue; }
                s.timeLeft -= dt;

                if (s.interval > 0f && def.tickEffects != null && def.tickEffects.Length > 0)
                {
                    s.tickIn -= dt;
                    while (s.tickIn <= Epsilon && unit.statuses.Contains(s))
                    {
                        s.tickIn += s.interval;
                        var sourceTeam = Combat.TeamOf(s.sourceId);
                        var one = new List<EffectArea> { new EffectArea { targets = new[] { unit.playerId } } };
                        foreach (var e in def.tickEffects)
                        {
                            if (e == null || !effects.TryGetValue(e.kind, out var fx)) continue;
                            if (!Combat.IsAlive(unit.playerId)) break;
                            fx.Apply(new EffectContext
                            {
                                runner = this, sourceId = s.sourceId, sourceTeam = sourceTeam,
                                abilityId = StatusSource(s.statusId), effect = e,
                                scale = s.stacks * (def.scaleWithStrength ? s.strength : 1f),
                            }, unit.playerId);
                        }
                    }
                }
                if (s.timeLeft <= Epsilon) EndStatus(unit, s, "expired");
            }
        }
    }
}

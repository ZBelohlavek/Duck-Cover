using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// The ability rules on the server. Each hero has slots holding abilities from the
    /// <see cref="AbilityLibrary"/>; a client asks to use one with a UseAbilityCommand, and this
    /// checks it (alive, unlocked, charges, not stunned), spends the resource,
    /// finds the targets and applies the ability's standard effects. It also runs status effects,
    /// spawned objects and delayed effects. Create one per match after MatchRules,
    /// send it the commands MatchRules.Apply returns NotHandled for, and call <see cref="Tick"/> after
    /// MatchRules.Tick with the same dt.
    ///
    /// The aim origin and direction come from the client's command: the Net layer should check the
    /// origin against where the server has the player before passing the command on.
    /// </summary>
    public sealed partial class AbilityRunner
    {
        const float Epsilon = 1e-4f;

        public AbilityState State { get; } = new AbilityState();
        public DamageRules Combat { get; }
        public AbilityLibrary Library { get; }

        readonly MatchRules match;
        readonly IWorldQuery world;
        readonly IGameEvents events;
        readonly Dictionary<string, IAbility> scripts = new Dictionary<string, IAbility>();
        readonly Dictionary<string, IEffect> effects = new Dictionary<string, IEffect>();
        readonly Dictionary<string, ResolvedAbility> resolved = new Dictionary<string, ResolvedAbility>();

        internal Ruleset Rules => match.Rules;
        MatchState Match => match.State;

        /// <summary>
        /// An extra check before an ability is used, set by whoever knows more than the ability rules
        /// (the Flag Rush carrier rules, for example): returns why not, or null.
        /// </summary>
        public Func<int, string, string> UseCheck;

        /// <param name="units">The units that are not players, when the match has any (CombatServer shares its roster).</param>
        public AbilityRunner(MatchRules match, AbilityLibrary library, IWorldQuery world, IGameEvents events, UnitRoster units = null)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            Library = library ?? throw new ArgumentNullException(nameof(library));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.events = events ?? throw new ArgumentNullException(nameof(events));

            var problems = new List<string>(Rules.ValidateAbilities());
            problems.AddRange(library.Validate());
            if (problems.Count > 0) throw new ArgumentException("Can't run abilities: " + string.Join("; ", problems));

            State.nextObjectId = Rules.firstObjectId;
            Combat = new DamageRules(match, world, events, State, units);

            effects[EffectKinds.Damage] = new DamageEffect();
            effects[EffectKinds.Heal] = new HealEffect();
            effects[EffectKinds.Shield] = new ShieldEffect();
            var status = new StatusEffect();
            effects[EffectKinds.Status] = status;
            effects[EffectKinds.Slow] = status;
            effects[EffectKinds.Stun] = status;
            effects[EffectKinds.Cleanse] = new CleanseEffect();
            effects[EffectKinds.Push] = new PushEffect();
            effects[EffectKinds.Spawn] = new SpawnEffect();
            effects[EffectKinds.Execute] = new ExecuteEffect();

            events.Subscribe(OnEvent);
        }

        /// <summary>Stop listening to the event stream (when the match object is thrown away).</summary>
        public void Dispose() => events.Unsubscribe(OnEvent);

        // ---- Setup ----

        /// <summary>Custom behaviour for abilities whose data names it in scriptId (the script's Id).</summary>
        public void RegisterScript(IAbility script)
        {
            if (script == null || string.IsNullOrEmpty(script.Id)) throw new ArgumentException("a script needs an Id");
            scripts[script.Id] = script;
        }

        /// <summary>Replace or add an effect kind.</summary>
        public void RegisterEffect(string kind, IEffect effect)
        {
            if (string.IsNullOrEmpty(kind) || effect == null) throw new ArgumentException("an effect needs a kind");
            effects[kind] = effect;
        }

        /// <summary>Abilities that name a script nobody registered. Check once everything is registered.</summary>
        public List<string> MissingScripts()
        {
            var missing = new List<string>();
            foreach (var a in Library.Abilities)
                if (!string.IsNullOrEmpty(a.scriptId) && !scripts.ContainsKey(a.scriptId)) missing.Add(a.id + " needs script '" + a.scriptId + "'");
            return missing;
        }

        /// <summary>
        /// Give a hero their abilities: slot 0, 1, 2... in order (E, F, then X).
        /// <paramref name="maxHealth"/> above 0 sets the hero's own max health, and
        /// <paramref name="passiveId"/> names the hero's passive. Called when the hero is known (hero
        /// pick), by whoever reads the hero data.
        /// </summary>
        public CommandResult SetLoadout(int playerId, string[] abilityIds, float maxHealth = 0f, string passiveId = "")
        {
            var unit = Combat.Unit(playerId);
            if (unit == null) return CommandResult.Reject("unknown player");
            abilityIds = abilityIds ?? new string[0];
            foreach (var id in abilityIds)
            {
                var a = Library.Ability(id);
                if (a == null) return CommandResult.Reject("unknown ability '" + id + "'");
                if (a.passive) return CommandResult.Reject("'" + id + "' is a passive and takes no slot");
            }
            if (!string.IsNullOrEmpty(passiveId))
            {
                var passive = Library.Ability(passiveId);
                if (passive == null) return CommandResult.Reject("unknown passive '" + passiveId + "'");
                if (!passive.passive) return CommandResult.Reject("'" + passiveId + "' is not a passive");
            }
            if (!string.IsNullOrEmpty(unit.passiveId)) Combat.RemoveModifiers(playerId, PassiveSource(unit.passiveId));
            unit.passiveId = passiveId ?? "";

            foreach (var slot in unit.slots) if (slot.casting) slot.casting = false;
            for (int i = 0; i < unit.slots.Count; i++) Lower(unit, i, "lowered");
            unit.slots.Clear();
            foreach (var id in abilityIds)
                unit.slots.Add(new AbilitySlotState { abilityId = id, charges = Resolved(unit, id).def.charges });
            WritePassive(unit);
            if (maxHealth > 0f) Combat.SetBaseMaxHealth(playerId, maxHealth);
            Publish(new LoadoutSetEvent { playerId = playerId, abilityIds = (string[])abilityIds.Clone(), passiveId = unit.passiveId });
            return CommandResult.Accepted;
        }

        /// <summary>Stat changes from an upgrade or passive. The same source again replaces the earlier ones.</summary>
        public void AddModifiers(int playerId, string source, IEnumerable<StatModifier> mods) => Combat.AddModifiers(playerId, source, mods);

        public void RemoveModifiers(int playerId, string source) => Combat.RemoveModifiers(playerId, source);

        /// <summary>
        /// A talent's changes to the hero's abilities, for the rest of the match. All or nothing: if
        /// one doesn't fit its ability, none is applied. Extra charges are granted at once.
        /// </summary>
        public CommandResult ApplyParamChanges(int playerId, IEnumerable<AbilityParamChange> changes)
        {
            var unit = Combat.Unit(playerId);
            if (unit == null) return CommandResult.Reject("unknown player");
            if (changes == null) return CommandResult.Accepted;
            var list = new List<AbilityParamChange>();
            foreach (var c in changes)
            {
                string problem = AbilityParams.Check(c, Library);
                if (problem != null) return CommandResult.Reject(problem);
                list.Add(c.Clone());
            }

            var before = new Dictionary<AbilitySlotState, int>();
            foreach (var slot in unit.slots) before[slot] = Resolved(unit, slot.abilityId).def.charges;
            unit.changes.AddRange(list);
            Forget(unit);
            foreach (var slot in unit.slots)
            {
                int max = Resolved(unit, slot.abilityId).def.charges;
                if (max > before[slot]) slot.charges += max - before[slot];
                if (slot.charges >= max) { slot.charges = max; slot.rechargeLeft = 0f; }
            }
            WritePassive(unit);
            return CommandResult.Accepted;
        }

        // ---- Queries (HUD, Game layer, scripts) ----

        public HeroUnit Unit(int playerId) => Combat.Unit(playerId);
        /// <summary>A living player or unit (any unit id).</summary>
        public bool IsAlive(int playerId) => Combat.IsAlive(playerId);
        public float Stat(int playerId, string stat) => Combat.Stat(playerId, stat);

        /// <summary>The ability in a hero's slot as they have it now (talents applied), or null.</summary>
        public AbilityDef AbilityIn(int playerId, int slot)
        {
            var unit = Combat.Unit(playerId);
            if (unit == null || slot < 0 || slot >= unit.slots.Count) return null;
            return Resolved(unit, unit.slots[slot].abilityId).def;
        }

        /// <summary>Seconds until the slot has a charge. 0 when ready.</summary>
        public float CooldownLeft(int playerId, int slot)
        {
            var unit = Combat.Unit(playerId);
            if (unit == null || slot < 0 || slot >= unit.slots.Count) return 0f;
            var s = unit.slots[slot];
            if (s.charges > 0) return 0f;
            return s.rechargeLeft / Math.Max(0.01f, RechargeRate(unit, Resolved(unit, s.abilityId).def));
        }

        /// <summary>
        /// How fast a slot recharges: the hero's cooldownRate, held back so the ability never comes round
        /// faster than <see cref="Ruleset.abilityCooldownFloor"/>. An ability whose own cooldown is under the
        /// floor (it gives a shortCooldownReason) is sped up as usual.
        /// </summary>
        public float RechargeRate(HeroUnit unit, AbilityDef def) =>
            RechargeRate(Combat.Stat(unit.playerId, StatIds.CooldownRate), def.cooldown, Rules.abilityCooldownFloor);

        /// <summary>The same for a cooldownRate stat value, so a client can show the cooldown the host runs.</summary>
        public static float RechargeRate(float cooldownRate, float cooldown, float floor)
        {
            float rate = Math.Max(0f, cooldownRate);
            // Abilities with their own short cooldown (a held shield, a reload) are exempt from the floor.
            if (rate <= 1f || cooldown < floor || floor <= 0f) return rate;
            return Math.Min(rate, cooldown / floor);
        }

        /// <summary>Seconds the ability takes to come round for this hero, cooldown-rate stats and the floor included.</summary>
        public float EffectiveCooldown(int playerId, int slot)
        {
            var unit = Combat.Unit(playerId);
            if (unit == null || slot < 0 || slot >= unit.slots.Count) return 0f;
            var def = Resolved(unit, unit.slots[slot].abilityId).def;
            return def.cooldown / Math.Max(0.01f, RechargeRate(unit, def));
        }

        public bool IsStunned(int playerId) => HasFlag(playerId, s => s.stun);
        public bool IsSilenced(int playerId) => HasFlag(playerId, s => s.stun || s.silence);
        public bool IsRooted(int playerId) => HasFlag(playerId, s => s.stun || s.root);
        public bool IsRevealed(int playerId) => HasFlag(playerId, s => s.reveal);
        /// <summary>Hidden by a stealth status, and not revealed.</summary>
        public bool IsStealthed(int playerId) => Combat.IsHidden(playerId);

        /// <summary>The speed multiplier the movement code should use: 0 while stunned or rooted.</summary>
        public float MoveSpeed(int playerId) => IsRooted(playerId) ? 0f : Combat.MoveSpeed(playerId);

        public bool TryGetPosition(int playerId, out Vec3 position)
        {
            if (world is IPositionQuery q) return q.TryGetPosition(playerId, Match.tick, out position);
            position = Vec3.Zero;
            return false;
        }

        /// <summary>Why the hero can't use the slot right now, or Accepted.</summary>
        public CommandResult CanUse(int playerId, int slot, string abilityId = null)
        {
            var p = Match.Player(playerId);
            if (p == null || !p.connected) return CommandResult.Reject("unknown player");
            if (!Match.IsPlaying) return CommandResult.Reject("abilities work once the match is live");
            if (!p.alive) return CommandResult.Reject("waiting to respawn");
            var unit = Combat.Unit(playerId);
            if (slot < 0 || slot >= unit.slots.Count) return CommandResult.Reject("no ability in slot " + slot);
            var s = unit.slots[slot];
            if (!string.IsNullOrEmpty(abilityId) && abilityId != s.abilityId) return CommandResult.Reject("slot " + slot + " holds " + s.abilityId);
            var def = Resolved(unit, s.abilityId).def;
            string why = UseCheck == null ? null : UseCheck(playerId, def.id);
            if (!string.IsNullOrEmpty(why)) return CommandResult.Reject(why);
            if (IsSilenced(playerId) && !def.breaksControl) return CommandResult.Reject(IsStunned(playerId) ? "stunned" : "silenced");
            foreach (var other in unit.slots) if (other.casting) return CommandResult.Reject("already casting");
            if (s.charges <= 0) return CommandResult.Reject("on cooldown");
            if (!string.IsNullOrEmpty(def.scriptId) && !scripts.ContainsKey(def.scriptId)) return CommandResult.Reject("script '" + def.scriptId + "' is not registered");
            return CommandResult.Accepted;
        }

        /// <summary>
        /// Heroes' fighting state, objects and pending effects. Units that are not players are left out:
        /// they travel in the compact <see cref="MatchSnapshot.units"/> list instead.
        /// </summary>
        public void FillSnapshot(MatchSnapshot snapshot)
        {
            var copy = State.Clone();
            copy.units.RemoveAll(u => UnitIds.IsUnit(u.playerId));
            snapshot.abilities = copy;
        }

        // ---- Commands ----

        /// <summary>Handles UseAbilityCommand; NotHandled for everything else. A refusal also publishes AbilityRejectedEvent.</summary>
        public CommandResult Apply(MatchCommand cmd)
        {
            if (!(cmd is UseAbilityCommand use)) return cmd == null ? CommandResult.Reject("no command") : CommandResult.NotHandled;
            var result = Use(use);
            if (!result.IsAccepted)
                Publish(new AbilityRejectedEvent { playerId = use.playerId, slot = use.slot, abilityId = use.abilityId ?? "", reason = result.reason });
            return result;
        }

        CommandResult Use(UseAbilityCommand use)
        {
            // A held ability that is up comes down whatever else is going on (silenced, out of charges).
            var holder = Combat.Unit(use.playerId);
            if (holder != null && use.slot >= 0 && use.slot < holder.slots.Count && (string.IsNullOrEmpty(use.abilityId) || use.abilityId == holder.slots[use.slot].abilityId))
            {
                var held = holder.slots[use.slot];
                if (held.raised) { Lower(holder, use.slot, "lowered"); return CommandResult.Accepted; }
                if (use.lower) return CommandResult.Accepted; // released before it went up: nothing to do
            }
            else if (use.lower) return CommandResult.Accepted;

            var can = CanUse(use.playerId, use.slot, use.abilityId);
            if (!can.IsAccepted) return can;
            var unit = Combat.Unit(use.playerId);
            var s = unit.slots[use.slot];
            var ability = Resolved(unit, s.abilityId);
            var def = ability.def;
            if (def.held) return Raise(unit, use.slot, use);

            Vec3 dir = use.aimDirection.Normalized;
            if (dir == Vec3.Zero) dir = Vec3.Forward;
            s.aimOrigin = use.aimOrigin;
            s.aimDirection = dir;
            s.castTick = use.tick;

            IAbility script = null;
            if (!string.IsNullOrEmpty(def.scriptId))
            {
                script = scripts[def.scriptId];
                if (!script.CanActivate(Context(unit, use.slot, ability, null))) return CommandResult.Reject("can't be used right now");
            }

            // A buff aimed at a teammate waits for one to be in reach rather than going to waste.
            if (def.targeting.needsTarget && def.castTime <= 0f)
            {
                var team = Match.Player(unit.playerId).team;
                bool any = false;
                foreach (var area in FindAreas(unit.playerId, team, def.targeting, use.aimOrigin, dir, use.tick))
                    if (area.targets.Length > 0) { any = true; break; }
                if (!any) return CommandResult.Reject("no target in reach");
            }

            // Anything that reaches past the caster gives a cloak away (and opens the ambush window first,
            // so the ability that breaks it already counts as an ambush).
            if (!SelfOnly(def)) BreakStealth(unit.playerId, "attacked");

            // Spend.
            s.charges--;
            if (s.rechargeLeft <= 0f) s.rechargeLeft = def.cooldown;

            if (def.castTime > 0f)
            {
                s.casting = true;
                s.castLeft = def.castTime;
                Publish(new AbilityCastStartedEvent { playerId = unit.playerId, slot = use.slot, abilityId = def.id, castTime = def.castTime, aimOrigin = s.aimOrigin, aimDirection = dir });
            }
            else Land(unit, use.slot);
            return CommandResult.Accepted;
        }

        void Land(HeroUnit unit, int slotIndex)
        {
            var s = unit.slots[slotIndex];
            var ability = Resolved(unit, s.abilityId);
            var def = ability.def;
            var team = Match.Player(unit.playerId).team;
            s.casting = false;
            s.castLeft = 0f;

            var areas = FindAreas(unit.playerId, team, def.targeting, s.aimOrigin, s.aimDirection, s.castTick);
            var points = new Vec3[areas.Count];
            var all = new List<int>();
            for (int i = 0; i < areas.Count; i++) { points[i] = areas[i].point; all.AddRange(areas[i].targets); }
            Publish(new AbilityUsedEvent
            {
                playerId = unit.playerId, slot = slotIndex, abilityId = def.id,
                aimOrigin = s.aimOrigin, aimDirection = s.aimDirection,
                points = points, targets = all.ToArray(),
                chargesLeft = s.charges, rechargeLeft = s.rechargeLeft,
            });

            bool requery = def.targeting.shape == Shapes.Sphere;
            float radius = def.targeting.radius * Combat.Stat(unit.playerId, StatIds.AbilityRadius);
            foreach (var effect in def.effects)
            {
                if (!ability.IsOn(effect)) continue;
                if (effect.minTargets > 0 && all.Count < effect.minTargets) continue; // didn't catch enough
                var e = effect;
                if (e.duration <= 0f && def.duration > 0f && LastsForAbility(e.kind))
                {
                    e = e.Clone();
                    e.duration = def.duration;
                }
                if (e.kind == EffectKinds.Damage && string.IsNullOrEmpty(e.tag) && s.notches > 0 && def.notchDamage > 0f)
                {
                    // Notches earned by kills sharpen the ability's own hit.
                    e = e.Clone();
                    e.amount += Math.Min(s.notches, def.maxNotches) * def.notchDamage;
                }
                if (e.delay > 0f)
                {
                    State.pending.Add(new PendingEffect
                    {
                        timeLeft = e.delay, sourceId = unit.playerId, team = team, abilityId = def.id, aim = s.aimDirection,
                        effect = e.Clone(), areas = areas.ConvertAll(a => a.Clone()),
                        requery = requery, radius = radius, affects = def.targeting.affects, maxTargets = def.targeting.maxTargets,
                    });
                    continue;
                }
                RunEffect(e, unit.playerId, team, def.id, 0, s.aimDirection, areas, 1f, ability);
            }

            if (!string.IsNullOrEmpty(def.scriptId) && scripts.TryGetValue(def.scriptId, out var script))
            {
                script.Activate(Context(unit, slotIndex, ability, areas.ToArray()));
                s.activeLeft = def.duration;
            }
        }

        /// <summary>
        /// An ability that only touches its caster (a stim, a cloak): it keeps stealth up. Anything aimed,
        /// thrown, placed or shared with teammates does not.
        /// </summary>
        static bool SelfOnly(AbilityDef def)
        {
            if (def.targeting == null || def.targeting.shape != Shapes.Self || def.targeting.affects != Affects.Self) return false;
            foreach (var e in def.effects)
            {
                if (e == null) continue;
                if (e.kind == EffectKinds.Spawn) return false;
                if (!string.IsNullOrEmpty(e.affects) && e.affects != Affects.Self) return false;
            }
            return true;
        }

        static bool LastsForAbility(string kind) =>
            kind == EffectKinds.Status || kind == EffectKinds.Slow || kind == EffectKinds.Shield;

        AbilityContext Context(HeroUnit unit, int slotIndex, ResolvedAbility ability, EffectArea[] areas)
        {
            var s = unit.slots[slotIndex];
            return new AbilityContext
            {
                playerId = unit.playerId, tick = s.castTick, slot = slotIndex,
                aimOrigin = s.aimOrigin, aimDirection = s.aimDirection,
                world = world, events = events, match = Match,
                runner = this, ability = ability.def, areas = areas,
            };
        }

        // ---- Time ----

        /// <summary>One server tick. Does nothing outside Play and Overtime.</summary>
        public void Tick(float dt)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            if (!Match.IsPlaying) return;

            Combat.Tick(dt);
            foreach (var unit in State.units.ToArray())
            {
                TickSlots(unit, dt);
                if (!Combat.IsAlive(unit.playerId)) continue;
                if (unit.ambushLeft > 0f) unit.ambushLeft = Math.Max(0f, unit.ambushLeft - dt);
                TickStatuses(unit, dt);
            }
            TickPending(dt);
            TickObjects(dt);
        }

        void TickSlots(HeroUnit unit, float dt)
        {
            for (int i = 0; i < unit.slots.Count; i++)
            {
                var s = unit.slots[i];
                var def = Resolved(unit, s.abilityId).def;
                if (s.charges < def.charges)
                {
                    s.rechargeLeft -= dt * RechargeRate(unit, def);
                    while (s.rechargeLeft <= Epsilon && s.charges < def.charges)
                    {
                        s.charges++;
                        s.rechargeLeft = s.charges < def.charges ? s.rechargeLeft + def.cooldown : 0f;
                    }
                }
                if (def.held) TickHeld(unit, i, Resolved(unit, s.abilityId), dt);
                if (s.casting)
                {
                    s.castLeft -= dt;
                    if (s.castLeft <= Epsilon) Land(unit, i);
                }
                else if (s.activeLeft > 0f && Match.IsAlive(unit.playerId))
                {
                    if (scripts.TryGetValue(def.scriptId ?? "", out var script)) script.Tick(Context(unit, i, Resolved(unit, s.abilityId), null), dt);
                    s.activeLeft = Math.Max(0f, s.activeLeft - dt);
                }
            }
        }

        // ---- Events from the other rules ----

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case PlayerSpawnedEvent spawned:
                {
                    var unit = Combat.Unit(spawned.playerId);
                    if (unit == null) return;
                    ClearStatuses(unit, "respawned");
                    unit.ambushLeft = 0f;
                    Combat.OnSpawned(spawned.playerId);
                    if (Rules.resetCooldownsOnRespawn)
                        foreach (var s in unit.slots) { s.charges = Resolved(unit, s.abilityId).def.charges; s.rechargeLeft = 0f; s.activeLeft = 0f; s.reserve = 1f; s.downFor = 0f; }
                    break;
                }
                case PlayerEliminatedEvent down:
                {
                    var unit = Combat.Unit(down.playerId);
                    if (unit == null) return;
                    CancelCasts(unit, "eliminated");
                    for (int i = 0; i < unit.slots.Count; i++) Lower(unit, i, "lowered");
                    ClearStatuses(unit, "eliminated");
                    unit.ambushLeft = 0f;
                    Combat.OnEliminated(down.playerId);
                    foreach (var s in unit.slots) s.activeLeft = 0f;
                    if (down.disconnected) RemoveObjectsOf(down.playerId, "ownerLeft");
                    break;
                }
                case UnitDestroyedEvent gone:
                {
                    // Combat drops the unit's state after this event; end its statuses first.
                    var unit = State.Unit(gone.unitId);
                    if (unit == null) return;
                    // Statuses end the way a hero's do when it goes down ("eliminated"), or "removed".
                    string reason = gone.reason == "killed" ? "eliminated" : gone.reason;
                    CancelCasts(unit, reason);
                    ClearStatuses(unit, reason);
                    Forget(unit);
                    break;
                }
                case DamagedEvent hit:
                    if (hit.eliminated) KillRewards(hit);
                    break;
            }
        }

        // ---- Kill rewards ----

        /// <summary>
        /// What a killing blow took down, for <see cref="KillRewardDef.victims"/>: a hero, a team's moving
        /// lane unit (minion), a neutral camp creature, or null (a Bastion, a training dummy's team...).
        /// Called while the victim is still in the roster (DamageRules publishes before it destroys).
        /// </summary>
        string VictimKind(int targetId)
        {
            if (!UnitIds.IsUnit(targetId)) return KillVictims.Hero;
            var u = Combat.Roster.Get(targetId);
            if (u == null) return null;
            if (u.team == TeamId.None) return KillVictims.Camp;
            return u.anchored ? null : KillVictims.Minion;
        }

        /// <summary>Pay the killer's kill rewards (slot abilities, then the passive) for a killing blow.</summary>
        void KillRewards(DamagedEvent hit)
        {
            int killer = hit.attackerId;
            if (killer < 0 || killer == hit.targetId || UnitIds.IsUnit(killer)) return;
            var unit = State.Unit(killer);
            var p = Match.Player(killer);
            if (unit == null || p == null) return;
            string victim = VictimKind(hit.targetId);
            if (victim == null) return;
            foreach (var slot in unit.slots) Reward(unit, p.team, slot, slot.abilityId, hit, victim);
            if (!string.IsNullOrEmpty(unit.passiveId)) Reward(unit, p.team, null, unit.passiveId, hit, victim);
        }

        void Reward(HeroUnit unit, TeamId team, AbilitySlotState slot, string abilityId, DamagedEvent hit, string victim)
        {
            var ability = Resolved(unit, abilityId);
            var def = ability.def;
            if (def.onKill == null || def.onKill.Length == 0) return;
            bool own = hit.abilityId == def.id;
            foreach (var k in def.onKill)
            {
                if (!ability.IsOn(k) || !k.Counts(victim)) continue;
                if (k.scope == KillScopes.Ability && !own || k.scope == KillScopes.Other && own) continue;
                if (slot != null && k.notches > 0 && def.maxNotches > 0) slot.notches = Math.Min(def.maxNotches, slot.notches + k.notches);
                if (Combat.IsAlive(unit.playerId) && k.effects != null)
                {
                    TryGetPosition(unit.playerId, out var at);
                    var areas = new List<EffectArea> { new EffectArea { point = at, targets = new[] { unit.playerId } } };
                    foreach (var effect in k.effects)
                    {
                        if (!ability.IsOn(effect)) continue;
                        var e = effect.Clone();
                        e.affects = Affects.Self;
                        RunEffect(e, unit.playerId, team, def.id, 0, Vec3.Forward, areas, 1f, ability);
                    }
                }
                Publish(new KillRewardEvent { playerId = unit.playerId, abilityId = def.id, victimId = hit.targetId, victim = victim, notches = slot == null ? 0 : slot.notches });
            }
        }

        void CancelCasts(HeroUnit unit, string reason)
        {
            for (int i = 0; i < unit.slots.Count; i++)
            {
                var s = unit.slots[i];
                if (!s.casting) continue;
                s.casting = false;
                s.castLeft = 0f;
                var def = Resolved(unit, s.abilityId).def;
                Publish(new AbilityCancelledEvent { playerId = unit.playerId, slot = i, abilityId = def.id, reason = reason });
            }
        }

        // ---- Helpers ----

        static string PassiveSource(string passiveId) => "passive:" + passiveId;

        /// <summary>Put the passive's stat changes (with talent changes applied) on the hero's stat stack.</summary>
        void WritePassive(HeroUnit unit)
        {
            if (string.IsNullOrEmpty(unit.passiveId)) return;
            Combat.AddModifiers(unit.playerId, PassiveSource(unit.passiveId), Resolved(unit, unit.passiveId).def.stats);
        }

        ResolvedAbility Resolved(HeroUnit unit, string abilityId)
        {
            string key = unit.playerId + ":" + abilityId;
            if (resolved.TryGetValue(key, out var r)) return r;
            r = AbilityParams.Resolve(Library.Ability(abilityId), unit.changes, Library, Rules.minCooldownSeconds);
            resolved[key] = r;
            return r;
        }

        void Forget(HeroUnit unit)
        {
            string prefix = unit.playerId + ":";
            var stale = new List<string>();
            foreach (var key in resolved.Keys) if (key.StartsWith(prefix)) stale.Add(key);
            foreach (var key in stale) resolved.Remove(key);
        }

        bool HasFlag(int playerId, Func<StatusDef, bool> flag)
        {
            var unit = State.Unit(playerId);
            if (unit == null) return false;
            foreach (var s in unit.statuses)
            {
                var def = Library.Status(s.statusId);
                if (def != null && flag(def)) return true;
            }
            return false;
        }

        internal void Publish(GameEvent e)
        {
            e.tick = Match.tick;
            events.Publish(e);
        }
    }
}

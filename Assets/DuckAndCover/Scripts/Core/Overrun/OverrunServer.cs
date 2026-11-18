using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Overrun's rules on the server (WP16), on top of the lanes (<see cref="LaneServer"/>):
    ///
    /// - Each team has a Forward Bastion out on the lane and a Core Bastion at its base door (the map's
    ///   <see cref="MapBastion"/> spots). Each is a unit: health that never comes back, and a shield
    ///   layer over it.
    /// - Minions (Scrappers, Wardens, Juggernauts) always damage enemy Bastions. Heroes, and anything
    ///   they place, damage a Bastion only while one of their own minions is within
    ///   <see cref="OverrunRuleset.minionRadius"/> of it; otherwise the damage does nothing.
    ///   Nothing heals or shields a Bastion from outside.
    /// - A Core Bastion takes no damage at all until its Forward Bastion has fallen.
    /// - The shield comes back to full after <see cref="OverrunRuleset.shieldRefillSeconds"/> with no
    ///   enemy minion near it.
    /// - Bastions shoot enemies in range: a hero who just hurt one of its team's heroes within its range
    ///   first, then minions, then heroes. All Bastion shots of a tick land together.
    /// - A Forward Bastion falling opens the side gate into its team's core plaza and takes
    ///   <see cref="OverrunRuleset.defenderRespawnBonus"/> off that team's respawn wait.
    /// - Every Bastion destroyed scores one for the other team (<see cref="MatchRules.ScoreBastion"/>);
    ///   the Core is the second, which wins. At the time cap the match rules ask <see cref="TieBreak"/>.
    /// - The late push from minute 11: waves every 20 s and Bastions take +50% damage.
    /// - Overrun experience: near your own wave, minion kills, Bastion damage, Bastions destroyed.
    ///
    /// The host builds one after its <see cref="LaneServer"/> and calls <see cref="Attach"/> after the
    /// lane server's; tests call <see cref="Tick"/> after the lane server's tick.
    /// </summary>
    public sealed class OverrunServer
    {
        const float Epsilon = 1e-4f;

        public OverrunRuleset Rules { get; }
        public OverrunState State { get; } = new OverrunState();
        public LaneServer Lanes { get; }
        public CombatServer Combat => Lanes.Combat;
        public MatchRules Match => Lanes.Combat.Match;
        /// <summary>Experience goes here when set (the host's HeroServer.Progression).</summary>
        public ProgressionRules Progression { get; set; }

        readonly IGameEvents events;
        readonly Func<int, int, bool> previousDamageGate, previousRestoreGate;
        readonly Func<TeamId, float> previousWaitAdjust;
        float nearWaveSince;

        MatchState M => Match.State;
        UnitRules Units => Combat.Units;
        DamageRules Damage => Combat.Damage;

        /// <param name="bastions">The map's Bastion spots (MapDef.bastions): a Forward and a Core for each team.</param>
        public OverrunServer(LaneServer lanes, IGameEvents events, List<MapBastion> bastions, ProgressionRules progression = null)
        {
            Lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Progression = progression;
            var match = lanes.Combat.Match;
            if (!match.OverrunMode) throw new ArgumentException("mode " + match.Mode.id + " is not Overrun");
            Rules = (match.Rules.overrun ?? new OverrunRuleset()).Clone();

            var problems = Rules.Validate();
            var spots = bastions ?? new List<MapBastion>();
            foreach (var team in new[] { TeamId.A, TeamId.B })
                foreach (var tier in new[] { BastionTier.Forward, BastionTier.Core })
                {
                    int n = spots.FindAll(b => b != null && b.team == team && b.tier == tier).Count;
                    if (n != 1) problems.Add("team " + team + " needs exactly one " + tier + " Bastion (the map has " + n + ")");
                }
            if (problems.Count > 0) throw new ArgumentException("Can't run Overrun: " + string.Join("; ", problems));

            foreach (var u in Rules.units) Units.Library.Add(u);
            foreach (var spot in spots)
            {
                var kind = Rules.Kind(spot.tier);
                int id = Units.Spawn(kind.id, spot.team, spot.position, spot.team == TeamId.B ? 180f : 0f);
                if (id < 0) throw new InvalidOperationException("Can't place the " + spot.team + " " + spot.tier + " Bastion");
                var b = new BastionState
                {
                    unitId = id, team = spot.team, tier = spot.tier, position = spot.position,
                    maxHealth = kind.maxHealth, health = kind.maxHealth, maxShield = Rules.Shield(spot.tier),
                    sealedOff = spot.tier == BastionTier.Core,
                };
                State.bastions.Add(b);
                SetShield(b, b.maxShield);
            }
            State.bastions.Sort((x, y) => x.unitId.CompareTo(y.unitId));
            Sync();

            previousDamageGate = Damage.DamageGate;
            Damage.DamageGate = DamageGate;
            previousRestoreGate = Damage.RestoreGate;
            Damage.RestoreGate = RestoreGate;
            previousWaitAdjust = match.Spawn.WaitAdjust;
            match.Spawn.WaitAdjust = WaitAdjust;
            match.TieBreak = TieBreak;
            events.Subscribe(OnEvent);
        }

        /// <summary>From a map's own Bastion spots.</summary>
        public OverrunServer(LaneServer lanes, IGameEvents events, MapDef map, ProgressionRules progression = null)
            : this(lanes, events, map == null ? null : map.bastions, progression) { }

        public void Dispose()
        {
            events.Unsubscribe(OnEvent);
            if (Damage.DamageGate == (Func<int, int, bool>)DamageGate) Damage.DamageGate = previousDamageGate;
            if (Damage.RestoreGate == (Func<int, int, bool>)RestoreGate) Damage.RestoreGate = previousRestoreGate;
            if (Match.Spawn.WaitAdjust == (Func<TeamId, float>)WaitAdjust) Match.Spawn.WaitAdjust = previousWaitAdjust;
            if (Match.TieBreak == (Func<TeamId>)TieBreak) Match.TieBreak = null;
        }

        /// <summary>Hook into a session, after the lane server: the Bastions act on every tick and ride in every snapshot.</summary>
        public void Attach(SessionHost host)
        {
            host.AddTickHandler(Tick);
            host.AddSnapshotFiller(FillSnapshot);
        }

        public void FillSnapshot(MatchSnapshot s)
        {
            Sync();
            s.overrun = State.Clone();
        }

        // ---- Who may hurt a Bastion ----

        /// <summary>
        /// Whether damage from <paramref name="attackerId"/> lands on <paramref name="targetId"/> (the
        /// rules above). Anything that isn't a Bastion goes on to the gate that was there before.
        /// </summary>
        public bool CanDamage(int attackerId, int targetId) => DamageGate(attackerId, targetId);

        bool DamageGate(int attackerId, int targetId)
        {
            var b = State.ByUnit(targetId);
            if (b == null) return previousDamageGate == null || previousDamageGate(attackerId, targetId);
            if (!b.alive || b.sealedOff) return false;
            var enemy = b.team.Other();
            if (UnitIds.IsUnit(attackerId))
            {
                var u = Units.Unit(attackerId);
                return u != null && u.team == enemy && Rules.IsMinion(u.typeId);
            }
            var p = M.Player(attackerId);
            return p != null && p.team == enemy && MinionNear(b, enemy);
        }

        /// <summary>Bastion health never comes back, and only its own shield covers it.</summary>
        bool RestoreGate(int sourceId, int targetId)
        {
            if (State.ByUnit(targetId) != null) return false;
            return previousRestoreGate == null || previousRestoreGate(sourceId, targetId);
        }

        /// <summary>A living minion of <paramref name="team"/> within the minion radius of the Bastion.</summary>
        public bool MinionNear(BastionState b, TeamId team)
        {
            foreach (var u in Units.Living())
                if (u.team == team && Rules.IsMinion(u.typeId) && Vec3.Distance(LaneDef.Flat(u.position), LaneDef.Flat(b.position)) <= Rules.minionRadius + Epsilon)
                    return true;
            return false;
        }

        float WaitAdjust(TeamId team)
        {
            float before = previousWaitAdjust == null ? 0f : previousWaitAdjust(team);
            return State.GateOpen(team) ? before - Rules.defenderRespawnBonus : before;
        }

        /// <summary>
        /// Who wins a match level on Bastions destroyed: the team that has taken the larger share of
        /// enemy Bastion health; level on that, the one that dealt more damage to enemy Bastions
        /// (shields too); level on that as well after <see cref="OverrunRuleset.suddenDeathSeconds"/>
        /// of sudden death, the one that killed more enemy minions. None while all of that is level.
        /// </summary>
        public TeamId TieBreak()
        {
            Sync(); // the match rules may ask before this tick's Overrun tick
            float byA = State.HealthTaken(TeamId.B), byB = State.HealthTaken(TeamId.A);
            if (Math.Abs(byA - byB) > 1e-5f) return byA > byB ? TeamId.A : TeamId.B;
            if (Math.Abs(State.damageToB - State.damageToA) > 1e-3f) return State.damageToB > State.damageToA ? TeamId.A : TeamId.B;
            if (M.phase == MatchPhase.Overtime && M.overtimeElapsed >= Rules.suddenDeathSeconds - Epsilon && State.minionsLostA != State.minionsLostB)
                return State.minionsLostB > State.minionsLostA ? TeamId.A : TeamId.B;
            return TeamId.None;
        }

        // ---- Time ----

        public void Tick(float dt)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            if (!M.IsPlaying) return;
            if (!State.latePush && M.elapsed >= Rules.latePushMinute * 60f - Epsilon) StartLatePush();

            var shots = new List<(int from, int to, float damage)>();
            foreach (var b in State.bastions)
            {
                if (!b.alive) continue;
                var enemy = b.team.Other();
                bool enemyNear = MinionNear(b, enemy);
                b.openToHeroes = !b.sealedOff && enemyNear;

                // The shield comes back once no enemy minion has been near for a while.
                if (enemyNear) b.quietFor = 0f;
                else b.quietFor += dt;
                if (b.quietFor >= Rules.shieldRefillSeconds - Epsilon && ShieldOf(b) < b.maxShield - Epsilon)
                {
                    SetShield(b, b.maxShield);
                    Publish(new BastionShieldRestoredEvent { team = b.team, tier = b.tier, unitId = b.unitId, shield = b.maxShield });
                }

                var kind = Units.Library.Unit(Units.Unit(b.unitId).typeId);
                b.cooldown = Math.Max(0f, b.cooldown - dt);
                b.targetId = PickTarget(b, kind);
                if (b.targetId >= 0 && b.cooldown <= 0f)
                {
                    shots.Add((b.unitId, b.targetId, kind.attackDamage));
                    b.cooldown = kind.attackSeconds;
                }
            }
            // Decided from the same start-of-tick picture, then all land: list order never favours a team.
            foreach (var s in shots) Damage.Damage(s.from, s.to, s.damage, OverrunRuleset.AttackId);

            PayNearWave();
            Sync();
        }

        void StartLatePush()
        {
            State.latePush = true;
            Lanes.Rules.waveSeconds = Rules.latePushWaveSeconds;
            if (Lanes.State.nextWaveIn > Rules.latePushWaveSeconds) Lanes.State.nextWaveIn = Rules.latePushWaveSeconds;
            var mods = new[] { new StatModifier(StatIds.DamageTaken, ModOp.Percent, Rules.latePushDamageTaken) };
            foreach (var b in State.bastions)
                if (b.alive) Damage.AddModifiers(b.unitId, OverrunRuleset.LatePushSource, mods);
            Publish(new LatePushStartedEvent { waveSeconds = Rules.latePushWaveSeconds, extraDamage = Rules.latePushDamageTaken });
        }

        // ---- Bastion fire ----

        /// <summary>
        /// The hero it holds a grudge against if still in range, else the closest enemy minion in range,
        /// else the closest enemy hero; only what it has a line of fire to. Ties to the lower id. -1 if none.
        /// </summary>
        int PickTarget(BastionState b, UnitDef kind)
        {
            float range = kind.attackRange;
            if (b.aggroId >= 0)
            {
                if (M.elapsed < b.aggroUntil && InRange(b, b.aggroId, range) && Units.LineOfFire(b.unitId, b.aggroId)) return b.aggroId;
                if (M.elapsed >= b.aggroUntil) b.aggroId = -1;
            }
            var enemy = b.team.Other();
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (var u in Units.Living())
            {
                if (u.team != enemy || !Rules.IsMinion(u.typeId)) continue;
                Consider(b, u.id, range, ref best, ref bestDist);
            }
            if (best >= 0) return best;
            foreach (var p in M.players)
            {
                if (p.team != enemy || !M.IsAlive(p.id) || Combat.Movement.Motion(p.id) == null) continue;
                Consider(b, p.id, range, ref best, ref bestDist);
            }
            return best;
        }

        void Consider(BastionState b, int id, float range, ref int best, ref float bestDist)
        {
            float d = Vec3.Distance(b.position, FeetOf(id));
            if (d > range || d > bestDist || (d == bestDist && id > best)) return;
            if (!Units.LineOfFire(b.unitId, id)) return;
            best = id;
            bestDist = d;
        }

        bool InRange(BastionState b, int id, float range) =>
            Damage.IsAlive(id) && Vec3.Distance(b.position, FeetOf(id)) <= range;

        Vec3 FeetOf(int id)
        {
            if (UnitIds.IsUnit(id)) { var u = Units.Unit(id); return u == null ? Vec3.Zero : u.position; }
            var m = Combat.Movement.Motion(id);
            return m == null ? Vec3.Zero : m.position;
        }

        // ---- Shields ----

        float ShieldOf(BastionState b)
        {
            var c = Damage.Unit(b.unitId);
            if (c == null) return 0f;
            foreach (var s in c.shields) if (s.abilityId == OverrunRuleset.ShieldId) return s.amount;
            return 0f;
        }

        /// <summary>Set the Bastion's own shield layer (it doesn't run out with time and doesn't count against Ruleset.maxShield).</summary>
        void SetShield(BastionState b, float amount)
        {
            var c = Damage.Unit(b.unitId);
            if (c == null) return;
            c.shields.RemoveAll(s => s.abilityId == OverrunRuleset.ShieldId);
            if (amount > 0f) c.shields.Add(new ShieldState { sourceId = b.unitId, abilityId = OverrunRuleset.ShieldId, amount = amount, timeLeft = float.MaxValue });
            b.shield = amount;
            b.quietFor = 0f;
        }

        /// <summary>Copy health and shield from the combat rules into the state clients see.</summary>
        void Sync()
        {
            foreach (var b in State.bastions)
            {
                if (!b.alive) { b.health = 0f; b.shield = 0f; b.openToHeroes = false; continue; }
                var c = Damage.Unit(b.unitId);
                b.health = c == null ? 0f : c.health;
                b.shield = ShieldOf(b);
            }
        }

        // ---- Events ----

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case DamagedEvent hit:
                    OnDamaged(hit);
                    break;
                case UnitDestroyedEvent gone:
                    OnUnitDestroyed(gone);
                    break;
            }
        }

        void OnDamaged(DamagedEvent hit)
        {
            if (hit.amount <= 0f) return;
            var b = State.ByUnit(hit.targetId);
            if (b != null)
            {
                if (b.team == TeamId.A) State.damageToA += hit.amount; else State.damageToB += hit.amount;
                bool hadShield = b.shield > 0f;
                b.shield = ShieldOf(b);
                if (b.alive && hadShield && b.shield <= 0f)
                    Publish(new BastionShieldBrokenEvent { team = b.team, tier = b.tier, unitId = b.unitId });
                if (!UnitIds.IsUnit(hit.attackerId) && hit.attackerId >= 0) Pay(hit.attackerId, hit.amount * Rules.xpPerBastionDamage);
                return;
            }

            // A hero hurting an enemy hero within a Bastion's range draws that Bastion's fire.
            if (UnitIds.IsUnit(hit.attackerId) || UnitIds.IsUnit(hit.targetId) || hit.attackerId < 0) return;
            var attacker = M.Player(hit.attackerId);
            var victim = M.Player(hit.targetId);
            if (attacker == null || victim == null || attacker.team == victim.team || victim.team == TeamId.None) return;
            Vec3 at = FeetOf(victim.id);
            foreach (var mine in State.bastions)
            {
                if (!mine.alive || mine.team != victim.team) continue;
                var kind = Units.Library.Unit(Units.Unit(mine.unitId).typeId);
                if (Vec3.Distance(mine.position, at) > kind.attackRange) continue;
                mine.aggroId = attacker.id;
                mine.aggroUntil = M.elapsed + Rules.aggroSeconds;
            }
        }

        void OnUnitDestroyed(UnitDestroyedEvent gone)
        {
            var b = State.ByUnit(gone.unitId);
            if (b == null)
            {
                if (gone.reason == "killed" && Rules.IsMinion(gone.typeId) && gone.killerId >= 0 && Damage.TeamOf(gone.killerId) != gone.team)
                {
                    if (gone.team == TeamId.A) State.minionsLostA++;
                    else if (gone.team == TeamId.B) State.minionsLostB++;
                }
                // A minion killed by an enemy hero.
                if (gone.reason == "killed" && Rules.IsMinion(gone.typeId) && gone.killerId >= 0 && !UnitIds.IsUnit(gone.killerId))
                {
                    var killer = M.Player(gone.killerId);
                    if (killer != null && killer.team != gone.team) Pay(killer.id, Rules.xpMinionKill);
                }
                return;
            }
            if (!b.alive) return;
            b.alive = false;
            b.health = 0f;
            b.shield = 0f;
            b.openToHeroes = false;
            b.targetId = -1;
            var by = b.team.Other();
            Publish(new BastionDestroyedEvent { team = b.team, tier = b.tier, unitId = b.unitId, byTeam = by, killerId = gone.killerId });
            if (b.tier == BastionTier.Forward)
            {
                if (b.team == TeamId.A) State.gateOpenA = true; else State.gateOpenB = true;
                var core = State.Bastion(b.team, BastionTier.Core);
                if (core != null) { core.sealedOff = false; core.quietFor = 0f; }
                Publish(new SideGateOpenedEvent { team = b.team });
            }
            foreach (var p in M.players)
                if (p.team == by && p.connected) Pay(p.id, Rules.xpBastionDestroyed);
            Match.ScoreBastion(by);
        }

        void PayNearWave()
        {
            float due = M.elapsed - nearWaveSince;
            if (due < 1f - Epsilon) return;
            nearWaveSince = M.elapsed;
            if (Progression == null || Rules.xpNearWavePerSecond <= 0f) return;
            var living = Units.Living();
            foreach (var p in M.players)
            {
                if (!M.IsAlive(p.id) || p.team == TeamId.None) continue;
                var m = Combat.Movement.Motion(p.id);
                if (m == null) continue;
                foreach (var u in living)
                {
                    if (u.team != p.team || !Rules.IsMinion(u.typeId) || Vec3.Distance(u.position, m.position) > Match.Rules.escortRadius) continue;
                    Pay(p.id, Rules.xpNearWavePerSecond * due);
                    break;
                }
            }
        }

        void Pay(int playerId, float amount)
        {
            if (Progression == null || amount <= 0f) return;
            Progression.Grant(playerId, amount, ExperienceSource.Overrun);
        }

        void Publish(GameEvent e)
        {
            e.tick = M.tick;
            events.Publish(e);
        }
    }
}

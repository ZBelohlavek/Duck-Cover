using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// The Flag Rush flag on the server (WP21). One neutral flag at a time:
    ///
    /// - It appears <see cref="Ruleset.flagFirstSpawnSeconds"/> into Play, then
    ///   <see cref="Ruleset.flagAfterCaptureSeconds"/> after each capture, at a spot from the map's
    ///   list (<see cref="MatchMap.flagSpots"/>) picked by the match's seeded generator, never the same
    ///   spot twice in a row. The spot is announced as soon as it is picked.
    /// - Touching it picks it up (nearest living player, lowest id on a tie). The carrier is in the
    ///   snapshot for everyone, can't sprint and can't use abilities that move them (a push on
    ///   themselves); everything else still works.
    /// - Carried into the capture zone of the enemy's base (the mode's zone set), it scores one
    ///   through <see cref="MatchRules.ScoreFlagCapture"/>.
    /// - The carrier going down or leaving drops it where they stood. Anyone may pick it up; untouched
    ///   for <see cref="Ruleset.flagDropResetSeconds"/> it vanishes and a new one appears elsewhere.
    /// - Experience for picking up, carrying, scoring (the most), stopping a carrier, and being near
    ///   a capture, paid through Progression when the host has heroes.
    ///
    /// The host builds one after its <see cref="CombatServer"/> (and after <see cref="HeroServer"/>,
    /// whose ability check it wraps) and calls <see cref="Attach"/>. Tests call <see cref="Tick"/>.
    /// </summary>
    public sealed class FlagServer
    {
        const float Epsilon = 1e-4f;

        public CombatServer Combat { get; }
        public MatchRules Match => Combat.Match;
        public FlagState State { get; } = new FlagState();
        /// <summary>Experience goes here when set (the host's HeroServer.Progression).</summary>
        public ProgressionRules Progression { get; set; }

        readonly IWorldQuery world;
        readonly IGameEvents events;
        readonly List<Vec3> spots;
        readonly ZoneId baseA, baseB;
        readonly Func<int, string, string> previousUseCheck;
        uint rng;
        float carryPaidUntil;

        Ruleset Rules => Match.Rules;
        MatchState M => Match.State;

        /// <param name="world">Answers who stands in each base's capture zone.</param>
        /// <param name="seed">The match's seed: the same seed picks the same spots.</param>
        public FlagServer(CombatServer combat, IWorldQuery world, IGameEvents events, int seed, ProgressionRules progression = null)
        {
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Progression = progression;
            var match = combat.Match;
            if (!match.FlagMode) throw new ArgumentException("mode " + match.Mode.id + " has no flag");
            var problems = match.Rules.ValidateFlag();
            if (problems.Count > 0) throw new ArgumentException("Can't run the flag: " + string.Join("; ", problems));
            spots = new List<Vec3>(match.Map.flagSpots);
            baseA = match.Mode.BaseZone(match.Map, TeamId.A).id;
            baseB = match.Mode.BaseZone(match.Map, TeamId.B).id;
            rng = (uint)seed ^ 0x9E3779B9u;
            if (rng == 0) rng = 1;

            if (Rules.flagCarrierNoSprint) combat.Movement.NoSprint = State.IsCarrier;
            previousUseCheck = combat.Abilities.UseCheck;
            combat.Abilities.UseCheck = UseCheck;
            events.Subscribe(OnEvent);
        }

        public void Dispose()
        {
            events.Unsubscribe(OnEvent);
            if (Combat.Movement.NoSprint == (Func<int, bool>)State.IsCarrier) Combat.Movement.NoSprint = null;
            if (Combat.Abilities.UseCheck == (Func<int, string, string>)UseCheck) Combat.Abilities.UseCheck = previousUseCheck;
        }

        /// <summary>Hook into a session: the flag moves on every tick and rides in every snapshot.</summary>
        public void Attach(SessionHost host)
        {
            host.AddTickHandler(Tick);
            host.AddSnapshotFiller(FillSnapshot);
        }

        public void FillSnapshot(MatchSnapshot s) => s.flag = State.Clone();

        /// <summary>The capture zone in <paramref name="team"/>'s base, where the other team scores.</summary>
        public ZoneId BaseOf(TeamId team) => team == TeamId.A ? baseA : team == TeamId.B ? baseB : ZoneId.None;

        // ---- Carrier limits ----

        /// <summary>Whether the ability moves its caster (a push on themselves, like a dash or a charge).</summary>
        public bool MovesCaster(string abilityId)
        {
            var def = Combat.Abilities.Library.Ability(abilityId);
            if (def == null || def.effects == null) return false;
            foreach (var e in def.effects)
                if (e != null && e.kind == EffectKinds.Push && e.affects == Affects.Self) return true;
            return false;
        }

        string UseCheck(int playerId, string abilityId)
        {
            if (State.IsCarrier(playerId) && MovesCaster(abilityId)) return "can't use movement abilities while carrying the flag";
            return previousUseCheck == null ? null : previousUseCheck(playerId, abilityId);
        }

        // ---- Time ----

        /// <summary>One server tick, after the match and combat ticks.</summary>
        public void Tick(float dt)
        {
            if (!M.IsPlaying) return;
            if (State.nextSpot < 0 && State.status == FlagStatus.Waiting)
            {
                carryPaidUntil = M.elapsed;
                Schedule(Rules.flagFirstSpawnSeconds);
            }

            switch (State.status)
            {
                case FlagStatus.Waiting:
                    if (M.elapsed >= State.appearAt - Epsilon) Appear();
                    break;
                case FlagStatus.AtSpot:
                case FlagStatus.Dropped:
                    if (TryPickUp()) break;
                    if (State.status == FlagStatus.Dropped && M.elapsed >= State.resetAt - Epsilon) ResetDropped();
                    break;
                case FlagStatus.Carried:
                    TickCarried();
                    break;
            }
        }

        void Schedule(float delay)
        {
            State.status = FlagStatus.Waiting;
            State.carrierId = -1;
            State.carrierTeam = TeamId.None;
            State.nextSpot = PickSpot(State.spot);
            State.nextPosition = spots[State.nextSpot];
            State.appearAt = M.elapsed + Math.Max(0f, delay);
            Publish(new FlagUpcomingEvent { spot = State.nextSpot, position = State.nextPosition, seconds = Math.Max(0f, delay) });
        }

        void Appear()
        {
            State.status = FlagStatus.AtSpot;
            State.spot = State.nextSpot;
            State.position = spots[State.spot];
            Publish(new FlagAppearedEvent { spot = State.spot, position = State.position });
        }

        /// <summary>A random spot from the map's list, never <paramref name="last"/> (when there is a choice).</summary>
        int PickSpot(int last)
        {
            if (spots.Count == 1) return 0;
            if (last < 0 || last >= spots.Count) return (int)(Next() % (uint)spots.Count);
            int pick = (int)(Next() % (uint)(spots.Count - 1));
            return pick >= last ? pick + 1 : pick;
        }

        /// <summary>xorshift32: small, seeded and the same on every machine.</summary>
        uint Next()
        {
            uint x = rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            rng = x;
            return x;
        }

        bool TryPickUp()
        {
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (var p in M.players)
            {
                if (!p.connected || !p.alive || (p.team != TeamId.A && p.team != TeamId.B)) continue;
                var m = Combat.Movement.Motion(p.id);
                if (m == null) continue;
                float dx = m.position.x - State.position.x, dz = m.position.z - State.position.z;
                float flat = dx * dx + dz * dz;
                if (flat > Rules.flagPickupRadius * Rules.flagPickupRadius) continue;
                if (Math.Abs(m.position.y - State.position.y) > Rules.flagPickupHeight) continue;
                if (flat < bestDist - Epsilon || (Math.Abs(flat - bestDist) <= Epsilon && p.id < best)) { best = p.id; bestDist = flat; }
            }
            if (best < 0) return false;

            var carrier = M.Player(best);
            bool fromDrop = State.status == FlagStatus.Dropped;
            State.status = FlagStatus.Carried;
            State.carrierId = carrier.id;
            State.carrierTeam = carrier.team;
            State.resetAt = 0f;
            carryPaidUntil = M.elapsed;
            Publish(new FlagTakenEvent { playerId = carrier.id, team = carrier.team, position = State.position, fromDrop = fromDrop });
            Pay(carrier.id, Rules.xpFlagPickup);
            return true;
        }

        void TickCarried()
        {
            var carrier = M.Player(State.carrierId);
            if (carrier == null || !carrier.connected || !carrier.alive)
            {
                Drop(-1); // the event normally does this first; a missed one can't leave the flag stuck
                return;
            }
            var m = Combat.Movement.Motion(carrier.id);
            if (m != null) State.position = m.position;

            float carried = M.elapsed - carryPaidUntil;
            if (carried >= 1f - Epsilon)
            {
                carryPaidUntil = M.elapsed;
                Pay(carrier.id, Rules.xpFlagCarryPerSecond * carried);
            }

            var enemyBase = BaseOf(carrier.team.Other());
            foreach (int id in world.PlayersInZone(enemyBase) ?? Array.Empty<int>())
                if (id == carrier.id) { Capture(carrier); return; }
        }

        void Capture(PlayerState carrier)
        {
            var assists = new List<int>();
            foreach (var p in M.players)
            {
                if (p.id == carrier.id || p.team != carrier.team || !p.connected || !p.alive) continue;
                var m = Combat.Movement.Motion(p.id);
                if (m != null && Vec3.Distance(m.position, State.position) <= Rules.flagAssistRadius) assists.Add(p.id);
            }
            State.captures++;
            Pay(carrier.id, Rules.xpFlagCapture);
            foreach (int id in assists) Pay(id, Rules.xpFlagCaptureAssist);
            var team = carrier.team;
            int carrierId = carrier.id;
            Schedule(Rules.flagAfterCaptureSeconds);
            // The event goes out before the score settles, so a match-winning capture reads in order.
            Publish(new FlagCapturedEvent { playerId = carrierId, team = team, scoreA = M.scoreA + (team == TeamId.A ? 1 : 0), scoreB = M.scoreB + (team == TeamId.B ? 1 : 0), assists = assists.ToArray() });
            Match.ScoreFlagCapture(team);
        }

        void Drop(int killerId)
        {
            if (State.status != FlagStatus.Carried) return;
            var who = State.carrierId;
            var team = State.carrierTeam;
            var m = Combat.Movement.Motion(who);
            if (m != null) State.position = m.position;
            State.status = FlagStatus.Dropped;
            State.carrierId = -1;
            State.carrierTeam = TeamId.None;
            State.resetAt = M.elapsed + Rules.flagDropResetSeconds;
            Publish(new FlagDroppedEvent { playerId = who, team = team, position = State.position, killerId = killerId, resetSeconds = Rules.flagDropResetSeconds });
            var killer = killerId >= 0 ? M.Player(killerId) : null;
            if (killer != null && killer.team != team) Pay(killerId, Rules.xpFlagStopCarrier);
        }

        void ResetDropped()
        {
            Publish(new FlagResetEvent { position = State.position });
            Schedule(Rules.flagAfterResetSeconds);
            if (Rules.flagAfterResetSeconds <= 0f) Appear();
        }

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case PlayerEliminatedEvent down:
                    if (State.IsCarrier(down.playerId)) Drop(down.killerId);
                    break;
                case PlayerLeftEvent left:
                    if (State.IsCarrier(left.playerId)) Drop(-1);
                    break;
            }
        }

        void Pay(int playerId, float amount)
        {
            if (Progression == null || amount <= 0f) return;
            Progression.Grant(playerId, amount, ExperienceSource.Flag);
        }

        void Publish(GameEvent e)
        {
            e.tick = M.tick;
            events.Publish(e);
        }
    }
}

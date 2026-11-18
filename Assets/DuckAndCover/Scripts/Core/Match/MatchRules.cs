using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// The match on the server: phases and timers, score and caps, overtime, the map shift trigger,
    /// hero pick, and the Zones and Spawn rules underneath. Clients send commands to
    /// <see cref="Apply"/>; the server's own systems report eliminations, joins and leaves through the
    /// other public methods; <see cref="Tick"/> moves time on. Everything that happens is published
    /// to the event stream, and <see cref="State"/> is the whole truth.
    /// </summary>
    public sealed class MatchRules
    {
        const float Epsilon = 1e-4f;

        public MatchState State { get; }
        public Ruleset Rules { get; }
        public ModeDef Mode { get; }
        public MatchMap Map { get; }
        public ZoneRules Zones { get; }
        public SpawnRules Spawn { get; }
        /// <summary>Flag Rush: score is captures, reported by the flag rules (Core/Flag) through <see cref="ScoreFlagCapture"/>.</summary>
        public bool FlagMode => Mode.IsFlag();

        readonly IGameEvents events;

        public MatchRules(Ruleset rules, ModeDef mode, MatchMap map, IWorldQuery world, IGameEvents events)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (world == null) throw new ArgumentNullException(nameof(world));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Rules = rules.Clone();
            Mode = mode.Clone();
            Map = map;

            var problems = new List<string>(Rules.Validate());
            problems.AddRange(Map.Validate());
            problems.AddRange(Mode.Validate(Map));
            if (problems.Count > 0) throw new ArgumentException("Can't run a match: " + string.Join("; ", problems));

            State = new MatchState { modeId = Mode.id };
            Zones = new ZoneRules(State, Rules, Mode, Map, world, events);
            Spawn = new SpawnRules(State, Rules, events);
            Zones.Setup();
        }

        // ---- Players ----

        /// <summary>
        /// Add a player to a team. Mid-match they start waiting to respawn, as if just eliminated at
        /// level 1, so a late joiner arrives with the next wave after a normal wait.
        /// A player who left and comes back keeps their team, hero and level.
        /// </summary>
        public CommandResult AddPlayer(int playerId, TeamId team)
        {
            if (team != TeamId.A && team != TeamId.B) return CommandResult.Reject("pick team A or B");
            if (State.phase == MatchPhase.MatchEnd) return CommandResult.Reject("the match is over");
            var p = State.Player(playerId);
            if (p != null && p.connected) return CommandResult.Reject("player " + playerId + " is already in the match");
            if (p != null && p.team != team && State.phase != MatchPhase.Lobby) return CommandResult.Reject("a returning player rejoins their own team");
            if (State.ConnectedCount(team) >= Rules.maxPlayersPerTeam) return CommandResult.Reject("team " + team + " is full");

            if (p == null)
            {
                p = new PlayerState { id = playerId, team = team };
                State.players.Add(p);
            }
            p.team = team;
            p.connected = true;
            p.alive = false;
            if (State.phase == MatchPhase.Deploy) Spawn.SpawnAtBase(p); // not yet fighting: straight into the spawn room
            else if (State.IsPlaying) p.respawnAt = State.elapsed + Spawn.WaitFor(p.level);
            return CommandResult.Accepted;
        }

        /// <summary>A player left. They stay listed with their stats and can rejoin; in the lobby they're removed.</summary>
        public void RemovePlayer(int playerId)
        {
            var p = State.Player(playerId);
            if (p == null || !p.connected) return;
            if (State.phase == MatchPhase.Lobby) { State.players.Remove(p); return; }
            if (p.alive && State.IsPlaying) Spawn.Eliminate(p, -1, disconnected: true);
            p.alive = false;
            p.connected = false;
        }

        /// <summary>Leave the lobby and start hero pick.</summary>
        public CommandResult StartMatch()
        {
            if (State.phase != MatchPhase.Lobby) return CommandResult.Reject("the match already started");
            foreach (var team in new[] { TeamId.A, TeamId.B })
                if (State.ConnectedCount(team) < Rules.minPlayersPerTeam)
                    return CommandResult.Reject("team " + team + " needs at least " + Rules.minPlayersPerTeam + " player(s)");
            Publish(new MatchStartedEvent { modeId = Mode.id });
            EnterPhase(MatchPhase.HeroPick, Rules.heroPickSeconds);
            return CommandResult.Accepted;
        }

        // ---- Commands from clients ----

        /// <summary>
        /// Validate and apply a client's request. Returns NotHandled for commands that belong to other
        /// modules (fire, abilities, upgrades, pings), so the server can route them on.
        /// </summary>
        public CommandResult Apply(MatchCommand cmd)
        {
            if (cmd == null) return CommandResult.Reject("no command");
            var p = State.Player(cmd.playerId);
            switch (cmd)
            {
                case PickHeroCommand pick:
                    if (p == null || !p.connected) return CommandResult.Reject("unknown player");
                    if (State.phase != MatchPhase.Lobby && State.phase != MatchPhase.HeroPick) return CommandResult.Reject("heroes are picked before the match starts");
                    if (string.IsNullOrEmpty(pick.heroId)) return CommandResult.Reject("no hero given");
                    if (Rules.uniqueHeroesPerTeam)
                        foreach (var mate in State.players)
                            if (mate != p && mate.connected && mate.team == p.team && mate.heroId == pick.heroId)
                                return CommandResult.Reject("a teammate already picked " + pick.heroId);
                    p.heroId = pick.heroId;
                    Publish(new HeroPickedEvent { playerId = p.id, team = p.team, heroId = p.heroId });
                    return CommandResult.Accepted;

                case PickSpawnCommand spawn:
                    if (p == null || !p.connected) return CommandResult.Reject("unknown player");
                    if (!Spawn.CanChoose(p.team, spawn.zone)) return CommandResult.Reject(spawn.zone + " is not a spawn for team " + p.team);
                    p.spawnChoice = spawn.zone;
                    return CommandResult.Accepted;

                default:
                    return CommandResult.NotHandled;
            }
        }

        // ---- Calls from the server's own systems ----

        /// <summary>A player went down (Combat decides that). They wait out their respawn timer.</summary>
        public bool EliminatePlayer(int playerId, int killerId = -1)
        {
            var p = State.Player(playerId);
            if (p == null || !p.alive || !p.connected || !State.IsPlaying) return false;
            Spawn.Eliminate(p, killerId, disconnected: false);
            return true;
        }

        /// <summary>Advance the match by <paramref name="dt"/> seconds: one server tick.</summary>
        public void Tick(float dt)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            State.tick++;
            switch (State.phase)
            {
                case MatchPhase.HeroPick:
                    if (CountDown(dt))
                    {
                        EnterPhase(MatchPhase.Deploy, Rules.deploySeconds);
                        Spawn.SpawnAll();
                    }
                    break;
                case MatchPhase.Deploy:
                    if (CountDown(dt))
                    {
                        EnterPhase(MatchPhase.Play, 0f);
                        if (!FlagMode) Zones.Unlock(); // a flag mode's base zones are only volumes, never points

                    }
                    break;
                case MatchPhase.Play:
                case MatchPhase.Overtime:
                    TickPlay(dt);
                    break;
                case MatchPhase.MatchEnd:
                    if (State.phaseTimeLeft > 0f) State.phaseTimeLeft = Math.Max(0f, State.phaseTimeLeft - dt);
                    break;
            }
        }

        /// <summary>
        /// Flag Rush: <paramref name="team"/> carried the flag into the enemy base. Adds a point and
        /// settles the shift and the result on the same tick. False if nothing could score.
        /// </summary>
        public bool ScoreFlagCapture(TeamId team)
        {
            if (!FlagMode || !State.IsPlaying || (team != TeamId.A && team != TeamId.B)) return false;
            if (team == TeamId.A) State.scoreA = Math.Min(Mode.scoreToWin, State.scoreA + 1);
            else State.scoreB = Math.Min(Mode.scoreToWin, State.scoreB + 1);
            CheckMapShift();
            if (State.phase == MatchPhase.Play) CheckCaps();
            else CheckFlagOvertime();
            return true;
        }

        public MatchSnapshot TakeSnapshot() => new MatchSnapshot { tick = State.tick, match = State.Clone() };

        // ---- Play ----

        void TickPlay(float dt)
        {
            State.elapsed += dt;
            if (State.phase == MatchPhase.Overtime) State.overtimeElapsed += dt;

            Spawn.Tick();
            Zones.TickSchedule();
            Zones.TickZones(dt);
            AddScore(TeamId.A, dt);
            AddScore(TeamId.B, dt);
            CheckMapShift();

            if (State.phase == MatchPhase.Play) CheckCaps();
            else if (FlagMode) CheckFlagOvertime();
            else CheckOvertime(dt);
        }

        void AddScore(TeamId team, float dt)
        {
            float earned = Zones.ScoreRate(team) * dt;
            if (earned <= 0f) return;
            int cap = Mode.scoreToWin;
            if (team == TeamId.A)
            {
                State.scoreCarryA += earned;
                int whole = (int)Math.Floor(State.scoreCarryA + Epsilon);
                State.scoreCarryA = Math.Max(0f, State.scoreCarryA - whole);
                State.scoreA = Math.Min(cap, State.scoreA + whole);
            }
            else
            {
                State.scoreCarryB += earned;
                int whole = (int)Math.Floor(State.scoreCarryB + Epsilon);
                State.scoreCarryB = Math.Max(0f, State.scoreCarryB - whole);
                State.scoreB = Math.Min(cap, State.scoreB + whole);
            }
        }

        void CheckMapShift()
        {
            if (State.mapShifted || Mode.mapShiftScore <= 0) return;
            TeamId team = TeamId.None;
            // If both cross on the same tick, the higher score triggers it.
            if (State.scoreA >= Mode.mapShiftScore && State.scoreA >= State.scoreB) team = TeamId.A;
            else if (State.scoreB >= Mode.mapShiftScore) team = TeamId.B;
            if (team == TeamId.None) return;
            State.mapShifted = true;
            Publish(new MapShiftEvent { team = team, score = State.Score(team) });
        }

        void CheckCaps()
        {
            TeamId leader;
            MatchEndReason reason;
            bool aCap = State.scoreA >= Mode.scoreToWin, bCap = State.scoreB >= Mode.scoreToWin;
            if (aCap || bCap)
            {
                // Both at the cap on one tick: the team with more in hand takes it, A on a perfect tie.
                leader = aCap && bCap ? (State.scoreCarryB > State.scoreCarryA ? TeamId.B : TeamId.A) : aCap ? TeamId.A : TeamId.B;
                reason = MatchEndReason.ScoreReached;
            }
            else if (State.elapsed >= Mode.timeCapSeconds - Epsilon)
            {
                leader = State.Leader;
                if (leader == TeamId.None)
                {
                    // Level at the time cap: play on until someone scores. Flag Rush calls that overtime.
                    if (FlagMode && Rules.overtimeEnabled) StartFlagOvertime();
                    return;
                }
                reason = MatchEndReason.TimeCap;
            }
            else return;

            if (Rules.overtimeEnabled && TrailingTeamFighting(leader.Other()))
            {
                State.overtimeLeader = leader;
                State.overtimeElapsed = 0f;
                State.overtimeClearFor = 0f;
                EnterPhase(MatchPhase.Overtime, 0f);
                Publish(new OvertimeStartedEvent { leader = leader });
                return;
            }
            EndMatch(leader, reason);
        }

        void CheckOvertime(float dt)
        {
            var leader = State.overtimeLeader;
            var trailing = leader.Other();
            if (State.Score(trailing) >= Mode.scoreToWin)
            {
                EndMatch(trailing, MatchEndReason.OvertimeComeback);
                return;
            }
            if (TrailingTeamFighting(trailing)) State.overtimeClearFor = 0f;
            else State.overtimeClearFor += dt;

            if (State.overtimeClearFor >= Rules.overtimeGraceSeconds - Epsilon)
            {
                EndMatch(HigherOr(leader), MatchEndReason.OvertimeHeld);
                return;
            }
            if (Rules.overtimeMaxSeconds > 0f && State.overtimeElapsed >= Rules.overtimeMaxSeconds - Epsilon)
                EndMatch(HigherOr(leader), MatchEndReason.OvertimeTimeout);
        }

        void StartFlagOvertime()
        {
            State.overtimeLeader = TeamId.None;
            State.overtimeElapsed = 0f;
            State.overtimeClearFor = 0f;
            EnterPhase(MatchPhase.Overtime, 0f);
            Publish(new OvertimeStartedEvent { leader = TeamId.None });
        }

        /// <summary>Flag Rush overtime: the next capture wins; past overtimeMaxSeconds (if set) it is a draw.</summary>
        void CheckFlagOvertime()
        {
            if (State.Leader != TeamId.None) { EndMatch(State.Leader, MatchEndReason.OvertimeCapture); return; }
            if (Rules.overtimeMaxSeconds > 0f && State.overtimeElapsed >= Rules.overtimeMaxSeconds - Epsilon)
                EndMatch(TeamId.None, MatchEndReason.OvertimeTimeout);
        }

        /// <summary>The trailing team has living players on a live zone it doesn't safely hold.</summary>
        bool TrailingTeamFighting(TeamId trailing)
        {
            foreach (var z in State.zones) if (z.IsFightingOn(trailing)) return true;
            return false;
        }

        TeamId HigherOr(TeamId fallback) => State.Leader == TeamId.None ? fallback : State.Leader;

        void EndMatch(TeamId winner, MatchEndReason reason)
        {
            State.winner = winner;
            State.endReason = reason;
            EnterPhase(MatchPhase.MatchEnd, Rules.matchEndSeconds);
            Publish(new MatchEndedEvent { winner = winner, reason = reason, scoreA = State.scoreA, scoreB = State.scoreB, elapsed = State.elapsed });
        }

        // ---- Helpers ----

        /// <summary>Run the phase countdown; true when it reaches zero.</summary>
        bool CountDown(float dt)
        {
            State.phaseTimeLeft -= dt;
            if (State.phaseTimeLeft > Epsilon) return false;
            State.phaseTimeLeft = 0f;
            return true;
        }

        void EnterPhase(MatchPhase phase, float seconds)
        {
            var from = State.phase;
            State.phase = phase;
            State.phaseTimeLeft = seconds;
            Publish(new PhaseChangedEvent { from = from, to = phase, seconds = seconds });
        }

        void Publish(GameEvent e)
        {
            e.tick = State.tick;
            events.Publish(e);
        }
    }
}

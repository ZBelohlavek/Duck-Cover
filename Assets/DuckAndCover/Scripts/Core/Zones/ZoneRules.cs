using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Zone ownership, capture and neutralise progress, contest, the live-zone schedule and the score
    /// rate. One set of rules runs every mode: the <see cref="ModeDef"/> says how many zones are live,
    /// whether they rotate and what they are worth. Driven by <see cref="MatchRules"/>.
    /// </summary>
    public sealed class ZoneRules
    {
        /// <summary>Tolerance for progress and schedules summed from float frame times.</summary>
        const float Epsilon = 1e-4f;

        readonly MatchState state;
        readonly Ruleset rules;
        readonly ModeDef mode;
        readonly ZoneSetDef set;
        readonly IWorldQuery world;
        readonly IGameEvents events;

        public ZoneRules(MatchState state, Ruleset rules, ModeDef mode, MatchMap map, IWorldQuery world, IGameEvents events)
        {
            this.state = state;
            this.rules = rules;
            this.mode = mode;
            // Overrun scores on Bastions, not zones: without a zone set it simply has none.
            set = map.ZoneSet(mode.zoneSet) ?? (mode.IsOverrun() ? new ZoneSetDef() : throw new ArgumentException("map has no zone set " + mode.zoneSet));
            this.world = world;
            this.events = events;
        }

        bool Rotates => mode.rotationSeconds > 0f && mode.activeZoneCount > 0;
        int LiveCount => mode.activeZoneCount == 0 ? set.zones.Count : mode.activeZoneCount;

        /// <summary>Create the zones from the map, all locked.</summary>
        public void Setup()
        {
            state.zones.Clear();
            foreach (var def in set.zones)
            {
                state.zones.Add(new ZoneState
                {
                    id = def.id,
                    owner = def.startOwner,
                    startOwner = def.startOwner,
                    forwardSpawnFor = def.forwardSpawnFor,
                });
            }
            state.rotationIndex = 0;
        }

        /// <summary>Unlock the first live zones when Play starts.</summary>
        public void Unlock()
        {
            state.rotationIndex = 0;
            for (int i = 0; i < LiveCount; i++) Activate(state.zones[i]);
            if (Rotates)
            {
                state.nextRotationAt = state.elapsed + mode.rotationSeconds;
                state.rotationAnnounced = false;
            }
        }

        /// <summary>The zones that go live at the next rotation.</summary>
        public List<ZoneState> UpcomingZones()
        {
            var list = new List<ZoneState>();
            if (!Rotates) return list;
            int n = state.zones.Count;
            int start = (state.rotationIndex + LiveCount) % n;
            for (int i = 0; i < LiveCount; i++) list.Add(state.zones[(start + i) % n]);
            return list;
        }

        /// <summary>Announce and perform rotations. Called before zones tick, with the match clock already advanced.</summary>
        public void TickSchedule()
        {
            if (!Rotates) return;
            if (!state.rotationAnnounced && state.elapsed >= state.nextRotationAt - mode.rotationWarningSeconds - Epsilon)
            {
                state.rotationAnnounced = true;
                foreach (var z in UpcomingZones())
                    Publish(new ZoneUpcomingEvent { zone = z.id, secondsUntilLive = Math.Max(0f, state.nextRotationAt - state.elapsed) });
            }
            if (state.elapsed < state.nextRotationAt - Epsilon) return;

            var next = UpcomingZones();
            foreach (var z in state.zones)
            {
                if (!z.active || next.Contains(z)) continue;
                z.active = false;
                Reset(z, TeamId.None);
                Publish(new ZoneDeactivatedEvent { zone = z.id });
            }
            state.rotationIndex = (state.rotationIndex + LiveCount) % state.zones.Count;
            foreach (var z in next) if (!z.active) Activate(z);
            state.nextRotationAt += mode.rotationSeconds;
            state.rotationAnnounced = false;
        }

        /// <summary>Count who stands on each live zone and move capture progress.</summary>
        public void TickZones(float dt)
        {
            foreach (var z in state.zones)
            {
                if (!z.active) continue;
                var onA = new List<int>();
                var onB = new List<int>();
                foreach (int id in world.PlayersInZone(z.id) ?? Array.Empty<int>())
                {
                    var p = state.Player(id);
                    if (p == null || !p.connected || !p.alive) continue;
                    if (p.team == TeamId.A) { if (!onA.Contains(id)) onA.Add(id); }
                    else if (p.team == TeamId.B) { if (!onB.Contains(id)) onB.Add(id); }
                }
                z.countA = onA.Count;
                z.countB = onB.Count;

                bool contested = onA.Count > 0 && onB.Count > 0;
                if (contested != z.contested)
                {
                    z.contested = contested;
                    Publish(new ZoneContestChangedEvent { zone = z.id, contested = contested });
                }
                if (contested) continue; // any enemy on the point freezes progress

                if (onA.Count == 0 && onB.Count == 0)
                {
                    if (z.progress > 0f)
                    {
                        z.progress = Math.Max(0f, z.progress - rules.progressDecayPerSecond * dt);
                        if (z.progress <= 0f) z.progressTeam = TeamId.None;
                    }
                    continue;
                }

                var team = onA.Count > 0 ? TeamId.A : TeamId.B;
                Push(z, team, onA.Count > 0 ? onA : onB, dt);
            }
        }

        /// <summary>Points per second the team is earning right now.</summary>
        public float ScoreRate(TeamId team)
        {
            int held = state.ZonesHeld(team);
            if (held == 0) return 0f;
            float rate = held * mode.scorePerSecond;
            int live = state.ActiveZoneCount;
            if (live > 1 && held == live) rate *= mode.allHeldMultiplier;
            return rate;
        }

        /// <summary>Capture speed multiplier for this many capturing players.</summary>
        public float CaptureSpeed(int players)
        {
            if (players <= 0) return 0f;
            var table = rules.captureSpeedByPlayers;
            return table[Math.Min(players, table.Length) - 1];
        }

        void Push(ZoneState z, TeamId team, List<int> players, float dt)
        {
            float speed = CaptureSpeed(players.Count);

            if (z.owner == team)
            {
                // Defending your own point: wind back the enemy's neutralise progress.
                if (z.progress > 0f)
                {
                    z.progress = Math.Max(0f, z.progress - speed * dt / rules.neutraliseSeconds);
                    if (z.progress <= 0f) z.progressTeam = TeamId.None;
                }
                return;
            }

            float seconds = z.owner == TeamId.None ? rules.captureSeconds : rules.neutraliseSeconds;
            float step = speed * dt / seconds;

            if (z.progressTeam != team && z.progress > 0f)
            {
                // Someone else's half-finished capture: undo it before starting your own.
                z.progress -= step;
                if (z.progress > 0f) return;
                z.progress = 0f;
                z.progressTeam = TeamId.None;
                return;
            }

            z.progressTeam = team;
            z.progress += step;
            if (z.progress < 1f - Epsilon) return;

            var ids = players.ToArray();
            if (z.owner == TeamId.None)
            {
                Reset(z, team);
                Publish(new ZoneCapturedEvent { zone = z.id, team = team, players = ids });
            }
            else
            {
                var previous = z.owner;
                Reset(z, TeamId.None);
                Publish(new ZoneNeutralisedEvent { zone = z.id, team = team, previousOwner = previous, players = ids });
            }
        }

        void Activate(ZoneState z)
        {
            z.active = true;
            Reset(z, z.startOwner);
            z.contested = false;
            z.countA = z.countB = 0;
            Publish(new ZoneActivatedEvent { zone = z.id, owner = z.owner });
        }

        static void Reset(ZoneState z, TeamId owner)
        {
            z.owner = owner;
            z.progress = 0f;
            z.progressTeam = TeamId.None;
        }

        void Publish(GameEvent e)
        {
            e.tick = state.tick;
            events.Publish(e);
        }
    }
}

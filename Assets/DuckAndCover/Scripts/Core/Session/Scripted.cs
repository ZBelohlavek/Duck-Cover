using System;
using System.Collections.Generic;
using System.Text;

namespace DuckAndCover.Core
{
    // Scripted play: what the network smoke test uses to run a whole match with no player
    // controller, physics or map. The host accepts these commands only when scripted play is
    // switched on (ScriptedPlay.Attach); a normal match refuses them like any unknown command.

    /// <summary>"I am standing on this zone" (None = on no zone).</summary>
    public sealed class ScriptedStandCommand : MatchCommand
    {
        public ZoneId zone;
    }

    /// <summary>"I took this enemy down."</summary>
    public sealed class ScriptedEliminateCommand : MatchCommand
    {
        public int targetPlayerId = -1;
    }

    /// <summary>
    /// A world with no physics: players are on the zone they were last put on. The session uses it
    /// until the player controller (WP4) and control points (WP6) supply the real one, and the smoke
    /// test moves players in it by command.
    /// </summary>
    public sealed class ScriptedWorld : IWorldQuery
    {
        readonly Dictionary<int, ZoneId> standingOn = new Dictionary<int, ZoneId>();

        public void Put(int playerId, ZoneId zone)
        {
            if (zone.IsValid) standingOn[playerId] = zone;
            else standingOn.Remove(playerId);
        }

        public void Clear(int playerId) => standingOn.Remove(playerId);

        public int[] PlayersInZone(ZoneId zone)
        {
            var ids = new List<int>();
            foreach (var kv in standingOn) if (kv.Value == zone) ids.Add(kv.Key);
            ids.Sort(); // dictionary order isn't promised; the rules must see the same thing every run
            return ids.ToArray();
        }

        public int[] PlayersInSphere(Vec3 center, float radius, int tick) => Array.Empty<int>();
        public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit) { hit = HitInfo.Miss; return false; }
        public SurfaceCell CellAt(Vec3 point) => SurfaceCell.None;
    }

    public static class ScriptedPlay
    {
        /// <summary>Let a host accept the scripted commands, moving players in <paramref name="world"/>.</summary>
        public static void Attach(SessionHost host, ScriptedWorld world, IGameEvents events)
        {
            var match = host.Match;
            host.AddCommandHandler(cmd =>
            {
                var state = match.State;
                switch (cmd)
                {
                    case ScriptedStandCommand stand:
                        if (!state.IsAlive(cmd.playerId)) return CommandResult.Reject("only living players move");
                        if (stand.zone.IsValid && state.Zone(stand.zone) == null) return CommandResult.Reject("no such zone");
                        world.Put(cmd.playerId, stand.zone);
                        return CommandResult.Accepted;
                    case ScriptedEliminateCommand kill:
                        var me = state.Player(cmd.playerId);
                        var target = state.Player(kill.targetPlayerId);
                        if (me == null || !state.IsAlive(cmd.playerId)) return CommandResult.Reject("only living players fight");
                        if (target == null || target.team == me.team) return CommandResult.Reject("not an enemy");
                        if (!match.EliminatePlayer(target.id, me.id)) return CommandResult.Reject("target isn't in the fight");
                        return CommandResult.Accepted;
                    default:
                        return CommandResult.NotHandled;
                }
            });
            // Someone who goes down or leaves is no longer standing anywhere.
            events.Subscribe(e =>
            {
                if (e is PlayerEliminatedEvent down) world.Clear(down.playerId);
                else if (e is PlayerLeftEvent left) world.Clear(left.playerId);
            });
        }

        /// <summary>A stand-in objective layout until the first map (WP9): three points and four hills.</summary>
        public static MatchMap PlaceholderMap() => new MatchMap
        {
            mapId = "placeholder",
            zoneSets =
            {
                new ZoneSetDef
                {
                    name = "points",
                    zones = { new ZoneDef(1, forwardSpawnFor: TeamId.A), new ZoneDef(2), new ZoneDef(3, forwardSpawnFor: TeamId.B) },
                },
                new ZoneSetDef
                {
                    name = "hills",
                    zones = { new ZoneDef(11), new ZoneDef(12), new ZoneDef(13), new ZoneDef(14) },
                },
            },
        };

        /// <summary>The usual ruleset with a short hero pick and deploy and a capped overtime, so a scripted run starts and ends.</summary>
        public static Ruleset SmokeRuleset()
        {
            var r = Ruleset.Default;
            r.heroPickSeconds = 3f;
            r.deploySeconds = 2f;
            r.matchEndSeconds = 5f;
            r.overtimeMaxSeconds = 30f; // scripted players never give a point up, so overtime needs an end
            return r;
        }
    }

    /// <summary>
    /// Plays one client by script: picks a hero, starts the match if it is the host's player, then
    /// walks between the live zones and takes an enemy down now and then. What it does depends only
    /// on its player id and the state it was sent, so a run can be repeated. Team A pushes harder
    /// than team B, so matches end with a clear result.
    /// </summary>
    public sealed class ScriptedPlayer
    {
        static readonly string[] Heroes = { "vanguard", "striker", "support", "controller", "generic" };

        /// <summary>Seconds of match time between changes of plan.</summary>
        const float PlanSeconds = 12f;

        readonly SessionClient client;
        readonly int expectedPlayers;
        int heroAttempt;
        bool startSent;
        int lastPlan = -1;
        int lastFightPlan = -1;
        ZoneId standing;
        bool wasAlive;

        public int CommandsSent { get; private set; }
        public int Rejections { get; private set; }
        public string LastRejection { get; private set; }
        public bool Finished => client.State != null && client.State.phase == MatchPhase.MatchEnd;

        /// <param name="expectedPlayers">The host's player starts the match once this many are connected.</param>
        public ScriptedPlayer(SessionClient client, int expectedPlayers)
        {
            this.client = client;
            this.expectedPlayers = expectedPlayers;
            client.Events.Subscribe(OnEvent);
        }

        void OnEvent(GameEvent e)
        {
            if (!(e is CommandRejectedEvent r)) return;
            Rejections++;
            LastRejection = r.command + ": " + r.reason;
            if (r.command == nameof(PickHeroCommand)) heroAttempt++;      // taken by a teammate: try the next
            if (r.command == nameof(StartMatchCommand)) startSent = false; // not enough players yet
        }

        /// <summary>Look at the latest state and send whatever the script calls for. Call after each snapshot.</summary>
        public void Step()
        {
            var state = client.State;
            var me = client.Me;
            if (state == null || me == null) return;

            switch (state.phase)
            {
                case MatchPhase.Lobby:
                    PickHero(me);
                    if (client.IsHostPlayer && !startSent && Connected() >= expectedPlayers)
                    {
                        startSent = true;
                        Send(new StartMatchCommand());
                    }
                    break;
                case MatchPhase.HeroPick:
                    PickHero(me);
                    break;
                case MatchPhase.Play:
                case MatchPhase.Overtime:
                    Play(state, me);
                    break;
            }
        }

        int pickedAttempt = -1;

        void PickHero(PlayerState me)
        {
            if (!string.IsNullOrEmpty(me.heroId) || pickedAttempt == heroAttempt) return;
            pickedAttempt = heroAttempt;
            Send(new PickHeroCommand { heroId = Heroes[(me.id + heroAttempt) % Heroes.Length] });
        }

        void Play(MatchState state, PlayerState me)
        {
            if (!me.alive)
            {
                wasAlive = false;
                return;
            }
            int plan = (int)(state.elapsed / PlanSeconds);
            if (!wasAlive)
            {
                // Back from a respawn (or just deployed): not on any zone, so plan afresh.
                wasAlive = true;
                standing = ZoneId.None;
                lastPlan = -1;
            }
            if (plan != lastPlan)
            {
                lastPlan = plan;
                var target = ChooseZone(state, me, plan);
                if (target != standing)
                {
                    standing = target;
                    Send(new ScriptedStandCommand { zone = target });
                }
            }
            // Now and then, one player takes an enemy down, so respawns run too.
            if (plan != lastFightPlan && plan % 3 == 2 && (plan / 3 + me.id) % 4 == 0)
            {
                lastFightPlan = plan;
                int enemy = FirstLivingEnemy(state, me);
                if (enemy > 0) Send(new ScriptedEliminateCommand { targetPlayerId = enemy });
            }
        }

        static ZoneId ChooseZone(MatchState state, PlayerState me, int plan)
        {
            var live = new List<ZoneId>();
            foreach (var z in state.zones) if (z.active) live.Add(z.id);
            if (live.Count == 0) return ZoneId.None;
            // Team B sits out two plans in three, so team A ends up ahead.
            if (me.team == TeamId.B && (plan + me.id) % 3 != 0) return ZoneId.None;
            return live[(me.id + plan) % live.Count];
        }

        static int FirstLivingEnemy(MatchState state, PlayerState me)
        {
            int best = -1;
            foreach (var p in state.players)
                if (p.team != me.team && p.connected && p.alive && (best < 0 || p.id < best)) best = p.id;
            return best;
        }

        int Connected()
        {
            int n = 0;
            foreach (var p in client.Roster) if (p.connected) n++;
            return n;
        }

        void Send(MatchCommand command)
        {
            CommandsSent++;
            client.Send(command);
        }
    }

    /// <summary>
    /// The parts of a match two machines must agree on, as text: result, score, zones, and each
    /// player's team, hero, level and tally. Leaves out clocks and capture progress, which are
    /// still moving while a state copy is in flight.
    /// </summary>
    public static class SessionDigest
    {
        public static string Of(MatchState s)
        {
            if (s == null) return "(no state)";
            var sb = new StringBuilder();
            sb.Append("mode=").Append(s.modeId).Append(" phase=").Append(s.phase)
              .Append(" score=").Append(s.scoreA).Append(':').Append(s.scoreB)
              .Append(" winner=").Append(s.winner).Append(" reason=").Append(s.endReason)
              .Append(" shifted=").Append(s.mapShifted).Append('\n');
            var zones = new List<ZoneState>(s.zones);
            zones.Sort((a, b) => a.id.value.CompareTo(b.id.value));
            foreach (var z in zones)
                sb.Append("zone ").Append(z.id.value).Append(" owner=").Append(z.owner).Append(" active=").Append(z.active).Append('\n');
            var players = new List<PlayerState>(s.players);
            players.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var p in players)
                sb.Append("player ").Append(p.id).Append(" team=").Append(p.team).Append(" hero=").Append(p.heroId)
                  .Append(" level=").Append(p.level).Append(" elims=").Append(p.eliminations).Append(" deaths=").Append(p.deaths)
                  .Append(" connected=").Append(p.connected).Append('\n');
            return sb.ToString();
        }
    }
}

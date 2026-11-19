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

    /// <summary>"I run to the flag." Flag Rush only: puts the player on the flag where it lies (and on no zone).</summary>
    public sealed class ScriptedToFlagCommand : MatchCommand
    {
    }

    /// <summary>
    /// "I run up behind my team's front minion." Overrun only: puts the player a few metres behind the
    /// minion of theirs that is furthest toward the enemy base (and on no zone).
    /// </summary>
    public sealed class ScriptedToLaneCommand : MatchCommand
    {
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
        /// <summary>Metres behind its team's front minion that <see cref="ScriptedToLaneCommand"/> puts a player.</summary>
        public const float BehindTheWave = 3f;

        /// <summary>
        /// Let a host accept the scripted commands, moving players in <paramref name="world"/>, and
        /// give it the combat rules (WP4) with that world as a map with no walls, so scripted players
        /// fight with real shots. With hero data (WP11) the heroes' abilities, leveling and upgrades
        /// run too. In Flag Rush the flag runs (seed 1, so runs repeat) and scripted players reach
        /// it with <see cref="ScriptedToFlagCommand"/>. In Overrun (WP19) the lanes, waves, camps and
        /// Bastions run on Breakwater's data (its spawn rooms, lane, camps and Bastion spots, with open
        /// ground between them), and scripted players join their wave with <see cref="ScriptedToLaneCommand"/>.
        /// Returns the combat rules.
        /// </summary>
        public static CombatServer Attach(SessionHost host, ScriptedWorld world, IGameEvents events,
            HeroCatalog heroes = null, AbilityLibrary abilities = null)
        {
            var match = host.Match;
            var laneMap = match.OverrunMode ? MapCatalog.ForMode(match.Mode.id) : null;
            var combat = new CombatServer(match, world, events, host.Settings.TickSeconds, laneMap == null ? null : laneMap.ToLayout(), abilities: abilities);
            combat.Attach(host);
            HeroServer heroServer = null;
            if (heroes != null) (heroServer = new HeroServer(combat, heroes, world, events)).Attach(host);
            var progression = heroServer == null ? null : heroServer.Progression;
            FlagServer flag = null;
            if (match.FlagMode) (flag = new FlagServer(combat, world, events, 1, progression)).Attach(host);
            LaneServer lanes = null;
            if (laneMap != null)
            {
                (lanes = new LaneServer(match, combat, world, events, laneMap, progression)).Attach(host);
                new OverrunServer(lanes, events, laneMap, progression).Attach(host);
            }
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
                    case ScriptedToFlagCommand _:
                        if (flag == null) return CommandResult.NotHandled;
                        if (!state.IsAlive(cmd.playerId)) return CommandResult.Reject("only living players move");
                        if (flag.State.status != FlagStatus.AtSpot && flag.State.status != FlagStatus.Dropped) return CommandResult.Reject("the flag isn't lying anywhere");
                        world.Clear(cmd.playerId);
                        combat.Movement.Teleport(cmd.playerId, flag.State.position, 0f);
                        return CommandResult.Accepted;
                    case ScriptedToLaneCommand _:
                    {
                        if (lanes == null) return CommandResult.NotHandled;
                        var player = state.Player(cmd.playerId);
                        if (player == null || !state.IsAlive(cmd.playerId)) return CommandResult.Reject("only living players move");
                        var front = FrontMinion(combat, match.Rules, player.team);
                        if (front == null) return CommandResult.Reject("no minion of yours on the lane");
                        float back = player.team == TeamId.A ? -BehindTheWave : BehindTheWave;
                        world.Clear(cmd.playerId);
                        combat.Movement.Teleport(cmd.playerId, front.position + new Vec3(0f, 0f, back), player.team == TeamId.A ? 0f : 180f);
                        return CommandResult.Accepted;
                    }
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
            return combat;
        }

        /// <summary>
        /// A team's minion furthest toward the enemy base, or null. Team A pushes toward +z on a lane map
        /// (Breakwater is mirrored end to end), team B toward -z; lower ids win ties, so runs repeat.
        /// </summary>
        static UnitState FrontMinion(CombatServer combat, Ruleset rules, TeamId team)
        {
            UnitState best = null;
            foreach (var u in combat.Units.Living())
            {
                if (u.team != team || rules.overrun == null || !rules.overrun.IsMinion(u.typeId)) continue;
                float ahead = team == TeamId.A ? u.position.z : -u.position.z;
                float bestAhead = best == null ? float.MinValue : team == TeamId.A ? best.position.z : -best.position.z;
                if (best == null || ahead > bestAhead + 1e-4f || (Math.Abs(ahead - bestAhead) <= 1e-4f && u.id < best.id)) best = u;
            }
            return best;
        }

        /// <summary>A stand-in objective layout for scripted play: three points, four hills, and Flag Rush's two bases and three flag spots.</summary>
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
                new ZoneSetDef
                {
                    name = "bases",
                    zones = { new ZoneDef(21, startOwner: TeamId.A), new ZoneDef(22, startOwner: TeamId.B) },
                },
            },
            flagSpots = { new Vec3(-10f, 0f, 0f), new Vec3(0f, 0f, 0f), new Vec3(10f, 0f, 0f) },
        };

        /// <summary>The usual ruleset with a short hero pick and deploy, a capped overtime so a scripted run starts, uses everything and ends.</summary>
        public static Ruleset SmokeRuleset()
        {
            var r = Ruleset.Default;
            r.heroPickSeconds = 3f;
            r.deploySeconds = 2f;
            r.matchEndSeconds = 5f;
            r.overtimeMaxSeconds = 30f; // scripted players never give a point up, so overtime needs an end
            r.flagFirstSpawnSeconds = 5f;   // Flag Rush: flags come round quickly too
            r.flagAfterCaptureSeconds = 4f;
            return r;
        }
    }

    /// <summary>
    /// Plays one client by script: picks a hero, starts the match if it is the host's player, then
    /// walks between the live zones and now and then shoots an enemy down with real shots from its
    /// spawn spot (WP4: moves, weapon switches, fire commands and lag-compensated hits all run). What it does depends only
    /// on its player id and the state it was sent, so a run can be repeated. Team A pushes harder
    /// than team B, so matches end with a clear result.
    /// </summary>
    public sealed class ScriptedPlayer
    {
        /// <summary>
        /// The heroes and an id with no data (it plays the catalog's fallback hero). New heroes go after "generic",
        /// so the first four players keep the lineup the session tests were tuned on.
        /// </summary>
        static readonly string[] Heroes = { "bulwark", "lancer", "mender", "rigger", "generic", "bolete" };

        /// <summary>Seconds of match time between changes of plan.</summary>
        const float PlanSeconds = 12f;

        readonly SessionClient client;
        readonly int expectedPlayers;
        int heroAttempt;
        bool startSent;
        int lastPlan = -1;
        ZoneId standing;
        bool wasAlive;
        int stepPlacement = -1;
        int firesSent;
        int abilityPlan = -1;
        readonly HashSet<int> choicesSent = new HashSet<int>();
        readonly WeaponLibrary weapons = StarterWeapons.Library();
        readonly Ruleset rules = Ruleset.Default;

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
            if (r.command == nameof(FireCommand)) firesSent--;             // that shot never counted
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
            // The server refills and resets weapons on respawn; start counting shots again from its number.
            if (!wasAlive && client.Snapshot != null && client.Snapshot.Weapons(client.PlayerId) is WeaponState mine) firesSent = mine.shotsFired;
            int plan = (int)(state.elapsed / PlanSeconds);
            if (!wasAlive)
            {
                // Back from a respawn (or just deployed): not on any zone, so plan afresh.
                wasAlive = true;
                standing = ZoneId.None;
                lastPlan = -1;
                flagStand = ZoneId.None;
                flagPlan = -1;
                carryingSince = -1f;
            }
            var flag = client.Snapshot == null ? null : client.Snapshot.flag;
            var overrun = client.Snapshot == null ? null : client.Snapshot.overrun;
            if (flag != null) PlayFlag(state, me, flag, plan); // shooting and abilities below run as in the other modes
            else if (overrun != null) PlayOverrun(state, me, overrun, plan);
            else if (plan != lastPlan)
            {
                lastPlan = plan;
                var target = ChooseZone(state, me, plan);
                if (target != standing)
                {
                    standing = target;
                    Send(new ScriptedStandCommand { zone = target });
                }
            }
            StepAside();
            // Now and then, one player shoots an enemy down, so damage, shields and respawns run too.
            // In Flag Rush anyone may go after an enemy carrier, so carriers go down and drop the flag.
            var carried = flag != null && flag.status == FlagStatus.Carried && flag.carrierTeam != me.team ? flag.carrierId : -1;
            if (carried > 0 && state.IsAlive(carried) && (int)(state.elapsed / 3f) % 2 == me.id % 2) ShootAt(carried);
            else if (plan % 3 == 2 && (plan / 3 + me.id) % 4 == 0)
            {
                int enemy = FirstLivingEnemy(state, me);
                if (enemy > 0) ShootAt(enemy);
            }
            // Once a plan, use an ability at the nearest enemy (the next one that is ready), and
            // take every upgrade as soon as it is offered: abilities and leveling run over the network too.
            if (plan != abilityPlan && plan % 3 == 1)
            {
                abilityPlan = plan;
                UseAbility(state, me, plan);
            }
            ChooseUpgrades(me);
        }

        int flagPlan = -1;
        ZoneId flagStand;
        float carryingSince = -1f;
        /// <summary>Flag Rush: seconds a scripted carrier takes to reach the enemy base.</summary>
        const float CarrySeconds = 6f;

        /// <summary>
        /// Flag Rush: a carrier heads for the enemy base; others run for a lying flag once a plan
        /// (team B only one plan in three, so team A ends up ahead, as with the zones).
        /// </summary>
        void PlayFlag(MatchState state, PlayerState me, FlagState flag, int plan)
        {
            if (flag.IsCarrier(me.id))
            {
                // The run home takes a while, so the other team has a chance to bring the carrier down.
                if (carryingSince < 0f) carryingSince = state.elapsed;
                var enemyBase = EnemyBase(state, me.team);
                if (enemyBase.IsValid && flagStand != enemyBase && state.elapsed >= carryingSince + CarrySeconds)
                {
                    flagStand = enemyBase;
                    Send(new ScriptedStandCommand { zone = enemyBase });
                }
                return;
            }
            flagStand = ZoneId.None;
            carryingSince = -1f;
            if (flag.status != FlagStatus.AtSpot && flag.status != FlagStatus.Dropped) return;
            if (plan == flagPlan) return;
            if (me.team == TeamId.B && (plan + me.id) % 3 != 0) return;
            flagPlan = plan;
            Send(new ScriptedToFlagCommand());
        }

        int lanePlan = -1;

        /// <summary>
        /// Overrun: once a plan, run up behind the team's front minion (team B only one plan in three, and
        /// it holds fire in the others, so team A's waves get the help and push), and shoot the nearest enemy minion in reach, or an enemy
        /// Bastion its minions have opened to heroes. Real shots, so unit hits and Bastion damage go
        /// through the server's checks.
        /// </summary>
        void PlayOverrun(MatchState state, PlayerState me, OverrunState overrun, int plan)
        {
            if (plan != lanePlan && (me.team == TeamId.A || (plan + me.id) % 3 == 0))
            {
                lanePlan = plan;
                Send(new ScriptedToLaneCommand());
            }
            // Team B fights only in the plans it goes to the lane, so team A's push lands.
            if (me.team == TeamId.B && (plan + me.id) % 3 != 0) return;
            var snap = client.Snapshot;
            var mine = snap.Motion(client.PlayerId);
            if (mine == null || snap.units == null) return;
            UnitSnapshot best = null;
            float bestDistance = float.MaxValue;
            foreach (var u in snap.units)
            {
                if (u.team == me.team || u.team == TeamId.None) continue;
                var bastion = overrun.ByUnit(u.id);
                if (bastion != null && !bastion.openToHeroes) continue;
                bool minion = rules.overrun.IsMinion(u.typeId);
                if (!minion && bastion == null) continue;
                // Minions first: a Bastion counts as a little further off than it is.
                float d = Vec3.Distance(mine.position, u.position) + (minion ? 0f : 15f);
                if (d < bestDistance - 1e-4f || (Math.Abs(d - bestDistance) <= 1e-4f && u.id < best.id)) { best = u; bestDistance = d; }
            }
            if (best == null) return;
            var kind = rules.UnitKind(best.typeId);
            ShootAtPoint(Hitboxes.Chest(rules, new BodyPose(best.position, false, true, kind == null ? 1f : kind.size)));
        }

        /// <summary>The capture zone in the other team's base: the zone whose startOwner is the enemy.</summary>
        static ZoneId EnemyBase(MatchState state, TeamId team)
        {
            foreach (var z in state.zones) if (z.startOwner == team.Other() && z.startOwner != TeamId.None) return z.id;
            return ZoneId.None;
        }

        void UseAbility(MatchState state, PlayerState me, int plan)
        {
            var snap = client.Snapshot;
            var mine = snap == null || snap.abilities == null ? null : snap.abilities.Unit(me.id);
            var m = snap == null ? null : snap.Motion(me.id);
            if (mine == null || m == null || mine.slots.Count == 0) return;
            // Take turns through the slots, skipping any still recharging.
            int first = (plan / 3 + me.id) % mine.slots.Count, slot = -1;
            for (int k = 0; k < mine.slots.Count && slot < 0; k++)
            {
                int i = (first + k) % mine.slots.Count;
                if (mine.slots[i].charges > 0) slot = i;
            }
            if (slot < 0) return;
            Vec3 eye = m.position + Vec3.Up * Hitboxes.EyeHeight(rules, m.crouched);
            Vec3 aim = Vec3.Forward;
            int enemy = FirstLivingEnemy(state, me);
            var them = enemy > 0 ? snap.Motion(enemy) : null;
            if (them != null) aim = (Hitboxes.Chest(rules, them.Pose(true)) - eye).Normalized;
            Send(new UseAbilityCommand { slot = slot, abilityId = mine.slots[slot].abilityId, aimOrigin = eye, aimDirection = aim });
        }

        void ChooseUpgrades(PlayerState me)
        {
            var snap = client.Snapshot;
            var mine = snap == null || snap.progression == null ? null : snap.progression.Player(me.id);
            if (mine == null) return;
            for (int level = 2; level <= mine.level; level++)
            {
                if (rules.progression.ChoiceAt(level) == UpgradeKind.None || mine.HasPicked(level) || choicesSent.Contains(level)) continue;
                choicesSent.Add(level);
                Send(new ChooseUpgradeCommand { level = level, option = (me.id + level) % 2 });
            }
        }

        /// <summary>After each placement, take one small step, so moves go through the server's checks.</summary>
        void StepAside()
        {
            var snap = client.Snapshot;
            var m = snap == null ? null : snap.Motion(client.PlayerId);
            if (m == null || m.placement == stepPlacement) return;
            stepPlacement = m.placement;
            Send(new MoveCommand { position = m.position + new Vec3(0.5f, 0f, 0f), yaw = m.yaw, placement = m.placement });
        }

        /// <summary>One shot at the enemy's chest, with a weapon that reaches, once the server has every earlier shot.</summary>
        void ShootAt(int enemyId)
        {
            var snap = client.Snapshot;
            var them = snap == null ? null : snap.Motion(enemyId);
            if (them != null) ShootAtPoint(Hitboxes.Chest(rules, them.Pose(true)));
        }

        /// <summary>One shot at a point (a chest), with a weapon that reaches, once the server has every earlier shot.</summary>
        void ShootAtPoint(Vec3 chest)
        {
            var snap = client.Snapshot;
            var me = snap == null ? null : snap.Motion(client.PlayerId);
            var arms = snap == null ? null : snap.Weapons(client.PlayerId);
            if (me == null || arms == null || me.placement != stepPlacement) return;
            if (arms.shotsFired != firesSent) return; // a shot still on its way
            Vec3 eye = me.position + Vec3.Up * Hitboxes.EyeHeight(rules, me.crouched);
            float distance = Vec3.Distance(eye, chest);

            var w = weapons.Weapon(arms.CurrentId);
            // Lobbed weapons (Bolete's spore bombs) count when the target is well inside their throw.
            if (w == null || w.Reach < distance + 1f)
            {
                for (int i = 0; i < arms.weapons.Length; i++)
                {
                    var other = weapons.Weapon(arms.weapons[i]);
                    if (i != arms.current && other != null && other.Reach >= distance + 1f)
                    {
                        Send(new SwitchWeaponCommand { slot = i });
                        return;
                    }
                }
                return;
            }
            if (arms.Reloading || arms.switchLeft > 0f || arms.CurrentAmmo <= 0 || arms.fireCredit < 1f) return;
            firesSent++;
            Send(new FireCommand { weaponId = w.id, origin = eye, direction = w.AimAt(eye, chest) });
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
        public static string Of(MatchState s) => Of(s, null);

        /// <summary>With Overrun's Bastions and side gates (which Bastions stand, which gates are open) when <paramref name="overrun"/> is given.</summary>
        public static string Of(MatchState s, OverrunState overrun)
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
            if (overrun != null)
            {
                var bastions = new List<BastionState>(overrun.bastions);
                bastions.Sort((a, b) => a.team != b.team ? a.team.CompareTo(b.team) : a.tier.CompareTo(b.tier));
                foreach (var b in bastions) sb.Append("bastion ").Append(b.team).Append(' ').Append(b.tier).Append(" alive=").Append(b.alive).Append('\n');
                sb.Append("gates A=").Append(overrun.gateOpenA).Append(" B=").Append(overrun.gateOpenB).Append(" latePush=").Append(overrun.latePush).Append('\n');
            }
            return sb.ToString();
        }
    }
}

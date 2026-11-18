using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Everything that fights on the server, put together for one match: WP7's ability runner (and
    /// with it health, shields and statuses), personal shields, movement checks, the lag
    /// compensation history and weapons. The host builds one after MatchRules and attaches it to its
    /// <see cref="SessionHost"/>; tests drive it directly.
    ///
    /// Each tick runs, in order: match, abilities (health, shields, statuses), personal shields,
    /// movement (records the history for this tick), weapons (reloads, projectiles).
    /// </summary>
    public sealed class CombatServer
    {
        /// <summary>Practice dummies get ids from here up, clear of real players.</summary>
        public const int FirstDummyId = 1000;

        public MatchRules Match { get; }
        public AbilityRunner Abilities { get; }
        public DamageRules Damage => Abilities.Combat;
        public HitHistory History { get; }
        public CombatWorld World { get; }
        public MovementRules Movement { get; }
        public WeaponRules Weapons { get; }

        readonly IGameEvents events;
        readonly List<int> dummies = new List<int>();

        public IReadOnlyList<int> Dummies => dummies;
        public Ruleset Rules => Match.Rules;

        /// <param name="geometry">The map: Raycast for walls and floors (players are ignored), PlayersInZone for zones.</param>
        /// <param name="tickSeconds">Seconds per server tick (NetSettings.TickSeconds).</param>
        public CombatServer(MatchRules match, IWorldQuery geometry, IGameEvents events, float tickSeconds,
            ArenaLayout layout = null, WeaponLibrary weapons = null, AbilityLibrary abilities = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            var problems = match.Rules.ValidateCombat();
            if (problems.Count > 0) throw new ArgumentException("Can't run combat: " + string.Join("; ", problems));

            History = new HitHistory(match.Rules, tickSeconds);
            World = new CombatWorld(geometry ?? throw new ArgumentNullException(nameof(geometry)), History, match.Rules);
            // The ability runner subscribes first, so its respawn reset (full health, shields cleared)
            // happens before the personal shield is given back below.
            Abilities = new AbilityRunner(match, abilities ?? new AbilityLibrary(), World, events);
            Movement = new MovementRules(match, Damage, History, events, layout);
            Weapons = new WeaponRules(match, Damage, Movement, World, events, weapons ?? StarterWeapons.Library(), tickSeconds);
            // Placed barriers and gates stop shots and rays; shots hurt placed objects.
            World.Objects = Abilities.State.objects;
            World.PassesObject = (shooterId, o) =>
            {
                var p = Match.State.Player(shooterId);
                return p != null && p.team == o.team && !Rules.friendlyFire;
            };
            Weapons.DamageObject = (objectId, amount, attackerId) => Abilities.DamageObject(objectId, amount, attackerId);
            Weapons.Slow = (targetId, sourceId, share, seconds) =>
                Abilities.ApplyStatus(targetId, AbilityLibrary.SlowStatus, sourceId, share, seconds, 0f, StatIds.WeaponSlow);
            events.Subscribe(OnEvent);
        }

        public void Dispose()
        {
            events.Unsubscribe(OnEvent);
            Abilities.Dispose();
        }

        void OnEvent(GameEvent e)
        {
            Movement.OnEvent(e);
            Weapons.OnEvent(e);
            if (e is PlayerSpawnedEvent spawned) RefillPersonalShield(spawned.playerId);
        }

        /// <summary>
        /// Route a command a client sent. Returns NotHandled for commands that belong elsewhere.
        /// <paramref name="hostPlayerId"/> is who may add dummies.
        /// </summary>
        public CommandResult Apply(MatchCommand cmd, int hostPlayerId = -1)
        {
            switch (cmd)
            {
                case MoveCommand move: return Movement.Apply(move);
                case FireCommand _:
                case ReloadCommand _:
                case SwitchWeaponCommand _:
                    return Weapons.Apply(cmd);
                case UseAbilityCommand _:
                    return Abilities.Apply(cmd);
                case AddDummyCommand add:
                    if (cmd.playerId != hostPlayerId) return CommandResult.Reject("only the host adds dummies");
                    return AddDummy(add.team);
                default:
                    return CommandResult.NotHandled;
            }
        }

        /// <summary>
        /// A practice target: a player nobody controls, standing at the layout's dummy spots. Only in
        /// the lobby, while the team has room. Dummies respawn like anyone else.
        /// </summary>
        public CommandResult AddDummy(TeamId team)
        {
            if (Match.State.phase != MatchPhase.Lobby) return CommandResult.Reject("dummies join in the lobby");
            int id = FirstDummyId + dummies.Count;
            var result = Match.AddPlayer(id, team);
            if (!result.IsAccepted) return result;
            dummies.Add(id);
            Movement.MarkDummy(id);
            return CommandResult.Accepted;
        }

        public void Tick(float dt)
        {
            Abilities.Tick(dt);
            TickPersonalShields(dt);
            Movement.Tick(dt);
            Weapons.Tick(dt);
        }

        public void FillSnapshot(MatchSnapshot s)
        {
            Abilities.FillSnapshot(s);
            Movement.FillSnapshot(s);
            Weapons.FillSnapshot(s);
            s.dummies = dummies.ToArray();
        }

        /// <summary>Hook into a session: commands, the tick and the snapshots.</summary>
        public void Attach(SessionHost host)
        {
            host.AddCommandHandler(cmd => Apply(cmd, host.HostPlayerId));
            host.AddTickHandler(Tick);
            host.AddSnapshotFiller(FillSnapshot);
        }

        // ---- Personal shields ----

        /// <summary>The ability id that marks a hero's own regenerating shield among their shields.</summary>
        public const string PersonalShieldId = "personal_shield";

        /// <summary>
        /// A hero's own shield is one of WP7's shield layers that never times out. Damage takes
        /// shields that run out soonest first, so ability shields go before it, then it, then health.
        /// </summary>
        public static ShieldState PersonalShield(HeroUnit u)
        {
            foreach (var s in u.shields) if (s.abilityId == PersonalShieldId) return s;
            return null;
        }

        void RefillPersonalShield(int playerId)
        {
            var u = Damage.Unit(playerId);
            if (u == null || Rules.personalShield <= 0f) return;
            var s = PersonalShield(u);
            if (s == null) u.shields.Add(s = new ShieldState { sourceId = -1, abilityId = PersonalShieldId });
            s.amount = Math.Min(Rules.personalShield, Math.Max(0f, Rules.maxShield - (u.Shield - s.amount)));
            s.timeLeft = float.MaxValue;
        }

        void TickPersonalShields(float dt)
        {
            if (Rules.personalShield <= 0f) return;
            foreach (var p in Match.State.players)
            {
                if (!Match.State.IsAlive(p.id)) continue;
                var u = Damage.Unit(p.id);
                var s = PersonalShield(u);
                if (s == null)
                {
                    // Broken by damage (WP7 drops empty layers): it grows back after the delay.
                    if (u.sinceDamaged < Rules.personalShieldDelaySeconds) continue;
                    u.shields.Add(s = new ShieldState { sourceId = -1, abilityId = PersonalShieldId });
                }
                s.timeLeft = float.MaxValue;
                if (u.sinceDamaged < Rules.personalShieldDelaySeconds || s.amount >= Rules.personalShield) continue;
                float room = Math.Max(0f, Rules.maxShield - u.Shield);
                s.amount += Math.Min(room, Math.Min(Rules.personalShield - s.amount, Rules.personalShieldRegenPerSecond * dt));
            }
        }
    }
}

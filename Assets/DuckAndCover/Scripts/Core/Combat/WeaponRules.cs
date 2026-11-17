using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Weapons on the server. A client fires with a <see cref="FireCommand"/> from where it sees
    /// itself, aiming where it sees its target; this checks the shot (alive, in play, the weapon in
    /// hand, ammo, fire rate, the origin near the server's eye position) and decides what it hit by
    /// rewinding every body to the tick the shooter saw (<see cref="HitHistory"/>). Damage goes
    /// through WP7's <see cref="DamageRules"/>, so shields, stats and eliminations work the same
    /// for weapons and abilities. Projectiles fly on the server, tick by tick.
    /// </summary>
    public sealed class WeaponRules
    {
        readonly MatchRules match;
        readonly DamageRules combat;
        readonly MovementRules movement;
        readonly CombatWorld world;
        readonly IGameEvents events;
        readonly float tickSeconds;
        readonly List<WeaponState> states = new List<WeaponState>();
        readonly List<ProjectileState> projectiles = new List<ProjectileState>();
        int nextProjectileId = 1;

        public WeaponLibrary Library { get; }
        /// <summary>
        /// Hurts a placed object (barrier, turret, mine) a shot hit: object id, final amount, attacker.
        /// Set by <see cref="CombatServer"/> to the ability rules; null = shots don't hurt objects.
        /// </summary>
        public Func<int, float, int, float> DamageObject;
        Ruleset Rules => match.Rules;
        MatchState State => match.State;

        public WeaponRules(MatchRules match, DamageRules combat, MovementRules movement, CombatWorld world, IGameEvents events, WeaponLibrary library, float tickSeconds)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.movement = movement ?? throw new ArgumentNullException(nameof(movement));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Library = library ?? throw new ArgumentNullException(nameof(library));
            this.tickSeconds = tickSeconds;
            var problems = library.Validate();
            if (problems.Count > 0) throw new ArgumentException("Can't run weapons: " + string.Join("; ", problems));
        }

        public IReadOnlyList<ProjectileState> Projectiles => projectiles;

        /// <summary>A player's weapons, made from the default loadout on first use. Null for unknown players.</summary>
        public WeaponState Weapons(int playerId)
        {
            foreach (var s in states) if (s.playerId == playerId) return s;
            var p = State.Player(playerId);
            if (p == null) return null;
            var made = new WeaponState { playerId = playerId };
            states.Add(made);
            states.Sort((a, b) => a.playerId.CompareTo(b.playerId));
            Equip(made, Library.LoadoutFor(p.heroId));
            return made;
        }

        /// <summary>Give a player these weapons (hero data), full and with slot 0 in hand.</summary>
        public CommandResult SetLoadout(int playerId, string[] weaponIds)
        {
            var s = Weapons(playerId);
            if (s == null) return CommandResult.Reject("unknown player");
            if (weaponIds == null || weaponIds.Length == 0) return CommandResult.Reject("no weapons");
            foreach (var id in weaponIds) if (Library.Weapon(id) == null) return CommandResult.Reject("unknown weapon '" + id + "'");
            Equip(s, weaponIds);
            return CommandResult.Accepted;
        }

        void Equip(WeaponState s, string[] ids)
        {
            s.weapons = (string[])ids.Clone();
            s.ammo = new int[ids.Length];
            Refill(s);
        }

        void Refill(WeaponState s)
        {
            for (int i = 0; i < s.weapons.Length; i++)
            {
                var w = Library.Weapon(s.weapons[i]);
                s.ammo[i] = w == null ? 0 : w.magazine;
            }
            s.current = 0;
            s.reloadLeft = 0f;
            s.switchLeft = 0f;
            s.fireCredit = Rules.fireBurstShots;
        }

        WeaponDef Current(WeaponState s) => Library.Weapon(s.CurrentId);

        // ---- Commands ----

        public CommandResult Apply(MatchCommand cmd)
        {
            switch (cmd)
            {
                case FireCommand fire: return Fire(fire);
                case ReloadCommand _: return Reload(cmd.playerId);
                case SwitchWeaponCommand sw: return Switch(cmd.playerId, sw.slot);
                default: return CommandResult.NotHandled;
            }
        }

        string CanAct(int playerId)
        {
            if (!State.IsPlaying) return "weapons are live once play starts";
            if (!State.IsAlive(playerId)) return "only living players fire";
            var u = combat.Unit(playerId);
            if (u != null && u.HasStatus(AbilityLibrary.StunStatus)) return "stunned";
            return null;
        }

        public CommandResult Fire(FireCommand cmd)
        {
            string why = CanAct(cmd.playerId);
            if (why != null) return CommandResult.Reject(why);
            var s = Weapons(cmd.playerId);
            var w = Current(s);
            if (w == null) return CommandResult.Reject("no weapon in hand");
            if (!string.IsNullOrEmpty(cmd.weaponId) && cmd.weaponId != w.id) return CommandResult.Reject(cmd.weaponId + " is not in hand");
            if (s.switchLeft > 0f) return CommandResult.Reject("still raising the weapon");
            if (s.Reloading) return CommandResult.Reject("reloading");
            if (s.CurrentAmmo <= 0) return CommandResult.Reject("empty");
            if (s.fireCredit < 1f - 1e-3f) return CommandResult.Reject("firing faster than the weapon allows");

            Vec3 dir = cmd.direction.Normalized;
            if (dir == Vec3.Zero) return CommandResult.Reject("no aim direction");
            var m = movement.Motion(cmd.playerId);
            if (m != null)
            {
                Vec3 eye = m.position + Vec3.Up * Hitboxes.EyeHeight(Rules, m.crouched);
                if (Vec3.Distance(eye, cmd.origin) > Rules.shotOriginTolerance) return CommandResult.Reject("shot from too far away from the player");
            }

            s.fireCredit -= 1f;
            s.ammo[s.current]--;
            s.shotsFired++;
            if (w.kind == WeaponKind.Projectile) Launch(cmd, w, s, dir);
            else Hitscan(cmd, w, s, dir);
            if (s.CurrentAmmo <= 0) StartReload(s, w);
            return CommandResult.Accepted;
        }

        /// <summary>The tick a shot is checked at: what the shooter saw, but no further back than the rewind window.</summary>
        public int RewindTick(int clientTick)
        {
            int now = State.tick;
            int window = (int)Math.Round(Rules.maxRewindSeconds / tickSeconds);
            if (clientTick <= 0 || clientTick > now) return now;
            return Math.Max(now - window, clientTick);
        }

        void Hitscan(FireCommand cmd, WeaponDef w, WeaponState s, Vec3 dir)
        {
            int tick = RewindTick(cmd.tick);
            var shot = new ShotFiredEvent
            {
                playerId = cmd.playerId, weaponId = w.id, origin = cmd.origin, ammoLeft = s.CurrentAmmo,
                ends = new Vec3[w.pellets], hitPlayers = new int[w.pellets], hitParts = new int[w.pellets],
            };
            var targets = new List<int>();
            var amounts = new List<float>();
            var objects = new List<int>();
            var objectAmounts = new List<float>();
            // A healing weapon stops on teammates (to heal them) instead of passing through.
            var passThrough = w.allyHeal > 0f ? null : PassThrough(cmd.playerId);
            float scale = WeaponDamage(cmd.playerId);
            for (int i = 0; i < w.pellets; i++)
            {
                Vec3 d = SpreadPattern.Direction(dir, i, w.pellets, w.spreadDegrees);
                shot.hitPlayers[i] = -1;
                if (!world.RaycastFrom(cmd.playerId, cmd.origin, d, w.range, tick, out var hit, passThrough))
                {
                    shot.ends[i] = cmd.origin + d * w.range;
                    continue;
                }
                shot.ends[i] = hit.point;
                if (hit.kind == HitKind.Device)
                {
                    Add(objects, objectAmounts, hit.deviceId, w.damage * scale * w.Falloff(hit.distance));
                    continue;
                }
                if (hit.kind != HitKind.Player) continue;
                shot.hitPlayers[i] = hit.playerId;
                shot.hitParts[i] = hit.bodyPart;
                float share = w.Falloff(hit.distance) * w.PartMultiplier(hit.bodyPart);
                // Heal shots on teammates are kept as negative amounts until the end.
                float amount = w.allyHeal > 0f && IsTeammate(cmd.playerId, hit.playerId) ? -w.allyHeal * share : w.damage * scale * share;
                Add(targets, amounts, hit.playerId, amount);
            }
            Publish(shot);
            // One damage (or heal) call per target per shot, in the order the pellets reached them.
            for (int i = 0; i < targets.Count; i++)
            {
                if (amounts[i] < 0f) combat.Heal(cmd.playerId, targets[i], -amounts[i], w.id);
                else combat.Damage(cmd.playerId, targets[i], amounts[i], w.id);
            }
            HurtObjects(cmd.playerId, objects, objectAmounts);
        }

        static void Add(List<int> ids, List<float> amounts, int id, float amount)
        {
            int k = ids.IndexOf(id);
            if (k < 0) { ids.Add(id); amounts.Add(amount); }
            else amounts[k] += amount;
        }

        bool IsTeammate(int shooterId, int targetId)
        {
            var a = State.Player(shooterId);
            var b = State.Player(targetId);
            return a != null && b != null && shooterId != targetId && a.team == b.team;
        }

        /// <summary>Objects take the attacker's damageDealt too, as abilities' damage to objects does.</summary>
        void HurtObjects(int attackerId, List<int> objects, List<float> amounts)
        {
            if (DamageObject == null) return;
            float dealt = Math.Max(0f, combat.Stat(attackerId, StatIds.DamageDealt));
            for (int i = 0; i < objects.Count; i++) DamageObject(objects[i], amounts[i] * dealt, attackerId);
        }

        void Launch(FireCommand cmd, WeaponDef w, WeaponState s, Vec3 dir)
        {
            var p = new ProjectileState
            {
                id = nextProjectileId++, ownerId = cmd.playerId, weaponId = w.id,
                position = cmd.origin, velocity = dir * w.projectileSpeed, gravity = w.projectileGravity,
            };
            projectiles.Add(p);
            Publish(new ProjectileLaunchedEvent
            {
                projectileId = p.id, ownerId = p.ownerId, weaponId = w.id, origin = p.position, velocity = p.velocity,
                gravity = p.gravity, ammoLeft = s.CurrentAmmo,
            });
        }

        /// <summary>With friendly fire off, shots fly through teammates instead of being soaked up by them.</summary>
        Func<int, bool> PassThrough(int shooterId)
        {
            if (Rules.friendlyFire) return null;
            var me = State.Player(shooterId);
            if (me == null) return null;
            return id => { var p = State.Player(id); return p != null && p.team == me.team; };
        }

        public CommandResult Reload(int playerId)
        {
            string why = CanAct(playerId);
            if (why != null) return CommandResult.Reject(why);
            var s = Weapons(playerId);
            var w = Current(s);
            if (w == null) return CommandResult.Reject("no weapon in hand");
            if (s.Reloading) return CommandResult.Accepted;
            if (s.CurrentAmmo >= w.magazine) return CommandResult.Reject("already full");
            StartReload(s, w);
            return CommandResult.Accepted;
        }

        void StartReload(WeaponState s, WeaponDef w)
        {
            float seconds = ReloadSeconds(s.playerId, w);
            s.reloadLeft = Math.Max(1e-3f, seconds);
            Publish(new ReloadStartedEvent { playerId = s.playerId, weaponId = w.id, seconds = seconds });
        }

        // ---- Hero stats (traits, talents, statuses) ----

        /// <summary>Seconds a reload takes for this player: the weapon's, divided by their reloadSpeed stat.</summary>
        public float ReloadSeconds(int playerId, WeaponDef w) => w.reloadSeconds / Math.Max(0.1f, combat.Stat(playerId, StatIds.ReloadSpeed));

        /// <summary>Seconds between shots for this player: the weapon's, divided by their fireRate stat.</summary>
        public float FireInterval(int playerId, WeaponDef w) => w.fireInterval / Math.Max(0.1f, combat.Stat(playerId, StatIds.FireRate));

        /// <summary>The player's weaponDamage stat (1 = the weapon's own numbers).</summary>
        float WeaponDamage(int playerId) => Math.Max(0f, combat.Stat(playerId, StatIds.WeaponDamage));

        public CommandResult Switch(int playerId, int slot)
        {
            if (!State.IsAlive(playerId)) return CommandResult.Reject("only living players switch weapons");
            var s = Weapons(playerId);
            if (slot < 0 || slot >= s.weapons.Length) return CommandResult.Reject("no weapon in slot " + slot);
            if (slot == s.current) return CommandResult.Accepted;
            s.current = slot;
            s.reloadLeft = 0f; // putting a weapon away cancels its reload
            s.switchLeft = Rules.weaponSwitchSeconds;
            s.fireCredit = Math.Min(s.fireCredit, 1f);
            Publish(new WeaponSwitchedEvent { playerId = playerId, slot = slot, weaponId = s.CurrentId });
            if (s.CurrentAmmo <= 0) StartReload(s, Current(s));
            return CommandResult.Accepted;
        }

        // ---- Events and time ----

        public void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case HeroPickedEvent picked:
                {
                    var s = Weapons(picked.playerId);
                    if (s != null) Equip(s, Library.LoadoutFor(picked.heroId));
                    break;
                }
                case PlayerSpawnedEvent spawned:
                {
                    var s = Weapons(spawned.playerId);
                    if (s != null) Refill(s);
                    break;
                }
            }
        }

        public void Tick(float dt)
        {
            foreach (var s in states)
            {
                var w = Current(s);
                if (w == null) continue;
                if (s.switchLeft > 0f) s.switchLeft = Math.Max(0f, s.switchLeft - dt);
                if (s.reloadLeft > 0f)
                {
                    s.reloadLeft -= dt;
                    if (s.reloadLeft <= 0f) { s.reloadLeft = 0f; s.ammo[s.current] = w.magazine; }
                }
                s.fireCredit = Math.Min(Rules.fireBurstShots, s.fireCredit + dt / FireInterval(s.playerId, w));
            }
            TickProjectiles(dt);
        }

        void TickProjectiles(float dt)
        {
            for (int i = 0; i < projectiles.Count; i++)
            {
                var p = projectiles[i];
                p.age += dt;
                Vec3 before = p.position;
                Vec3 v0 = p.velocity;
                p.velocity = v0 - Vec3.Up * (p.gravity * dt);
                Vec3 after = before + (v0 + p.velocity) * (0.5f * dt);
                Vec3 step = after - before;
                float len = step.Magnitude;
                bool done = false;
                if (len > 1e-5f && world.RaycastFrom(p.ownerId, before, step / len, len, State.tick, out var hit, PassThrough(p.ownerId)))
                {
                    Explode(p, hit.point, hit.kind == HitKind.Player ? hit.playerId : -1, hit.kind == HitKind.Device ? hit.deviceId : -1);
                    done = true;
                }
                else if (p.age >= Rules.projectileMaxSeconds)
                {
                    Explode(p, after, -1, -1);
                    done = true;
                }
                else p.position = after;
                if (done) { projectiles.RemoveAt(i); i--; }
            }
        }

        void Explode(ProjectileState p, Vec3 point, int direct, int directObject)
        {
            var w = Library.Weapon(p.weaponId);
            Publish(new ProjectileExplodedEvent
            {
                projectileId = p.id, ownerId = p.ownerId, weaponId = p.weaponId, point = point, directHit = direct,
                splashRadius = w == null ? 0f : w.splashRadius,
            });
            if (w == null) return;
            float scale = WeaponDamage(p.ownerId);
            var targets = new List<int>();
            var amounts = new List<float>();
            if (direct >= 0 && w.damage > 0f) { targets.Add(direct); amounts.Add(w.damage * scale); }
            if (directObject >= 0)
                HurtObjects(p.ownerId, new List<int> { directObject }, new List<float> { (w.damage + w.splashDamage) * scale });
            if (w.splashRadius > 0f && w.splashDamage > 0f)
            {
                foreach (int id in world.PlayersInSphere(point, w.splashRadius, State.tick))
                {
                    if (!world.History.PoseAt(id, State.tick, out var pose)) continue;
                    float dist = Vec3.Distance(Hitboxes.Chest(Rules, pose), point);
                    float amount = w.SplashAt(dist) * scale;
                    if (id == p.ownerId) amount *= Rules.selfSplashShare;
                    if (amount <= 0f) continue;
                    Vec3 toward = (Hitboxes.Chest(Rules, pose) - point).Normalized;
                    // Start a little off the surface it hit, so that wall doesn't block its own splash.
                    if (!world.HasLineOfSight(point + toward * 0.05f, id, State.tick)) continue;
                    int k = targets.IndexOf(id);
                    if (k < 0) { targets.Add(id); amounts.Add(amount); }
                    else amounts[k] += amount;
                }
            }
            for (int i = 0; i < targets.Count; i++) combat.Damage(p.ownerId, targets[i], amounts[i], p.weaponId);
        }

        public void FillSnapshot(MatchSnapshot s)
        {
            s.weapons = new List<WeaponState>(states.Count);
            foreach (var w in states) s.weapons.Add(w.Clone());
            s.projectiles = new List<ProjectileState>(projectiles.Count);
            foreach (var p in projectiles) s.projectiles.Add(p.Clone());
        }

        void Publish(GameEvent e)
        {
            e.tick = State.tick;
            events.Publish(e);
        }
    }
}

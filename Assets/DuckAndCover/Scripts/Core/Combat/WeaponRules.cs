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
        /// Slows a player a weapon hit (the weaponSlow stat): target, shooter, share of speed, seconds.
        /// Set by <see cref="CombatServer"/> to the ability rules' slow status; null = hits don't slow.
        /// </summary>
        public Action<int, int, float, float> Slow;
        /// <summary>Clears a teammate's harmful statuses (the weaponCleanse stat). Set by <see cref="CombatServer"/>.</summary>
        public Action<int> Cleanse;
        /// <summary>
        /// Hurts a placed object (barrier, turret, mine) a shot hit: object id, final amount, attacker.
        /// Set by <see cref="CombatServer"/> to the ability rules; null = shots don't hurt objects.
        /// </summary>
        public Func<int, float, int, float> DamageObject;
        /// <summary>
        /// Told the shooter's id just before a shot is checked for hits (after it is accepted), so a cloak
        /// can end and its ambush window count for that shot. Set by <see cref="CombatServer"/>.
        /// </summary>
        public Action<int> BeforeShot;
        /// <summary>Why a player may not fire right now beyond the weapon's own rules (a shield held up), or null. Set by CombatServer.</summary>
        public Func<int, string> FireCheck;
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
            s.magazines = new int[ids.Length];
            Refill(s);
        }

        void Refill(WeaponState s)
        {
            for (int i = 0; i < s.weapons.Length; i++)
            {
                var w = Library.Weapon(s.weapons[i]);
                s.magazines[i] = Magazine(s.playerId, w);
                s.ammo[i] = s.magazines[i];
            }
            s.current = 0;
            s.reloadLeft = 0f;
            s.switchLeft = 0f;
            s.fireCredit = Rules.fireBurstShots;
            s.healRounds = 0;
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
            string why = CanAct(cmd.playerId) ?? (FireCheck == null ? null : FireCheck(cmd.playerId));
            if (why != null) return CommandResult.Reject(why);
            var s = Weapons(cmd.playerId);
            var w = Current(s);
            if (w == null) return CommandResult.Reject("no weapon in hand");
            if (!string.IsNullOrEmpty(cmd.weaponId) && cmd.weaponId != w.id) return CommandResult.Reject(cmd.weaponId + " is not in hand");
            if (s.switchLeft > 0f) return CommandResult.Reject("still raising the weapon");
            if (s.Reloading) return CommandResult.Reject("reloading");
            bool melee = w.kind == WeaponKind.Melee;
            bool noAmmo = w.NoAmmo();
            if (!noAmmo && s.CurrentAmmo <= 0) return CommandResult.Reject("empty");
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
            s.shotsFired++;
            if (BeforeShot != null) BeforeShot(cmd.playerId);
            if (melee)
            {
                Swing(cmd, w, dir);
                return CommandResult.Accepted;
            }
            if (!noAmmo) s.ammo[s.current]--;
            bool healRound = s.healRounds > 0;
            if (healRound) s.healRounds--;
            if (w.kind == WeaponKind.Projectile) Launch(cmd, w, s, dir);
            else Hitscan(cmd, w, s, dir, healRound);
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

        void Hitscan(FireCommand cmd, WeaponDef w, WeaponState s, Vec3 dir, bool healRound)
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
            // A healing weapon (or a healing round) stops on teammates (to heal them) instead of passing through.
            float allyHeal = healRound ? s.healPerRound : w.allyHeal;
            var passThrough = allyHeal > 0f ? null : PassThrough(cmd.playerId);
            float scale = WeaponDamage(cmd.playerId);
            float reach = Math.Max(0.1f, combat.Stat(cmd.playerId, StatIds.WeaponRange));
            float range = w.range * reach;
            for (int i = 0; i < w.pellets; i++)
            {
                Vec3 d = SpreadPattern.Direction(dir, i, w.pellets, w.spreadDegrees);
                shot.hitPlayers[i] = -1;
                if (!world.RaycastFrom(cmd.playerId, cmd.origin, d, range, tick, out var hit, passThrough))
                {
                    shot.ends[i] = cmd.origin + d * range;
                    continue;
                }
                shot.ends[i] = hit.point;
                if (hit.kind == HitKind.Device)
                {
                    // Healing rounds only heal: they don't hurt enemies or their objects.
                    if (!healRound) Add(objects, objectAmounts, hit.deviceId, w.damage * scale * w.Falloff(hit.distance / reach));
                    continue;
                }
                if (hit.kind != HitKind.Player) continue;
                shot.hitPlayers[i] = hit.playerId;
                shot.hitParts[i] = hit.bodyPart;
                bool teammate = IsTeammate(cmd.playerId, hit.playerId);
                if (healRound && !teammate) continue; // a healing round stops on an enemy and does nothing
                float share = w.Falloff(hit.distance / reach) * PartMultiplier(cmd.playerId, w, hit.bodyPart);
                // Heal shots on teammates are kept as negative amounts until the end.
                // weaponDamage makes a healing weapon heal harder too.
                float amount = allyHeal > 0f && teammate ? -allyHeal * scale * share : w.damage * scale * share;
                Add(targets, amounts, hit.playerId, amount);
            }
            Publish(shot);
            // One damage (or heal) call per target per shot, in the order the pellets reached them.
            for (int i = 0; i < targets.Count; i++)
            {
                if (amounts[i] < 0f)
                {
                    HealTeammate(cmd.playerId, targets[i], -amounts[i], w.id);
                    if (healRound) SpreadHeal(cmd.playerId, targets[i], -amounts[i], s.healSplash, w.id, tick);
                }
                else AfterHit(cmd.playerId, targets[i], combat.Damage(cmd.playerId, targets[i], amounts[i], w.id), dir);
            }
            HurtObjects(cmd.playerId, objects, objectAmounts);
        }

        /// <summary>
        /// A melee swing, judged against bodies as the swinger saw them: every enemy (player or unit) whose
        /// chest is within the weapon's reach (times the weaponRange stat) of the swinger's eye, inside the
        /// arc around their level aim and no more than <see cref="Ruleset.meleeReachUp"/> above or below,
        /// with nothing of the map or an enemy barrier in between. Nearest first, up to maxTargets. Every hit
        /// is a body hit with no falloff. An enemy object straight ahead within reach (a shield, a drone, a
        /// wall) takes the swing too, and a solid one stops it from reaching anyone behind.
        /// </summary>
        void Swing(FireCommand cmd, WeaponDef w, Vec3 dir)
        {
            int tick = RewindTick(cmd.tick);
            float reach = w.range * Math.Max(0.1f, combat.Stat(cmd.playerId, StatIds.WeaponRange));
            Vec3 level = new Vec3(dir.x, 0f, dir.z);
            if (level.Magnitude < 1e-4f) level = Vec3.Forward; // looking straight up or down: swing ahead
            level = level.Normalized;
            float halfArc = w.arcDegrees * 0.5f;

            var found = new List<int>();
            var dist = new List<float>();
            foreach (int id in world.PlayersInSphere(cmd.origin, reach + Rules.meleeReachUp, tick))
            {
                if (id == cmd.playerId || !combat.IsAlive(id) || IsTeammate(cmd.playerId, id)) continue;
                if (!world.History.PoseAt(id, tick, out var pose)) continue;
                Vec3 chest = Hitboxes.Chest(Rules, pose);
                Vec3 to = chest - cmd.origin;
                if (Math.Abs(to.y) > Rules.meleeReachUp) continue;
                Vec3 flat = new Vec3(to.x, 0f, to.z);
                float d = flat.Magnitude;
                // The body's own width counts: a chest just past the reach still takes the edge of the blade.
                if (d > reach + Rules.bodyRadius) continue;
                if (d > 1e-3f && halfArc < 180f)
                {
                    float cos = Vec3.Dot(flat / d, level);
                    double angle = Math.Acos(Math.Max(-1f, Math.Min(1f, cos))) * 180.0 / Math.PI;
                    if (angle > halfArc) continue;
                }
                if (!world.HasLineOfSightFor(cmd.playerId, cmd.origin, id, tick)) continue;
                int k = 0;
                while (k < dist.Count && (dist[k] < d || (dist[k] == d && found[k] < id))) k++;
                found.Insert(k, id);
                dist.Insert(k, d);
            }
            if (w.maxTargets > 0 && found.Count > w.maxTargets) found.RemoveRange(w.maxTargets, found.Count - w.maxTargets);

            float scale = WeaponDamage(cmd.playerId);
            var objects = new List<int>();
            var objectAmounts = new List<float>();
            if (world.RaycastFrom(cmd.playerId, cmd.origin, level, reach, tick, out var hit, PassThrough(cmd.playerId)) && hit.kind == HitKind.Device)
                Add(objects, objectAmounts, hit.deviceId, w.damage * scale);

            Publish(new MeleeSwungEvent
            {
                playerId = cmd.playerId, weaponId = w.id, origin = cmd.origin, direction = level,
                reach = reach, arcDegrees = w.arcDegrees, hitIds = found.ToArray(),
            });
            foreach (int id in found)
            {
                Vec3 away = level;
                if (world.History.PoseAt(id, tick, out var at)) away = at.position - cmd.origin;
                AfterHit(cmd.playerId, id, combat.Damage(cmd.playerId, id, w.damage * scale, w.id), away);
            }
            HurtObjects(cmd.playerId, objects, objectAmounts);
        }

        /// <summary>A weapon's heal on a teammate, with the cleansing that the weaponCleanse stat adds.</summary>
        void HealTeammate(int shooterId, int targetId, float amount, string weaponId)
        {
            combat.Heal(shooterId, targetId, amount, weaponId);
            if (Cleanse != null && combat.Stat(shooterId, StatIds.WeaponCleanse) > 0f) Cleanse(targetId);
        }

        /// <summary>
        /// Where a healing round goes after the teammate it hit: a splash on teammates within
        /// <paramref name="splash"/> metres of them (Ruleset.healSplashShare), then, for each point of the
        /// shooter's healRicochet stat, a jump to the nearest hurt teammate within Ruleset.healRicochetRange
        /// that the round can reach (Ruleset.healRicochetShare). Nobody is healed twice by one round, and
        /// the shooter never heals themselves.
        /// </summary>
        void SpreadHeal(int shooterId, int hitId, float amount, float splash, string weaponId, int tick)
        {
            var done = new List<int> { shooterId, hitId };
            if (!world.History.PoseAt(hitId, tick, out var hitPose)) return;
            Vec3 center = Hitboxes.Chest(Rules, hitPose);
            if (splash > 0f && Rules.healSplashShare > 0f)
            {
                foreach (int id in world.PlayersInSphere(center, splash, tick))
                {
                    if (done.Contains(id) || !IsTeammate(shooterId, id) || !combat.IsAlive(id)) continue;
                    done.Add(id);
                    HealTeammate(shooterId, id, amount * Rules.healSplashShare, weaponId);
                }
            }

            int jumps = (int)Math.Round(Math.Max(0f, combat.Stat(shooterId, StatIds.HealRicochet)));
            int from = hitId;
            Vec3 at = center;
            for (int j = 0; j < jumps && Rules.healRicochetShare > 0f; j++)
            {
                int next = -1;
                float best = float.MaxValue;
                Vec3 nextAt = Vec3.Zero;
                foreach (int id in world.PlayersInSphere(at, Rules.healRicochetRange, tick))
                {
                    if (done.Contains(id) || !IsTeammate(shooterId, id) || !combat.IsAlive(id)) continue;
                    var u = combat.Unit(id);
                    if (u == null || u.health >= combat.MaxHealth(id) - 1e-3f) continue;
                    if (!world.History.PoseAt(id, tick, out var pose)) continue;
                    Vec3 chest = Hitboxes.Chest(Rules, pose);
                    float d = Vec3.Distance(chest, at);
                    if (d >= best || !world.HasLineOfSightFor(shooterId, at, id, tick)) continue;
                    next = id;
                    best = d;
                    nextAt = chest;
                }
                if (next < 0) break;
                done.Add(next);
                Publish(new HealRoundBouncedEvent { shooterId = shooterId, fromId = from, toId = next, from = at, to = nextAt });
                HealTeammate(shooterId, next, amount * Rules.healRicochetShare, weaponId);
                from = next;
                at = nextAt;
            }
        }

        static void Add(List<int> ids, List<float> amounts, int id, float amount)
        {
            int k = ids.IndexOf(id);
            if (k < 0) { ids.Add(id); amounts.Add(amount); }
            else amounts[k] += amount;
        }

        /// <summary>Someone else on the shooter's team: a player or a unit (neutral units are nobody's).</summary>
        bool IsTeammate(int shooterId, int targetId) => shooterId != targetId && combat.SameTeam(shooterId, targetId);

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
            if (State.Player(shooterId) == null) return null;
            return id => id != shooterId && combat.SameTeam(shooterId, id);
        }

        public CommandResult Reload(int playerId)
        {
            string why = CanAct(playerId);
            if (why != null) return CommandResult.Reject(why);
            var s = Weapons(playerId);
            var w = Current(s);
            if (w == null) return CommandResult.Reject("no weapon in hand");
            if (w.NoAmmo()) return CommandResult.Reject("nothing to reload");
            if (s.Reloading) return CommandResult.Accepted;
            if (s.CurrentAmmo >= Magazine(playerId, w)) return CommandResult.Reject("already full");
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

        /// <summary>Fill the magazine of the weapon in hand at once, cancelling any reload (an ability's "reload" effect).</summary>
        public void RefillCurrent(int playerId)
        {
            var s = Weapons(playerId);
            var w = s == null ? null : Current(s);
            if (w == null || s.current >= s.ammo.Length) return;
            s.reloadLeft = 0f;
            s.ammo[s.current] = Magazine(playerId, w);
            s.healRounds = 0;
        }

        /// <summary>
        /// Fill the magazine of the weapon in hand with healing rounds at once (an ability's "healRounds"
        /// effect): each heals a teammate it hits by <paramref name="perRound"/> (times the weaponDamage stat),
        /// and splashes <paramref name="splash"/> metres around them. They don't hurt enemies.
        /// </summary>
        public void LoadHealRounds(int playerId, float perRound, float splash)
        {
            var s = Weapons(playerId);
            if (s == null || perRound <= 0f || !State.IsAlive(playerId)) return;
            RefillCurrent(playerId);
            if (Current(s) == null) return;
            s.healRounds = s.CurrentAmmo;
            s.healPerRound = perRound;
            s.healSplash = Math.Max(0f, splash);
            Publish(new HealRoundsLoadedEvent { playerId = playerId, weaponId = s.CurrentId, rounds = s.healRounds });
        }

        /// <summary>Rounds in a full magazine for this player: the weapon's, times their magazineSize stat (at least 1).</summary>
        public int Magazine(int playerId, WeaponDef w) =>
            w == null ? 0 : Math.Max(1, (int)Math.Round(w.magazine * Math.Max(0f, combat.Stat(playerId, StatIds.MagazineSize))));

        /// <summary>
        /// What a weapon hit does beyond its damage, from the shooter's stats: life steal heals them by a
        /// share of what the target lost, weaponPush shoves the target along <paramref name="direction"/>
        /// (only within weaponPushRange metres, when that is set)
        /// and weaponSlow slows them. Only hits on someone else that did damage count.
        /// </summary>
        void AfterHit(int shooterId, int targetId, float dealt, Vec3 direction)
        {
            if (dealt <= 0f || shooterId == targetId || !State.IsAlive(shooterId)) return;
            float steal = combat.Stat(shooterId, StatIds.LifeSteal);
            if (steal > 0f) combat.Heal(shooterId, shooterId, dealt * steal, StatIds.LifeSteal);
            if (!combat.IsAlive(targetId)) return;
            float push = combat.Stat(shooterId, StatIds.WeaponPush);
            float pushRange = combat.Stat(shooterId, StatIds.WeaponPushRange);
            if (push > 0f && pushRange > 0f && !Within(shooterId, targetId, pushRange)) push = 0f; // a point-blank push only
            if (push > 0f && direction.Magnitude > 1e-4f)
            {
                Vec3 flat = new Vec3(direction.x, 0f, direction.z);
                if (flat.Magnitude > 1e-4f)
                    Publish(new PushedEvent
                    {
                        targetId = targetId, sourceId = shooterId, mode = PushDirections.Aim, origin = Vec3.Zero,
                        direction = flat.Normalized, distance = push, seconds = Rules.weaponPushSeconds, abilityId = StatIds.WeaponPush,
                    });
            }
            float slow = combat.Stat(shooterId, StatIds.WeaponSlow);
            if (slow > 0f && Slow != null) Slow(targetId, shooterId, Math.Min(1f, slow), Rules.weaponSlowSeconds);
        }

        /// <summary>Whether the target stands within <paramref name="metres"/> of the shooter right now (unknown positions: no).</summary>
        bool Within(int shooterId, int targetId, float metres) =>
            world.TryGetPosition(shooterId, State.tick, out var from) && world.TryGetPosition(targetId, State.tick, out var to)
            && Vec3.Distance(from, to) <= metres;

        /// <summary>
        /// The weapon's multiplier for the body part hit, with the shooter's headDamage stat on head hits: their
        /// head hits deal that share more (0.25 turns a 1.75x head hit into 1.75 x 1.25 = 2.19x).
        /// </summary>
        float PartMultiplier(int shooterId, WeaponDef w, int bodyPart)
        {
            float m = w.PartMultiplier(bodyPart);
            if (bodyPart == BodyParts.Head) m *= 1f + Math.Max(0f, combat.Stat(shooterId, StatIds.HeadDamage));
            return m;
        }

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
            s.healRounds = 0; // and drops its healing rounds
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
                    if (s.reloadLeft <= 0f) { s.reloadLeft = 0f; s.ammo[s.current] = Magazine(s.playerId, w); s.healRounds = 0; }
                }
                // Keep the full sizes current (a trait can change them mid-life); rounds in hand stay until a reload.
                for (int i = 0; i < s.weapons.Length && i < s.magazines.Length; i++)
                    s.magazines[i] = Magazine(s.playerId, Library.Weapon(s.weapons[i]));
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
                    Explode(p, hit.point, hit.kind == HitKind.Player ? hit.playerId : -1, hit.kind == HitKind.Device ? hit.deviceId : -1,
                        hit.kind == HitKind.Player ? hit.bodyPart : BodyParts.None);
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

        void Explode(ProjectileState p, Vec3 point, int direct, int directObject, int bodyPart = BodyParts.None)
        {
            var w = Library.Weapon(p.weaponId);
            // The shooter's splashRadius stat widens the blast; damage still falls off from centre to edge.
            float widen = combat.IsKnown(p.ownerId) ? Math.Max(0f, combat.Stat(p.ownerId, StatIds.SplashRadius)) : 1f;
            float splashRadius = w == null ? 0f : w.splashRadius * widen;
            Publish(new ProjectileExplodedEvent
            {
                projectileId = p.id, ownerId = p.ownerId, weaponId = p.weaponId, point = point, directHit = direct,
                splashRadius = splashRadius,
            });
            if (w == null) return;
            float scale = WeaponDamage(p.ownerId);
            var targets = new List<int>();
            var amounts = new List<float>();
            // A direct hit counts where it struck (head or legs), as a bullet does.
            if (direct >= 0 && w.damage > 0f) { targets.Add(direct); amounts.Add(w.damage * scale * PartMultiplier(p.ownerId, w, bodyPart)); }
            if (directObject >= 0)
                HurtObjects(p.ownerId, new List<int> { directObject }, new List<float> { (w.damage + w.splashDamage) * scale });
            if (splashRadius > 0f && w.splashDamage > 0f)
            {
                foreach (int id in world.PlayersInSphere(point, splashRadius, State.tick))
                {
                    if (!world.History.PoseAt(id, State.tick, out var pose)) continue;
                    float dist = Vec3.Distance(Hitboxes.Chest(Rules, pose), point);
                    float amount = w.SplashAt(widen > 0f ? dist / widen : dist) * scale;
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
            for (int i = 0; i < targets.Count; i++)
            {
                Vec3 away = p.velocity;
                if (targets[i] != direct && world.History.PoseAt(targets[i], State.tick, out var at))
                    away = Hitboxes.Chest(Rules, at) - point;
                AfterHit(p.ownerId, targets[i], combat.Damage(p.ownerId, targets[i], amounts[i], p.weaponId), away);
            }
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

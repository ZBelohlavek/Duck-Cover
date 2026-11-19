using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Draws the fight on this machine from what the server sends: a body for every other player
    /// (moved smoothly between state copies), tracers, projectiles and explosions. It also owns the
    /// <see cref="LocalPlayer"/>. Nothing here decides anything; it only shows state and events.
    /// </summary>
    public class CombatView : MonoBehaviour
    {
        public static CombatView Instance { get; private set; }

        sealed class Body
        {
            public PlaceholderBody view;
            public TeamId team;
            public Vector3 from, to;
            public float yawFrom, yawTo;
            public bool crouched;
            public bool hasPose;
            public PlayerMotion motion;
        }

        sealed class Flying
        {
            public GameObject go;
            public Vector3 velocity;
            public float gravity;
        }

        static readonly Color TracerColor = new Color(1f, 0.92f, 0.55f);
        static readonly Color BlastColor = new Color(1f, 0.55f, 0.15f);

        readonly Dictionary<int, Body> bodies = new Dictionary<int, Body>();
        readonly Dictionary<int, Flying> projectiles = new Dictionary<int, Flying>();
        readonly List<int> scratch = new List<int>();
        SessionClient client;
        LocalPlayer local;
        Transform effects;

        int fromTick, toTick;
        float arrivedAt, interval = 0.1f;

        /// <summary>The server tick the bodies on screen are drawn at (between the last two state copies).</summary>
        public int RenderTick => toTick <= 0 ? 0 : Mathf.RoundToInt(Mathf.Lerp(fromTick, toTick, Blend));

        float Blend => Mathf.Clamp01((Time.unscaledTime - arrivedAt) / Mathf.Max(0.01f, interval));

        public LocalPlayer Local => local;

        /// <summary>
        /// Where a player's feet are drawn right now: the local player's own body, or another player's
        /// body between state copies. False if they aren't shown.
        /// </summary>
        public bool DrawnFeet(int playerId, out Vector3 feet)
        {
            feet = Vector3.zero;
            if (client != null && playerId == client.PlayerId && local != null && local.Active)
            {
                feet = local.transform.position;
                return true;
            }
            if (!bodies.TryGetValue(playerId, out var b) || b.view == null || !b.view.gameObject.activeSelf) return false;
            feet = Vector3.Lerp(b.from, b.to, Blend);
            return true;
        }

        /// <summary>The body drawn for another player, or null.</summary>
        public PlaceholderBody BodyOf(int playerId) => bodies.TryGetValue(playerId, out var b) ? b.view : null;

        void Awake()
        {
            Instance = this;
            effects = new GameObject("Effects").transform;
            effects.SetParent(transform, false);
            local = LocalPlayer.Create(this);
            local.transform.SetParent(transform, false);
            gameObject.AddComponent<AbilityView>(); // what abilities do (WP11)
            gameObject.AddComponent<FlagView>(); // the Flag Rush flag (WP21)
            gameObject.AddComponent<UnitView>(); // minions, camp guards and Bastion bars (WP19)
        }

        void OnDestroy()
        {
            Bind(null);
            if (Instance == this) Instance = null;
        }

        void Bind(SessionClient next)
        {
            if (client == next) return;
            if (client != null)
            {
                client.Events.Unsubscribe(OnEvent);
                client.SnapshotReceived -= OnSnapshot;
            }
            client = next;
            foreach (var b in bodies.Values) if (b.view != null) Destroy(b.view.gameObject);
            bodies.Clear();
            foreach (var p in projectiles.Values) if (p.go != null) Destroy(p.go);
            projectiles.Clear();
            fromTick = toTick = 0;
            if (client != null)
            {
                client.Events.Subscribe(OnEvent);
                client.SnapshotReceived += OnSnapshot;
            }
            if (local != null) local.Bind(client);
            if (client == null) ResetCamera();
        }

        static void ResetCamera()
        {
            var cam = Camera.main;
            if (cam != null) cam.transform.SetPositionAndRotation(MapScene.OverviewPosition, MapScene.OverviewRotation);
        }

        void Update()
        {
            var runner = SessionRunner.Instance;
            Bind(runner == null ? null : runner.Client);
            if (client == null || client.Snapshot == null || runner.ClientRules == null) return;

            var snapshot = client.Snapshot;
            var cam = Camera.main;
            float k = Blend;
            foreach (var pair in bodies)
            {
                var b = pair.Value;
                var p = snapshot.match.Player(pair.Key);
                bool show = b.hasPose && p != null && p.alive && p.connected;
                var unit = snapshot.abilities == null ? null : snapshot.abilities.Unit(pair.Key);
                Vector3 feet = Vector3.Lerp(b.from, b.to, k);
                if (show && unit != null && unit.stealthed && Hidden(snapshot, p.team, feet, runner.ClientRules)) show = false;
                if (b.view.gameObject.activeSelf != show) b.view.gameObject.SetActive(show);
                if (!show) continue;
                b.view.Pose(feet, Mathf.LerpAngle(b.yawFrom, b.yawTo, k), b.crouched);
                if (b.motion != null)
                {
                    var weapons = snapshot.Weapons(pair.Key);
                    b.view.Drive(b.motion.velocity.ToVector3(), b.motion.pitch, b.motion.grounded, b.motion.sprinting, b.motion.vaulting, weapons != null && weapons.Reloading);
                }
                float max = unit == null ? 1f : Mathf.Max(1f, unit.stats.Get(StatIds.MaxHealth));
                b.view.ShowBars(unit == null ? 1f : unit.health / max, unit == null ? 0f : unit.Shield / Mathf.Max(1f, runner.ClientRules.maxShield), cam);
            }

            float dt = Time.deltaTime;
            foreach (var f in projectiles.Values)
            {
                if (f.go == null) continue;
                Vector3 v0 = f.velocity;
                f.velocity = v0 + Vector3.down * (f.gravity * dt);
                f.go.transform.position += (v0 + f.velocity) * (0.5f * dt);
            }

            // Out of the fight (waiting to respawn, or not deployed yet): watch from above.
            if (local != null && !local.Active) ResetCamera();
        }

        /// <summary>
        /// A cloaked player is drawn for their own team, and for an enemy only within the shimmer range of
        /// this machine's player (so a cloak can't stand on top of someone unseen).
        /// </summary>
        bool Hidden(MatchSnapshot snapshot, TeamId team, Vector3 feet, Ruleset rules)
        {
            var me = snapshot.match.Player(client.PlayerId);
            if (me == null || me.team == team) return false;
            if (local == null || !local.Active) return true;
            return (feet - local.transform.position).sqrMagnitude > rules.stealthShimmerRange * rules.stealthShimmerRange;
        }

        void OnSnapshot()
        {
            var snapshot = client.Snapshot;
            var runner = SessionRunner.Instance;
            if (snapshot == null || runner == null || runner.ClientRules == null) return;

            float now = Time.unscaledTime;
            if (toTick > 0 && snapshot.tick > toTick) interval = Mathf.Clamp(now - arrivedAt, 0.02f, 0.3f);
            fromTick = toTick > 0 ? toTick : snapshot.tick;
            toTick = snapshot.tick;
            arrivedAt = now;

            scratch.Clear();
            scratch.AddRange(bodies.Keys);
            foreach (var m in snapshot.motion)
            {
                if (m.playerId == client.PlayerId) continue;
                var p = snapshot.match.Player(m.playerId);
                if (p == null) continue;
                scratch.Remove(m.playerId);
                if (!bodies.TryGetValue(m.playerId, out var b) || b.view == null || b.team != p.team || b.view.HeroId != (p.heroId ?? ""))
                {
                    if (b != null && b.view != null) Destroy(b.view.gameObject);
                    b = new Body { team = p.team };
                    b.view = PlaceholderBody.Create("Player " + m.playerId, p.team, runner.ClientRules, snapshot.IsDummy(m.playerId), p.heroId);
                    b.view.transform.SetParent(transform, false);
                    bodies[m.playerId] = b;
                }
                Vector3 at = m.position.ToVector3();
                // Carry on from where the body is drawn now; a jump too big to be a step is a teleport.
                bool snap = !b.hasPose || (at - b.to).sqrMagnitude > 36f;
                b.from = snap ? at : b.to;
                b.yawFrom = snap ? m.yaw : b.yawTo;
                b.to = at;
                b.yawTo = m.yaw;
                b.crouched = m.crouched;
                b.motion = m;
                b.hasPose = true;
            }
            foreach (int gone in scratch)
            {
                if (bodies[gone].view != null) Destroy(bodies[gone].view.gameObject);
                bodies.Remove(gone);
            }
        }

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case ShotFiredEvent shot:
                    if (shot.playerId == client.PlayerId) break; // drawn already, the moment it was fired
                    Vector3 from = shot.origin.ToVector3() + Vector3.down * 0.15f;
                    if (bodies.TryGetValue(shot.playerId, out var shooter) && shooter.view != null && shooter.view.gameObject.activeSelf)
                    {
                        if (shooter.view.Model != null) shooter.view.Model.Fired();
                        from = shooter.view.MuzzleOr(from);
                    }
                    foreach (var end in shot.ends) Tracer(from, end.ToVector3());
                    break;
                case AbilityUsedEvent used:
                    if (bodies.TryGetValue(used.playerId, out var caster) && caster.view != null && caster.view.Model != null) caster.view.Model.UsedAbility(used.abilityId);
                    break;
                case MeleeSwungEvent swung:
                    if (swung.playerId == client.PlayerId) break; // drawn already, the moment it was swung
                    DrawSwing(swung.playerId, swung.reach, swung.arcDegrees, swung.origin, swung.direction);
                    break;
                case ProjectileLaunchedEvent launched:
                {
                    var go = Paint.Shape(PrimitiveType.Sphere, effects, "Projectile " + launched.projectileId, launched.origin.ToVector3(), Vector3.one * 0.22f, BlastColor, false);
                    projectiles[launched.projectileId] = new Flying { go = go, velocity = launched.velocity.ToVector3(), gravity = launched.gravity };
                    break;
                }
                case ProjectileExplodedEvent boom:
                {
                    if (projectiles.TryGetValue(boom.projectileId, out var f))
                    {
                        if (f.go != null) Destroy(f.go);
                        projectiles.Remove(boom.projectileId);
                    }
                    float size = Mathf.Max(0.6f, boom.splashRadius * 2f);
                    var blast = Paint.Shape(PrimitiveType.Sphere, effects, "Blast", boom.point.ToVector3(), Vector3.one * size, BlastColor, false);
                    Destroy(blast, 0.12f);
                    break;
                }
            }
        }

        /// <summary>
        /// The local player's own shot, drawn the moment the trigger is pulled: each pellet goes to
        /// the first wall or the first body (as drawn right now) it would reach. The server's answer
        /// decides the damage.
        /// </summary>
        public void DrawOwnShot(WeaponDef def, Vec3 origin, Vec3 direction, Vector3 muzzle)
        {
            var runner = SessionRunner.Instance;
            var rules = runner == null ? null : runner.ClientRules;
            if (rules == null) return;
            float k = Blend;
            for (int i = 0; i < def.pellets; i++)
            {
                Vec3 d = SpreadPattern.Direction(direction, i, def.pellets, def.spreadDegrees);
                float reach = def.range;
                if (PhysicsWorld.Cast(origin, d, reach, out var wall)) reach = wall.distance;
                foreach (var pair in bodies)
                {
                    var b = pair.Value;
                    if (b.view == null || !b.view.gameObject.activeSelf) continue;
                    if (!rules.friendlyFire && b.team == client.Team) continue;
                    var pose = new BodyPose(Vector3.Lerp(b.from, b.to, k).ToVec3(), b.crouched);
                    if (Hitboxes.Raycast(rules, pose, origin, d, reach, out float hit, out _)) reach = hit;
                }
                if (UnitView.Instance != null && UnitView.Instance.Raycast(rules, client.Team, origin, d, reach, out float unitHit)) reach = unitHit;
                Tracer(muzzle, (origin + d * reach).ToVector3());
            }
        }

        /// <summary>
        /// A melee swing: a short-lived fan of blade streaks across the arc, at chest height in front of the
        /// swinger, so other players see where the axe went. Placeholder until swing animations.
        /// </summary>
        public void DrawSwing(int playerId, float reach, float arcDegrees, Vec3 origin, Vec3 direction)
        {
            Vector3 eye = origin.ToVector3() + Vector3.down * 0.35f;
            Vector3 level = direction.ToVector3();
            level.y = 0f;
            if (level.sqrMagnitude < 1e-6f) return;
            level = level.normalized;
            const int Streaks = 7;
            float half = Mathf.Min(arcDegrees, 160f) * 0.5f;
            Vector3 last = Vector3.zero;
            for (int i = 0; i < Streaks; i++)
            {
                float a = Mathf.Lerp(-half, half, i / (float)(Streaks - 1));
                Vector3 tip = eye + Quaternion.Euler(0f, a, 0f) * level * Mathf.Max(0.5f, reach) + Vector3.down * (0.3f * i / (Streaks - 1));
                if (i > 0) Streak(last, tip, 0.12f + 0.02f * i);
                last = tip;
            }
        }

        void Streak(Vector3 from, Vector3 to, float life)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 0.05f) return;
            var go = Paint.Block(effects, "Swing", (from + to) * 0.5f, new Vector3(0.06f, 0.06f, length), TracerColor, false);
            go.transform.rotation = Quaternion.LookRotation(along);
            Destroy(go, life);
        }

        void Tracer(Vector3 from, Vector3 to)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 0.05f) return;
            var go = Paint.Block(effects, "Tracer", (from + to) * 0.5f, new Vector3(0.025f, 0.025f, length), TracerColor, false);
            go.transform.rotation = Quaternion.LookRotation(along);
            Destroy(go, 0.07f);
            var spark = Paint.Block(effects, "Impact", to, Vector3.one * 0.09f, TracerColor, false);
            Destroy(spark, 0.12f);
        }
    }
}

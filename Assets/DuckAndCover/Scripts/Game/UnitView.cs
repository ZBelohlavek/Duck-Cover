using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Draws the units the server sends (WP19): lane minions in their team's colour, neutral camp guards,
    /// each moved smoothly between state copies with a health and shield bar over it, and a puff where
    /// one falls. Bastions are units too, but their pillars belong to the map (<see cref="MapView"/>):
    /// here they only get a bar. Placeholder shapes sized from the unit kind (a Juggernaut stands half
    /// again as tall as a hero, a Scrapper well under). Nothing here decides anything.
    /// </summary>
    public class UnitView : MonoBehaviour
    {
        public static UnitView Instance { get; private set; }

        public static readonly Color Wild = new Color(0.62f, 0.66f, 0.32f);
        static readonly Color Dark = new Color(0.08f, 0.09f, 0.11f);
        static readonly Color Glow = new Color(0.55f, 0.95f, 1.00f);
        static readonly Color BarBack = new Color(0.05f, 0.05f, 0.06f);
        static readonly Color HealthColor = new Color(0.35f, 0.90f, 0.40f);
        static readonly Color ShieldColor = new Color(0.45f, 0.85f, 1.00f);
        static readonly Color PuffColor = new Color(0.30f, 0.30f, 0.32f);

        sealed class Body
        {
            public GameObject root;
            public Transform figure, bar, healthFill, shieldFill;
            public float barWidth;
            public TeamId team;
            public string typeId;
            public float size, height;
            public bool bastion;
            public bool hasPose;
            public Vector3 from, to;
            public float yawFrom, yawTo;
        }

        readonly Dictionary<int, Body> bodies = new Dictionary<int, Body>();
        readonly List<int> scratch = new List<int>();
        SessionClient client;
        float arrivedAt, interval = 0.1f;
        int lastTick;

        float Blend => Mathf.Clamp01((Time.unscaledTime - arrivedAt) / Mathf.Max(0.01f, interval));

        /// <summary>Units on screen now.</summary>
        public int Count => bodies.Count;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            Bind(null);
            if (Instance == this) Instance = null;
        }

        void Bind(SessionClient next)
        {
            if (client == next) return;
            if (client != null) client.SnapshotReceived -= OnSnapshot;
            client = next;
            foreach (var b in bodies.Values) if (b.root != null) Destroy(b.root);
            bodies.Clear();
            lastTick = 0;
            if (client != null) client.SnapshotReceived += OnSnapshot;
        }

        void Update()
        {
            var runner = SessionRunner.Instance;
            Bind(runner == null ? null : runner.Client);
            if (bodies.Count == 0) return;
            var cam = Camera.main;
            float k = Blend;
            foreach (var b in bodies.Values)
            {
                if (b.root == null) continue;
                b.root.transform.position = Vector3.Lerp(b.from, b.to, k);
                if (b.figure != null) b.figure.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(b.yawFrom, b.yawTo, k), 0f);
                if (cam != null && b.bar != null) b.bar.rotation = Quaternion.LookRotation(b.bar.position - cam.transform.position);
            }
        }

        void OnSnapshot()
        {
            var snapshot = client.Snapshot;
            var runner = SessionRunner.Instance;
            if (snapshot == null || runner == null || runner.ClientRules == null) return;

            float now = Time.unscaledTime;
            if (lastTick > 0 && snapshot.tick > lastTick) interval = Mathf.Clamp(now - arrivedAt, 0.02f, 0.3f);
            lastTick = snapshot.tick;
            arrivedAt = now;

            var rules = runner.ClientRules;
            scratch.Clear();
            scratch.AddRange(bodies.Keys);
            if (snapshot.units != null)
                foreach (var u in snapshot.units)
                {
                    scratch.Remove(u.id);
                    if (!bodies.TryGetValue(u.id, out var b) || b.team != u.team || b.typeId != u.typeId)
                    {
                        if (b != null && b.root != null) Destroy(b.root);
                        bool bastion = snapshot.overrun != null && snapshot.overrun.ByUnit(u.id) != null;
                        b = Create(u, rules, bastion);
                        bodies[u.id] = b;
                    }
                    Vector3 at = u.position.ToVector3();
                    // Carry on from where the body is drawn now; a jump too big to be a step is a teleport.
                    bool snap = !b.hasPose || (at - b.to).sqrMagnitude > 36f;
                    b.from = snap ? at : b.root.transform.position;
                    b.hasPose = true;
                    b.yawFrom = snap ? u.yaw : b.yawTo;
                    b.to = at;
                    b.yawTo = u.yaw;
                    float max = Mathf.Max(1f, u.maxHealth);
                    Fill(b.healthFill, b.barWidth, u.health / max);
                    // A Bastion's shield is out of its own maximum; a minion's out of a hero's personal shield.
                    var bs = snapshot.overrun == null ? null : snapshot.overrun.ByUnit(u.id);
                    float shieldMax = bs != null ? Mathf.Max(1f, bs.maxShield) : Mathf.Max(1f, rules.maxShield);
                    Fill(b.shieldFill, b.barWidth, u.shield / shieldMax);
                }
            foreach (int gone in scratch)
            {
                var b = bodies[gone];
                if (b.root != null)
                {
                    if (!b.bastion) Puff(b.root.transform.position + Vector3.up * b.height * 0.5f, b.size);
                    Destroy(b.root);
                }
                bodies.Remove(gone);
            }
        }

        void Puff(Vector3 at, float size)
        {
            var puff = Paint.Shape(PrimitiveType.Sphere, transform, "UnitPuff", at, Vector3.one * Mathf.Max(0.8f, 1.4f * size), PuffColor, false);
            Destroy(puff, 0.18f);
        }

        /// <summary>
        /// Where the local player's shot would first meet a unit's hitboxes (as drawn now), for drawing
        /// tracers. Units of the player's own team are skipped unless friendly fire is on.
        /// </summary>
        public bool Raycast(Ruleset rules, TeamId shooterTeam, Vec3 origin, Vec3 direction, float reach, out float hit)
        {
            hit = reach;
            bool any = false;
            float k = Blend;
            foreach (var b in bodies.Values)
            {
                if (b.root == null) continue;
                if (!rules.friendlyFire && b.team == shooterTeam) continue;
                var pose = new BodyPose(Vector3.Lerp(b.from, b.to, k).ToVec3(), false, true, b.size);
                if (Hitboxes.Raycast(rules, pose, origin, direction, hit, out float d, out _) && d < hit)
                {
                    hit = d;
                    any = true;
                }
            }
            return any;
        }

        Body Create(UnitSnapshot u, Ruleset rules, bool bastion)
        {
            var kind = rules.UnitKind(u.typeId);
            float size = kind == null || kind.size <= 0f ? 1f : kind.size;
            var root = new GameObject((bastion ? "Bastion bar " : "Unit ") + u.id + " " + u.typeId);
            root.transform.SetParent(transform, false);
            var b = new Body
            {
                root = root, team = u.team, typeId = u.typeId, size = size, bastion = bastion,
                to = u.position.ToVector3(), yawTo = u.yaw,
            };
            Color main = u.team == TeamId.None ? Wild : PlaceholderBody.ColorFor(u.team);
            if (bastion)
            {
                // The map draws the pillar; the bar floats over it.
                b.height = MapScene.BastionHeight(TierOf(u.typeId)) + 2.2f;
                b.barWidth = 3.2f;
            }
            else
            {
                b.height = rules.standHeight * size;
                b.barWidth = 0.9f * Mathf.Max(0.8f, size);
                var figure = new GameObject("Figure").transform;
                figure.SetParent(root.transform, false);
                b.figure = figure;
                BuildFigure(figure, u.typeId, size, rules, main);
            }
            var bar = new GameObject("Bar").transform;
            bar.SetParent(root.transform, false);
            bar.localPosition = new Vector3(0f, b.height + 0.35f, 0f);
            float thick = bastion ? 2.4f : 1f;
            Paint.Block(bar, "Back", new Vector3(0f, 0f, 0.01f), new Vector3(b.barWidth + 0.06f, 0.2f * thick, 0.01f), BarBack, false);
            b.healthFill = Paint.Block(bar, "Health", new Vector3(0f, -0.035f * thick, 0f), new Vector3(b.barWidth, 0.09f * thick, 0.012f), HealthColor, false).transform;
            b.shieldFill = Paint.Block(bar, "Shield", new Vector3(0f, 0.05f * thick, 0f), new Vector3(b.barWidth, 0.05f * thick, 0.012f), ShieldColor, false).transform;
            b.bar = bar;
            SetLayer(root.transform, PhysicsWorld.PlayerLayer);
            root.transform.position = b.to;
            return b;
        }

        static BastionTier TierOf(string typeId) => typeId == OverrunRuleset.CoreBastion ? BastionTier.Core : BastionTier.Forward;

        /// <summary>
        /// Our own placeholder robots, facing +z: a Scrapper is a squat box on two legs with one eye, a
        /// Warden a taller capsule under a glowing ring (its shield pulse), a Juggernaut a broad block with
        /// heavy shoulders, and camp creatures horned boxes in the jungle's colour (the Experience camp's corrupted
        /// Scrappers keep the Scrapper shape).
        /// </summary>
        static void BuildFigure(Transform f, string typeId, float size, Ruleset r, Color main)
        {
            float h = r.standHeight * size;
            Color trim = Color.Lerp(main, Color.white, 0.4f);
            switch (typeId)
            {
                case LaneRuleset.Warden:
                    Paint.Shape(PrimitiveType.Capsule, f, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(h * 0.38f, h * 0.45f, h * 0.38f), main, false);
                    Paint.Block(f, "Eye", new Vector3(0f, h * 0.72f, h * 0.17f), new Vector3(h * 0.22f, h * 0.08f, h * 0.06f), Dark, false);
                    Paint.Shape(PrimitiveType.Cylinder, f, "Ring", new Vector3(0f, h * 1.05f, 0f), new Vector3(h * 0.5f, 0.02f, h * 0.5f), Glow, false);
                    break;
                case LaneRuleset.Juggernaut:
                    Paint.Block(f, "LegLeft", new Vector3(-h * 0.14f, h * 0.17f, 0f), new Vector3(h * 0.16f, h * 0.34f, h * 0.18f), trim, false);
                    Paint.Block(f, "LegRight", new Vector3(h * 0.14f, h * 0.17f, 0f), new Vector3(h * 0.16f, h * 0.34f, h * 0.18f), trim, false);
                    Paint.Block(f, "Torso", new Vector3(0f, h * 0.6f, 0f), new Vector3(h * 0.55f, h * 0.5f, h * 0.36f), main, false);
                    Paint.Block(f, "ShoulderLeft", new Vector3(-h * 0.36f, h * 0.8f, 0f), new Vector3(h * 0.2f, h * 0.18f, h * 0.3f), trim, false);
                    Paint.Block(f, "ShoulderRight", new Vector3(h * 0.36f, h * 0.8f, 0f), new Vector3(h * 0.2f, h * 0.18f, h * 0.3f), trim, false);
                    Paint.Block(f, "Visor", new Vector3(0f, h * 0.76f, h * 0.18f), new Vector3(h * 0.3f, h * 0.06f, h * 0.03f), Glow, false);
                    break;
                case LaneRuleset.CampGuard:
                case LaneRuleset.CampBrute:
                case LaneRuleset.CampRunner:
                case LaneRuleset.CampBoss:
                    Paint.Block(f, "Body", new Vector3(0f, h * 0.45f, 0f), new Vector3(h * 0.45f, h * 0.9f, h * 0.35f), main, false);
                    Paint.Block(f, "HornLeft", new Vector3(-h * 0.17f, h * 0.97f, 0f), new Vector3(h * 0.07f, h * 0.18f, h * 0.07f), Dark, false);
                    Paint.Block(f, "HornRight", new Vector3(h * 0.17f, h * 0.97f, 0f), new Vector3(h * 0.07f, h * 0.18f, h * 0.07f), Dark, false);
                    Paint.Block(f, "Eye", new Vector3(0f, h * 0.75f, h * 0.18f), new Vector3(h * 0.25f, h * 0.06f, h * 0.02f), new Color(1f, 0.85f, 0.3f), false);
                    break;
                default: // Scrapper, and any kind not drawn yet
                    Paint.Block(f, "LegLeft", new Vector3(-h * 0.12f, h * 0.18f, 0f), new Vector3(h * 0.08f, h * 0.36f, h * 0.1f), Dark, false);
                    Paint.Block(f, "LegRight", new Vector3(h * 0.12f, h * 0.18f, 0f), new Vector3(h * 0.08f, h * 0.36f, h * 0.1f), Dark, false);
                    Paint.Block(f, "Body", new Vector3(0f, h * 0.6f, 0f), new Vector3(h * 0.42f, h * 0.42f, h * 0.36f), main, false);
                    Paint.Shape(PrimitiveType.Sphere, f, "Eye", new Vector3(0f, h * 0.66f, h * 0.18f), Vector3.one * (h * 0.13f), Glow, false);
                    Paint.Block(f, "Antenna", new Vector3(h * 0.12f, h * 0.88f, -h * 0.05f), new Vector3(h * 0.03f, h * 0.16f, h * 0.03f), trim, false);
                    break;
            }
        }

        static void Fill(Transform fill, float width, float share)
        {
            if (fill == null) return;
            share = Mathf.Clamp01(share);
            var scale = fill.localScale;
            fill.localScale = new Vector3(Mathf.Max(0.0001f, width * share), scale.y, scale.z);
            var p = fill.localPosition;
            fill.localPosition = new Vector3(-width * (1f - share) * 0.5f, p.y, p.z);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }
    }
}

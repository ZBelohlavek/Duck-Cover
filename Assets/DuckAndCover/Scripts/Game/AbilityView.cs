using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Draws what abilities do, from what the server sends (WP11): the objects heroes place
    /// (barriers and gates as walls in the owner's team colour, fields as discs on the ground, mines,
    /// sentries, drones as balls; shields and drones that ride on a hero follow the body drawn here), a flash where an ability lands (a disc for areas, a beam for rays), wind-ups, and
    /// mines going off. Placeholder art; it decides nothing.
    ///
    /// Walls get a collider on the players' layer, so this machine's player bumps into them while the
    /// map's rays (<see cref="PhysicsWorld"/>) skip them: the rules keep their own copy for shots.
    /// </summary>
    public class AbilityView : MonoBehaviour
    {
        const float FlashSeconds = 0.35f;

        sealed class Placed
        {
            public GameObject go;
            public TeamId team;
            /// <summary>A wall's panel and top edge, rescaled as it opens out.</summary>
            public Transform wall, frame;
            public float drawnWidth;
        }

        readonly Dictionary<int, Placed> placed = new Dictionary<int, Placed>();
        readonly List<int> scratch = new List<int>();
        SessionClient client;
        Transform flashes;

        void Awake()
        {
            flashes = new GameObject("AbilityFlashes").transform;
            flashes.SetParent(transform, false);
        }

        void OnDestroy() => Bind(null);

        void Bind(SessionClient next)
        {
            if (client == next) return;
            if (client != null) client.Events.Unsubscribe(OnEvent);
            client = next;
            foreach (var p in placed.Values) if (p.go != null) Destroy(p.go);
            placed.Clear();
            if (client != null) client.Events.Subscribe(OnEvent);
        }

        void Update()
        {
            var runner = SessionRunner.Instance;
            Bind(runner == null ? null : runner.Client);
            var snapshot = client == null ? null : client.Snapshot;
            var state = snapshot == null ? null : snapshot.abilities;
            if (state == null) return;

            scratch.Clear();
            scratch.AddRange(placed.Keys);
            foreach (var o in state.objects)
            {
                scratch.Remove(o.id);
                if (!placed.TryGetValue(o.id, out var p))
                {
                    p = new Placed { team = o.team };
                    p.go = Build(o, p);
                    placed[o.id] = p;
                }
                Refresh(o, p);
            }
            foreach (int gone in scratch)
            {
                if (placed[gone].go != null) Destroy(placed[gone].go);
                placed.Remove(gone);
            }
        }

        // ---- Placed objects ----

        static Color TeamColor(TeamId team) => PlaceholderBody.ColorFor(team);

        /// <summary>
        /// Objects that move or grow: a shield or drone follows the body it rides on as drawn on this
        /// machine (the local player's own body, or another's between state copies), and a wall that
        /// opens out is rescaled to its width now.
        /// </summary>
        void Refresh(ObjectState o, Placed p)
        {
            if (p.go == null || o.def == null) return;
            var view = CombatView.Instance;
            if (o.followId >= 0 && view != null && view.DrawnFeet(o.followId, out Vector3 feet))
            {
                Vector3 facing = o.facing.ToVector3();
                if (client != null && o.followId == client.PlayerId && view.Local != null) facing = Quaternion.Euler(0f, view.Local.Yaw, 0f) * Vector3.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
                facing = facing.normalized;
                p.go.transform.position = feet + facing * o.def.followAhead + Vector3.up * o.def.followRise;
                p.go.transform.rotation = Quaternion.LookRotation(facing);
            }
            float width = o.Width;
            if (p.wall != null && Mathf.Abs(width - p.drawnWidth) > 1e-3f)
            {
                p.drawnWidth = width;
                var ws = p.wall.localScale;
                p.wall.localScale = new Vector3(width, ws.y, ws.z);
                if (p.frame != null)
                {
                    var fs = p.frame.localScale;
                    p.frame.localScale = new Vector3(width + 0.1f, fs.y, fs.z);
                }
            }
        }

        GameObject Build(ObjectState o, Placed p)
        {
            var root = new GameObject("Object " + o.id + " " + o.defId);
            root.transform.SetParent(transform, false);
            Color team = TeamColor(o.team);
            Vector3 at = o.position.ToVector3();
            var def = o.def ?? new ObjectDef();

            if (def.width > 0f && def.height > 0f)
            {
                Vector3 facing = o.facing.ToVector3();
                facing.y = 0f;
                if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
                root.transform.position = at;
                root.transform.rotation = Quaternion.LookRotation(facing.normalized);
                float width = o.Width;
                var wall = Paint.Block(root.transform, "Wall", new Vector3(0f, def.height * 0.5f, 0f),
                    new Vector3(width, def.height, ObjectDef.SolidDepth), Color.Lerp(team, Color.white, def.followOwner ? 0.5f : 0.25f), def.blocksMovement);
                wall.layer = PhysicsWorld.PlayerLayer;
                var frame = Paint.Block(root.transform, "Frame", new Vector3(0f, def.height, 0f), new Vector3(width + 0.1f, 0.12f, ObjectDef.SolidDepth + 0.1f), team, false);
                p.wall = wall.transform;
                p.frame = frame.transform;
                // Your own raised shield stands a metre in front of your eyes: draw only its top edge
                // so you can still see what you are blocking (a placeholder for a see-through shield).
                if (client != null && o.followId == client.PlayerId)
                {
                    var r = wall.GetComponent<Renderer>();
                    if (r != null) r.enabled = false;
                }
                p.drawnWidth = width;
                return root;
            }

            if (def.hitRadius > 0f)
            {
                // A drone: a ball with a ring around it, at its spot (not on the floor).
                root.transform.position = at;
                float d = def.hitRadius * 2f;
                Paint.Shape(PrimitiveType.Sphere, root.transform, "Body", Vector3.zero, Vector3.one * d, Color.Lerp(team, Color.white, 0.3f), false);
                Paint.Block(root.transform, "Ring", Vector3.zero, new Vector3(d * 1.6f, d * 0.12f, d * 1.6f), Color.Lerp(team, Color.black, 0.4f), false);
                Paint.Block(root.transform, "Eye", new Vector3(0f, 0f, def.hitRadius * 0.9f), Vector3.one * d * 0.25f, Color.white, false);
                return root;
            }

            root.transform.position = Ground(at);
            if (def.trigger == Triggers.Proximity)
            {
                Paint.Shape(PrimitiveType.Cylinder, root.transform, "Mine", new Vector3(0f, 0.05f, 0f), new Vector3(0.45f, 0.05f, 0.45f), team, false);
                Paint.Block(root.transform, "Light", new Vector3(0f, 0.12f, 0f), Vector3.one * 0.1f, Color.white, false);
            }
            else if (def.trigger == Triggers.Pulse && def.maxTargets == 1)
            {
                Paint.Block(root.transform, "Base", new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 0.5f), Color.Lerp(team, Color.black, 0.4f), false);
                Paint.Block(root.transform, "Head", new Vector3(0f, 0.55f, 0f), new Vector3(0.32f, 0.26f, 0.32f), team, false);
                Paint.Block(root.transform, "Barrel", new Vector3(0f, 0.57f, 0.25f), new Vector3(0.07f, 0.07f, 0.35f), Color.Lerp(team, Color.black, 0.6f), false);
            }
            else if (def.trigger == Triggers.Pulse)
            {
                float r = Mathf.Max(0.5f, o.radius);
                Paint.Shape(PrimitiveType.Cylinder, root.transform, "Field", new Vector3(0f, 0.02f, 0f), new Vector3(r * 2f, 0.01f, r * 2f), Color.Lerp(team, Color.white, 0.55f), false);
                Paint.Shape(PrimitiveType.Cylinder, root.transform, "Core", new Vector3(0f, 0.15f, 0f), new Vector3(0.3f, 0.15f, 0.3f), team, false);
            }
            else Paint.Block(root.transform, "Object", new Vector3(0f, 0.2f, 0f), Vector3.one * 0.4f, team, false);
            return root;
        }

        /// <summary>The map under a point (up to a few metres down), so things sit on the floor here even if the rules place them in the air.</summary>
        static Vector3 Ground(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 6f, PhysicsWorld.GeometryMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return at;
        }

        // ---- Flashes ----

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case AbilityUsedEvent used:
                    Landed(used);
                    break;
                case AbilityCastStartedEvent cast:
                {
                    Vector3 at = cast.aimOrigin.ToVector3() + cast.aimDirection.ToVector3() * 0.8f;
                    var glow = Paint.Shape(PrimitiveType.Sphere, flashes, "WindUp", at, Vector3.one * 0.35f, Color.Lerp(TeamColorOf(cast.playerId), Color.white, 0.5f), false);
                    Destroy(glow, Mathf.Max(0.1f, cast.castTime));
                    break;
                }
                case HealRoundBouncedEvent bounce:
                {
                    // A healing round jumping between teammates: a short green streak.
                    Vector3 from = bounce.from.ToVector3(), to = bounce.to.ToVector3();
                    Vector3 along = to - from;
                    if (along.magnitude < 0.05f) break;
                    var streak = Paint.Block(flashes, "Ricochet", (from + to) * 0.5f, new Vector3(0.05f, 0.05f, along.magnitude), new Color(0.40f, 0.95f, 0.55f), false);
                    streak.transform.rotation = Quaternion.LookRotation(along);
                    Destroy(streak, FlashSeconds * 0.6f);
                    break;
                }
                case ObjectTriggeredEvent triggered:
                {
                    var blast = Paint.Shape(PrimitiveType.Sphere, flashes, "Triggered", triggered.position.ToVector3(), Vector3.one * 1.2f, Color.white, false);
                    Destroy(blast, 0.12f);
                    break;
                }
            }
        }

        Color TeamColorOf(int playerId)
        {
            var p = client == null || client.State == null ? null : client.State.Player(playerId);
            return TeamColor(p == null ? TeamId.A : p.team);
        }

        void Landed(AbilityUsedEvent used)
        {
            var lib = HeroData.Abilities;
            var baseDef = lib.Ability(used.abilityId);
            if (baseDef == null || used.points == null) return;
            var snapshot = client.Snapshot;
            var unit = snapshot == null || snapshot.abilities == null ? null : snapshot.abilities.Unit(used.playerId);
            var def = unit == null ? baseDef : AbilityParams.Resolve(baseDef, unit.changes, lib).def;
            Color color = Color.Lerp(TeamColorOf(used.playerId), Color.white, 0.35f);
            var t = def.targeting;

            if (t.shape == Shapes.Sphere)
            {
                float scale = unit == null ? 1f : Mathf.Max(0f, unit.stats.Get(StatIds.AbilityRadius));
                float r = Mathf.Max(0.3f, t.radius * scale);
                foreach (var p in used.points)
                {
                    var disc = Paint.Shape(PrimitiveType.Cylinder, flashes, "Area " + def.id, Ground(p.ToVector3()) + Vector3.up * 0.04f,
                        new Vector3(r * 2f, 0.01f, r * 2f), color, false);
                    Destroy(disc, FlashSeconds);
                }
            }
            else if (t.shape == Shapes.Ray)
            {
                Vector3 from = used.aimOrigin.ToVector3() + Vector3.down * 0.2f;
                foreach (var p in used.points)
                {
                    Vector3 to = p.ToVector3();
                    Vector3 along = to - from;
                    float length = along.magnitude;
                    if (length < 0.05f) continue;
                    var beam = Paint.Block(flashes, "Beam " + def.id, (from + to) * 0.5f, new Vector3(0.06f, 0.06f, length), color, false);
                    beam.transform.rotation = Quaternion.LookRotation(along);
                    Destroy(beam, FlashSeconds * 0.6f);
                }
            }
        }
    }
}

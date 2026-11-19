using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Keeps the built map in step with the match: zone rings and beacons take the colour of the
    /// team holding them (grey while locked), only the zones the match's mode plays on show (Flag
    /// Rush's base capture zones, in their team's colour, only in Flag Rush), and when the rules say the map has shifted
    /// (<see cref="MatchState.mapShifted"/>, set with the MapShiftEvent) the shutters roll up
    /// over <see cref="ShiftSeconds"/>. It follows the state rather than the event, so a player who
    /// joins after the shift sees the map already open, and a new match shuts it again.
    /// The host's physics is this same scene, so the server's rays see the shutters go too.
    ///
    /// In Overrun (WP19) each base's side gate opens on its own, when that base's Forward Bastion falls
    /// (<see cref="OverrunState.GateOpen"/>): a shift block whose name ends in A or B belongs to that
    /// team's base. A Bastion that has fallen drops to a dark stump and loses its crown.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        public static readonly Color Locked = new Color(0.45f, 0.47f, 0.50f);
        public static readonly Color Neutral = new Color(0.92f, 0.92f, 0.92f);

        /// <summary>Seconds for a shutter to roll up.</summary>
        public const float ShiftSeconds = 2.5f;
        /// <summary>A rolled-up shutter keeps this much of its height, tucked under the lintel.</summary>
        const float RolledHeight = 0.25f;

        /// <summary>Shows the map shifted without a match (for the screenshot run). Ignored while a match runs.</summary>
        public static bool PreviewShift;

        class ShiftPiece
        {
            public Transform transform;
            public MapBlock block;
            /// <summary>0 = as before the shift, 1 = as after it.</summary>
            public float t;
        }

        class BastionMarks
        {
            public MapBastion spot;
            public GameObject pillar, crown;
            /// <summary>A modelled Bastion's stump (null for the code-made pillar).</summary>
            public GameObject fallen;
            public bool down;
            public Color crownShown = Color.white;
        }

        class ZoneMarks
        {
            public MapZone zone;
            public GameObject[] parts;
            public Color shown;
            public bool visible = true;
        }

        readonly List<ShiftPiece> pieces = new List<ShiftPiece>();
        readonly List<ZoneMarks> zones = new List<ZoneMarks>();
        readonly List<BastionMarks> bastions = new List<BastionMarks>();

        static readonly Color Rubble = new Color(0.20f, 0.20f, 0.22f);
        /// <summary>A fallen Bastion's stump keeps this share of its height.</summary>
        const float StumpShare = 0.18f;

        public void AddBastion(MapBastion spot, GameObject pillar, GameObject crown) =>
            bastions.Add(new BastionMarks { spot = spot, pillar = pillar, crown = crown });

        /// <summary>A modelled Bastion: the standing model, its crystal (the crown) and the stump shown once it falls.</summary>
        public void AddBastion(MapBastion spot, GameObject standing, GameObject crystal, GameObject fallen) =>
            bastions.Add(new BastionMarks { spot = spot, pillar = standing, crown = crystal, fallen = fallen, crownShown = TeamColor(spot.team) });

        /// <summary>The base a shift block belongs to in Overrun: Breakwater names each pair's blocks ...A and ...B.</summary>
        static TeamId BaseOf(MapBlock b)
        {
            if (string.IsNullOrEmpty(b.name)) return TeamId.None;
            char last = b.name[b.name.Length - 1];
            return last == 'A' ? TeamId.A : last == 'B' ? TeamId.B : TeamId.None;
        }

        public void AddShiftBlock(GameObject go, MapBlock block)
        {
            if (go == null) return;
            var piece = new ShiftPiece { transform = go.transform, block = block, t = 0f };
            pieces.Add(piece);
            Pose(piece);
        }

        public void AddZone(MapZone zone, GameObject[] parts) =>
            zones.Add(new ZoneMarks { zone = zone, parts = parts, shown = Locked });

        void Update()
        {
            var runner = SessionRunner.Instance;
            var state = runner == null || runner.Client == null ? null : runner.Client.State;
            bool shifted = state != null ? state.mapShifted : PreviewShift;
            var overrun = runner == null || runner.Client == null || runner.Client.Snapshot == null ? null : runner.Client.Snapshot.overrun;

            float step = Time.deltaTime / ShiftSeconds;
            foreach (var p in pieces)
            {
                // Overrun opens each base's gate on its own; a block of neither base follows the whole-map shift.
                var team = overrun == null ? TeamId.None : BaseOf(p.block);
                bool open = team != TeamId.None ? overrun.GateOpen(team) : shifted;
                float target = open ? 1f : 0f;
                if (Mathf.Approximately(p.t, target)) continue;
                // A new match (or a late join) snaps; a shift during play rolls.
                p.t = state != null && state.phase != MatchPhase.Play && state.phase != MatchPhase.Overtime ? target : Mathf.MoveTowards(p.t, target, step);
                Pose(p);
            }

            foreach (var b in bastions)
            {
                var live = overrun == null ? null : overrun.Bastion(b.spot.team, b.spot.tier);
                bool down = live != null && !live.alive;
                if (down != b.down)
                {
                    b.down = down;
                    ShowBastion(b);
                }
                // The crown says who may hurt it: grey while sealed, the enemy's colour while their minions
                // are near (their heroes can hurt it now), white otherwise.
                // A modelled Bastion's crystal rests in its own team's colour instead of white.
                var idle = b.fallen != null ? TeamColor(b.spot.team) : Color.white;
                var crown = live == null || down ? idle : live.sealedOff ? Locked : live.openToHeroes ? TeamColor(b.spot.team.Other()) : idle;
                if (b.crown != null && crown != b.crownShown)
                {
                    b.crownShown = crown;
                    if (b.fallen != null)
                    {
                        var r = b.crown.GetComponent<Renderer>();
                        if (r != null) r.sharedMaterial = EnvArt.Glow(crown);
                    }
                    else Paint.Apply(b.crown, crown);
                }
            }

            string modeSet = state == null ? null : SessionRunner.ModeById(state.modeId).zoneSet;
            foreach (var z in zones)
            {
                // In a match only the mode's zones show; with no match, everything but the flag bases.
                bool visible = modeSet != null ? z.zone.set == modeSet : z.zone.set != "bases";
                if (visible != z.visible)
                {
                    z.visible = visible;
                    foreach (var part in z.parts) if (part != null) part.SetActive(visible);
                }
                if (!visible) continue;
                var color = z.zone.set == "bases" ? TeamColor(z.zone.startOwner) : ColorFor(state, z.zone.id);
                if (color == z.shown) continue;
                z.shown = color;
                foreach (var part in z.parts) if (part != null) Paint.Apply(part, color);
            }
        }

        /// <summary>Standing: the full pillar in its team's colour under the crown. Fallen: a dark stump, no crown.</summary>
        static void ShowBastion(BastionMarks b)
        {
            if (b.pillar == null) return;
            if (b.fallen != null)
            {
                // Modelled: swap the standing Bastion for its broken stump.
                b.pillar.SetActive(!b.down);
                b.fallen.SetActive(b.down);
                return;
            }
            float h = MapScene.BastionHeight(b.spot.tier) * (b.down ? StumpShare : 1f);
            var p = b.spot.position.ToVector3();
            var size = b.pillar.transform.localScale;
            b.pillar.transform.localScale = new Vector3(size.x, h * 0.5f, size.z);
            b.pillar.transform.localPosition = p + new Vector3(0f, h * 0.5f, 0f);
            Paint.Apply(b.pillar, b.down ? Rubble : TeamColor(b.spot.team));
            if (b.crown != null) b.crown.SetActive(!b.down);
        }

        static Color TeamColor(TeamId team) => team == TeamId.A ? MapScene.TeamA : team == TeamId.B ? MapScene.TeamB : Neutral;

        static Color ColorFor(MatchState state, ZoneId id)
        {
            var zs = state == null ? null : state.Zone(id);
            if (zs == null || !zs.active) return Locked;
            if (zs.owner == TeamId.A) return MapScene.TeamA;
            if (zs.owner == TeamId.B) return MapScene.TeamB;
            return Neutral;
        }

        /// <summary>
        /// Shutters (RemovedOnShift) roll up into their lintel: the bottom rises while the top stays.
        /// Anything AddedOnShift drops into place from above. The collider moves with the box.
        /// </summary>
        static void Pose(ShiftPiece p)
        {
            var b = p.block;
            float k = Mathf.SmoothStep(0f, 1f, p.t);
            if (b.shift == ShiftRole.RemovedOnShift)
            {
                float h = Mathf.Lerp(b.size.y, RolledHeight, k);
                float top = b.Max.y;
                p.transform.localScale = new Vector3(b.size.x, h, b.size.z);
                p.transform.localPosition = new Vector3(b.center.x, top - h * 0.5f, b.center.z);
            }
            else if (b.shift == ShiftRole.AddedOnShift)
            {
                p.transform.localPosition = new Vector3(b.center.x, b.center.y + (1f - k) * 8f, b.center.z);
                p.transform.gameObject.SetActive(p.t > 0f);
            }
        }
    }
}

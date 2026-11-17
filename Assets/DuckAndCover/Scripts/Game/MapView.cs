using System.Collections.Generic;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Keeps the built map in step with the match: zone rings and beacons take the colour of the
    /// team holding them (grey while locked), and when the rules say the map has shifted
    /// (<see cref="MatchState.mapShifted"/>, set with the MapShiftEvent) the shutters roll up
    /// over <see cref="ShiftSeconds"/>. It follows the state rather than the event, so a player who
    /// joins after the shift sees the map already open, and a new match shuts it again.
    /// The host's physics is this same scene, so the server's rays see the shutters go too.
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

        class ZoneMarks
        {
            public MapZone zone;
            public GameObject[] parts;
            public Color shown;
        }

        readonly List<ShiftPiece> pieces = new List<ShiftPiece>();
        readonly List<ZoneMarks> zones = new List<ZoneMarks>();

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

            float step = Time.deltaTime / ShiftSeconds;
            foreach (var p in pieces)
            {
                float target = shifted ? 1f : 0f;
                if (Mathf.Approximately(p.t, target)) continue;
                // A new match (or a late join) snaps; a shift during play rolls.
                p.t = state != null && state.phase != MatchPhase.Play && state.phase != MatchPhase.Overtime ? target : Mathf.MoveTowards(p.t, target, step);
                Pose(p);
            }

            foreach (var z in zones)
            {
                var color = ColorFor(state, z.zone.id);
                if (color == z.shown) continue;
                z.shown = color;
                foreach (var part in z.parts) if (part != null) Paint.Apply(part, color);
            }
        }

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

using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Draws the Flag Rush flag from what the server sends (WP21): a pole and cloth where it stands or
    /// lies, riding on its carrier's back, and a tall beacon over it that everyone sees from anywhere,
    /// white while the flag is free and in the carrier's team colour while it is carried. While the
    /// next flag is on its way, a ring marks the spot it will appear at. Placeholder art; it decides
    /// nothing. Shows nothing in other modes (the snapshot has no flag).
    /// </summary>
    public class FlagView : MonoBehaviour
    {
        public static readonly Color FreeColor = new Color(1.00f, 0.92f, 0.35f);
        static readonly Color PoleColor = new Color(0.85f, 0.85f, 0.88f);
        static readonly Color BeaconFree = new Color(1.00f, 0.97f, 0.80f);

        const float PoleHeight = 2.4f;
        const float BeaconHeight = 30f;
        /// <summary>On a carrier the flag sits this far behind and above their feet, like a banner on a back.</summary>
        static readonly Vector3 CarryOffset = new Vector3(0f, 0.6f, -0.35f);

        GameObject root, flag, cloth, beacon, upcoming;
        Color shownCloth, shownBeacon;

        void Awake()
        {
            root = new GameObject("Flag");
            root.transform.SetParent(transform, false);

            flag = new GameObject("FlagPole");
            flag.transform.SetParent(root.transform, false);
            Paint.Shape(PrimitiveType.Cylinder, flag.transform, "Pole", new Vector3(0f, PoleHeight * 0.5f, 0f), new Vector3(0.07f, PoleHeight * 0.5f, 0.07f), PoleColor, false);
            cloth = Paint.Block(flag.transform, "Cloth", new Vector3(0.48f, PoleHeight - 0.32f, 0f), new Vector3(0.9f, 0.56f, 0.04f), FreeColor, false);
            Paint.Shape(PrimitiveType.Sphere, flag.transform, "Tip", new Vector3(0f, PoleHeight + 0.06f, 0f), new Vector3(0.14f, 0.14f, 0.14f), FreeColor, false);
            shownCloth = FreeColor;

            // A thin, tall column: seen over the walls from anywhere on the map.
            beacon = Paint.Shape(PrimitiveType.Cylinder, root.transform, "FlagBeacon", new Vector3(0f, PoleHeight + BeaconHeight * 0.5f, 0f),
                new Vector3(0.18f, BeaconHeight * 0.5f, 0.18f), BeaconFree, false);
            shownBeacon = BeaconFree;

            upcoming = Paint.Shape(PrimitiveType.Cylinder, transform, "FlagUpcoming", Vector3.zero, new Vector3(2.4f, 0.012f, 2.4f), FreeColor, false);

            root.SetActive(false);
            upcoming.SetActive(false);
        }

        void LateUpdate()
        {
            var runner = SessionRunner.Instance;
            var client = runner == null ? null : runner.Client;
            var f = client == null || client.Snapshot == null ? null : client.Snapshot.flag;
            var state = client == null ? null : client.State;
            bool live = f != null && state != null && (state.IsPlaying || state.phase == MatchPhase.MatchEnd);

            bool waiting = live && f.status == FlagStatus.Waiting && f.nextSpot >= 0;
            if (upcoming.activeSelf != waiting) upcoming.SetActive(waiting);
            if (waiting)
            {
                // A slow pulse, so the ring reads as "something is coming".
                float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 4f);
                upcoming.transform.position = f.nextPosition.ToVector3() + Vector3.up * 0.03f;
                upcoming.transform.localScale = new Vector3(2.4f * pulse, 0.012f, 2.4f * pulse);
            }

            bool onMap = live && f.OnMap;
            if (root.activeSelf != onMap) root.SetActive(onMap);
            if (!onMap) return;

            Vector3 feet = f.position.ToVector3();
            float yaw = 0f;
            bool carried = f.status == FlagStatus.Carried;
            bool mine = carried && f.carrierId == client.PlayerId;
            if (carried && CombatView.Instance != null && CombatView.Instance.DrawnFeet(f.carrierId, out var drawn)) feet = drawn;
            if (carried)
            {
                var motion = client.Snapshot.Motion(f.carrierId);
                if (motion != null) yaw = motion.yaw;
            }
            var turn = Quaternion.Euler(0f, yaw, 0f);
            root.transform.SetPositionAndRotation(carried ? feet + turn * CarryOffset : feet, turn);
            // The carrier's own view would be full of pole: they see the beacon and the HUD instead.
            if (flag.activeSelf == mine) flag.SetActive(!mine);

            Color team = carried ? PlaceholderBody.ColorFor(f.carrierTeam) : FreeColor;
            if (team != shownCloth)
            {
                shownCloth = team;
                Paint.Apply(cloth, team);
            }
            Color glow = carried ? team : BeaconFree;
            if (glow != shownBeacon)
            {
                shownBeacon = glow;
                Paint.Apply(beacon, glow);
            }
            // A dropped flag's beacon blinks while it waits to reset.
            bool show = f.status != FlagStatus.Dropped || Mathf.Repeat(Time.time, 0.8f) < 0.55f;
            if (beacon.activeSelf != show) beacon.SetActive(show);
        }
    }
}

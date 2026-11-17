using System;
using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The map as the rules see it: Unity physics answers rays against walls, floors and props.
    /// Players are not in it (the rules add their hitboxes from the lag compensation history), so
    /// every player body sits on the Ignore Raycast layer. Zones are answered by the control points
    /// once they exist (WP6); until then nobody stands on any.
    /// </summary>
    public sealed class PhysicsWorld : IWorldQuery
    {
        /// <summary>Unity's built-in "Ignore Raycast" layer, used for player bodies.</summary>
        public const int PlayerLayer = 2;
        /// <summary>Everything a shot or a foot can touch: all layers but the players' own.</summary>
        public const int GeometryMask = ~(1 << PlayerLayer);

        /// <summary>WP6 sets this to say who stands in a zone.</summary>
        public Func<ZoneId, int[]> ZoneQuery;

        public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit) => Cast(origin, dir, max, out hit);

        /// <summary>A ray against the map only. Also what a client uses to draw its own shots at once.</summary>
        public static bool Cast(Vec3 origin, Vec3 dir, float max, out HitInfo hit)
        {
            hit = HitInfo.Miss;
            if (!Physics.Raycast(origin.ToVector3(), dir.ToVector3(), out RaycastHit h, max, GeometryMask, QueryTriggerInteraction.Ignore)) return false;
            hit = new HitInfo
            {
                kind = HitKind.World, point = h.point.ToVec3(), normal = h.normal.ToVec3(), distance = h.distance,
                playerId = -1, deviceId = -1, cell = SurfaceCell.None,
            };
            return true;
        }

        public int[] PlayersInZone(ZoneId zone) => ZoneQuery == null ? Array.Empty<int>() : ZoneQuery(zone) ?? Array.Empty<int>();
        public int[] PlayersInSphere(Vec3 center, float radius, int tick) => Array.Empty<int>();
        public SurfaceCell CellAt(Vec3 point) => SurfaceCell.None;
    }
}

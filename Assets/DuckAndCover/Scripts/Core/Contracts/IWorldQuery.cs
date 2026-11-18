using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Core -> world. Core never touches physics; it asks through this. Implemented in Unity by the
    /// Game layer (WP4 Raycast, WP6 PlayersInZone, WP7 PlayersInSphere, WP5 CellAt) and by fakes in
    /// Tests/CoreSim.
    /// </summary>
    public interface IWorldQuery
    {
        /// <summary>
        /// Casts against hitboxes and surfaces as they were at <paramref name="tick"/> (lag
        /// compensation). <paramref name="dir"/> is normalized; <paramref name="max"/> is in metres.
        /// </summary>
        bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit);

        /// <summary>
        /// Every player whose body is inside the zone's volume right now, alive or not, in any order.
        /// The rules filter out eliminated and disconnected players themselves. Never null.
        /// </summary>
        int[] PlayersInZone(ZoneId zone);

        /// <summary>
        /// Bodies within <paramref name="radius"/> metres of <paramref name="center"/> as they were at
        /// <paramref name="tick"/> (area abilities, WP7). Combat's own world (CombatWorld) answers
        /// with unit ids from the hit history: players and units that are not players (WP14) alike.
        /// A map-only implementation may return players only. Never null.
        /// </summary>
        int[] PlayersInSphere(Vec3 center, float radius, int tick);

        /// <summary>
        /// The breakable cell at a world point, or <see cref="SurfaceCell.None"/>. Breakable cover is a
        /// later package; until then implementations return None.
        /// </summary>
        SurfaceCell CellAt(Vec3 point);
    }

    /// <summary>What a raycast struck.</summary>
    public enum HitKind
    {
        Nothing = 0,
        /// <summary>Unbreakable geometry (skeleton walls, floors, props).</summary>
        World = 1,
        /// <summary>A cell of a breakable surface; see <see cref="HitInfo.cell"/>.</summary>
        Surface = 2,
        /// <summary>
        /// A body's hitbox: a player's or a unit's (WP14); see <see cref="HitInfo.playerId"/>, which then
        /// holds a unit id (a player's id, or an id from <see cref="UnitIds.First"/> up).
        /// </summary>
        Player = 3,
        /// <summary>A camera, scout bot or other team device; see <see cref="HitInfo.deviceId"/>.</summary>
        Device = 4,
    }

    /// <summary>Result of <see cref="IWorldQuery.Raycast"/>.</summary>
    [Serializable]
    public struct HitInfo
    {
        public HitKind kind;
        public Vec3 point;
        public Vec3 normal;
        public float distance;
        /// <summary>The unit id of the body that was hit (a player's id or a unit's), or -1.</summary>
        public int playerId;
        /// <summary>Hitbox region id (head, body, limb) as defined by the Combat module.</summary>
        public int bodyPart;
        /// <summary>Device that was hit, or -1.</summary>
        public int deviceId;
        /// <summary>Surface cell that was hit, or <see cref="SurfaceCell.None"/>.</summary>
        public SurfaceCell cell;

        public static HitInfo Miss => new HitInfo { kind = HitKind.Nothing, playerId = -1, deviceId = -1, cell = SurfaceCell.None };
    }

    /// <summary>
    /// The state a breakable cell can be in. The Destruction module (WP2) owns the transitions.
    /// </summary>
    public enum CellState : byte
    {
        Intact = 0,
        Cracked = 1,
        Open = 2,
        Patched = 3,
        Reinforced = 4,
        Locked = 5,
    }

    /// <summary>Surface materials from the plan. What breaks each one lives in the ruleset.</summary>
    public enum SurfaceMaterial : byte
    {
        /// <summary>Outer shell, pillars, stair cores, most floors. Never breaks.</summary>
        Skeleton = 0,
        /// <summary>Reinforced panels, some site walls. Heavy breach charge only.</summary>
        Hard = 1,
        /// <summary>Interior walls, floor hatches. Charges, grenades, bullets and melee up to a cap.</summary>
        Soft = 2,
        /// <summary>Boards on doors and windows, patches. Anything, quickly.</summary>
        Barricade = 3,
    }

    /// <summary>
    /// Address of one 25 cm cell: which surface, and the column and row on its grid (0,0 is the
    /// bottom-left cell seen from the surface's front).
    /// </summary>
    [Serializable]
    public struct SurfaceCell : IEquatable<SurfaceCell>
    {
        public int surfaceId;
        public int x;
        public int y;

        public SurfaceCell(int surfaceId, int x, int y) { this.surfaceId = surfaceId; this.x = x; this.y = y; }

        /// <summary>No cell. Surface ids start at 1.</summary>
        public static readonly SurfaceCell None = new SurfaceCell(0, -1, -1);
        public bool IsValid => surfaceId > 0 && x >= 0 && y >= 0;

        public static bool operator ==(SurfaceCell a, SurfaceCell b) => a.Equals(b);
        public static bool operator !=(SurfaceCell a, SurfaceCell b) => !a.Equals(b);
        public bool Equals(SurfaceCell o) => surfaceId == o.surfaceId && x == o.x && y == o.y;
        public override bool Equals(object obj) => obj is SurfaceCell c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(surfaceId, x, y);
        public override string ToString() => IsValid ? "Surface" + surfaceId + "[" + x + "," + y + "]" : "NoCell";
    }
}

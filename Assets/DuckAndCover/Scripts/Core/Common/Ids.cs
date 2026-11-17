using System;

namespace DuckAndCover.Core
{
    /// <summary>One of the two teams in a match. Teams keep their id for the whole match.</summary>
    public enum TeamId
    {
        None = -1,
        A = 0,
        B = 1,
    }

    public static class TeamIdExtensions
    {
        public static TeamId Other(this TeamId team) =>
            team == TeamId.A ? TeamId.B : team == TeamId.B ? TeamId.A : TeamId.None;
    }

    /// <summary>
    /// A control point (or a king-of-the-hill spot) on the map. Ids come from the map definition.
    /// Default (0) is reserved for "no zone".
    /// </summary>
    [Serializable]
    public struct ZoneId : IEquatable<ZoneId>
    {
        public int value;

        public ZoneId(int value) { this.value = value; }

        public static readonly ZoneId None = new ZoneId(0);
        public bool IsValid => value != 0;

        public static bool operator ==(ZoneId a, ZoneId b) => a.value == b.value;
        public static bool operator !=(ZoneId a, ZoneId b) => a.value != b.value;
        public bool Equals(ZoneId o) => value == o.value;
        public override bool Equals(object obj) => obj is ZoneId s && Equals(s);
        public override int GetHashCode() => value;
        public override string ToString() => IsValid ? "Zone" + value : "NoZone";
    }
}

using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// One of the two teams in a match. Teams keep their id for the whole match; which side they
    /// play (attack or defense) changes between rounds, so never use TeamId to mean a side.
    /// </summary>
    public enum TeamId
    {
        None = -1,
        A = 0,
        B = 1,
    }

    /// <summary>The role a team plays in the current round.</summary>
    public enum Side
    {
        Attack = 0,
        Defense = 1,
    }

    public static class TeamIdExtensions
    {
        public static TeamId Other(this TeamId team) =>
            team == TeamId.A ? TeamId.B : team == TeamId.B ? TeamId.A : TeamId.None;

        public static Side Other(this Side side) => side == Side.Attack ? Side.Defense : Side.Attack;
    }

    /// <summary>
    /// A charge site on the map. Ids come from the map definition; the Objective module decides how
    /// sites are grouped into the pairs defenders vote on. Default (0) is reserved for "no site".
    /// </summary>
    [Serializable]
    public struct SiteId : IEquatable<SiteId>
    {
        public int value;

        public SiteId(int value) { this.value = value; }

        public static readonly SiteId None = new SiteId(0);
        public bool IsValid => value != 0;

        public static bool operator ==(SiteId a, SiteId b) => a.value == b.value;
        public static bool operator !=(SiteId a, SiteId b) => a.value != b.value;
        public bool Equals(SiteId o) => value == o.value;
        public override bool Equals(object obj) => obj is SiteId s && Equals(s);
        public override int GetHashCode() => value;
        public override string ToString() => IsValid ? "Site" + value : "NoSite";
    }
}

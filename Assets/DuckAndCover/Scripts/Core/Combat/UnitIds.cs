namespace DuckAndCover.Core
{
    /// <summary>
    /// The combat rules key everything that fights by a unit id (WP14). A player is a unit whose id is
    /// their player id; units that are not players (minions, Bastions, camp guards) take ids from
    /// <see cref="First"/> up, clear of real players and of practice dummies (from 1000). Health,
    /// shields, statuses, damage, healing and the hit history all take unit ids.
    /// </summary>
    public static class UnitIds
    {
        /// <summary>The first id given to a unit that is not a player.</summary>
        public const int First = 20000;

        /// <summary>Whether the id belongs to a unit that is not a player.</summary>
        public static bool IsUnit(int id) => id >= First;
    }
}

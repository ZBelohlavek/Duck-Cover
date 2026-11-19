namespace DuckAndCover.Core
{
    /// <summary>
    /// Client -> server request. Clients send requests, never results: each command is validated by
    /// Core on the server (MatchRules.Apply), which may reject it.
    /// </summary>
    public abstract class MatchCommand
    {
        /// <summary>Player who sent the command. The server overwrites this with the real sender.</summary>
        public int playerId;
        /// <summary>Client tick the command was issued on (used for lag compensation).</summary>
        public int tick;
    }

    /// <summary>Fire the current weapon along a ray.</summary>
    public class FireCommand : MatchCommand
    {
        public string weaponId;
        public Vec3 origin;
        public Vec3 direction;
    }

    /// <summary>Activate the ability (gadget now, hero ability later) in a slot.</summary>
    public class UseAbilityCommand : MatchCommand
    {
        public int slot;
        public string abilityId;
        public Vec3 aimOrigin;
        public Vec3 aimDirection;
        /// <summary>
        /// For a held ability (a shield): the key was released, so take it down. Does nothing if it is
        /// not up. Without it, using a held ability that is up also lowers it.
        /// </summary>
        public bool lower;
    }

    /// <summary>Use something in the world: pick up the kit, plant, defuse, open a door.</summary>
    public class InteractCommand : MatchCommand
    {
        /// <summary>Id of the interactable, as registered by the map or the Objective module.</summary>
        public int targetId;
        /// <summary>True when the interact key is pressed, false when it is released (cancels a hold).</summary>
        public bool pressed = true;
    }

    /// <summary>Mark a spot, an enemy or a device for the team.</summary>
    public class PingCommand : MatchCommand
    {
        public Vec3 point;
        /// <summary>Enemy player the ping was placed on, or -1.</summary>
        public int targetPlayerId = -1;
        /// <summary>Device the ping was made through (Backline), or -1 for the player's own view.</summary>
        public int deviceId = -1;
    }

    /// <summary>Spend support energy on a Backline action through a device.</summary>
    public class SupportActionCommand : MatchCommand
    {
        public string actionId;
        public int deviceId;
        public Vec3 target;
    }

    /// <summary>Choose a hero during hero pick. One of each hero per team.</summary>
    public class PickHeroCommand : MatchCommand
    {
        public string heroId;
    }

    /// <summary>
    /// Choose where to come back after an elimination: <see cref="ZoneId.None"/> for the base, or
    /// the team's forward point, which is used only while the team holds it uncontested.
    /// The choice stays until changed.
    /// </summary>
    public class PickSpawnCommand : MatchCommand
    {
        public ZoneId zone;
    }

    /// <summary>
    /// Take one of the two upgrades offered at a level (Progression, WP8). Allowed at any moment,
    /// alive or waiting to respawn.
    /// </summary>
    public class ChooseUpgradeCommand : MatchCommand
    {
        public int level;
        /// <summary>0 for the first (left) option, 1 for the second (right).</summary>
        public int option;
    }
}

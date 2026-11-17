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

    /// <summary>Choose a loadout (and later a hero) during the pick phase.</summary>
    public class PickLoadoutCommand : MatchCommand
    {
        public string loadoutId;
    }

    /// <summary>Defenders vote for the site pair they will guard. Ids come from the map.</summary>
    public class VoteSiteCommand : MatchCommand
    {
        public int sitePair;
    }
}

using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // State, commands and events for movement and weapons (WP4).

    /// <summary>
    /// Where a player is and how they move, as the server last accepted it. Movement is owned by
    /// the player's own machine (the plan's first step); the server checks each move and keeps this.
    /// </summary>
    [Serializable]
    public class PlayerMotion
    {
        public int playerId;
        /// <summary>Feet position.</summary>
        public Vec3 position;
        public Vec3 velocity;
        /// <summary>Degrees: yaw 0 looks along +z, pitch positive looks down (Unity's convention).</summary>
        public float yaw;
        public float pitch;
        public bool crouched;
        public bool grounded = true;
        public bool sprinting;
        public bool vaulting;
        /// <summary>
        /// Counts the server's own teleports (spawns, corrections). A move made before the latest one
        /// is stale and ignored, so a correction can't be undone by moves already in flight.
        /// </summary>
        public int placement;

        public PlayerMotion Clone() => (PlayerMotion)MemberwiseClone();

        public BodyPose Pose(bool alive) => new BodyPose(position, crouched, alive);
    }

    /// <summary>A player's weapons right now.</summary>
    [Serializable]
    public class WeaponState
    {
        public int playerId;
        /// <summary>Weapon ids in slot order.</summary>
        public string[] weapons = new string[0];
        /// <summary>Rounds left in each slot's magazine.</summary>
        public int[] ammo = new int[0];
        /// <summary>Each slot's full magazine for this player (the weapon's, times their magazineSize stat).</summary>
        public int[] magazines = new int[0];
        /// <summary>The slot in hand.</summary>
        public int current;
        /// <summary>Seconds until the reload in progress finishes, 0 when not reloading.</summary>
        public float reloadLeft;
        /// <summary>Seconds until a weapon being raised can fire.</summary>
        public float switchLeft;
        /// <summary>Shots the fire-rate check will still allow right now (refills at the weapon's rate).</summary>
        public float fireCredit;
        public int shotsFired;

        public string CurrentId => current >= 0 && current < weapons.Length ? weapons[current] : "";
        public int CurrentAmmo => current >= 0 && current < ammo.Length ? ammo[current] : 0;
        /// <summary>The full magazine of the weapon in hand, 0 when unknown (an older snapshot).</summary>
        public int CurrentMagazine => current >= 0 && current < magazines.Length ? magazines[current] : 0;
        public bool Reloading => reloadLeft > 0f;

        public WeaponState Clone()
        {
            var c = (WeaponState)MemberwiseClone();
            c.weapons = (string[])weapons.Clone();
            c.ammo = (int[])ammo.Clone();
            c.magazines = (int[])magazines.Clone();
            return c;
        }
    }

    /// <summary>A shot on its way: a grenade, a rocket.</summary>
    [Serializable]
    public class ProjectileState
    {
        public int id;
        public int ownerId;
        public string weaponId = "";
        public Vec3 position;
        public Vec3 velocity;
        public float gravity;
        public float age;

        public ProjectileState Clone() => (ProjectileState)MemberwiseClone();
    }

    /// <summary>One spawn spot: feet position and the way it faces.</summary>
    [Serializable]
    public class SpawnPoint
    {
        public Vec3 position;
        public float yaw;
        /// <summary>The forward zone this spot belongs to, or None for a base spawn room.</summary>
        public ZoneId zone;

        public SpawnPoint() { }
        public SpawnPoint(float x, float y, float z, float yaw, int zone = 0)
        {
            position = new Vec3(x, y, z);
            this.yaw = yaw;
            this.zone = new ZoneId(zone);
        }
    }

    /// <summary>
    /// The spawn spots of a map: each team's base, and forward spots per zone. The map supplies it
    /// (WP9); until then the boot room's <see cref="Placeholder"/> layout.
    /// </summary>
    [Serializable]
    public class ArenaLayout
    {
        public List<SpawnPoint> spawnsA = new List<SpawnPoint>();
        public List<SpawnPoint> spawnsB = new List<SpawnPoint>();
        /// <summary>Spots for coming back at a held forward zone (SpawnPoint.zone says which).</summary>
        public List<SpawnPoint> forward = new List<SpawnPoint>();
        /// <summary>Where practice dummies stand.</summary>
        public List<SpawnPoint> dummies = new List<SpawnPoint>();
        /// <summary>Launch pads (Overrun maps): the movement check lets a player who steps on one fly its arc.</summary>
        public List<LaunchPad> launchPads = new List<LaunchPad>();

        public List<SpawnPoint> Base(TeamId team) => team == TeamId.B ? spawnsB : spawnsA;

        /// <summary>
        /// The old boot room test arena: 48 m long, team A's spawn at the -z end facing +z, team B's
        /// at +z facing back. Tests and scripted runs still use it; the game uses the map's layout
        /// (<see cref="MapDef.ToLayout"/>).
        /// </summary>
        public static ArenaLayout Placeholder()
        {
            var a = new ArenaLayout();
            for (int i = 0; i < 5; i++)
            {
                float x = -4f + 2f * i;
                a.spawnsA.Add(new SpawnPoint(x, 0f, -21f, 0f));
                a.spawnsB.Add(new SpawnPoint(x, 0f, 21f, 180f));
            }
            a.forward.Add(new SpawnPoint(-2f, 0f, -11f, 0f, 1));
            a.forward.Add(new SpawnPoint(2f, 0f, -11f, 0f, 1));
            a.forward.Add(new SpawnPoint(-2f, 0f, 11f, 180f, 3));
            a.forward.Add(new SpawnPoint(2f, 0f, 11f, 180f, 3));
            a.dummies.Add(new SpawnPoint(-3f, 0f, 7.5f, 180f));
            a.dummies.Add(new SpawnPoint(3f, 0f, 9f, 180f));
            a.dummies.Add(new SpawnPoint(7f, 0f, 3f, 225f));
            return a;
        }
    }

    // ---- Commands ----

    /// <summary>
    /// Where my player is now. Sent by the player's own machine about 20 times a second. The server
    /// checks the speed and keeps it, or refuses it and puts the player back (PlayerPlacedEvent).
    /// </summary>
    public sealed class MoveCommand : MatchCommand
    {
        public Vec3 position;
        public Vec3 velocity;
        public float yaw;
        public float pitch;
        public bool crouched;
        public bool grounded = true;
        public bool sprinting;
        public bool vaulting;
        /// <summary>The <see cref="PlayerMotion.placement"/> this move started from.</summary>
        public int placement;
    }

    /// <summary>Reload the weapon in hand.</summary>
    public sealed class ReloadCommand : MatchCommand { }

    /// <summary>Put the weapon in hand away and raise the one in a slot.</summary>
    public sealed class SwitchWeaponCommand : MatchCommand
    {
        public int slot;
    }

    /// <summary>Host only, in the lobby: add a practice dummy (a player nobody controls) to a team.</summary>
    public sealed class AddDummyCommand : MatchCommand
    {
        public TeamId team = TeamId.B;
    }

    // ---- Events ----

    /// <summary>The server put a player somewhere: a spawn, or a correction after a refused move.</summary>
    public sealed class PlayerPlacedEvent : GameEvent
    {
        public int playerId;
        public Vec3 position;
        public float yaw;
        public int placement;
        /// <summary>True when a refused move caused it.</summary>
        public bool correction;
    }

    /// <summary>A hitscan shot was fired. Clients draw tracers for other players' shots from it.</summary>
    public sealed class ShotFiredEvent : GameEvent
    {
        public int playerId;
        public string weaponId = "";
        public Vec3 origin;
        /// <summary>Where each pellet ended (what it hit, or the end of its range).</summary>
        public Vec3[] ends = new Vec3[0];
        /// <summary>Per pellet: the player hit, or -1.</summary>
        public int[] hitPlayers = new int[0];
        /// <summary>Per pellet: the BodyParts value, or 0.</summary>
        public int[] hitParts = new int[0];
        public int ammoLeft;
    }

    public sealed class ProjectileLaunchedEvent : GameEvent
    {
        public int projectileId;
        public int ownerId;
        public string weaponId = "";
        public Vec3 origin;
        public Vec3 velocity;
        public float gravity;
        public int ammoLeft;
    }

    public sealed class ProjectileExplodedEvent : GameEvent
    {
        public int projectileId;
        public int ownerId;
        public string weaponId = "";
        public Vec3 point;
        /// <summary>The player hit directly, or -1.</summary>
        public int directHit = -1;
        public float splashRadius;
    }

    public sealed class ReloadStartedEvent : GameEvent
    {
        public int playerId;
        public string weaponId = "";
        public float seconds;
    }

    public sealed class WeaponSwitchedEvent : GameEvent
    {
        public int playerId;
        public int slot;
        public string weaponId = "";
    }
}

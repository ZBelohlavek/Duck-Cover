using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Numbers for movement, weapons, hitboxes, lag compensation and personal shields (WP4). Value
    // fields only, so the base Ruleset.Clone (MemberwiseClone) copies them. Checked by
    // ValidateCombat, which CombatServer calls. Weapon stats themselves are data (WeaponDef).
    public partial class Ruleset
    {
        // ---- Movement (metres, seconds) ----
        public float walkSpeed = 5.2f;
        public float sprintSpeed = 7.6f;
        public float crouchSpeed = 2.6f;
        /// <summary>Ground acceleration toward the wished speed, m/s².</summary>
        public float groundAcceleration = 60f;
        /// <summary>Share of ground acceleration available in the air.</summary>
        public float airControl = 0.35f;
        public float gravity = 22f;
        public float jumpHeight = 1.05f;
        /// <summary>Capsule height standing and crouched. Hitboxes follow the same heights.</summary>
        public float standHeight = 1.8f;
        public float crouchHeight = 1.2f;
        public float bodyRadius = 0.35f;
        /// <summary>Camera (and shot origin) height above the feet.</summary>
        public float eyeHeight = 1.62f;
        public float crouchEyeHeight = 1.05f;
        /// <summary>Ledges between these heights (above the feet) can be vaulted.</summary>
        public float vaultMinHeight = 0.45f;
        public float vaultMaxHeight = 1.25f;
        /// <summary>How far ahead a ledge may be to start a vault.</summary>
        public float vaultReach = 0.75f;
        public float vaultSeconds = 0.32f;
        /// <summary>Sprinting only counts when moving within this angle of straight ahead.</summary>
        public float sprintMaxAngle = 50f;

        // ---- Server checks on player-owned movement ----
        /// <summary>A client may move up to its top speed times this (jitter, rounding).</summary>
        public float moveTolerance = 1.3f;
        /// <summary>Movement allowance a player can bank, in seconds of top speed (bursty packets).</summary>
        public float moveBurstSeconds = 0.6f;
        /// <summary>Extra metres always allowed on top of the allowance.</summary>
        public float moveSlack = 0.5f;
        /// <summary>Highest a player may rise in one move (a jump plus a vault, with slack).</summary>
        public float maxRisePerMove = 2.6f;

        // ---- Hitboxes (metres above the feet, standing; crouching scales them) ----
        public float headRadius = 0.2f;
        /// <summary>Height of the head's centre when standing.</summary>
        public float headHeight = 1.6f;
        /// <summary>Body capsule from the hips to the shoulders.</summary>
        public float torsoBottom = 0.85f;
        public float torsoTop = 1.38f;
        public float torsoRadius = 0.32f;
        /// <summary>Units walking the lane also check for cover this high above their feet (waist-high cover stops them).</summary>
        public float unitKneeHeight = 0.4f;
        /// <summary>Leg capsule from the ankles to the hips.</summary>
        public float legsBottom = 0.15f;
        public float legsTop = 0.85f;
        public float legsRadius = 0.24f;

        // ---- Lag compensation ----
        /// <summary>How far back the server rewinds a shot (the plan: half a second).</summary>
        public float maxRewindSeconds = 0.5f;
        /// <summary>A shot's origin may be this far from the server's eye position for the shooter.</summary>
        public float shotOriginTolerance = 1.5f;

        // ---- Weapons ----
        /// <summary>Fire-rate allowance a player can bank, in shots (bursty packets).</summary>
        public float fireBurstShots = 1.5f;
        /// <summary>Seconds to put one weapon away and raise another.</summary>
        public float weaponSwitchSeconds = 0.35f;
        /// <summary>Share of splash damage a player takes from their own explosions.</summary>
        public float selfSplashShare = 0f;
        /// <summary>Projectiles that hit nothing are removed after this many seconds.</summary>
        public float projectileMaxSeconds = 4f;
        /// <summary>A melee swing reaches chests this far above or below the swinger's eye (metres).</summary>
        public float meleeReachUp = 1.6f;
        /// <summary>Seconds a weapon hit's push (the weaponPush stat) takes to play out.</summary>
        public float weaponPushSeconds = 0.12f;
        /// <summary>Seconds a weapon hit's slow (the weaponSlow stat) lasts; each hit refreshes it.</summary>
        public float weaponSlowSeconds = 1f;
        /// <summary>Share of a healing round's heal that its splash (the healRounds effect's radius) gives teammates near the one hit.</summary>
        public float healSplashShare = 0.5f;
        /// <summary>How far a healing round jumps to the next hurt teammate (the healRicochet stat), metres between chests.</summary>
        public float healRicochetRange = 6f;
        /// <summary>Share of the healing round's heal each jump gives.</summary>
        public float healRicochetShare = 0.6f;

        // ---- Personal shield (regenerates; ability shields stack on top, up to maxShield) ----
        /// <summary>Every hero's own shield. 0 turns personal shields off.</summary>
        public float personalShield = 50f;
        /// <summary>The shield starts coming back this long after the last damage.</summary>
        public float personalShieldDelaySeconds = 3f;
        public float personalShieldRegenPerSecond = 25f;

        /// <summary>Problems with the combat numbers, empty when usable.</summary>
        public List<string> ValidateCombat()
        {
            var e = new List<string>();
            if (walkSpeed <= 0f || sprintSpeed < walkSpeed || crouchSpeed <= 0f) e.Add("speeds must be positive and sprint at least walk");
            if (groundAcceleration <= 0f || airControl < 0f || airControl > 1f) e.Add("acceleration must be positive and airControl 0..1");
            if (gravity <= 0f || jumpHeight < 0f) e.Add("gravity must be positive and jumpHeight not negative");
            if (crouchHeight <= 0f || standHeight < crouchHeight || bodyRadius <= 0f) e.Add("crouchHeight must be positive and below standHeight");
            if (eyeHeight > standHeight || crouchEyeHeight > crouchHeight) e.Add("eyes must be inside the body");
            if (vaultMinHeight < 0f || vaultMaxHeight < vaultMinHeight || vaultSeconds <= 0f) e.Add("vault heights or time are wrong");
            if (moveTolerance < 1f || moveBurstSeconds <= 0f || moveSlack < 0f) e.Add("movement checks must allow at least top speed");
            if (headRadius <= 0f || torsoRadius <= 0f || legsRadius <= 0f) e.Add("hitbox radii must be positive");
            if (torsoTop <= torsoBottom || legsTop <= legsBottom) e.Add("hitbox capsules must have a top above the bottom");
            if (maxRewindSeconds < 0f || shotOriginTolerance <= 0f) e.Add("lag compensation numbers are wrong");
            if (meleeReachUp <= 0f) e.Add("meleeReachUp must be positive");
            if (fireBurstShots < 1f || weaponSwitchSeconds < 0f) e.Add("fireBurstShots must be at least 1");
            if (selfSplashShare < 0f || selfSplashShare > 1f) e.Add("selfSplashShare must be 0..1");
            if (projectileMaxSeconds <= 0f) e.Add("projectileMaxSeconds must be positive");
            if (weaponPushSeconds <= 0f || weaponSlowSeconds <= 0f) e.Add("weaponPushSeconds and weaponSlowSeconds must be positive");
            if (healSplashShare < 0f || healRicochetShare < 0f || healRicochetRange < 0f) e.Add("healSplashShare, healRicochetShare and healRicochetRange can't be negative");
            if (personalShield < 0f || personalShieldDelaySeconds < 0f || personalShieldRegenPerSecond < 0f) e.Add("personal shield numbers can't be negative");
            return e;
        }

        /// <summary>Top speed for the movement checks: sprint, or the jump take-off speed if higher.</summary>
        public float TopSpeed => sprintSpeed;

        /// <summary>Upward speed that reaches <see cref="jumpHeight"/>.</summary>
        public float JumpVelocity => (float)System.Math.Sqrt(2f * gravity * System.Math.Max(0f, jumpHeight));
    }
}

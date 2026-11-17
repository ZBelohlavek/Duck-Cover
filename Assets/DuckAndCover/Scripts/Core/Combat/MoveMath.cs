using System;

namespace DuckAndCover.Core
{
    /// <summary>What the player is asking their body to do this frame.</summary>
    public struct MoveInput
    {
        /// <summary>-1..1: backward to forward, and left to right.</summary>
        public float forward;
        public float right;
        public bool sprint;
        public bool crouch;
        /// <summary>Jump was pressed this frame.</summary>
        public bool jump;
        /// <summary>Degrees; 0 looks along +z.</summary>
        public float yaw;
    }

    /// <summary>
    /// The feel of first-person movement as plain maths: wished speed, acceleration, gravity, jump
    /// and the vault test. The Game layer feeds it input and lets the physics engine handle
    /// collisions. Kept here so it has tests, and so a later server-run movement uses the same code.
    /// </summary>
    public static class MoveMath
    {
        const float Deg = (float)(Math.PI / 180.0);

        /// <summary>Flat forward for a yaw.</summary>
        public static Vec3 Forward(float yaw) => new Vec3((float)Math.Sin(yaw * Deg), 0f, (float)Math.Cos(yaw * Deg));

        public static Vec3 Right(float yaw) => new Vec3((float)Math.Cos(yaw * Deg), 0f, -(float)Math.Sin(yaw * Deg));

        /// <summary>Where a player looks: yaw as above, pitch positive looks down.</summary>
        public static Vec3 AimDirection(float yaw, float pitch)
        {
            float cp = (float)Math.Cos(pitch * Deg);
            return new Vec3((float)Math.Sin(yaw * Deg) * cp, -(float)Math.Sin(pitch * Deg), (float)Math.Cos(yaw * Deg) * cp);
        }

        /// <summary>Sprinting needs the key, standing, and moving mostly forward.</summary>
        public static bool IsSprinting(Ruleset r, MoveInput input)
        {
            if (!input.sprint || input.crouch || input.forward <= 0f) return false;
            double angle = Math.Atan2(Math.Abs(input.right), input.forward) / Deg;
            return angle <= r.sprintMaxAngle;
        }

        /// <summary>The flat speed the input asks for, before slows.</summary>
        public static float WishSpeed(Ruleset r, MoveInput input) =>
            input.crouch ? r.crouchSpeed : IsSprinting(r, input) ? r.sprintSpeed : r.walkSpeed;

        /// <summary>
        /// The velocity after <paramref name="dt"/> seconds. On the ground the flat part chases the
        /// wished velocity at groundAcceleration (in the air, airControl of it); the vertical part
        /// jumps, sticks to the ground, or falls. <paramref name="speedScale"/> is the hero's move
        /// speed stat (slows), 0 for a stunned or rooted hero.
        /// </summary>
        public static Vec3 Step(Ruleset r, Vec3 velocity, MoveInput input, bool grounded, float speedScale, float dt)
        {
            Vec3 wish = Forward(input.yaw) * input.forward + Right(input.yaw) * input.right;
            float m = wish.Magnitude;
            if (m > 1f) wish = wish / m;
            wish = wish * (WishSpeed(r, input) * Math.Max(0f, speedScale));

            Vec3 flat = new Vec3(velocity.x, 0f, velocity.z);
            Vec3 diff = wish - flat;
            float change = r.groundAcceleration * (grounded ? 1f : r.airControl) * dt;
            float gap = diff.Magnitude;
            flat = gap <= change || gap < 1e-6f ? wish : flat + diff * (change / gap);

            float y;
            if (grounded) y = input.jump && speedScale > 0f ? r.JumpVelocity : -2f; // a small push down keeps the feet on slopes
            else y = velocity.y - r.gravity * dt;
            return new Vec3(flat.x, y, flat.z);
        }

        /// <summary>A ledge this far above the feet can be vaulted.</summary>
        public static bool CanVault(Ruleset r, float ledgeHeight) => ledgeHeight >= r.vaultMinHeight && ledgeHeight <= r.vaultMaxHeight;

        /// <summary>Where a vault is after <paramref name="t"/> of its time (0..1): up first, then over.</summary>
        public static Vec3 VaultPosition(Vec3 from, Vec3 to, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            float up = Math.Min(1f, t * 1.6f);
            float over = t <= 0.35f ? 0f : (t - 0.35f) / 0.65f;
            return new Vec3(from.x + (to.x - from.x) * over, from.y + (to.y - from.y) * up, from.z + (to.z - from.z) * over);
        }
    }
}

using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// One standard effect: damage, heal, shield, status (and slow, stun), cleanse, push, spawn.
    /// Abilities, statuses and objects are lists of these. A new kind is a new class registered with
    /// AbilityRunner.RegisterEffect.
    /// </summary>
    public interface IEffect
    {
        /// <summary>Apply to one target. Spawn ignores the target (-1) and uses <see cref="EffectContext.point"/>.</summary>
        void Apply(EffectContext ctx, int targetId);
    }

    /// <summary>What an effect needs to know about where it comes from.</summary>
    public class EffectContext
    {
        public AbilityRunner runner;
        /// <summary>The player credited: the caster, or the owner of the object or status. -1 for the world.</summary>
        public int sourceId = -1;
        public TeamId sourceTeam = TeamId.None;
        public string abilityId = "";
        /// <summary>The spawned object the effect comes from, or 0.</summary>
        public int objectId;
        public EffectDef effect;
        /// <summary>Where the ability landed (the area centre, the ray's hit point, the object).</summary>
        public Vec3 point;
        /// <summary>The caster's aim direction.</summary>
        public Vec3 aim;
        /// <summary>Multiplies amounts (status stacks).</summary>
        public float scale = 1f;
        /// <summary>The casting ability with talents applied, when there is one (spawn reads its objects).</summary>
        public ResolvedAbility ability;

        public DamageRules Combat => runner.Combat;
    }

    public sealed class DamageEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId) =>
            ctx.Combat.Damage(ctx.sourceId, targetId, ctx.effect.amount * ctx.scale, ctx.abilityId, ctx.objectId);
    }

    public sealed class HealEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId) =>
            ctx.Combat.Heal(ctx.sourceId, targetId, ctx.effect.amount * ctx.scale, ctx.abilityId, ctx.objectId);
    }

    public sealed class ShieldEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId) =>
            ctx.Combat.AddShield(ctx.sourceId, targetId, ctx.effect.amount * ctx.scale, ctx.effect.duration, ctx.abilityId, ctx.objectId);
    }

    /// <summary>Kinds "status", "slow" and "stun": puts a status on the target.</summary>
    public sealed class StatusEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId)
        {
            var e = ctx.effect;
            float strength = e.kind == EffectKinds.Stun || e.amount <= 0f ? 1f : e.amount;
            ctx.runner.ApplyStatus(targetId, AbilityLibrary.StatusIdFor(e), ctx.sourceId, strength, e.duration, e.interval, ctx.abilityId);
        }
    }

    /// <summary>Removes every harmful status.</summary>
    public sealed class CleanseEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId) => ctx.runner.Cleanse(targetId);
    }

    /// <summary>Shoves the target; on the caster along their aim it is a dash.</summary>
    public sealed class PushEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId)
        {
            var e = ctx.effect;
            if (!ctx.runner.IsAlive(targetId)) return;
            Vec3 dir = Vec3.Zero;
            if (e.direction == PushDirections.Aim) dir = ctx.aim.Normalized;
            else if (ctx.runner.TryGetPosition(targetId, out var pos))
            {
                dir = (pos - ctx.point).Normalized;
                if (e.direction == PushDirections.Toward) dir = -dir;
            }
            ctx.runner.Publish(new PushedEvent
            {
                targetId = targetId,
                sourceId = ctx.sourceId,
                mode = e.direction,
                origin = ctx.point,
                direction = dir,
                distance = e.amount * ctx.scale,
                seconds = e.duration,
                abilityId = ctx.abilityId,
            });
        }
    }

    /// <summary>Places an object where the ability landed.</summary>
    public sealed class SpawnEffect : IEffect
    {
        public void Apply(EffectContext ctx, int targetId)
        {
            ObjectDef def = null;
            if (ctx.ability != null) ctx.ability.objects.TryGetValue(ctx.effect.objectId, out def);
            if (def == null) def = ctx.runner.Library.Object(ctx.effect.objectId);
            if (def == null) return;
            ctx.runner.SpawnObject(def, ctx.sourceId, ctx.point, ctx.abilityId, ctx.aim);
        }
    }

    /// <summary>
    /// Optional extra the Game layer's world can implement so the rules know where heroes stand
    /// (push directions). Without it, pushes away or toward a point leave the direction to the Game layer.
    /// </summary>
    public interface IPositionQuery
    {
        bool TryGetPosition(int playerId, int tick, out Vec3 position);
    }
}

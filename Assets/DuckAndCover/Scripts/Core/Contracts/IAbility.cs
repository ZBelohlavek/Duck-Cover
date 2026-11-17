namespace DuckAndCover.Core
{
    /// <summary>
    /// Gadgets now, hero abilities later. Run by the AbilityRunner (WP7). Activate runs on the
    /// server only; Tick runs every server tick while the ability is active.
    /// </summary>
    public interface IAbility
    {
        string Id { get; }
        bool CanActivate(AbilityContext ctx);
        void Activate(AbilityContext ctx);      // server only
        void Tick(AbilityContext ctx, float dt);
    }

    /// <summary>
    /// What an ability can see and do. Partial on purpose: modules add the state they expose (match
    /// state, budgets, destruction) in their own files rather than editing this one.
    /// </summary>
    public partial class AbilityContext
    {
        public int playerId;
        public int tick;
        public int slot;
        public Vec3 aimOrigin;
        public Vec3 aimDirection;
        public IWorldQuery world;
        public IGameEvents events;
    }
}

namespace DuckAndCover.Core
{
    /// <summary>
    /// A Backline action an eliminated player spends support energy on (scan pulse, remote breach,
    /// remote seal, disrupt). Heroes can add their own later. Use runs on the server only.
    /// </summary>
    public interface ISupportAction
    {
        string Id { get; }
        int EnergyCost { get; }
        bool CanUse(SupportContext ctx);
        void Use(SupportContext ctx);           // server only
    }

    /// <summary>
    /// What a support action can see and do. Partial on purpose, like <see cref="AbilityContext"/>.
    /// </summary>
    public partial class SupportContext
    {
        public int playerId;
        public int tick;
        public TeamId team;
        /// <summary>The device the action is triggered through.</summary>
        public int deviceId;
        public Vec3 devicePosition;
        public Vec3 target;
        public IWorldQuery world;
        public IGameEvents events;
    }
}

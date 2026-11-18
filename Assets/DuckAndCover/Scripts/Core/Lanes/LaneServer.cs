using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Lanes, waves and neutral camps for one match (WP15), on top of a <see cref="CombatServer"/>.
    /// The host builds one when the map has lanes or camps (WP16's Overrun does), after the combat
    /// server, and attaches it to its <see cref="SessionHost"/>; tests drive it directly. Call
    /// <see cref="Tick"/> after the combat server's tick. Its unit kinds join the combat server's
    /// unit library when it is made.
    /// </summary>
    public sealed class LaneServer
    {
        readonly MatchRules match;
        readonly IGameEvents events;

        public LaneRuleset Rules { get; }
        public LaneState State { get; } = new LaneState();
        public WaveRules Waves { get; }
        public CampRules Camps { get; }
        public CombatServer Combat { get; }

        /// <param name="geometry">The map (walls and floors only), for lines of fire and walking.</param>
        /// <param name="lanes">The map's lanes (MapDef.lanes).</param>
        /// <param name="camps">The map's camps (MapDef.camps).</param>
        public LaneServer(MatchRules match, CombatServer combat, IWorldQuery geometry, IGameEvents events, List<LaneDef> lanes, List<CampDef> camps, ProgressionRules progression = null)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            Rules = (match.Rules.lanes ?? new LaneRuleset()).Clone();

            var problems = Rules.Validate();
            var laneList = new List<LaneDef>();
            foreach (var l in lanes ?? new List<LaneDef>()) { problems.AddRange(l.Validate()); laneList.Add(l.Clone()); }
            var campList = new List<CampDef>();
            var ids = new HashSet<string>();
            foreach (var c in camps ?? new List<CampDef>())
            {
                if (string.IsNullOrEmpty(c.id) || !ids.Add(c.id)) problems.Add("every camp needs its own id ('" + c.id + "')");
                if (c.padRadius <= 0f) problems.Add("camp '" + c.id + "': padRadius must be positive");
                campList.Add(c.Clone());
            }
            if (problems.Count > 0) throw new ArgumentException("Can't run lanes: " + string.Join("; ", problems));

            foreach (var u in Rules.units) combat.Units.Library.Add(u);
            Waves = new WaveRules(match, combat, geometry, events, Rules, State, laneList);
            Camps = new CampRules(match, combat, Waves, events, Rules, State, campList) { Progression = progression };
            events.Subscribe(OnEvent);
        }

        /// <summary>From a map's own lanes and camps.</summary>
        public LaneServer(MatchRules match, CombatServer combat, IWorldQuery geometry, IGameEvents events, MapDef map, ProgressionRules progression = null)
            : this(match, combat, geometry, events, map == null ? null : map.lanes, map == null ? null : map.camps, progression) { }

        public void Dispose() => events.Unsubscribe(OnEvent);

        void OnEvent(GameEvent e) => Camps.OnEvent(e);

        public void Tick(float dt)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            Waves.Tick(dt);
            Camps.Tick(dt);
        }

        public void FillSnapshot(MatchSnapshot s)
        {
            var snap = new LaneSnapshot
            {
                nextWaveIn = State.nextWaveIn, wavesA = State.wavesA, wavesB = State.wavesB,
                juggernautA = State.juggernautA, juggernautB = State.juggernautB,
            };
            foreach (var c in State.camps)
                snap.camps.Add(new CampSnapshot
                {
                    id = c.id, kind = c.kind, phase = c.phase, progress = c.progress, progressTeam = c.progressTeam,
                    contested = c.contested, respawnIn = c.respawnIn, takenBy = c.takenBy,
                });
            foreach (var b in State.boosts) snap.boosts.Add(new CampBoost { playerId = b.playerId, kind = b.kind, timeLeft = b.timeLeft });
            s.lanes = snap;
        }

        /// <summary>Hook into a session, after the combat server has attached: the tick and the snapshots.</summary>
        public void Attach(SessionHost host)
        {
            host.AddTickHandler(Tick);
            host.AddSnapshotFiller(FillSnapshot);
        }
    }
}

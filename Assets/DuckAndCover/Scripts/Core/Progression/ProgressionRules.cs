using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// In-match leveling (WP8). Players earn experience from being in the match, standing on points,
    /// captures, eliminations and supporting teammates; each level offers a choice of upgrades from the
    /// hero's data. Choices can be made at any moment, alive or waiting to respawn, in any order, and
    /// unspent ones wait. Choices are final for the match.
    ///
    /// The server creates one beside <see cref="MatchRules"/> on the same event stream, calls
    /// <see cref="Tick"/> right after every <c>MatchRules.Tick</c>, and routes commands that
    /// MatchRules returns NotHandled for to <see cref="Apply"/>. Combat reports damage and healing
    /// through <see cref="RecordDamage"/> and <see cref="RecordSupport"/>.
    /// </summary>
    public sealed class ProgressionRules
    {
        public ProgressionState State { get; } = new ProgressionState();
        public ProgressionRuleset Rules { get; }
        public HeroCatalog Heroes { get; }

        readonly MatchRules match;
        readonly IWorldQuery world;
        readonly IGameEvents events;

        MatchState M => match.State;

        public ProgressionRules(MatchRules match, HeroCatalog heroes, IWorldQuery world, IGameEvents events)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            Heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Rules = (match.Rules.progression ?? new ProgressionRuleset()).Clone();

            var problems = new List<string>(Rules.Validate());
            if (problems.Count == 0) problems.AddRange(heroes.Validate(Rules));
            if (problems.Count > 0) throw new ArgumentException("Can't run progression: " + string.Join("; ", problems));

            events.Subscribe(OnEvent);
        }

        /// <summary>Stop listening to the event stream (end of match).</summary>
        public void Dispose() => events.Unsubscribe(OnEvent);

        // ---- Server tick ----

        /// <summary>Pay time-based experience for the match time that passed since the last call.</summary>
        public void Tick()
        {
            float dt = M.elapsed - State.paidUntil;
            State.paidUntil = M.elapsed;
            if (dt <= 0f || !M.IsPlaying) return;

            foreach (var p in M.players) Ensure(p.id);
            float average = AverageLevel();

            // Being in the match: everyone connected, alive or waiting.
            foreach (var p in M.players)
                if (p.connected && p.team != TeamId.None) Grant(p, Rules.xpPerSecondInMatch * dt, average);

            // Standing on a live point: held, being taken or being fought over all count.
            var paid = new HashSet<int>();
            foreach (var z in M.zones)
            {
                if (!z.active) continue;
                foreach (int id in world.PlayersInZone(z.id) ?? Array.Empty<int>())
                {
                    var p = M.Player(id);
                    if (p == null || !p.connected || !p.alive || p.team == TeamId.None || !paid.Add(id)) continue;
                    Grant(p, Rules.xpPerSecondOnZone * dt, average);
                }
            }

            State.contributions.RemoveAll(c => c.at < M.elapsed - Rules.assistWindowSeconds);
        }

        // ---- Reports from Combat ----

        /// <summary>An attacker damaged an enemy: they share the experience if the target goes down soon.</summary>
        public void RecordDamage(int attackerId, int targetId)
        {
            var a = M.Player(attackerId);
            var t = M.Player(targetId);
            if (!M.IsPlaying || a == null || t == null || a.team == t.team) return;
            Remember(Contribution.Damage, attackerId, targetId);
        }

        /// <summary>
        /// A player healed or shielded a teammate: experience scaled to the amount, and a share of any
        /// elimination that teammate makes soon. Self-healing and overhealing (amount 0) earn nothing.
        /// </summary>
        public void RecordSupport(int helperId, int allyId, float amount)
        {
            var h = M.Player(helperId);
            var a = M.Player(allyId);
            if (!M.IsPlaying || h == null || a == null || helperId == allyId || h.team != a.team || amount <= 0f) return;
            Ensure(helperId);
            Grant(h, amount * Rules.xpPerSupportPoint, AverageLevel());
            Remember(Contribution.Support, helperId, allyId);
        }

        /// <summary>
        /// Give a player experience from any source (custom upgrade scripts, future objectives).
        /// Catch-up applies. Returns what was added.
        /// </summary>
        public float Grant(int playerId, float amount, ExperienceSource source)
        {
            var p = M.Player(playerId);
            if (!M.IsPlaying || p == null || !p.connected || amount <= 0f) return 0f;
            Ensure(playerId);
            float paid = Grant(p, amount, AverageLevel());
            if (paid > 0f) Publish(new ExperienceAwardedEvent { playerId = playerId, amount = paid, source = source });
            return paid;
        }

        void Remember(int kind, int from, int to)
        {
            foreach (var c in State.contributions)
                if (c.kind == kind && c.from == from && c.to == to) { c.at = M.elapsed; return; }
            State.contributions.Add(new Contribution { kind = kind, from = from, to = to, at = M.elapsed });
        }

        // ---- Commands ----

        /// <summary>Handles <see cref="ChooseUpgradeCommand"/>; NotHandled for everything else.</summary>
        public CommandResult Apply(MatchCommand cmd)
        {
            if (cmd is ChooseUpgradeCommand choose) return Choose(choose.playerId, choose.level, choose.option);
            return CommandResult.NotHandled;
        }

        /// <summary>Take option <paramref name="option"/> at <paramref name="level"/>. Alive or not, any order.</summary>
        public CommandResult Choose(int playerId, int level, int option)
        {
            var p = M.Player(playerId);
            if (p == null || !p.connected) return CommandResult.Reject("unknown player");
            if (M.phase == MatchPhase.MatchEnd) return CommandResult.Reject("the match is over");
            var prog = Ensure(playerId);
            if (level < 1 || level > prog.level) return CommandResult.Reject("level " + level + " not reached yet");
            if (Rules.ChoiceAt(level) == UpgradeKind.None) return CommandResult.Reject("level " + level + " offers no choice");
            if (prog.HasPicked(level)) return CommandResult.Reject("level " + level + " is already chosen");
            var hero = Heroes.Resolve(p.heroId);
            var offer = hero?.Level(level);
            if (offer == null || offer.options == null || offer.options.Length == 0) return CommandResult.Reject("no upgrades for hero '" + p.heroId + "'");
            if (option < 0 || option >= offer.options.Length) return CommandResult.Reject("option " + option + " doesn't exist");

            prog.picks[level - 1] = option;
            prog.pickedIds[level - 1] = offer.options[option];
            Publish(new UpgradeChosenEvent
            {
                playerId = playerId,
                team = p.team,
                heroId = hero.id,
                level = level,
                kind = Rules.ChoiceAt(level),
                option = option,
                upgradeId = offer.options[option],
            });
            return CommandResult.Accepted;
        }

        // ---- Queries ----

        public PlayerProgress Progress(int playerId) => State.Player(playerId);

        public int Level(int playerId) { var p = State.Player(playerId); return p == null ? 1 : p.level; }

        /// <summary>Levels reached whose choice is still open, lowest first (the HUD prompts for the first).</summary>
        public List<int> PendingLevels(int playerId)
        {
            var list = new List<int>();
            var p = State.Player(playerId);
            if (p == null) return list;
            for (int level = 1; level <= p.level; level++)
                if (Rules.ChoiceAt(level) != UpgradeKind.None && !p.HasPicked(level)) list.Add(level);
            return list;
        }

        /// <summary>The upgrades on offer at a level for the player's hero, left first. Empty if none.</summary>
        public UpgradeDef[] Options(int playerId, int level)
        {
            var hero = HeroOf(playerId);
            var offer = hero?.Level(level);
            if (offer == null || offer.options == null) return new UpgradeDef[0];
            var list = new UpgradeDef[offer.options.Length];
            for (int i = 0; i < list.Length; i++) list[i] = hero.Upgrade(offer.options[i]);
            return list;
        }

        /// <summary>Every upgrade the player has taken, by level.</summary>
        public List<UpgradeDef> Chosen(int playerId)
        {
            var list = new List<UpgradeDef>();
            var p = State.Player(playerId);
            var hero = HeroOf(playerId);
            if (p == null || hero == null) return list;
            foreach (var id in p.pickedIds) if (!string.IsNullOrEmpty(id)) list.Add(hero.Upgrade(id));
            return list;
        }

        /// <summary>The ultimate form the player picked, or null: the ultimate is usable once this is set.</summary>
        public UpgradeDef UltimateForm(int playerId)
        {
            foreach (var u in Chosen(playerId)) if (u.Kind == UpgradeKind.Ultimate) return u;
            return null;
        }

        /// <summary>0..1 of the way to the next level (1 at the top level), for the HUD bar.</summary>
        public float LevelFraction(int playerId)
        {
            var p = State.Player(playerId);
            if (p == null) return 0f;
            if (p.level >= Rules.maxLevel) return 1f;
            float from = Rules.xpToReach[p.level - 1], to = Rules.xpToReach[p.level];
            return Math.Max(0f, Math.Min(1f, (p.xp - from) / (to - from)));
        }

        /// <summary>Average level of connected players in the match (1 before anyone joins).</summary>
        public float AverageLevel()
        {
            int n = 0, sum = 0;
            foreach (var p in M.players)
            {
                if (!p.connected || p.team == TeamId.None) continue;
                n++;
                sum += Level(p.id);
            }
            return n == 0 ? 1f : (float)sum / n;
        }

        /// <summary>Copy the progression state into a snapshot taken by MatchRules.</summary>
        public void AddTo(MatchSnapshot snapshot) => snapshot.progression = State.Clone();

        // ---- Experience from match events ----

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case ZoneCapturedEvent cap:
                    Share(cap.players, Rules.xpPerCapture, ExperienceSource.Capture);
                    break;
                case ZoneNeutralisedEvent neu:
                    Share(neu.players, Rules.xpPerNeutralise, ExperienceSource.Neutralise);
                    break;
                case PlayerEliminatedEvent elim:
                    OnElimination(elim);
                    break;
            }
        }

        void Share(int[] ids, float total, ExperienceSource source)
        {
            if (ids == null || ids.Length == 0 || total <= 0f) return;
            var list = new List<PlayerState>();
            foreach (int id in ids)
            {
                var p = M.Player(id);
                if (p != null && p.connected && !list.Contains(p)) list.Add(p);
            }
            if (list.Count == 0) return;
            float average = AverageLevel();
            foreach (var p in list) Award(p, total / list.Count, source, average);
        }

        void OnElimination(PlayerEliminatedEvent e)
        {
            var victim = M.Player(e.playerId);
            float now = M.elapsed;
            if (victim == null || e.disconnected)
            {
                State.contributions.RemoveAll(c => c.kind == Contribution.Damage && c.to == e.playerId);
                return;
            }

            // Who shares it: the attacker, everyone who damaged the target recently, and everyone who
            // recently healed or shielded the attacker. Never the target's own team.
            var share = new List<PlayerState>();
            void Add(int id)
            {
                var p = M.Player(id);
                if (p != null && p.connected && p.team != victim.team && p.team != TeamId.None && !share.Contains(p)) share.Add(p);
            }
            if (e.killerId >= 0) Add(e.killerId);
            foreach (var c in State.contributions)
                if (c.kind == Contribution.Damage && c.to == victim.id && c.at >= now - Rules.assistWindowSeconds - 1e-4f) Add(c.from);
            if (e.killerId >= 0)
                foreach (var c in State.contributions)
                    if (c.kind == Contribution.Support && c.to == e.killerId && c.at >= now - Rules.assistWindowSeconds - 1e-4f) Add(c.from);
            State.contributions.RemoveAll(c => c.kind == Contribution.Damage && c.to == victim.id);

            if (share.Count == 0) return;
            float average = AverageLevel();
            int victimLevel = Level(victim.id);
            foreach (var p in share)
            {
                float bounty = 1f + Rules.bountyPerLevel * (victimLevel - Level(p.id));
                bounty = Math.Max(Rules.bountyMin, Math.Min(Rules.bountyMax, bounty));
                Award(p, Rules.xpPerElimination / share.Count * bounty, ExperienceSource.Elimination, average);
            }
        }

        // ---- Granting ----

        void Award(PlayerState p, float amount, ExperienceSource source, float average)
        {
            float paid = Grant(p, amount, average);
            if (paid > 0f) Publish(new ExperienceAwardedEvent { playerId = p.id, amount = paid, source = source });
        }

        /// <summary>Add experience with catch-up, level up as far as it reaches. Returns what was added.</summary>
        float Grant(PlayerState p, float amount, float average)
        {
            if (amount <= 0f) return 0f;
            var prog = Ensure(p.id);
            if (prog.level <= average - Rules.catchUpLevelGap + 1e-4f) amount *= 1f + Rules.catchUpBonus;
            prog.xp += amount;
            while (prog.level < Rules.maxLevel && prog.xp >= Rules.xpToReach[prog.level])
            {
                prog.level++;
                p.level = prog.level;
                var kind = Rules.ChoiceAt(prog.level);
                var offer = kind == UpgradeKind.None ? null : Heroes.Resolve(p.heroId)?.Level(prog.level);
                Publish(new LevelUpEvent
                {
                    playerId = p.id,
                    team = p.team,
                    level = prog.level,
                    kind = kind,
                    options = offer?.options == null ? new string[0] : (string[])offer.options.Clone(),
                });
            }
            return amount;
        }

        PlayerProgress Ensure(int playerId)
        {
            var prog = State.Player(playerId);
            if (prog != null) return prog;
            prog = new PlayerProgress { playerId = playerId, picks = new int[Rules.maxLevel], pickedIds = new string[Rules.maxLevel] };
            for (int i = 0; i < Rules.maxLevel; i++) { prog.picks[i] = -1; prog.pickedIds[i] = ""; }
            State.players.Add(prog);
            var p = M.Player(playerId);
            if (p != null) p.level = prog.level;
            return prog;
        }

        HeroDef HeroOf(int playerId)
        {
            var p = M.Player(playerId);
            return p == null ? null : Heroes.Resolve(p.heroId);
        }

        void Publish(GameEvent e)
        {
            e.tick = M.tick;
            events.Publish(e);
        }
    }
}

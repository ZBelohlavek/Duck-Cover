using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Everything Progression knows about a match: each player's experience, level and choices, and
    /// the recent damage and help that decide who shares an elimination. Plain serializable data.
    /// </summary>
    [Serializable]
    public class ProgressionState
    {
        public List<PlayerProgress> players = new List<PlayerProgress>();
        /// <summary>Recent damage to a target and help to a teammate, for elimination sharing.</summary>
        public List<Contribution> contributions = new List<Contribution>();
        /// <summary>The match clock (MatchState.elapsed) Progression last paid time-based experience up to.</summary>
        public float paidUntil;

        public PlayerProgress Player(int id)
        {
            foreach (var p in players) if (p.playerId == id) return p;
            return null;
        }

        public ProgressionState Clone()
        {
            var c = (ProgressionState)MemberwiseClone();
            c.players = new List<PlayerProgress>(players.Count);
            foreach (var p in players) c.players.Add(p.Clone());
            c.contributions = new List<Contribution>(contributions.Count);
            foreach (var x in contributions) c.contributions.Add(x.Clone());
            return c;
        }
    }

    /// <summary>One player's experience, level and the option picked at each level.</summary>
    [Serializable]
    public class PlayerProgress
    {
        public int playerId;
        /// <summary>Total experience this match.</summary>
        public float xp;
        public int level = 1;
        /// <summary>
        /// By level (entry 0 is level 1): the option index taken, or -1 when the level offers nothing
        /// or hasn't been chosen yet.
        /// </summary>
        public int[] picks = new int[0];
        /// <summary>Upgrade ids taken, by level like <see cref="picks"/> ("" where none).</summary>
        public string[] pickedIds = new string[0];

        public bool HasPicked(int level) => level >= 1 && level <= picks.Length && picks[level - 1] >= 0;

        public PlayerProgress Clone()
        {
            var c = (PlayerProgress)MemberwiseClone();
            c.picks = (int[])picks.Clone();
            c.pickedIds = (string[])pickedIds.Clone();
            return c;
        }
    }

    /// <summary>
    /// Damage dealt to a target (kind Damage: from = attacker, to = target) or help given to a
    /// teammate (kind Support: from = helper, to = ally), at a match time.
    /// </summary>
    [Serializable]
    public class Contribution
    {
        public const int Damage = 0;
        public const int Support = 1;

        public int kind;
        public int from;
        public int to;
        public float at;

        public Contribution Clone() => (Contribution)MemberwiseClone();
    }
}

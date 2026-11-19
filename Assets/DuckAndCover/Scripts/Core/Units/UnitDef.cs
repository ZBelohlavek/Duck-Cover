using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// One kind of unit that is not a player: its health, size and speed. Plain data, so later
    /// packages can load more kinds (Scrappers, Wardens, Juggernauts, Bastions, camp guards) from data
    /// without new code.
    /// </summary>
    [Serializable]
    public class UnitDef
    {
        public string id = "";
        public string name = "";
        /// <summary>Health before modifiers.</summary>
        public float maxHealth = 200f;
        /// <summary>Hitbox size against a hero's (1 = the same three hitboxes as a hero).</summary>
        public float size = 1f;
        /// <summary>Walking speed in metres a second before slows (the moveSpeed stat multiplies it).</summary>
        public float moveSpeed = 4f;
        /// <summary>Fixed in place (Bastions): pushes and knockbacks don't move it.</summary>
        public bool anchored;

        // ---- Fighting (WP15). A kind with no attackDamage doesn't shoot. ----
        /// <summary>Damage of one shot, through DamageRules.Damage (so the unit's damageDealt stat scales it).</summary>
        public float attackDamage;
        /// <summary>Seconds between shots.</summary>
        public float attackSeconds = 1f;
        /// <summary>Shots reach this far, chest to chest, in metres.</summary>
        public float attackRange = 10f;
        /// <summary>Enemies closer than this are noticed and chased.</summary>
        public float sightRange = 14f;
        /// <summary>
        /// A melee unit (the Warden): it walks up to its target, and its attackRange counts from the target's
        /// outside (<see cref="footprint"/>, or a body's radius) rather than from its feet, so it can reach a Bastion.
        /// </summary>
        public bool melee;
        /// <summary>Metres from the centre to the outside, for melee reach; 0 = a body's radius scaled by size.</summary>
        public float footprint;

        public UnitDef Clone() => (UnitDef)MemberwiseClone();
    }

    /// <summary>A file of unit kinds.</summary>
    [Serializable]
    public class UnitDataFile
    {
        public UnitDef[] units = new UnitDef[0];
    }

    /// <summary>Every unit kind the match knows, by id.</summary>
    public sealed class UnitLibrary
    {
        readonly List<UnitDef> units = new List<UnitDef>();

        public IReadOnlyList<UnitDef> Units => units;

        /// <summary>A hero-sized practice unit with a hero's default health: the WP14 test unit.</summary>
        public const string TrainingDrone = "training_drone";

        public static UnitLibrary From(params UnitDataFile[] files)
        {
            var lib = new UnitLibrary();
            foreach (var f in files)
            {
                if (f == null || f.units == null) continue;
                foreach (var u in f.units) if (u != null) lib.Add(u);
            }
            return lib;
        }

        /// <summary>The kinds every match has until a mode brings its own: the training drone.</summary>
        public static UnitLibrary Starter() => From(new UnitDataFile
        {
            units = new[] { new UnitDef { id = TrainingDrone, name = "Training drone", maxHealth = 200f, size = 1f, moveSpeed = 4f } },
        });

        /// <summary>Add a kind, replacing one with the same id.</summary>
        public void Add(UnitDef def)
        {
            if (def == null) return;
            units.RemoveAll(u => u.id == def.id);
            units.Add(def.Clone());
        }

        public UnitDef Unit(string id)
        {
            foreach (var u in units) if (u.id == id) return u;
            return null;
        }

        /// <summary>Problems with the data, empty when usable.</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            var seen = new HashSet<string>();
            foreach (var u in units)
            {
                if (string.IsNullOrEmpty(u.id)) { e.Add("a unit kind has no id"); continue; }
                if (!seen.Add(u.id)) e.Add("unit kind '" + u.id + "' is listed twice");
                if (u.maxHealth <= 0f) e.Add(u.id + ": maxHealth must be positive");
                if (u.size <= 0f) e.Add(u.id + ": size must be positive");
                if (u.moveSpeed < 0f) e.Add(u.id + ": moveSpeed can't be negative");
                if (u.attackDamage < 0f) e.Add(u.id + ": attackDamage can't be negative");
                if (u.attackDamage > 0f && (u.attackSeconds <= 0f || u.attackRange <= 0f || u.sightRange < u.attackRange))
                    e.Add(u.id + ": a unit that attacks needs positive attackSeconds and attackRange, and sightRange at least attackRange");
            }
            return e;
        }
    }
}

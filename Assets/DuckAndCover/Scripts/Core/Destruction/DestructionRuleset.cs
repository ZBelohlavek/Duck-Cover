using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Every number the Destruction and Breach modules use. Starting values from the plan; tune here,
    /// never in rules code. Lives on <see cref="Ruleset.destruction"/>. Saved with JsonUtility, so
    /// public fields and arrays only.
    /// </summary>
    [Serializable]
    public class DestructionRuleset
    {
        // ---- Grid ----

        /// <summary>Edge of one cell in metres.</summary>
        public float cellSize = 0.25f;

        /// <summary>Hit points of a fresh cell by effective material (patched cells count as barricade,
        /// reinforced cells as hard). At most 255.</summary>
        public int hardHp = 120;
        public int softHp = 60;
        public int barricadeHp = 30;

        /// <summary>An intact cell shows as cracked once its hit points fall to this share of the maximum.</summary>
        public float crackAtFraction = 0.5f;

        /// <summary>Damage each source deals to one cell of each material. A source missing from the
        /// table does nothing.</summary>
        public SourceDamage[] damage =
        {
            new SourceDamage { source = DamageSource.Bullet,         hard = 0,   soft = 20,  barricade = 10 },
            new SourceDamage { source = DamageSource.Melee,          hard = 0,   soft = 30,  barricade = 30 },
            new SourceDamage { source = DamageSource.Grenade,        hard = 0,   soft = 60,  barricade = 30 },
            new SourceDamage { source = DamageSource.StandardCharge, hard = 0,   soft = 255, barricade = 255 },
            new SourceDamage { source = DamageSource.HeavyCharge,    hard = 255, soft = 255, barricade = 255 },
        };

        // ---- Damage shapes ----

        /// <summary>Grenade blast radius on a surface, metres.</summary>
        public float grenadeRadius = 0.5f;
        /// <summary>Standard charge: a crouch-size hole, metres.</summary>
        public float standardChargeWidth = 0.75f;
        public float standardChargeHeight = 1.0f;
        /// <summary>Heavy charge: a door-size hole, metres.</summary>
        public float heavyChargeWidth = 1.0f;
        public float heavyChargeHeight = 2.0f;

        // ---- Bullets peek, charges open ----

        /// <summary>Small damage (bullets, melee, grenades) may open at most this share of any one wall or
        /// hatch. Past it, cells crack without opening. Door and window boards are exempt.</summary>
        public float peekCapFraction = 0.15f;
        /// <summary>Small damage may not grow a connected hole past this many cells (4 = a 50 cm square),
        /// so a hole a player can walk through always needs a charge.</summary>
        public int peekMaxHoleCells = 4;
        /// <summary>The smallest opening a player fits through, metres. Used to tell peek holes from breaches.</summary>
        public float passableWidth = 0.75f;
        public float passableHeight = 1.0f;

        // ---- Team budgets ----

        /// <summary>Breach points per round for the attacking team.</summary>
        public int attackBreachPoints = 12;
        /// <summary>Breach points per round for the defending team, for their own rotation holes.</summary>
        public int defenseBreachPoints = 2;
        public int standardChargeCost = 2;
        public int heavyChargeCost = 4;

        /// <summary>Fortify points per round for the defending team.</summary>
        public int fortifyPoints = 8;
        public int patchCost = 1;
        public int reinforceCost = 2;
        /// <summary>The largest hole one patch closes, in cells (32 = a door-size hole).</summary>
        public int patchMaxCells = 32;
        /// <summary>A reinforcement covers this width of wall, full height, metres.</summary>
        public float reinforceWidth = 1.0f;

        // ---- Zone integrity ----

        /// <summary>A zone locks when its integrity falls to this share (60% standing).</summary>
        public float zoneFloor = 0.6f;
        /// <summary>Zones around charge sites lock earlier.</summary>
        public float siteZoneFloor = 0.75f;
        /// <summary>Patched cells count as this share restored (half the lost integrity comes back).</summary>
        public float patchIntegrityRefund = 0.5f;
        /// <summary>The wall meter warns when a zone is within this share of its floor.</summary>
        public float integrityWarnMargin = 0.1f;

        /// <summary>Damage <paramref name="source"/> deals to a cell of <paramref name="material"/>.</summary>
        public int DamageTo(DamageSource source, SurfaceMaterial material)
        {
            if (damage == null) return 0;
            foreach (var d in damage)
            {
                if (d.source != source) continue;
                switch (material)
                {
                    case SurfaceMaterial.Hard: return d.hard;
                    case SurfaceMaterial.Soft: return d.soft;
                    case SurfaceMaterial.Barricade: return d.barricade;
                    default: return 0;
                }
            }
            return 0;
        }

        public int MaxHp(SurfaceMaterial material)
        {
            switch (material)
            {
                case SurfaceMaterial.Hard: return hardHp;
                case SurfaceMaterial.Soft: return softHp;
                case SurfaceMaterial.Barricade: return barricadeHp;
                default: return 0;
            }
        }

        public int ChargeCost(DamageSource source) =>
            source == DamageSource.HeavyCharge ? heavyChargeCost : source == DamageSource.StandardCharge ? standardChargeCost : 0;

        /// <summary>Metres to whole cells, rounding to the nearest cell and never below one.</summary>
        public int Cells(float metres) => Math.Max(1, (int)Math.Round(metres / cellSize));

        /// <summary>Adds a message for every value that would break the rules.</summary>
        public void Validate(List<string> errors)
        {
            if (cellSize <= 0f) errors.Add("destruction.cellSize must be positive");
            foreach (var (name, hp) in new[] { ("hardHp", hardHp), ("softHp", softHp), ("barricadeHp", barricadeHp) })
                if (hp < 1 || hp > 255) errors.Add("destruction." + name + " must be 1..255");
            if (crackAtFraction < 0f || crackAtFraction > 1f) errors.Add("destruction.crackAtFraction must be 0..1");
            if (peekCapFraction < 0f || peekCapFraction > 1f) errors.Add("destruction.peekCapFraction must be 0..1");
            if (peekMaxHoleCells < 0) errors.Add("destruction.peekMaxHoleCells must not be negative");
            if (Cells(passableWidth) * Cells(passableHeight) <= peekMaxHoleCells)
                errors.Add("destruction.peekMaxHoleCells must be smaller than a passable hole, or bullets could open a way through");
            if (zoneFloor <= 0f || zoneFloor > 1f || siteZoneFloor <= 0f || siteZoneFloor > 1f)
                errors.Add("destruction zone floors must be in (0, 1]");
            if (patchIntegrityRefund < 0f || patchIntegrityRefund > 1f) errors.Add("destruction.patchIntegrityRefund must be 0..1");
            foreach (var (name, v) in new[] { ("attackBreachPoints", attackBreachPoints), ("defenseBreachPoints", defenseBreachPoints),
                         ("fortifyPoints", fortifyPoints), ("standardChargeCost", standardChargeCost), ("heavyChargeCost", heavyChargeCost),
                         ("patchCost", patchCost), ("reinforceCost", reinforceCost), ("patchMaxCells", patchMaxCells) })
                if (v < 0) errors.Add("destruction." + name + " must not be negative");
            if (damage != null)
                foreach (var d in damage)
                    if (d.hard < 0 || d.soft < 0 || d.barricade < 0) errors.Add("destruction.damage for " + d.source + " must not be negative");
        }
    }

    /// <summary>One row of <see cref="DestructionRuleset.damage"/>.</summary>
    [Serializable]
    public struct SourceDamage
    {
        public DamageSource source;
        public int hard;
        public int soft;
        public int barricade;
    }
}

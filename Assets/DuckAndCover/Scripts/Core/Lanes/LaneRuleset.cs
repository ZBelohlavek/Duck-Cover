using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public partial class Ruleset
    {
        /// <summary>Waves, minions and neutral camps (WP15). Cloned by the lane rules at the start of a match.</summary>
        public LaneRuleset lanes = new LaneRuleset();
    }

    /// <summary>What a wave holds from a minute of the match on (the last row whose minute has come is used).</summary>
    [Serializable]
    public class WaveRow
    {
        public float fromMinute;
        public int scrappers = 3;
        public int wardens = 1;

        public WaveRow() { }
        public WaveRow(float fromMinute, int scrappers, int wardens) { this.fromMinute = fromMinute; this.scrappers = scrappers; this.wardens = wardens; }
    }

    /// <summary>The numbers for one kind of neutral camp.</summary>
    [Serializable]
    public class CampKindDef
    {
        public CampKind kind;
        /// <summary>The unit kind of its guards.</summary>
        public string guardType = LaneRuleset.CampGuard;
        /// <summary>
        /// Kinds by guard, in spot order and repeating, for a camp with mixed guards (the Damage camp's
        /// Mossback with its Thornbacks). Empty means every guard is <see cref="guardType"/>.
        /// </summary>
        public string[] guardMix = new string[0];
        /// <summary>Guards placed around the pad when the map gives no spots.</summary>
        public int guards = 2;
        /// <summary>Seconds one hero needs on the pad, once the guards are down, to take it.</summary>
        public float captureSeconds = 5f;
        /// <summary>Seconds after it is taken until its guards are back.</summary>
        public float respawnSeconds = 120f;
        /// <summary>The boost (Damage: damageDealt share; Speed: moveSpeed share), 0.1 = +10%.</summary>
        public float boost;
        /// <summary>How long the boost lasts.</summary>
        public float boostSeconds;
        /// <summary>Experience split between the heroes who helped (Experience camps).</summary>
        public float experience;

        /// <summary>The kind of the camp's guard at this spot.</summary>
        public string GuardTypeAt(int index) =>
            guardMix != null && guardMix.Length > 0 ? guardMix[index % guardMix.Length] : guardType;

        public CampKindDef Clone()
        {
            var c = (CampKindDef)MemberwiseClone();
            c.guardMix = (string[])(guardMix ?? new string[0]).Clone();
            return c;
        }
    }

    /// <summary>
    /// The numbers for lanes, waves and neutral camps (WP15): the minion kinds, how often waves leave
    /// and what they hold, how minions fight, and each camp kind's guards, timers and reward. All
    /// starting values from the approved Overrun plan, to tune in the balance pass.
    /// </summary>
    [Serializable]
    public class LaneRuleset
    {
        public const string Scrapper = "scrapper";
        public const string Warden = "warden";
        public const string Juggernaut = "juggernaut";
        public const string CampGuard = "camp_guard";
        public const string CampBrute = "camp_brute";
        /// <summary>Speed camp: Reedrunners, fast jungle birds.</summary>
        public const string CampRunner = "camp_runner";
        /// <summary>Experience camp: corrupted Scrappers, lane minions the jungle grew over.</summary>
        public const string CampHusk = "camp_husk";
        /// <summary>Juggernaut camp: Rootjaw, one boss beast.</summary>
        public const string CampBoss = "camp_boss";

        /// <summary>The ability id minion and guard shots carry in damage events.</summary>
        public const string AttackId = "minion_attack";
        /// <summary>The ability id of the Warden's shield.</summary>
        public const string WardenShieldId = "warden_shield";

        // ---- Waves ----
        /// <summary>Seconds from the start of play to the first wave.</summary>
        public float firstWaveSeconds = 0f;
        /// <summary>
        /// Seconds between waves, on a fixed timer (nobody calls a wave). Sized on Breakwater so that with no
        /// heroes in the way the next waves reach the spot where the last pair met about 5 s after the last
        /// minion there fell: a wave walks ~28 s to the middle and two mirrored waves take ~30 s to wear each
        /// other out (`report wavepace`, checked by WavePaceTests).
        /// </summary>
        public float waveSeconds = 36f;
        public WaveRow[] waveRows = { new WaveRow(0f, 3, 1) };
        /// <summary>No new minions for a team that has this many alive (keeps a lane match near 40 units).</summary>
        public int maxMinionsPerTeam = 20;
        /// <summary>Metres between minions standing side by side in a wave.</summary>
        public float waveSpacing = 1.4f;
        /// <summary>A minion walks toward the lane point this far ahead of where it is.</summary>
        public float lookAhead = 3f;
        /// <summary>Minions stop this far short of the far end of the lane (the enemy base).</summary>
        public float laneEndStop = 2f;
        /// <summary>A minion checks this far past its step for cover in the way, and turns before it touches it.</summary>
        public float steerProbe = 0.6f;
        /// <summary>Turns, in degrees either side, a minion tries to get round cover, in order.</summary>
        public float[] steerAngles = { 30f, 60f, 90f, 120f };

        // ---- Wardens ----
        public float wardenPulseSeconds = 4f;
        public float wardenShield = 25f;
        public float wardenShieldSeconds = 5f;
        public float wardenRadius = 6f;

        // ---- Camp guards ----
        /// <summary>Guards only fight heroes within this distance of their camp's pad.</summary>
        public float guardLeash = 14f;
        /// <summary>Metres from the pad's centre for guards placed around it.</summary>
        public float guardRing = 2.5f;

        /// <summary>Minion and guard kinds, given to the unit library when the lane rules start.</summary>
        public UnitDef[] units =
        {
            new UnitDef { id = Scrapper, name = "Scrapper", maxHealth = 150f, size = 0.7f, moveSpeed = 4f, attackDamage = 12f, attackSeconds = 1f, attackRange = 10f, sightRange = 14f },
            new UnitDef { id = Warden, name = "Warden", maxHealth = 200f, size = 0.85f, moveSpeed = 4f, attackDamage = 6f, attackSeconds = 1f, attackRange = 10f, sightRange = 14f },
            new UnitDef { id = Juggernaut, name = "Juggernaut", maxHealth = 1200f, size = 1.15f, moveSpeed = 3.5f, attackDamage = 40f, attackSeconds = 1.5f, attackRange = 8f, sightRange = 14f },
            new UnitDef { id = CampGuard, name = "Thornback", maxHealth = 300f, size = 0.9f, moveSpeed = 0f, attackDamage = 15f, attackSeconds = 1f, attackRange = 12f, sightRange = 12f },
            new UnitDef { id = CampBrute, name = "Mossback", maxHealth = 500f, size = 1.3f, moveSpeed = 0f, attackDamage = 20f, attackSeconds = 1.2f, attackRange = 12f, sightRange = 12f },
            new UnitDef { id = CampRunner, name = "Reedrunner", maxHealth = 150f, size = 0.95f, moveSpeed = 0f, attackDamage = 8f, attackSeconds = 0.6f, attackRange = 10f, sightRange = 12f },
            new UnitDef { id = CampHusk, name = "Corrupted Scrapper", maxHealth = 300f, size = 0.7f, moveSpeed = 0f, attackDamage = 12f, attackSeconds = 1f, attackRange = 12f, sightRange = 12f },
            new UnitDef { id = CampBoss, name = "Rootjaw", maxHealth = 3600f, size = 2f, moveSpeed = 0f, attackDamage = 70f, attackSeconds = 1.4f, attackRange = 12f, sightRange = 12f },
        };

        /// <summary>One entry per camp kind.</summary>
        public CampKindDef[] camps =
        {
            new CampKindDef { kind = CampKind.Juggernaut, guardType = CampBoss, guards = 1, captureSeconds = 5f, respawnSeconds = 240f },
            new CampKindDef { kind = CampKind.Damage, guardType = CampGuard, guardMix = new[] { CampBrute, CampGuard, CampGuard }, guards = 3, captureSeconds = 5f, respawnSeconds = 180f, boost = 0.10f, boostSeconds = 90f },
            new CampKindDef { kind = CampKind.Experience, guardType = CampHusk, guards = 2, captureSeconds = 5f, respawnSeconds = 120f, experience = 150f },
            new CampKindDef { kind = CampKind.Speed, guardType = CampRunner, guards = 2, captureSeconds = 5f, respawnSeconds = 90f, boost = 0.15f, boostSeconds = 60f },
        };

        public CampKindDef Camp(CampKind kind)
        {
            foreach (var c in camps) if (c.kind == kind) return c;
            return null;
        }

        /// <summary>The wave makeup at a match minute.</summary>
        public WaveRow RowAt(float minute)
        {
            WaveRow row = null;
            foreach (var r in waveRows) if (r.fromMinute <= minute && (row == null || r.fromMinute >= row.fromMinute)) row = r;
            return row ?? new WaveRow(0f, 0, 0);
        }

        public LaneRuleset Clone()
        {
            var c = (LaneRuleset)MemberwiseClone();
            c.steerAngles = (float[])(steerAngles ?? new float[0]).Clone();
            c.waveRows = Array.ConvertAll(waveRows ?? new WaveRow[0], r => new WaveRow(r.fromMinute, r.scrappers, r.wardens));
            c.units = Array.ConvertAll(units ?? new UnitDef[0], u => u.Clone());
            c.camps = Array.ConvertAll(camps ?? new CampKindDef[0], k => k.Clone());
            return c;
        }

        /// <summary>Problems with the numbers, empty when usable.</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            if (firstWaveSeconds < 0f) e.Add("firstWaveSeconds can't be negative");
            if (waveSeconds <= 0f) e.Add("waveSeconds must be positive");
            if (waveRows == null || waveRows.Length == 0) e.Add("waveRows needs at least one row");
            else foreach (var r in waveRows) if (r.scrappers < 0 || r.wardens < 0) { e.Add("a wave can't hold a negative count"); break; }
            if (maxMinionsPerTeam < 1) e.Add("maxMinionsPerTeam must be at least 1");
            if (steerProbe < 0f || steerAngles == null) e.Add("steerProbe can't be negative and steerAngles needs a list");
            if (waveSpacing <= 0f || lookAhead <= 0f || laneEndStop < 0f) e.Add("waveSpacing and lookAhead must be positive, laneEndStop not negative");
            if (wardenPulseSeconds <= 0f || wardenShield < 0f || wardenShieldSeconds <= 0f || wardenRadius < 0f) e.Add("Warden numbers are wrong");
            if (guardLeash <= 0f || guardRing < 0f) e.Add("guard numbers are wrong");

            var lib = UnitLibrary.From(new UnitDataFile { units = units ?? new UnitDef[0] });
            e.AddRange(lib.Validate());
            foreach (var id in new[] { Scrapper, Warden, Juggernaut })
                if (lib.Unit(id) == null) e.Add("units needs a '" + id + "' kind");

            foreach (CampKind kind in Enum.GetValues(typeof(CampKind)))
            {
                var c = Camp(kind);
                if (c == null) { e.Add("camps needs an entry for " + kind); continue; }
                if (lib.Unit(c.guardType) == null) e.Add(kind + " camp: unknown guard kind '" + c.guardType + "'");
                foreach (var g in c.guardMix ?? new string[0])
                    if (lib.Unit(g) == null) e.Add(kind + " camp: unknown guard kind '" + g + "' in guardMix");
                if (c.guards < 1 || c.captureSeconds <= 0f || c.respawnSeconds < 0f) e.Add(kind + " camp: needs a guard, positive captureSeconds and respawnSeconds not negative");
                if ((kind == CampKind.Damage || kind == CampKind.Speed) && (c.boost <= 0f || c.boostSeconds <= 0f)) e.Add(kind + " camp: needs a positive boost and boostSeconds");
                if (kind == CampKind.Experience && c.experience <= 0f) e.Add("Experience camp: needs positive experience");
            }
            return e;
        }
    }
}

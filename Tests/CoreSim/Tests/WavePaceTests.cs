using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// Wave pace on Breakwater (Zach, Nov 19): waves leave on a constant timer, sized so that with no
/// heroes in the way the next pair of waves reaches the spot where the last pair met about
/// <see cref="TargetGap"/> seconds after the last minion of that clash fell.
/// </summary>
static class WavePaceTests
{
    public const float TargetGap = 5f;

    /// <summary>One clash of mirrored waves with no heroes near the lane.</summary>
    public sealed class Pace
    {
        public float walk;      // seconds from a wave leaving to its first shot
        public float fight;     // seconds from the first shot to the last minion of the pair falling
        public float clashZ;    // where the last minion fell
        public float gap;       // seconds from that fall until the next wave reaches that spot
        public int survivors;   // minions of the first pair left standing
    }

    public static Pace Measure(float waveSeconds)
    {
        var r = OverrunHarness.QuickRules();
        r.lanes.waveSeconds = waveSeconds;
        r.lanes.firstWaveSeconds = 1f; // after the harness has started play, so the events are heard
        r.overrun.latePushMinute = 1000f;
        var h = new OverrunHarness(r, waves: true, perTeam: 1);
        var first = new HashSet<int>();
        var second = new HashSet<int>();
        float t = 0f, firstShot = -1f, lastFall = -1f, arrive = -1f, z = 0f;
        float spawnAt = -1f;
        h.Events.Subscribe(e =>
        {
            if (e is WaveSpawnedEvent w)
            {
                if (w.wave == 1) { foreach (var id in w.unitIds) first.Add(id); if (spawnAt < 0f) spawnAt = t; }
                else if (w.wave == 2) foreach (var id in w.unitIds) second.Add(id);
            }
            else if (e is UnitDestroyedEvent d && first.Contains(d.unitId)) { lastFall = t; z = d.position.z; }
            else if (e is DamagedEvent g && first.Contains(g.attackerId) && firstShot < 0f) firstShot = t;
        });
        int steps = (int)(120f / OverrunHarness.Dt);
        for (int i = 0; i < steps && arrive < 0f; i++)
        {
            h.Tick();
            t += OverrunHarness.Dt;
            bool firstDone = first.Count > 0 && first.All(id => { var u = h.Units.Unit(id); return u == null || !u.alive; });
            if (!firstDone) continue;
            // The second pair reaches the clash spot: its leading minion gets within a body of it.
            foreach (var id in second)
            {
                var u = h.Units.Unit(id);
                if (u != null && u.alive && Math.Abs(u.position.z - z) < 2f) { arrive = t; break; }
            }
        }
        return new Pace
        {
            walk = firstShot - spawnAt, fight = lastFall - firstShot, clashZ = z,
            gap = arrive < 0f ? float.NaN : arrive - lastFall,
            survivors = first.Count(id => { var u = h.Units.Unit(id); return u != null && u.alive; }),
        };
    }

    [Test]
    static void NextWavesReachTheClashAboutFiveSecondsAfterItEnds()
    {
        var rules = Ruleset.Default.lanes;
        var p = Measure(rules.waveSeconds);
        Check.Equal(0, p.survivors, "mirrored waves wipe each other out");
        Check.True(Math.Abs(p.clashZ) < 15f, "they meet near the middle (z " + p.clashZ.ToString("0.0") + ")");
        Check.True(Math.Abs(p.gap - TargetGap) <= 1.5f, "next waves arrive " + p.gap.ToString("0.0") + " s after the clash ends (target " + TargetGap + " s)");
    }

    /// <summary>dotnet run --project Tests/CoreSim -- report wavepace [seconds ...]: the clash and the gap for wave timers.</summary>
    public static int Report(string[] args)
    {
        var tries = args.Length > 0 ? args.Select(a => float.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray()
                                    : new[] { Ruleset.Default.lanes.waveSeconds, 20f, 25f, 30f };
        Console.WriteLine("Mirrored waves on Breakwater, no heroes near. Gap = next waves reach the clash spot after its last minion fell.");
        Console.WriteLine("  timer    walk   fight   clash z   gap");
        foreach (var s in tries)
        {
            var p = Measure(s);
            Console.WriteLine($"  {s,5:0.0}s  {p.walk,5:0.0}s  {p.fight,5:0.0}s  {p.clashZ,7:0.0}   {p.gap,4:0.0}s");
        }
        return 0;
    }
}

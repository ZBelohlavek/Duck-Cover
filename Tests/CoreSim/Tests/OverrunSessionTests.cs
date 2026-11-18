using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// Overrun over the network (WP19): the map each mode plays on, the unit kinds clients draw by, and a
/// whole scripted Overrun match on a loopback network with Breakwater's lane, camps and Bastions.
/// </summary>
static class OverrunSessionTests
{
    [Test]
    static void EachModePlaysItsMap()
    {
        Check.Equal(Breakwater.Id, MapCatalog.MapIdForMode(ModeDef.Overrun().id), "Overrun plays Breakwater");
        foreach (var mode in new[] { ModeDef.CaptureAndHold(), ModeDef.KingOfTheHill(), ModeDef.FlagRush() })
            Check.Equal(GantryYard.Id, MapCatalog.MapIdForMode(mode.id), mode.id + " plays Gantry Yard");
        var map = MapCatalog.ForMode("overrun");
        Check.True(map != null && map.IsLaneMap, "the Overrun map has a lane");
        Check.True(map.bastions.Count == 4, "and four Bastions");
        Check.True(map.dummies.Count >= 2, "and spots for practice dummies");
        Check.True(ModeDef.Overrun().Validate(map.ToMatchMap()).Count == 0, "Overrun runs on it: " + string.Join("; ", ModeDef.Overrun().Validate(map.ToMatchMap())));
        Check.Equal("Breakwater", MapCatalog.DisplayName(Breakwater.Id), "Breakwater's menu name");
        Check.Equal("Gantry Yard", MapCatalog.DisplayName(GantryYard.Id), "Gantry Yard's menu name");
    }

    [Test]
    static void ClientsFindUnitKindsInTheRuleset()
    {
        var r = Ruleset.Default;
        foreach (var id in new[] { LaneRuleset.Scrapper, LaneRuleset.Warden, LaneRuleset.Juggernaut, LaneRuleset.CampGuard, LaneRuleset.CampBrute, OverrunRuleset.ForwardBastion, OverrunRuleset.CoreBastion })
        {
            var k = r.UnitKind(id);
            Check.True(k != null && k.id == id && k.size > 0f, "kind " + id + " is known with a size");
        }
        Check.True(r.UnitKind("no_such_unit") == null, "an unknown kind is null");
        Check.True(r.UnitKind(LaneRuleset.Juggernaut).size > 1f && r.UnitKind(LaneRuleset.Scrapper).size < 1f, "a Juggernaut is bigger than a hero, a Scrapper smaller");
    }

    [Test]
    static void ScriptedOverrunMatchOverTheNetwork()
    {
        var h = new SessionHarness(ModeDef.Overrun(), heroes: true);
        var scripts = new List<ScriptedPlayer>();
        for (int i = 0; i < 4; i++)
        {
            var c = h.Join();
            var s = new ScriptedPlayer(c, 4);
            scripts.Add(s);
            c.SnapshotReceived += s.Step;
        }
        int mostUnits = 0, biggest = 0;
        int limit = (int)((ModeDef.Overrun().timeCapSeconds + 120f) * h.Host.Settings.tickRate);
        for (int i = 0; i < limit && h.Match.State.phase != MatchPhase.MatchEnd; i++)
        {
            h.Host.Tick();
            h.Net.Pump();
            var snap = h.Clients[0].Snapshot;
            if (snap != null && snap.units != null && snap.units.Count > mostUnits)
            {
                mostUnits = snap.units.Count;
                biggest = Math.Max(biggest, WireCodec.Encode(new SnapshotMessage { snapshot = h.Host.MakeSnapshot() }).Length);
            }
        }
        h.Ticks(4);

        var s0 = h.Match.State;
        var events = h.Events.History;
        Check.Equal(MatchPhase.MatchEnd, s0.phase, "the Overrun match finished");
        Check.Equal(TeamId.A, s0.winner, "team A, which pushes with its waves, won");
        Check.True(events.OfType<WaveSpawnedEvent>().Count() >= 20, "waves walked: " + events.OfType<WaveSpawnedEvent>().Count());
        Check.True(mostUnits >= 30, "the lane filled up: at most " + mostUnits + " units in one state copy");
        Check.True(biggest < 8000, "a state copy with " + mostUnits + " units is " + biggest + " bytes");
        Check.True(events.OfType<UnitDestroyedEvent>().Any(e => e.killerId > 0 && !UnitIds.IsUnit(e.killerId)), "heroes shot minions down");
        Check.True(events.OfType<DamagedEvent>().Any(e => UnitIds.IsUnit(e.attackerId) && e.abilityId == OverrunRuleset.AttackId), "Bastions fired");
        Check.True(events.OfType<ExperienceAwardedEvent>().Any(e => e.source == ExperienceSource.Overrun), "Overrun experience was paid");
        Check.False(events.OfType<ZoneCapturedEvent>().Any(), "no zone was ever captured");

        var overrun = h.Clients[0].Snapshot.overrun;
        Check.True(overrun != null && overrun.bastions.Count == 4, "clients see the four Bastions");
        string truth = SessionDigest.Of(s0, overrun);
        Check.True(truth.Contains("bastion A Core alive=True"), "the digest lists the Bastions:\n" + truth);
        for (int i = 0; i < h.Clients.Count; i++)
        {
            Check.Equal(truth, SessionDigest.Of(h.Clients[i].State, h.Clients[i].Snapshot.overrun), "client " + (i + 1) + " agrees with the host");
            Check.True(scripts[i].Finished, "client " + (i + 1) + " saw the end");
        }
        Console.WriteLine("          Overrun over the network: " + s0.endReason + " at " + s0.elapsed.ToString("0") + " s, score " + s0.scoreA + ":" + s0.scoreB +
                          ", most units " + mostUnits + " (" + biggest + " bytes a state copy)");
    }

    [Test]
    static void LaneCommandOnlyInOverrun()
    {
        var h = new SessionHarness(ModeDef.KingOfTheHill(), heroes: true);
        var a = h.Join();
        h.Join();
        h.Send(a, new StartMatchCommand());
        h.RunTo(MatchPhase.Play);
        var rejected = new List<string>();
        a.Events.Subscribe(e => { if (e is CommandRejectedEvent r) rejected.Add(r.command); });
        h.Send(a, new ScriptedToLaneCommand());
        Check.True(rejected.Contains(nameof(ScriptedToLaneCommand)), "outside Overrun nobody runs to the lane");
    }
}

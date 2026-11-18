using System.Linq;
using DuckAndCover.Core;
using static MatchHarness;

/// <summary>Experience sources, levels, catch-up and the choice rules (WP8).</summary>
static class ProgressionTests
{
    [Test]
    static void ProgressionDefaultsMatchThePlan()
    {
        var r = new ProgressionRuleset();
        Check.Equal(0, r.Validate().Count, "defaults are valid: " + string.Join("; ", r.Validate()));
        Check.Equal(20, r.maxLevel, "levels 1 to 20");
        Check.Equal("2,4,6,8,10,12,14,16,18,20", string.Join(",", r.ChoiceLevels()), "a pick every other level");
        Check.Equal("2,6,14", string.Join(",", r.ChoiceLevels().Where(l => r.ChoiceAt(l) == UpgradeKind.Trait)), "traits");
        Check.Equal("4,12,16,18", string.Join(",", r.ChoiceLevels().Where(l => r.ChoiceAt(l) == UpgradeKind.Talent)), "talents");
        Check.Equal(UpgradeKind.Ultimate, r.ChoiceAt(8), "ultimate form at 8");
        Check.Equal(8, Ruleset.Default.ultimateUnlockLevel, "the ultimate unlocks with its form");
        Check.Equal(UpgradeKind.Passive, r.ChoiceAt(10), "the passive at 10, halfway");
        Check.Equal(UpgradeKind.Capstone, r.ChoiceAt(20), "capstone at 20");
        Check.Equal(9, r.BoostLevels(20), "nine levels give the automatic boost");
        Check.Equal(0, r.BoostLevels(2), "none by level 2");
        Check.Equal(4900, r.xpToReach[19], "level 20 needs what level 10 needed before");
        Check.Equal(UpgradeKind.None, r.ChoiceAt(1), "level 1 is the base kit");
        Check.Equal(10, r.ChoiceLevels().Count, "ten choices");
        Check.Equal(2, r.optionsPerLevel, "two options each");
        Check.Near(5f, r.xpPerSecondOnZone, "on a point");
        Check.Near(2f, r.xpPerSecondInMatch, "being in the match");
        Check.Near(100f, r.xpPerCapture, "capture");
        Check.Near(100f, r.xpPerElimination, "elimination");
        Check.Equal(4, r.catchUpLevelGap, "catch-up from four levels below");
        Check.Near(0.25f, r.catchUpBonus, "catch-up bonus");
        Check.True(Ruleset.Default.progression != null, "the ruleset carries progression numbers");
    }

    [Test]
    static void ProgressionRulesetValidation()
    {
        var r = new ProgressionRuleset { maxLevel = 10, xpToReach = new[] { 0, 100, 100, 300, 400, 500, 600, 700, 800, 900 },
            levelChoices = new[] { "", "trait", "", "", "", "", "", "", "", "" } };
        Check.True(r.Validate().Any(e => e.Contains("rise")), "thresholds must rise");
        r = new ProgressionRuleset { maxLevel = 10, xpToReach = new[] { 0, 100, 200, 300, 400, 500, 600, 700, 800, 900 },
            levelChoices = new[] { "", "trait", "perk", "", "", "", "", "", "", "" } };
        Check.True(r.Validate().Any(e => e.Contains("perk")), "unknown kind");
        r = new ProgressionRuleset { maxLevel = 5 };
        Check.True(r.Validate().Any(e => e.Contains("xpToReach")), "thresholds per level");
        var rules = Ruleset.Default;
        rules.progression.catchUpLevelGap = 0;
        Check.True(ThrowsOnCreate(rules), "a match won't start with bad progression numbers");
    }

    static bool ThrowsOnCreate(Ruleset rules)
    {
        try { new ProgressionHarness(rules: rules, toPlay: false); return false; }
        catch (System.ArgumentException) { return true; }
    }

    [Test]
    static void NoExperienceBeforePlay()
    {
        var p = new ProgressionHarness(toPlay: false);
        p.Match.StartMatch();
        p.RunUntil(() => p.S.phase == MatchPhase.Deploy, 60f);
        p.Run(5f);
        Check.Near(0f, p.Xp(p.A[0]), "nothing during hero pick and deploy");
    }

    [Test]
    static void EveryoneEarnsForBeingInTheMatch()
    {
        var p = new ProgressionHarness();
        p.Match.EliminatePlayer(p.B[0], p.A[0]);
        float before = p.Xp(p.B[0]);
        p.Run(5f);
        Check.Near(10f, p.Xp(p.A[1]) , "2 per second", 0.05f);
        Check.Near(10f, p.Xp(p.B[0]) - before, "waiting to respawn still earns", 0.05f);
        p.Match.RemovePlayer(p.B[1]);
        float left = p.Xp(p.B[1]);
        p.Run(5f);
        Check.Near(left, p.Xp(p.B[1]), "a player who left earns nothing");
    }

    [Test]
    static void StandingOnAPointEarns()
    {
        var p = new ProgressionHarness();
        p.World.Put(p.A[0], Mid);
        p.World.Put(p.B[0], NearB); // B's own point: holding it counts too
        p.World.Put(p.A[1], Mid);
        p.Match.EliminatePlayer(p.A[1], p.B[2]);
        p.Run(4f); // short of a capture
        Check.Near(4f * 7f, p.Xp(p.A[0]), "5 on the point plus 2 in the match", 0.1f);
        Check.Near(4f * 7f, p.Xp(p.B[0]), "holding your own point counts", 0.1f);
        Check.True(p.Xp(p.A[1]) < 4f * 2.5f, "a downed player on the point earns only the match rate");
        Check.Near(8f, p.Xp(p.A[2]), "off the point: match rate only", 0.1f);
    }

    [Test]
    static void CaptureIsShared()
    {
        var p = new ProgressionHarness();
        p.World.Put(p.A.Take(2), Mid);
        p.RunUntil(() => p.H.Zone(Mid).owner == TeamId.A, 30f);
        var awards = p.Of<ExperienceAwardedEvent>().Where(e => e.source == ExperienceSource.Capture).ToList();
        Check.Equal(2, awards.Count, "both capturers paid");
        Check.True(awards.All(e => System.Math.Abs(e.amount - 50f) < 0.01f), "100 shared by two");

        p.World.ClearAll();
        p.World.Put(p.B.Take(1), Mid);
        p.RunUntil(() => p.H.Zone(Mid).owner == TeamId.None, 30f);
        var neu = p.Of<ExperienceAwardedEvent>().Where(e => e.source == ExperienceSource.Neutralise).ToList();
        Check.Equal(1, neu.Count, "neutraliser paid");
        Check.Near(100f, neu[0].amount, "the whole 100 for one");
    }

    [Test]
    static void EliminationIsShared()
    {
        var p = new ProgressionHarness();
        int victim = p.B[0], killer = p.A[0], damager = p.A[1], healer = p.A[2], stale = p.A[3];
        p.P.RecordDamage(stale, victim);
        p.Run(11f); // older than the 10 s window
        p.P.RecordDamage(damager, victim);
        p.P.RecordDamage(p.B[1], victim); // friendly fire is ignored
        p.P.RecordSupport(healer, killer, 20f);
        p.Run(1f);
        p.Match.EliminatePlayer(victim, killer);
        var paid = p.Of<ExperienceAwardedEvent>().Where(e => e.source == ExperienceSource.Elimination).ToList();
        Check.Equal("1,2,3", string.Join(",", paid.Select(e => e.playerId).OrderBy(i => i)), "attacker, damager and the attacker's healer");
        Check.True(paid.All(e => System.Math.Abs(e.amount - 100f / 3f) < 0.01f), "100 split three ways");

        // The damage record is used up; the next elimination of the same player pays the killer alone.
        p.Run(20f);
        p.Match.EliminatePlayer(victim, killer);
        var second = p.Of<ExperienceAwardedEvent>().Where(e => e.source == ExperienceSource.Elimination).Skip(3).ToList();
        Check.Equal(1, second.Count, "only the attacker");
        Check.Near(100f, second[0].amount, "the whole 100");

        // Leaving isn't an elimination anyone earns from.
        p.P.RecordDamage(killer, p.B[1]);
        p.Match.RemovePlayer(p.B[1]);
        Check.Equal(4, p.Of<ExperienceAwardedEvent>().Count(e => e.source == ExperienceSource.Elimination), "nothing for a disconnect");
    }

    [Test]
    static void SupportExperienceScalesWithAmount()
    {
        var p = new ProgressionHarness();
        float before = p.Xp(p.A[0]);
        p.P.RecordSupport(p.A[0], p.A[1], 40f);
        Check.Near(10f, p.Xp(p.A[0]) - before, "0.25 per point healed");
        before = p.Xp(p.A[0]);
        p.P.RecordSupport(p.A[0], p.A[0], 40f);
        p.P.RecordSupport(p.A[0], p.B[0], 40f);
        p.P.RecordSupport(p.A[0], p.A[1], 0f);
        Check.Near(before, p.Xp(p.A[0]), "no experience for self, enemies or overhealing");
    }

    [Test]
    static void BountyFollowsLevels()
    {
        var p = new ProgressionHarness();
        p.RaiseTo(p.B[0], 9);
        p.Match.EliminatePlayer(p.B[0], p.A[0]);
        var high = p.Of<ExperienceAwardedEvent>().Last(e => e.source == ExperienceSource.Elimination);
        Check.Near(140f, high.amount, "eight levels above: +40%", 0.01f);

        p.RaiseTo(p.A[1], 17);
        p.Match.EliminatePlayer(p.B[1], p.A[1]);
        var low = p.Of<ExperienceAwardedEvent>().Last(e => e.source == ExperienceSource.Elimination);
        Check.Near(50f, low.amount, "sixteen levels below: floored at half", 0.01f);
    }

    [Test]
    static void TrailingPlayersCatchUp()
    {
        var p = new ProgressionHarness(perTeam: 2);
        foreach (var id in new[] { p.A[0], p.A[1], p.B[0] }) p.RaiseTo(id, 7);
        // Average (7+7+7+1)/4 = 5.5: B[1] at level 1 is four or more below.
        float a = p.Xp(p.A[0]), b = p.Xp(p.B[1]);
        p.Run(10f);
        Check.Near(20f, p.Xp(p.A[0]) - a, "level 7 earns the normal rate", 0.1f);
        Check.Near(25f, p.Xp(p.B[1]) - b, "the trailing player earns 25% more", 0.1f);
    }

    [Test]
    static void LevelsRiseAtThresholds()
    {
        var p = new ProgressionHarness();
        int id = p.A[0];
        var t = p.P.Rules.xpToReach;
        p.Give(id, t[1] - 1f);
        Check.Equal(1, p.Level(id), "just short of level 2");
        p.Give(id, 1f);
        Check.Equal(2, p.Level(id), "level 2 at its threshold");
        Check.Equal(2, p.S.Player(id).level, "the match sees the level");
        var up = p.Of<LevelUpEvent>().Last();
        Check.Equal(UpgradeKind.Trait, up.kind, "level 2 offers a trait");
        Check.Equal("sturdy,quick_step", string.Join(",", up.options), "options from the hero's data, left first");

        p.Give(id, t[4] - p.Xp(id) + 1f);
        Check.Equal(5, p.Level(id), "one big grant can raise several levels");
        Check.Equal("3,4,5", string.Join(",", p.Of<LevelUpEvent>().Skip(1).Select(e => e.level)), "one event per level");
        Check.Equal("2,4", string.Join(",", p.P.PendingLevels(id)), "every choice waits (odd levels offer none)");

        p.Give(id, 100000f);
        Check.Equal(20, p.Level(id), "capped at 20");
        Check.Near(1f, p.P.LevelFraction(id), "the bar is full at the top");
    }

    [Test]
    static void ChoicesAtAnyTimeInAnyOrder()
    {
        var p = new ProgressionHarness();
        int id = p.A[0];
        p.RaiseTo(id, 7);
        p.Match.EliminatePlayer(id, p.B[0]);
        Check.False(p.S.Player(id).alive, "waiting to respawn");

        Check.True(p.P.Apply(new ChooseUpgradeCommand { playerId = id, level = 4, option = 1 }).IsAccepted, "choose while waiting, out of order");
        Check.True(p.P.Apply(new ChooseUpgradeCommand { playerId = id, level = 2, option = 0 }).IsAccepted, "then an earlier level");
        var chosen = p.Of<UpgradeChosenEvent>();
        Check.Equal("short_dash,sturdy", string.Join(",", chosen.Select(e => e.upgradeId)), "the picked upgrades");
        Check.Equal(UpgradeKind.Talent, chosen[0].kind, "kind on the event");

        Check.False(p.P.Choose(id, 2, 1).IsAccepted, "choices are final");
        Check.False(p.P.Choose(id, 8, 0).IsAccepted, "level not reached");
        Check.False(p.P.Choose(id, 1, 0).IsAccepted, "level 1 offers nothing");
        Check.False(p.P.Choose(id, 3, 0).IsAccepted, "nor do odd levels");
        Check.False(p.P.Choose(id, 6, 2).IsAccepted, "no third option");
        Check.False(p.P.Choose(999, 2, 0).IsAccepted, "unknown player");
        Check.Equal(CommandStatus.NotHandled, p.P.Apply(new PingCommand { playerId = id }).status, "other commands pass through");
        Check.Equal("6", string.Join(",", p.P.PendingLevels(id)), "the rest still wait");

        p.H.Run(0f);
        p.RunUntil(() => p.S.Player(id).alive, 30f, "respawn");
        Check.True(p.P.Choose(id, 6, 0).IsAccepted, "and while alive");
        Check.Equal("sturdy,short_dash,fast_hands", string.Join(",", p.P.Chosen(id).Select(u => u.id)), "build in level order");
    }

    [Test]
    static void UltimateNeedsItsForm()
    {
        var p = new ProgressionHarness();
        int id = p.A[0];
        p.RaiseTo(id, 7);
        Check.True(p.P.UltimateForm(id) == null, "no ultimate before level 8");
        p.RaiseTo(id, 8);
        Check.True(p.P.UltimateForm(id) == null, "level 8 reached, form not chosen");
        Check.Equal("long_overdrive,wide_overdrive", string.Join(",", p.P.Options(id, 8).Select(u => u.id)), "two forms");
        p.P.Choose(id, 8, 1);
        Check.Equal("wide_overdrive", p.P.UltimateForm(id)?.id, "chosen form");
    }

    [Test]
    static void RespawnWaitGrowsWithLevel()
    {
        var p = new ProgressionHarness();
        int id = p.A[0];
        p.RaiseTo(id, 11);
        p.Match.EliminatePlayer(id, p.B[0]);
        var e = p.Of<PlayerEliminatedEvent>().Last();
        Check.Near(p.S.elapsed + 8f + 5f, e.respawnAt, "8 s plus half a second per level above 1");
    }

    [Test]
    static void NoChoicesAfterTheMatch()
    {
        var p = new ProgressionHarness(perTeam: 1);
        p.RaiseTo(p.A[0], 2);
        p.World.Put(p.A[0], NearA);
        p.World.Put(p.A[0], Mid);
        p.RunTo(MatchPhase.MatchEnd, 1200f);
        Check.False(p.P.Choose(p.A[0], 2, 0).IsAccepted, "rejected once the match is over");
        float xp = p.Xp(p.B[0]);
        p.Run(5f);
        Check.Near(xp, p.Xp(p.B[0]), "no experience after the match");
    }

    [Test]
    static void UnknownHeroUsesFallback()
    {
        var p = new ProgressionHarness(perTeam: 1, toPlay: false);
        p.Match.StartMatch();
        p.Match.Apply(new PickHeroCommand { playerId = p.A[0], heroId = "not_authored_yet" });
        p.RunTo(MatchPhase.Play);
        p.RaiseTo(p.A[0], 2);
        Check.True(p.P.Choose(p.A[0], 2, 0).IsAccepted, "the fallback hero's options");

        var none = new ProgressionHarness(perTeam: 1, heroes: new HeroCatalog(ProgressionHarness.DataCatalog().heroes));
        none.RaiseTo(none.A[0], 2);
        Check.Equal(2, none.Level(none.A[0]), "still levels without hero data");
        Check.Equal(0, none.Of<LevelUpEvent>().Last().options.Length, "no options to show");
        Check.False(none.P.Choose(none.A[0], 2, 0).IsAccepted, "nothing to choose");
    }

    [Test]
    static void RejoiningKeepsProgress()
    {
        var p = new ProgressionHarness();
        int id = p.B[2];
        p.RaiseTo(id, 3);
        p.P.Choose(id, 2, 1);
        p.Match.RemovePlayer(id);
        p.Run(3f);
        Check.True(p.Match.AddPlayer(id, TeamId.B).IsAccepted, "rejoins");
        Check.Equal(3, p.Level(id), "level kept");
        Check.Equal(3, p.S.Player(id).level, "the match agrees");
        Check.Equal("quick_step", p.P.Chosen(id).Single().id, "choices kept");
    }

    [Test]
    static void SnapshotCarriesProgression()
    {
        var p = new ProgressionHarness();
        p.RaiseTo(p.A[0], 3);
        p.P.Choose(p.A[0], 2, 0);
        var snap = p.Match.TakeSnapshot();
        p.P.AddTo(snap);
        Check.Equal(3, snap.progression.Player(p.A[0]).level, "level in the snapshot");
        Check.Equal("sturdy", snap.progression.Player(p.A[0]).pickedIds[1], "choice in the snapshot");
        p.P.Choose(p.A[0], 3, 0);
        p.Give(p.A[0], 1000f);
        Check.Equal(-1, snap.progression.Player(p.A[0]).picks[2], "a deep copy: later picks don't leak in");
        Check.Equal(3, snap.progression.Player(p.A[0]).level, "a deep copy: later levels don't leak in");
    }

    [Test]
    static void SameInputsSameProgression()
    {
        string Play()
        {
            var p = new ProgressionHarness();
            p.World.Put(p.A.Take(3), Mid);
            p.World.Put(p.B.Take(2), NearB);
            p.Run(30f);
            p.World.ClearAll();
            p.World.Put(p.B.Take(4), Mid);
            for (int i = 0; i < 6; i++)
            {
                p.P.RecordDamage(p.B[i % 4], p.A[i % 5]);
                p.Match.EliminatePlayer(p.A[i % 5], p.B[i % 4]);
                p.Run(15f);
            }
            p.Run(120f);
            return p.Log() + "\n" + string.Join(",", p.P.State.players.Select(x => x.playerId + ":" + x.level + ":" + x.xp.ToString("F2")));
        }
        string first = Play();
        Check.True(first.Contains("LevelUpEvent"), "players levelled");
        Check.Equal(first, Play(), "same inputs, same events and experience");
    }
}

/// <summary>A small batch of the bot-match simulation, as a guard on pacing (the full table is `report progression`).</summary>
static class ProgressionSimTests
{
    [Test]
    static void BotMatchesLevelOnThePlansPace()
    {
        var s = ProgressionSim.Run(24, 7);
        Check.True(s.medianLevel5 > 180f && s.medianLevel5 < 330f, "the ultimate (level 8) near minute 4: " + s.medianLevel5 + " s");
        Check.True(s.medianLevel10 > 600f && s.medianLevel10 < 780f, "level 20 near minute 12 for those who get there: " + s.medianLevel10 + " s");
        Check.True(s.averageFinalLevel > 16f, "players end near the top: " + s.averageFinalLevel);
        Check.True(s.supportVsOthers > 0.8f && s.supportVsOthers < 1.25f, "support levels at about the same pace: " + s.supportVsOthers);
        Check.True(s.eliminationsPerMinute > 2f, "fights happen: " + s.eliminationsPerMinute + " per minute");
    }

    [Test]
    static void BotMatchIsRepeatableAndBotsSpendTheirChoices()
    {
        var a = ProgressionSim.Play(3, keepLog: true);
        var b = ProgressionSim.Play(3, keepLog: true);
        Check.Equal(a.log, b.log, "same seed, same match");
        Check.True(a.log != ProgressionSim.Play(4, keepLog: true).log, "another seed, another match");
        Check.True(a.winner != TeamId.None, "the match ends with a winner");
        Check.True(a.picks >= 60, "bots chose upgrades through the match: " + a.picks);
        Check.True(a.pendingAtEnd <= 6, "few choices left waiting at the end: " + a.pendingAtEnd);
    }
}

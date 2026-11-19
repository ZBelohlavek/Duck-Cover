using System;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// Skulk, the goblin jungler: his Scrap Rifle, Shrapnel Charge (E), the Jungle Stalker passive (in F's
/// place: faster in the jungle, rewards for finishing camp creatures) and Serrated Knife (X), whose kills
/// add notches for the match. His two paths: Jungle (left) and Headhunter (right). Also the rules he
/// brings for everyone: the jungle condition, the arc shape and kill rewards.
/// </summary>
static class SkulkKitTests
{
    const int Shrapnel = 0, Knife = 1;
    const string Drone = UnitLibrary.TrainingDrone;

    /// <summary>Skulk at the origin facing +z and a Bulwark (300 health) 2 m ahead, with no shields.</summary>
    static HeroHarness Duel(params string[] upgrades)
    {
        var h = With(A1, "skulk", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 2f);
        foreach (var u in upgrades) Check.True(h.Heroes.ApplyUpgrade(A1, "skulk", u).IsAccepted, u);
        h.NoShield(B1);
        h.Forget();
        return h;
    }

    /// <summary>Damage one knife slash does to the Bulwark now (the cooldown is skipped).</summary>
    static float Slash(HeroHarness h)
    {
        Ready(h, Knife);
        h.NoShield(B1);
        float before = h.Health(B1);
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "slash");
        return before - h.Health(B1);
    }

    static void Ready(HeroHarness h, int slot)
    {
        var s = h.Unit(A1).slots[slot];
        s.charges = Math.Max(1, s.charges);
        s.rechargeLeft = 0f;
    }

    /// <summary>Bring the Bulwark back to life, full and in front of Skulk, after a kill.</summary>
    static void Revive(HeroHarness h)
    {
        h.Run(h.R.respawnBaseSeconds + h.R.respawnWaveSeconds + 1f);
        if (!h.S.IsAlive(B1)) h.Run(5f);
        Check.True(h.S.IsAlive(B1), "the Bulwark is back");
        h.Place(B1, 0f, 2f);
        h.NoShield(B1);
    }

    /// <summary>Knife the Bulwark down from low health: one kill by the knife.</summary>
    static void KnifeKill(HeroHarness h)
    {
        h.Unit(B1).health = 10f;
        Ready(h, Knife);
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "finishing slash");
        Check.False(h.S.IsAlive(B1), "the Bulwark goes down");
    }

    static int Notches(HeroHarness h) => h.Unit(A1).slots[Knife].notches;

    // ---- The kit ----

    [Test]
    static void TheKitIsShrapnelAPassiveAndTheKnife()
    {
        var h = Duel();
        var unit = h.Unit(A1);
        Check.Equal("skulk_shrapnel,skulk_knife", string.Join(",", unit.slots.Select(s => s.abilityId)), "E and X in the slots");
        Check.Equal("skulk_jungle_stalker", unit.passiveId, "Jungle Stalker is his base passive");
        Check.Equal("scrap_rifle", h.Combat.Weapons.Weapons(A1).weapons[0], "the Scrap Rifle in hand");
        Check.Equal(1, h.Combat.Weapons.Weapons(A1).weapons.Length, "and no other gun");
        Check.False(h.Abilities.AbilityIn(A1, Knife).ultimate, "the knife runs on a cooldown");
        Check.Equal(AbilityKeys.E, AbilityKeys.ForSlot(Shrapnel, 2, true), "E is Shrapnel Charge");
        Check.Equal(AbilityKeys.X, AbilityKeys.ForSlot(Knife, 2, true), "X is the knife");
        Check.Equal(-1, AbilityKeys.SlotFor(AbilityKeys.F, 2, true), "F is the passive's place: no key");
        Check.Equal(AbilityKeys.F, AbilityKeys.ForSlot(1, 2, false), "a two-slot hero without a base passive keeps F");
        Check.Equal(AbilityKeys.X, AbilityKeys.ForSlot(2, 3, true), "and a three-slot hero keeps X last");
    }

    [Test]
    static void ShrapnelChargeBurstsAfterItsFuse()
    {
        var h = Duel();
        h.Place(B1, 0f, 6f);
        h.Place(B2, 0f, 11f);
        h.NoShield(B2);
        float near = h.Health(B1), far = h.Health(B2);
        Check.True(h.Use(A1, Shrapnel, Ahead).IsAccepted, "toss");
        h.Run(0.4f);
        Check.Near(near, h.Health(B1), "nothing before the fuse");
        h.Run(0.4f);
        Check.Near(near - 60f, h.Health(B1), "60 damage within 3 m of where it lands (8 m ahead)", 0.01f);
        Check.Near(far, h.Health(B2), "nothing 3 m past the edge");
        Check.Equal("on cooldown", h.Use(A1, Shrapnel, Ahead).reason, "one charge");
    }

    [Test]
    static void TheKnifeSweepsAnArcInFront()
    {
        var h = Duel();
        h.Place(B1, 1.5f, 1.5f);  // 45 degrees off the aim, 2.1 m away: in the 110 degree arc
        h.Place(B2, 0f, -2f);     // behind him
        h.NoShield(B2);
        float side = h.Health(B1), back = h.Health(B2);
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "slash");
        Check.Near(side - 45f, h.Health(B1), "45 damage to the enemy beside the aim, once", 0.01f);
        Check.Near(back, h.Health(B2), "nobody behind him is cut");
        var used = h.Last<AbilityUsedEvent>();
        Check.Equal(1, used.targets.Length, "one target");

        var far = Duel();
        far.Place(B1, 0f, 4f);
        float before = far.Health(B1);
        Check.True(far.Use(A1, Knife, Ahead).IsAccepted, "slash at 4 m");
        Check.Near(before, far.Health(B1), "out of reach at 4 m");

        var arc = new AbilityDef { id = "a", cooldown = 1f, targeting = new TargetingDef { shape = Shapes.Arc }, effects = new[] { new EffectDef { kind = EffectKinds.Damage, amount = 1f } } };
        var lib = AbilityLibrary.From(new AbilityDataFile { abilities = new[] { arc } });
        Check.True(lib.Validate().Any(e => e.Contains("an arc needs a range")), "an arc without reach is caught");
    }

    // ---- Notches ----

    [Test]
    static void KnifeKillsAddNotchesThatLastTheMatch()
    {
        var h = Duel();
        Check.Near(45f, Slash(h), "45 with no notches");
        KnifeKill(h);
        Check.Equal(1, Notches(h), "a hero killed by the knife adds a notch");
        Check.True(h.Of<KillRewardEvent>().Any(e => e.abilityId == "skulk_knife" && e.victim == KillVictims.Hero && e.notches == 1), "announced");
        Revive(h);
        Check.Near(48f, Slash(h), "each notch adds 3");

        // Skulk going down keeps them.
        h.Combat.Damage.Damage(B1, A1, 1000f);
        Check.False(h.S.IsAlive(A1), "Skulk goes down");
        h.Run(h.R.respawnBaseSeconds + h.R.respawnWaveSeconds + 3f);
        Check.True(h.S.IsAlive(A1), "and comes back");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 2f);
        Check.Equal(1, Notches(h), "the notch stays through his death");

        // A kill by anything else gives none (without Head Count).
        h.Unit(B1).health = 5f;
        h.Run(0.3f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "rifle shot");
        Check.False(h.S.IsAlive(B1), "rifle kill");
        Check.Equal(1, Notches(h), "the rifle's kill adds no notch");

        // The cap: ten.
        h.Unit(A1).slots[Knife].notches = 10;
        Revive(h);
        KnifeKill(h);
        Check.Equal(10, Notches(h), "never more than ten");
        Revive(h);
        Check.Near(75f, Slash(h), "45 + 10 x 3");
    }

    [Test]
    static void MinionsAndCampCreaturesCountBastionsDont()
    {
        var h = Duel();
        h.Place(B1, 40f, 40f);
        int minion = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 2f), 180f);
        h.Unit(minion).health = 10f;
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "slash the minion");
        Check.False(h.Combat.Damage.IsAlive(minion), "minion down");
        Check.Equal(1, Notches(h), "a minion counts");
        Check.False(h.Unit(A1).HasStatus("skulk_spoils"), "but a minion is not a camp: no Spoils");

        int camp = h.Combat.Units.Spawn(Drone, TeamId.None, new Vec3(0f, 0f, 2f), 180f);
        h.Unit(camp).health = 10f;
        Ready(h, Knife);
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "slash the camp creature");
        Check.Equal(2, Notches(h), "a camp creature counts");

        int bastion = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 2f), 180f);
        h.Combat.Damage.Roster.Get(bastion).anchored = true;
        h.Unit(bastion).health = 10f;
        Ready(h, Knife);
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "slash a Bastion");
        Check.False(h.Combat.Damage.IsAlive(bastion), "it falls");
        Check.Equal(2, Notches(h), "a Bastion is no trophy");
    }

    // ---- Jungle Stalker ----

    [Test]
    static void FinishingCampCreaturesHealsAndGivesSpoils()
    {
        var h = Duel();
        h.Place(B1, 40f, 40f);
        h.Unit(A1).health = 100f;
        int camp = h.Combat.Units.Spawn(Drone, TeamId.None, new Vec3(0f, 0f, 9f), 180f);
        h.Unit(camp).health = 5f;
        h.Run(0.3f);
        Check.True(h.FireAt(A1, Hitboxes.Chest(h.R, h.Combat.Damage.Roster.Get(camp).Pose())).IsAccepted, "rifle at the camp creature");
        Check.False(h.Combat.Damage.IsAlive(camp), "finished off");
        Check.Near(135f, h.Health(A1), "heals 35", 0.01f);
        Check.True(h.Unit(A1).HasStatus("skulk_spoils"), "Spoils");
        Check.Near(1.1f, h.Stat(A1, StatIds.DamageDealt), "10% more damage", 0.001f);
        h.Run(15.2f);
        Check.Near(1f, h.Stat(A1, StatIds.DamageDealt), "for 15 seconds", 0.001f);

        // Killing a hero gives nothing from the passive without Trophy Hunter.
        h.Place(B1, 0f, 2f);
        h.Unit(A1).health = 100f;
        KnifeKill(h);
        Check.Near(100f, h.Health(A1), "no heal for a hero");
        Check.False(h.Unit(A1).HasStatus("skulk_spoils"), "no Spoils for a hero");
    }

    [Test]
    static void JungleStalkerIsFasterInTheJungle()
    {
        var map = Breakwater.Build();
        var jungle = new JungleArea(map, new Ruleset());
        var lane = map.lanes[0];
        Check.False(jungle.Contains(lane.PointAt(lane.Length * 0.3f)), "the lane is not jungle");
        Check.False(jungle.Contains(lane.PointAt(lane.Length * 0.5f) + new Vec3(map.laneWidth * 0.4f, 0f, 0f)), "nor its edges");
        foreach (var c in map.camps) Check.True(jungle.Contains(c.pad + new Vec3(3f, 0f, 0f)), "camp " + c.id + "'s clearing is jungle");
        foreach (var r in map.routes)
        {
            var a = r.points[r.points.Count / 2 - 1];
            var b = r.points[r.points.Count / 2];
            var mid = a + (b - a) * 0.5f;
            lane.Project(mid, 0f, float.MaxValue, out float off);
            if (off > map.laneWidth * 0.5f) Check.True(jungle.Contains(mid), "route " + r.name + " is jungle");
        }
        var core = map.Bastion(TeamId.A, BastionTier.Core);
        Check.False(jungle.Contains(core.position + new Vec3(-8f, 0f, -6f)), "the base plaza is not jungle");
        Check.True(JungleArea.For(GantryYard.Build(), new Ruleset()) == null, "Gantry Yard has no jungle");

        // On a map with a jungle: only there.
        var h = Duel();
        var at = map.camps[0].pad;
        h.Combat.Damage.Jungle = jungle;
        h.Place(A1, at.x, at.z);
        Check.Near(1.15f, h.Combat.Damage.MoveSpeed(A1), "15% faster in a camp clearing", 0.001f);
        var onLane = lane.PointAt(lane.Length * 0.3f);
        h.Place(A1, onLane.x, onLane.z);
        Check.Near(1f, h.Combat.Damage.MoveSpeed(A1), "normal speed on the lane", 0.001f);

        // On a map without one: anywhere off a live point.
        var g = Duel();
        Check.Near(1.15f, g.Combat.Damage.MoveSpeed(A1), "no jungle: faster off the points", 0.001f);
        var zone = g.S.zones.First(z => z.active);
        g.Map.OnZone[A1] = zone.id;
        Check.Near(1f, g.Combat.Damage.MoveSpeed(A1), "and normal on one", 0.001f);
    }

    // ---- Left: the Jungle path ----

    [Test]
    static void JunglePathGrowsTheJungleBuffsAndCampRewards()
    {
        var h = Duel("skulk_trailwise", "skulk_thick_hide", "skulk_undergrowth", "skulk_jungle_king");
        Check.Near(1.30f, h.Combat.Damage.MoveSpeed(A1), "Trailwise and Jungle King: 15 + 5 + 10%", 0.001f);
        Check.Near(0.92f, h.Stat(A1, StatIds.DamageTaken), "Thick Hide", 0.001f);
        Check.Near(1.2f, h.Stat(A1, StatIds.CooldownRate), "Undergrowth", 0.001f);
        Check.Near(1.15f, h.Stat(A1, StatIds.DamageDealt), "Jungle King's damage", 0.001f);
        var zone = h.S.zones.First(z => z.active);
        h.Map.OnZone[A1] = zone.id;
        Check.Near(1f, h.Combat.Damage.MoveSpeed(A1), "none of it on a point", 0.001f);

        var b = Duel("skulk_big_game", "skulk_rich_pickings");
        b.Place(B1, 40f, 40f);
        b.Unit(A1).health = 100f;
        int camp = b.Combat.Units.Spawn(Drone, TeamId.None, new Vec3(0f, 0f, 2f), 180f);
        b.Unit(camp).health = 5f;
        Check.True(b.Use(A1, Knife, Ahead).IsAccepted, "knife the camp creature");
        Check.Near(160f, b.Health(A1), "Big Game: heals 35 + 25", 0.01f);
        Check.Near(1.2f, b.Stat(A1, StatIds.DamageDealt), "Rich Pickings: Spoils is 20%", 0.001f);
        b.Run(20f);
        Check.Near(1.2f, b.Stat(A1, StatIds.DamageDealt), "and still on after 20 s", 0.001f);
        b.Run(5.5f);
        Check.Near(1f, b.Stat(A1, StatIds.DamageDealt), "gone after 25 s", 0.001f);
    }

    [Test]
    static void JunglePathKnifeAndShrapnelUpgrades()
    {
        var h = Duel("skulk_field_dressing", "skulk_trophy_rack", "skulk_satchel", "skulk_lurker");
        h.Unit(A1).health = 100f;
        KnifeKill(h);
        Check.Near(130f, h.Health(A1), "Field Dressing: a knife kill heals 30", 0.01f);
        Check.Equal(15, h.Abilities.AbilityIn(A1, Knife).maxNotches, "Trophy Rack: 15 notches");
        Check.Equal(2, h.Unit(A1).slots[Shrapnel].charges, "Satchel: two Shrapnel Charges");
        Check.Near(8f, h.Stat(A1, StatIds.HealthRegen), "Lurker: regen in the jungle", 0.001f);
    }

    // ---- Right: the Headhunter path ----

    [Test]
    static void HeadhunterPathFeedsOnHeroKills()
    {
        var h = Duel("skulk_head_count", "skulk_trophy_hunter");
        h.Unit(A1).health = 100f;
        h.Unit(B1).health = 5f;
        h.Run(0.3f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "rifle kill");
        Check.False(h.S.IsAlive(B1), "down");
        Check.Equal(1, Notches(h), "Head Count: a rifle kill on a hero notches the knife");
        Check.Near(135f, h.Health(A1), "Trophy Hunter: a hero kill heals 35", 0.01f);
        Check.True(h.Unit(A1).HasStatus("skulk_spoils"), "and gives Spoils");
        Revive(h);
        KnifeKill(h);
        Check.Equal(2, Notches(h), "a knife kill adds one, not two");

        var c = Duel("skulk_headhunter", "skulk_keen_edge");
        KnifeKill(c);
        Check.Equal(2, Notches(c), "Headhunter: two notches for a hero");
        Check.Equal(10, c.Abilities.AbilityIn(A1, Knife).maxNotches, "still 10 notches at most");
        Revive(c);
        Check.Near(45f + 2f * 5f, Slash(c), "Keen Edge: each notch is worth 5");
    }

    [Test]
    static void BloodletterAndShredder()
    {
        var h = Duel("skulk_bloodletter");
        float before = h.Health(B1);
        Check.True(h.Use(A1, Knife, Ahead).IsAccepted, "slash");
        h.Run(4.1f);
        Check.Near(before - 45f - 4f * 6f, h.Health(B1), "Bloodletter: 6 a second for 4 seconds on top", 0.5f);

        var s = Duel("skulk_shredder");
        s.Place(B1, 0f, 8f);
        Check.True(s.Use(A1, Shrapnel, Ahead).IsAccepted, "toss");
        s.Run(0.7f);
        Check.Near(1.15f, s.Stat(B1, StatIds.DamageTaken), "Shredder: 15% more damage taken", 0.001f);
        s.Run(4.1f);
        Check.Near(1f, s.Stat(B1, StatIds.DamageTaken), "for 4 seconds", 0.001f);
    }

    [Test]
    static void KillRewardsAreCheckedAndChangedByTalents()
    {
        var lib = DataLibrary();
        Check.Equal(0, lib.Validate().Count, "data is valid");
        var bad = new AbilityDef
        {
            id = "bad", cooldown = 1f, targeting = new TargetingDef(), effects = new[] { new EffectDef { kind = EffectKinds.Heal, amount = 1f } },
            onKill = new[] { new KillRewardDef { victims = "dragon", scope = "sometimes", notches = 1 } },
        };
        var errors = AbilityLibrary.From(new AbilityDataFile { abilities = new[] { bad } }).Validate();
        Check.True(errors.Any(e => e.Contains("unknown victim 'dragon'")), "unknown victims");
        Check.True(errors.Any(e => e.Contains("unknown scope")), "unknown scopes");
        Check.True(errors.Any(e => e.Contains("holds none")), "notches on an ability without room for them");

        Check.Equal(null, AbilityParams.Check(new AbilityParamChange { ability = "skulk_knife", param = "kill.heroes.notches", op = ModOp.Add, value = 1f }, lib), "kill.NAME.notches");
        Check.Equal(null, AbilityParams.Check(new AbilityParamChange { ability = "skulk_jungle_stalker", param = "kill.effect.heal.amount", op = ModOp.Add, value = 1f }, lib), "kill.effect.KEY.amount");
        Check.Equal(null, AbilityParams.Check(new AbilityParamChange { ability = "skulk_jungle_stalker", param = AbilityParams.Enable, op = ModOp.Set, text = "trophyHunter" }, lib), "a kill reward's tag");
        Check.True(AbilityParams.Check(new AbilityParamChange { ability = "skulk_shrapnel", param = "maxNotches", op = ModOp.Add, value = 1f }, lib) != null, "no notches on Shrapnel Charge");
    }

    // ---- Zach's rules for every kit: cooldowns, no one-use kills ----

    [Test]
    static void NoOneUseKillsAFullHealthHero()
    {
        // Level 20 down each path, the knife at its most notches and Spoils up: one knife (with its bleed)
        // or one Shrapnel Charge never takes a full-health 200 hero down on its own.
        foreach (var path in new[] { 0, 1 })
        {
            var h = Duel();
            var hero = ProgressionHarness.DataCatalog().Get("skulk");
            foreach (var l in hero.levels) Check.True(h.Heroes.ApplyUpgrade(A1, "skulk", l.options[path]).IsAccepted, l.options[path]);
            h.Heroes.ApplyLevelBoost(A1, 20);
            var knife = h.Abilities.AbilityIn(A1, Knife);
            h.Unit(A1).slots[Knife].notches = knife.maxNotches;
            h.Abilities.ApplyStatus(A1, "skulk_spoils", A1, path == 0 ? 0.2f : 0.1f, 30f);
            foreach (int slot in new[] { Shrapnel, Knife })
            {
                h.Place(B1, 0f, slot == Knife ? 2f : 8f);
                h.Unit(B1).health = 200f;
                h.NoShield(B1);
                Ready(h, slot);
                Check.True(h.Use(A1, slot, Ahead).IsAccepted, "use");
                h.Run(4.5f);
                float dealt = 200f - h.Health(B1);
                if (Environment.GetEnvironmentVariable("SKULK_TRACE") == "1") Console.WriteLine("          path " + path + " slot " + slot + ": " + dealt);
                Check.True(h.S.IsAlive(B1) && dealt < 200f, (path == 0 ? "Jungle" : "Headhunter") + " path, slot " + slot + ": " + dealt.ToString("0") + " of 200");
                if (!h.S.IsAlive(B1)) Revive(h);
            }
            var kit = new[] { h.Abilities.AbilityIn(A1, Shrapnel), knife };
            foreach (var a in kit) Check.True(!a.ultimate && a.cooldown >= 13f && a.cooldown <= 20f, a.id + " cooldown " + a.cooldown + " s");
        }
    }
}

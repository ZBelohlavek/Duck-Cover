using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;
using static AbilityHarness;

/// <summary>Slots, cooldowns, wind-ups, ultimates, targeting shapes and each standard effect (WP7).</summary>
static class AbilityTests
{
    // ---- Slots and cooldowns ----

    [Test]
    static void CooldownAndRejections()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_guard", "test_overdrive");
        Check.True(h.Use(A1).IsAccepted, "first use");
        Check.Near(60f, h.Unit(A1).Shield, "the effect landed at once");
        var used = h.Last<AbilityUsedEvent>();
        Check.Equal("test_guard", used.abilityId, "used event");
        Check.Equal(0, used.chargesLeft, "no charges left");
        Check.Near(12f, used.rechargeLeft, "cooldown in the event");

        h.Forget();
        var again = h.Use(A1);
        Check.Equal("on cooldown", again.reason, "second use refused");
        Check.Equal("on cooldown", h.Last<AbilityRejectedEvent>().reason, "and the client is told");
        Check.Near(12f, h.Abilities.CooldownLeft(A1, 0), "cooldown left");
        h.Run(11.9f);
        Check.False(h.Use(A1).IsAccepted, "not yet");
        h.Run(0.15f);
        Check.Near(0f, h.Abilities.CooldownLeft(A1, 0), "ready");
        Check.True(h.Use(A1).IsAccepted, "ready after 12 s");

        Check.Equal("no ability in slot 5", h.Use(A1, 5).reason, "bad slot");
        Check.Equal("unknown player", h.Use(999).reason, "unknown player");
        Check.Equal(1, h.S.Player(A1).level, "level 1");
        Check.Equal("ultimate not charged", h.Use(A1, 1).reason, "the ultimate is unlocked at level 1, only uncharged");
        var wrong = h.Abilities.Apply(new UseAbilityCommand { playerId = A1, slot = 0, abilityId = "test_dash", aimDirection = Vec3.Right });
        Check.Equal("slot 0 holds test_guard", wrong.reason, "the command's ability id must match the slot");
        Check.Equal(CommandStatus.NotHandled, h.Abilities.Apply(new PingCommand { playerId = A1 }).status, "other commands are passed on");

        h.Combat.Damage(-1, A1, 9999f);
        Check.Equal("waiting to respawn", h.Use(A1).reason, "not while down");
        h.Run(12f);
        Check.True(h.S.IsAlive(A1), "back");
        Check.True(h.Use(A1).IsAccepted, "cooldowns are reset on respawn");
        Check.Equal("unknown ability 'nope'", h.Abilities.SetLoadout(A1, new[] { "nope" }).reason, "loadouts are checked");
        Check.Equal("'test_regen' is a passive and takes no slot", h.Abilities.SetLoadout(A1, new[] { "test_regen" }).reason, "passives take no slot");
    }

    [Test]
    static void NothingBeforeTheMatchIsLive()
    {
        var h = new AbilityHarness(toPlay: false);
        h.Abilities.SetLoadout(A1, new[] { "test_guard" });
        Check.Equal("abilities work once the match is live", h.Use(A1).reason, "not in the lobby");
        h.Abilities.Tick(1f);
        Check.Near(0f, h.Combat.Damage(B1, A1, 10f), "no damage either");
    }

    [Test]
    static void ChargesRefillOneAtATime()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_mine"); // 2 charges, 9 s each
        Check.True(h.Use(A1).IsAccepted, "first charge");
        h.Run(3f);
        Check.True(h.Use(A1).IsAccepted, "second charge");
        Check.False(h.Use(A1).IsAccepted, "empty");
        Check.Near(6f, h.Abilities.CooldownLeft(A1, 0), "the first charge has been recovering for 3 s", 0.06f);
        h.Run(6.1f);
        Check.Equal(1, h.Unit(A1).slots[0].charges, "one back at 9 s");
        h.Run(8.8f);
        Check.Equal(1, h.Unit(A1).slots[0].charges, "the second takes its own 9 s");
        h.Run(0.3f);
        Check.Equal(2, h.Unit(A1).slots[0].charges, "full");
        Check.Near(0f, h.Unit(A1).slots[0].rechargeLeft, "and the timer stops");
    }

    [Test]
    static void CooldownRateSpeedsRecovery()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_guard");
        h.Abilities.AddModifiers(A1, "quick", new[] { new StatModifier(StatIds.CooldownRate, ModOp.Percent, 1f) });
        h.Use(A1);
        Check.Near(6f, h.Abilities.CooldownLeft(A1, 0), "the HUD time accounts for the rate");
        h.Run(6.1f);
        Check.True(h.Use(A1).IsAccepted, "12 s cooldown in 6 s at double rate");
    }

    // ---- Wind-up ----

    [Test]
    static void WindUpLandsLaterAndStunsCancelIt()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_bolt"); // 0.5 s wind-up
        h.Place(A1, 0); h.Place(B1, 10);
        Check.True(h.Use(A1).IsAccepted, "cast starts");
        Check.Near(0.5f, h.Last<AbilityCastStartedEvent>().castTime, "cast started event");
        Check.Equal(0, h.Of<AbilityUsedEvent>().Count, "nothing landed yet");
        Check.Equal("already casting", h.Use(A1).reason, "one cast at a time");
        h.Place(B1, 10, 30); // steps out of the line during the wind-up...
        h.Place(B2, 10);     // ...and someone else steps in
        h.Run(0.45f);
        Check.Near(200f, h.Health(B2), "still winding up");
        h.Run(0.1f);
        Check.Near(180f, h.Health(B2), "lands on who is in the line when the wind-up ends");
        Check.Near(200f, h.Health(B1), "not on who was there when it started");

        h.Run(20f);
        h.Forget();
        h.Use(A1);
        h.Abilities.ApplyStatus(A1, AbilityLibrary.StunStatus, B1, 1f, 1f);
        Check.Equal("stunned", h.Last<AbilityCancelledEvent>().reason, "a stun cancels the wind-up");
        h.Run(1f);
        Check.Equal(0, h.Of<AbilityUsedEvent>().Count, "it never lands");
        Check.False(h.Use(A1).IsAccepted, "and the cooldown is spent");
    }

    // ---- Ultimates ----

    [Test]
    static void UltimateChargesUnlocksAndSpends()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_guard", "test_overdrive");
        Check.Equal(1, h.S.Player(A1).level, "level 1");
        Check.Equal("ultimate not charged", h.Use(A1, 1).reason, "unlocked but empty");
        h.Run(10f);
        Check.Near(8f, h.Unit(A1).ultimateCharge, "0.8 a second over time", 0.05f);
        h.Combat.Damage(A1, B1, 100f);
        Check.Near(13f, h.Unit(A1).ultimateCharge, "plus 0.05 per damage dealt", 0.05f);
        h.Combat.Damage(B1, A2, 100f);
        h.Combat.Heal(A1, A2, 100f);
        Check.Near(18f, h.Unit(A1).ultimateCharge, "and per healing given", 0.05f);
        h.Combat.Damage(-1, A1, 50f);
        h.Combat.Heal(A1, A1, 50f);
        Check.Near(18f, h.Unit(A1).ultimateCharge, "healing yourself gives none", 0.05f);

        h.Forget();
        h.Abilities.AddUltimateCharge(A1, 500f);
        Check.Near(100f, h.Unit(A1).ultimateCharge, "capped at the maximum");
        Check.Equal(1, h.Of<UltimateReadyEvent>().Count, "ready event, once");
        Check.True(h.Abilities.UltimateReady(A1, 1), "ready");
        h.Place(A1, 0); h.Place(A2, 3); h.Place(A3, 30); h.Place(B1, 2);
        Check.True(h.Use(A1, 1).IsAccepted, "used");
        Check.Near(0f, h.Unit(A1).ultimateCharge, "charge spent");
        Check.True(h.Unit(A1).HasStatus("test_haste"), "caster hasted");
        Check.True(h.Unit(A2).HasStatus("test_haste"), "teammate in range hasted");
        Check.False(h.Unit(A3).HasStatus("test_haste"), "teammate out of range is not");
        Check.False(h.Unit(B1).HasStatus("test_haste"), "enemies are not");
        Check.Near(6f, h.Unit(A1).Status("test_haste").timeLeft, "the status lasts the ability's duration");

        h.Abilities.AddUltimateCharge(A1, 30f);
        h.Combat.Damage(-1, A1, 9999f);
        h.Run(3f); // a level 1 respawn wait is short, so stay inside it
        Check.Near(30f, h.Unit(A1).ultimateCharge, "no charge over time while down");
        h.Run(8f);
        Check.True(h.S.IsAlive(A1) && h.Unit(A1).ultimateCharge > 30f, "ultimate charge is kept through a respawn");
    }

    [Test]
    static void InterruptedUltimateRefundsHalf()
    {
        var h = new AbilityHarness(extra: lib => lib.Add(new AbilityDef
        {
            id = "slow_ult", ultimate = true, ultimateCost = 80f, castTime = 1f,
            targeting = new TargetingDef { shape = Shapes.Self },
            effects = new[] { new EffectDef { kind = EffectKinds.Shield, amount = 100f } },
        }));
        h.Give(A1, "slow_ult");
        h.Abilities.AddUltimateCharge(A1, 80f);
        Check.True(h.Use(A1).IsAccepted, "wind-up starts");
        Check.Near(0f, h.Unit(A1).ultimateCharge, "spent");
        h.Combat.Damage(B1, A1, 9999f);
        Check.Equal("eliminated", h.Last<AbilityCancelledEvent>().reason, "cancelled by going down");
        Check.Near(40f, h.Unit(A1).ultimateCharge, "half the cost comes back");
    }

    // ---- Targeting ----

    [Test]
    static void RayHitsTheFirstValidThingInLine()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_burn_shot");
        h.Place(A1, 0); h.Place(B1, 10); h.Place(B2, 20);
        h.Use(A1);
        Check.True(h.Unit(B1).HasStatus("test_burn"), "the nearest enemy is hit");
        Check.False(h.Unit(B2).HasStatus("test_burn"), "the one behind is not");
        Check.Near(9.5f, h.Last<AbilityUsedEvent>().points[0].x, "the landing point is the hit point");

        h.Run(1.1f);
        h.Place(A2, 5); // a teammate in the way blocks the shot (the ray stops at the first body)
        h.Forget();
        h.Use(A1);
        Check.Equal(0, h.Last<AbilityUsedEvent>().targets.Length, "blocked by a teammate: no target");

        h.Run(1.1f);
        h.Place(A2, 0, 50);
        h.World.WallAt = 6f;
        h.Use(A1);
        Check.Equal(0, h.Last<AbilityUsedEvent>().targets.Length, "blocked by a wall");
        h.World.WallAt = -1f;

        h.Run(1.1f);
        h.Use(A1, 0, new Vec3(0, 0, 1));
        Check.Near(30f, h.Last<AbilityUsedEvent>().points[0].z, "a miss lands at full range");
    }

    [Test]
    static void RayFanSpreadsLeftToRight()
    {
        var h = new AbilityHarness(extra: lib => lib.Add(new AbilityDef
        {
            id = "fan", cooldown = 1f,
            targeting = new TargetingDef { shape = Shapes.Ray, range = 20f, count = 3, spreadDegrees = 90f, affects = Affects.Enemies },
            effects = new[] { new EffectDef { kind = EffectKinds.Damage, amount = 10f } },
        }));
        h.Give(A1, "fan");
        h.Place(A1, 0);
        h.Place(B1, 10, 0);    // straight ahead
        h.Place(B2, 7, 7);     // 45 degrees to one side
        h.Place(B3, 7, -7);    // 45 degrees to the other
        h.Use(A1);
        Check.Equal(3, h.Last<AbilityUsedEvent>().points.Length, "three rays");
        Check.Near(190f, h.Health(B1), "centre ray");
        Check.Near(190f, h.Health(B2), "one edge");
        Check.Near(190f, h.Health(B3), "other edge");
    }

    [Test]
    static void SphereCatchesTheRightPeopleInOrder()
    {
        var h = new AbilityHarness(extra: lib =>
        {
            lib.Add(new AbilityDef
            {
                id = "burst", cooldown = 1f,
                targeting = new TargetingDef { shape = Shapes.Sphere, radius = 5f, affects = Affects.Enemies, maxTargets = 2 },
                effects = new[] { new EffectDef { kind = EffectKinds.Damage, amount = 10f } },
            });
            lib.Add(new AbilityDef
            {
                id = "mixed", cooldown = 1f,
                targeting = new TargetingDef { shape = Shapes.Sphere, radius = 5f, affects = Affects.All },
                effects = new[]
                {
                    new EffectDef { kind = EffectKinds.Damage, amount = 10f, affects = Affects.Enemies },
                    new EffectDef { kind = EffectKinds.Heal, amount = 5f, affects = Affects.Allies },
                    new EffectDef { kind = EffectKinds.Shield, amount = 20f, affects = Affects.Self },
                },
            });
        });
        h.Give(A1, "burst", "mixed");
        h.Place(A1, 0); h.Place(A2, 1); h.Place(B1, 2); h.Place(B2, 3); h.Place(B3, 4); h.Place(A3, 40);
        h.Combat.Damage(-1, A2, 50f);
        h.Use(A1);
        Check.Equal("101 102", string.Join(" ", h.Last<AbilityUsedEvent>().targets), "enemies only, lowest ids first, up to maxTargets");
        Check.Near(200f, h.Health(B3), "the third is spared by the limit");

        h.Combat.Damage(-1, B3, 9999f);
        h.Use(A1, 1);
        Check.Equal("1 2 101 102", string.Join(" ", h.Last<AbilityUsedEvent>().targets), "'all' catches both teams, but not the eliminated");
        Check.Near(180f, h.Health(B1), "enemies take the damage effect");
        Check.Near(155f, h.Health(A2), "allies take the heal effect, not the damage");
        Check.Near(200f, h.Health(A1), "the caster takes neither");
        Check.Near(20f, h.Unit(A1).Shield, "a 'self' effect reaches the caster once");
        Check.Near(0f, h.Unit(A2).Shield, "and nobody else");
    }

    [Test]
    static void AimedSphereLandsAtRangeOrOnAWall()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_grenade");
        h.Place(A1, 0); h.Place(B1, 20, 3);
        h.Use(A1, 0, new Vec3(0, 0, 1));
        Check.Near(20f, h.Last<AbilityUsedEvent>().points[0].z, "at full range with nothing in the way");
        h.Run(11f);
        h.World.WallAt = 8f;
        h.Use(A1);
        Check.Near(8f, h.Last<AbilityUsedEvent>().points[0].x, "against the wall");
    }

    [Test]
    static void ExtraAreasFormARing()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_grenade");
        h.Abilities.ApplyParamChanges(A1, new[] { new AbilityParamChange("test_grenade", "count", ModOp.Add, 2f) });
        h.Place(A1, 0);
        h.Use(A1);
        var pts = h.Last<AbilityUsedEvent>().points;
        Check.Equal(3, pts.Length, "three areas");
        Check.Near(20f, pts[0].x, "the first where it was aimed");
        Check.Near(4f, Vec3.Distance(pts[0], pts[1]), "the others one radius out");
        Check.Near(8f, Vec3.Distance(pts[1], pts[2]), "on opposite sides");
    }

    [Test]
    static void DelayedEffectsLookAgainWhenTheyGoOff()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_grenade"); // 1 s fuse, radius 4, lands 20 m out
        h.Place(A1, 0); h.Place(B1, 20); h.Place(B2, 40);
        h.Use(A1);
        Check.Equal(1, h.Last<AbilityUsedEvent>().targets.Length, "one enemy where it landed");
        Check.Equal(2, h.Abilities.State.pending.Count, "damage and slow wait on the fuse");
        h.Place(B1, 40); // walks out
        h.Place(B2, 21); // walks in
        h.Run(0.9f);
        Check.Near(200f, h.Health(B2), "not yet");
        h.Run(0.2f);
        Check.Near(200f, h.Health(B1), "who left the blast is safe");
        Check.Near(140f, h.Health(B2), "who walked into it is hit");
        Check.True(h.Unit(B2).HasStatus(AbilityLibrary.SlowStatus), "and slowed");
        Check.Equal(0, h.Abilities.State.pending.Count, "nothing left waiting");
        Check.Equal("test_grenade", h.Last<DamagedEvent>().abilityId, "credited to the ability");
    }

    // ---- The standard effects ----

    [Test]
    static void HealAndCleanseOnAnAlly()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_patch");
        h.Place(A1, 0); h.Place(A2, 8);
        h.Combat.Damage(-1, A2, 120f);
        h.Abilities.ApplyStatus(A2, AbilityLibrary.SlowStatus, B1, 0.4f, 5f);
        h.Abilities.ApplyStatus(A2, "test_haste", A1);
        h.Use(A1);
        Check.Near(130f, h.Health(A2), "healed 50");
        Check.False(h.Unit(A2).HasStatus(AbilityLibrary.SlowStatus), "the slow is cleansed");
        Check.True(h.Unit(A2).HasStatus("test_haste"), "helpful statuses stay");
        Check.Equal("cleansed", h.Last<StatusEndedEvent>().reason, "ended event says why");

        h.Run(9f);
        h.Place(A2, 0, 40); h.Place(B1, 8);
        h.Combat.Damage(-1, B1, 50f);
        h.Use(A1);
        Check.Near(150f, h.Health(B1), "an ally-only ray does nothing to an enemy");
    }

    [Test]
    static void PushAndDash()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_dash", "test_bolt");
        h.Place(A1, 0); h.Place(B1, 10);
        h.Use(A1, 0, new Vec3(0, 0, 2));
        var dash = h.Last<PushedEvent>();
        Check.Equal(A1, dash.targetId, "a dash is a push on the caster");
        Check.Equal(new Vec3(0, 0, 1), dash.direction, "along the (normalized) aim");
        Check.Near(6f, dash.distance, "distance");
        Check.Near(0.25f, dash.seconds, "over time");
        Check.Equal(0, h.Abilities.State.objects.Count, "the tagged healing trail is off without the talent");

        h.Use(A1, 1);
        h.Run(0.55f);
        var knock = h.Last<PushedEvent>();
        Check.Equal(B1, knock.targetId, "the bolt pushes its target");
        Check.Equal("away", knock.mode, "mode for the Game layer");
        Check.True(knock.direction.x > 0.99f, "away from where it landed");
        Check.True(h.Abilities.IsStunned(B1), "and stuns");
    }

    [Test]
    static void DamageEffectUsesTheCastersStats()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_bolt");
        h.Abilities.AddModifiers(A1, "t", new[] { new StatModifier(StatIds.DamageDealt, ModOp.Percent, 0.5f) });
        h.Place(A1, 0); h.Place(B1, 10);
        h.Use(A1);
        h.Run(0.55f);
        Check.Near(170f, h.Health(B1), "20 * 1.5");
    }

    [Test]
    static void AbilityRadiusStatWidensAreas()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_overdrive");
        h.Abilities.AddUltimateCharge(A1, 100f);
        h.Abilities.AddModifiers(A1, "power_surge", new[] { new StatModifier(StatIds.AbilityRadius, ModOp.Percent, 0.5f) });
        h.Place(A1, 0); h.Place(A2, 8.5f);
        h.Use(A1);
        Check.True(h.Unit(A2).HasStatus("test_haste"), "8.5 m is inside 6 m * 1.5");
    }

    // ---- Scripts ----

    sealed class CountingScript : IAbility
    {
        public string Id => "counting";
        public bool allow = true;
        public int activated, ticks;
        public int targetsSeen;
        public bool CanActivate(AbilityContext ctx) => allow;
        public void Activate(AbilityContext ctx)
        {
            activated++;
            targetsSeen = ctx.areas.Sum(a => a.targets.Length);
            ctx.runner.Combat.Heal(ctx.playerId, ctx.playerId, 5f, ctx.ability.id);
        }
        public void Tick(AbilityContext ctx, float dt) => ticks++;
    }

    [Test]
    static void ScriptsRunBesideTheStandardEffects()
    {
        var h = new AbilityHarness(extra: lib => lib.Add(new AbilityDef
        {
            id = "scripted", cooldown = 5f, duration = 1f, scriptId = "counting",
            targeting = new TargetingDef { shape = Shapes.Sphere, radius = 5f, affects = Affects.Enemies },
            effects = new[] { new EffectDef { kind = EffectKinds.Damage, amount = 10f } },
        }));
        Check.Equal(1, h.Abilities.MissingScripts().Count, "an unregistered script is reported");
        h.Give(A1, "scripted");
        h.Place(A1, 0); h.Place(B1, 2);
        Check.Equal("script 'counting' is not registered", h.Use(A1).reason, "and can't be used");

        var script = new CountingScript { allow = false };
        h.Abilities.RegisterScript(script);
        Check.Equal(0, h.Abilities.MissingScripts().Count, "registered");
        Check.Equal("can't be used right now", h.Use(A1).reason, "the script can refuse");
        Check.Equal(1, h.Unit(A1).slots[0].charges, "a refusal spends nothing");

        script.allow = true;
        h.Combat.Damage(-1, A1, 50f);
        Check.True(h.Use(A1).IsAccepted, "used");
        Check.Equal(1, script.activated, "Activate ran once");
        Check.Equal(1, script.targetsSeen, "it sees what the targeting caught");
        Check.Near(190f, h.Health(B1), "the data effects still apply");
        Check.Near(155f, h.Health(A1), "and the script acted through the runner");
        h.Run(2f);
        Check.Equal(20, script.ticks, "Tick ran every tick for the ability's duration (1 s)");
    }

    sealed class DoubleDamage : IEffect
    {
        public void Apply(EffectContext ctx, int targetId) => ctx.Combat.Damage(ctx.sourceId, targetId, ctx.effect.amount * 2f, ctx.abilityId);
    }

    [Test]
    static void EffectKindsCanBeReplaced()
    {
        var h = new AbilityHarness();
        h.Abilities.RegisterEffect(EffectKinds.Damage, new DoubleDamage());
        h.Give(A1, "test_bolt");
        h.Place(A1, 0); h.Place(B1, 10);
        h.Use(A1);
        h.Run(0.55f);
        Check.Near(160f, h.Health(B1), "the registered effect ran instead");
    }

    // ---- Snapshot and determinism ----

    [Test]
    static void SnapshotIsADeepCopy()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_mine", "test_guard");
        h.Place(A1, 0);
        h.Use(A1); h.Use(A1, 1);
        var snap = h.Match.TakeSnapshot();
        h.Abilities.FillSnapshot(snap);
        Check.Equal(1, snap.abilities.objects.Count, "the mine is in the snapshot");
        Check.Near(60f, snap.abilities.Unit(A1).Shield, "and the shield");
        h.Combat.Damage(B1, A1, 100f);
        h.Abilities.RemoveObject(snap.abilities.objects[0].id);
        Check.Near(60f, snap.abilities.Unit(A1).Shield, "later changes don't reach the copy");
        Check.Near(200f, snap.abilities.Unit(A1).health, "health neither");
        Check.Equal(1, snap.abilities.objects.Count, "objects neither");
    }

    static string Fight()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_grenade", "test_burn_shot", "test_mine");
        h.Give(B1, "test_bolt", "test_guard", "test_turret");
        h.Place(A1, 0); h.Place(B1, 12); h.Place(B2, 14, 2);
        for (int i = 0; i < 200; i++)
        {
            if (i % 7 == 0) h.Use(A1, i / 7 % 3);
            if (i % 11 == 0) h.Use(B1, i / 11 % 3, new Vec3(-1, 0, 0));
            h.Run(Dt);
        }
        return h.Log();
    }

    [Test]
    static void SameInputsSameEvents()
    {
        string first = Fight();
        Check.True(first.Contains(nameof(DamagedEvent)) && first.Contains(nameof(ObjectSpawnedEvent)), "the scripted fight did something");
        Check.Equal(first, Fight(), "a replay gives the identical event log");
    }
}

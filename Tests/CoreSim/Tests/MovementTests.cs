using System.Linq;
using DuckAndCover.Core;

/// <summary>Player-owned movement and the server's checks on it (WP4).</summary>
static class MovementTests
{
    const int A1 = CombatHarness.A1, A2 = CombatHarness.A2, B1 = CombatHarness.B1;

    [Test]
    static void DeployPlacesEveryoneOnTheirSpawns()
    {
        var h = new CombatHarness(toPlay: false);
        Check.True(h.Match.StartMatch().IsAccepted, "start");
        while (h.S.phase != MatchPhase.Deploy) h.Tick();
        var layout = ArenaLayout.Placeholder();
        var placed = h.Of<PlayerPlacedEvent>();
        Check.Equal(4, placed.Count, "four players placed");
        Check.True(placed.Where(p => p.playerId < 100).All(p => layout.spawnsA.Any(s => s.position == p.position)), "team A on A's spawns");
        Check.True(placed.Where(p => p.playerId > 100).All(p => layout.spawnsB.Any(s => s.position == p.position)), "team B on B's spawns");
        Check.False(h.M(A1).position == h.M(A2).position, "teammates get different spots");
        Check.Equal(180f, h.M(B1).yaw, "facing the way the spot faces");
    }

    [Test]
    static void NormalMovesAreKept()
    {
        var h = new CombatHarness();
        var start = h.M(A1).position;
        // Sprint for a second in 20 moves, one per tick.
        for (int i = 1; i <= 20; i++)
        {
            Check.True(h.Move(A1, start + Vec3.Forward * (h.R.sprintSpeed * 0.05f * i)).IsAccepted, "sprint step " + i);
            h.Tick();
        }
        Check.Near(start.z + h.R.sprintSpeed, h.M(A1).position.z, "a second of sprint is kept", 1e-3f);
        Check.Equal(0, h.Of<PlayerPlacedEvent>().Count, "no corrections");
        Check.True(h.Move(A1, h.M(A1).position + Vec3.Up * 1.0f).IsAccepted, "a jump goes up");
    }

    [Test]
    static void TooFastIsRefusedAndPutBack()
    {
        var h = new CombatHarness();
        var start = h.M(A1).position;
        int placement = h.M(A1).placement;
        var r = h.Move(A1, start + Vec3.Forward * 20f);
        Check.False(r.IsAccepted, "20 m in one step is refused");
        Check.True(r.reason.Contains("too fast"), "and says why: " + r.reason);
        Check.True(h.M(A1).position == start, "the server keeps the old position");
        var put = h.Last<PlayerPlacedEvent>();
        Check.True(put != null && put.correction && put.playerId == A1 && put.position == start, "the player is put back");
        Check.Equal(placement + 1, h.M(A1).placement, "a new placement");

        // Moves already in flight from before the correction are dropped quietly.
        var stale = new MoveCommand { playerId = A1, position = start + Vec3.Forward * 0.2f, placement = placement };
        Check.True(h.Combat.Apply(stale).IsAccepted, "a stale move isn't an error");
        Check.True(h.M(A1).position == start, "but it isn't applied");
        Check.True(h.Move(A1, start + Vec3.Forward * 0.2f).IsAccepted, "moves from the new placement work");
    }

    [Test]
    static void ManySmallStepsCantBeatTheAllowance()
    {
        var h = new CombatHarness();
        var p = h.M(A1).position;
        int refused = 0;
        // 40 steps of 1 m with no ticks between: far faster than sprinting.
        for (int i = 0; i < 40; i++)
        {
            p = h.M(A1).position + Vec3.Forward * 1f;
            if (!h.Move(A1, p).IsAccepted) refused++;
        }
        float most = h.R.sprintSpeed * h.R.moveTolerance * h.R.moveBurstSeconds + h.R.moveSlack;
        Check.True(refused > 0, "bursts are capped");
        Check.True(h.M(A1).position.z - h.Combat.Movement.Layout.spawnsA[0].position.z <= most + 1f, "no further than the banked allowance");
    }

    [Test]
    static void RisingTooFarIsRefused()
    {
        var h = new CombatHarness();
        Check.False(h.Move(A1, h.M(A1).position + Vec3.Up * 5f).IsAccepted, "5 m straight up is refused");
        Check.True(h.Move(A1, h.M(A1).position - Vec3.Up * 5f).IsAccepted, "falling is allowed");
    }

    [Test]
    static void StunnedAndSlowedPlayers()
    {
        var h = new CombatHarness();
        h.Unit(A1).statuses.Add(new StatusInstance { statusId = AbilityLibrary.StunStatus, timeLeft = 5f, duration = 5f });
        Check.False(h.Move(A1, h.M(A1).position + Vec3.Forward * 2f).IsAccepted, "a stunned player can't walk");
        Check.True(h.Move(A1, h.M(A1).position + Vec3.Forward * 0.1f - Vec3.Up).IsAccepted, "but can fall and settle");
        h.Unit(A1).statuses.Clear();

        float full = h.Combat.Movement.TopSpeed(A1);
        h.Combat.Damage.AddModifiers(A1, "test-slow", new[] { new StatModifier(StatIds.MoveSpeed, ModOp.Multiply, 0.5f) });
        Check.Near(full * 0.5f, h.Combat.Movement.TopSpeed(A1), "a slow lowers the top speed the checks allow");
    }

    [Test]
    static void DeadPlayersDontMove()
    {
        var h = new CombatHarness();
        var at = h.M(B1).position;
        h.Match.EliminatePlayer(B1, A1);
        Check.True(h.Move(B1, at + Vec3.Forward).IsAccepted, "a move in flight from before going down is no error");
        Check.True(h.M(B1).position == at, "but it doesn't move the body");
        h.Tick();
        Check.True(h.Combat.History.PoseAt(B1, h.S.tick, out var pose) && !pose.alive, "the history marks them down");
    }

    [Test]
    static void RespawnPlacesAgain()
    {
        var h = new CombatHarness();
        h.Move(B1, h.M(B1).position + Vec3.Forward * 0.5f);
        h.Match.EliminatePlayer(B1, A1);
        int before = h.M(B1).placement;
        h.Run(h.Match.Spawn.WaitFor(1) + h.R.respawnWaveSeconds + 0.2f);
        Check.True(h.S.IsAlive(B1), "back");
        Check.True(h.M(B1).placement > before, "placed again");
        Check.True(ArenaLayout.Placeholder().spawnsB.Any(s => s.position == h.M(B1).position), "on a team B spawn");
    }
}

/// <summary>The movement maths the player's own machine runs (WP4).</summary>
static class MoveMathTests
{
    static readonly Ruleset R = Ruleset.Default;
    const float Dt = 1f / 60f;

    static Vec3 Run(MoveInput input, float seconds, Vec3 v = default, bool grounded = true, float scale = 1f)
    {
        for (float t = 0f; t < seconds; t += Dt) { v = MoveMath.Step(R, v, input, grounded, scale, Dt); input.jump = false; }
        return v;
    }

    static float Flat(Vec3 v) => (float)System.Math.Sqrt(v.x * v.x + v.z * v.z);

    [Test]
    static void SpeedsByStance()
    {
        Check.Near(R.walkSpeed, Flat(Run(new MoveInput { forward = 1f }, 1f)), "walking reaches walk speed", 1e-3f);
        Check.Near(R.sprintSpeed, Flat(Run(new MoveInput { forward = 1f, sprint = true }, 1f)), "sprinting forward", 1e-3f);
        Check.Near(R.walkSpeed, Flat(Run(new MoveInput { forward = -1f, sprint = true }, 1f)), "no sprinting backward", 1e-3f);
        Check.Near(R.walkSpeed, Flat(Run(new MoveInput { right = 1f, sprint = true }, 1f)), "or sideways", 1e-3f);
        Check.Near(R.crouchSpeed, Flat(Run(new MoveInput { forward = 1f, sprint = true, crouch = true }, 1f)), "crouching is slow even with sprint held", 1e-3f);
        Check.Near(R.walkSpeed, Flat(Run(new MoveInput { forward = 1f, right = 1f }, 1f)), "diagonals aren't faster", 1e-3f);
        Check.Near(R.walkSpeed * 0.5f, Flat(Run(new MoveInput { forward = 1f }, 1f, scale: 0.5f)), "a slow scales it", 1e-3f);
        Check.Near(0f, Flat(Run(new MoveInput { forward = 1f }, 1f, scale: 0f)), "a stun stops it", 1e-3f);
        Check.Near(0f, Flat(Run(new MoveInput(), 0.5f, new Vec3(5f, 0f, 0f))), "letting go stops", 1e-3f);
    }

    [Test]
    static void FacingFollowsYaw()
    {
        Check.True(Vec3.Approximately(Vec3.Forward, MoveMath.Forward(0f)), "yaw 0 is +z");
        Check.True(Vec3.Approximately(Vec3.Right, MoveMath.Forward(90f)), "yaw 90 is +x");
        Check.True(Vec3.Approximately(Vec3.Right, MoveMath.Right(0f)), "right of +z is +x");
        var v = Run(new MoveInput { forward = 1f, yaw = 90f }, 1f);
        Check.True(v.x > 5f && System.Math.Abs(v.z) < 1e-3f, "forward moves along the facing");
        Check.True(Vec3.Approximately(-Vec3.Up, MoveMath.AimDirection(0f, 90f)), "pitch 90 looks straight down");
        Check.Near(1f, MoveMath.AimDirection(33f, -20f).Magnitude, "aim is unit length", 1e-4f);
    }

    [Test]
    static void JumpReachesItsHeight()
    {
        var v = MoveMath.Step(R, Vec3.Zero, new MoveInput { jump = true }, true, 1f, Dt);
        Check.Near(R.JumpVelocity, v.y, "a jump leaves the ground at the jump speed");
        float y = 0f, top = 0f;
        for (int i = 0; i < 600 && y >= 0f; i++)
        {
            y += v.y * Dt;
            if (y > top) top = y;
            v = MoveMath.Step(R, v, new MoveInput(), false, 1f, Dt);
        }
        Check.True(System.Math.Abs(top - R.jumpHeight) < 0.06f, "and tops out near jumpHeight (" + top + ")");
        Check.True(MoveMath.Step(R, Vec3.Zero, new MoveInput { jump = true }, false, 1f, Dt).y < 0f, "no jumping in the air");
        Check.True(MoveMath.Step(R, Vec3.Zero, new MoveInput { jump = true }, true, 0f, Dt).y < 0f, "or while stunned");
    }

    [Test]
    static void AirControlIsWeaker()
    {
        var ground = MoveMath.Step(R, Vec3.Zero, new MoveInput { forward = 1f }, true, 1f, Dt);
        var air = MoveMath.Step(R, Vec3.Zero, new MoveInput { forward = 1f }, false, 1f, Dt);
        Check.Near(Flat(ground) * R.airControl, Flat(air), "the air gives a share of ground acceleration", 1e-4f);
    }

    [Test]
    static void VaultRules()
    {
        Check.False(MoveMath.CanVault(R, 0.2f), "a kerb is stepped, not vaulted");
        Check.True(MoveMath.CanVault(R, 1.0f), "a metre-high wall can be vaulted");
        Check.False(MoveMath.CanVault(R, 2.0f), "a two-metre wall can't");
        Vec3 from = Vec3.Zero, to = new Vec3(0f, 1f, 1f);
        Check.True(Vec3.Approximately(from, MoveMath.VaultPosition(from, to, 0f)), "starts where you stand");
        Check.True(Vec3.Approximately(to, MoveMath.VaultPosition(from, to, 1f)), "ends on the ledge");
        var mid = MoveMath.VaultPosition(from, to, 0.35f);
        Check.True(mid.y > 0.5f && mid.z == 0f, "goes up before it goes over");
        // A vault is within what the server's checks allow.
        var h = new CombatHarness();
        var start = h.M(1).position;
        for (int i = 1; i <= 7; i++) { Check.True(h.Move(1, MoveMath.VaultPosition(start, start + new Vec3(0f, 1.2f, 1.1f), i / 7f)).IsAccepted, "vault step " + i); h.Tick(); }
    }
}

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

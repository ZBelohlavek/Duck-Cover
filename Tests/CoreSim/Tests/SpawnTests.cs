using System.Linq;
using DuckAndCover.Core;
using static MatchHarness;

/// <summary>Eliminations, respawn waits and waves, forward spawns, joins and leaves (WP1).</summary>
static class SpawnTests
{
    [Test]
    static void WaitGrowsWithLevel()
    {
        var h = new MatchHarness(rules: NoWaves());
        Check.Near(8f, h.Match.Spawn.WaitFor(1), "level 1");
        Check.Near(17f, h.Match.Spawn.WaitFor(10), "level 10");

        int p = h.A[0];
        Check.True(h.Match.EliminatePlayer(p, h.B[0]), "eliminated");
        Check.False(h.S.IsAlive(p), "out of the world");
        Check.Near(8f, h.Match.Spawn.TimeUntilRespawn(p), "8 s to wait");
        float t = h.RunUntil(() => h.S.IsAlive(p), 30f);
        Check.Near(8f, t, "back after 8 s", 0.06f);
        Check.Equal(1, h.S.Player(h.B[0]).eliminations, "killer credited");
        Check.Equal(1, h.S.Player(p).deaths, "death counted");

        h.S.Player(p).level = 10; // Progression sets this
        h.Match.EliminatePlayer(p);
        t = h.RunUntil(() => h.S.IsAlive(p), 30f);
        Check.Near(17f, t, "level 10 waits 17 s", 0.06f);
        Check.False(h.Match.EliminatePlayer(999), "unknown player");
    }

    static Ruleset NoWaves() { var r = Ruleset.Default; r.respawnWaveSeconds = 0f; return r; }

    [Test]
    static void PlayersReturnInWaves()
    {
        var h = new MatchHarness(); // waves every 2 s
        h.Run(0.5f);
        h.Match.EliminatePlayer(h.A[0]); // ready at 8.5 -> wave at 10
        h.Run(1f);
        h.Match.EliminatePlayer(h.A[1]); // ready at 9.5 -> same wave
        h.Run(1.1f);
        h.Match.EliminatePlayer(h.A[2]); // ready at 10.6 -> wave at 12
        Check.Near(10f - h.S.elapsed, h.Match.Spawn.TimeUntilRespawn(h.A[0]), "the HUD timer includes the wave", 0.06f);
        h.RunUntil(() => h.S.IsAlive(h.A[0]), 30f);
        Check.Near(10f, h.S.elapsed, "first back on the wave at 10 s", 0.06f);
        Check.True(h.S.IsAlive(h.A[1]), "teammates who fell together return together");
        Check.False(h.S.IsAlive(h.A[2]), "a later fall waits for the next wave");
        h.RunUntil(() => h.S.IsAlive(h.A[2]), 30f);
        Check.Near(12f, h.S.elapsed, "next wave at 12 s", 0.06f);
    }

    [Test]
    static void ForwardSpawnOnlyWhileHeldAndQuiet()
    {
        var h = new MatchHarness(rules: NoWaves());
        int p = h.A[0];
        Check.False(h.Match.Apply(new PickSpawnCommand { playerId = p, zone = NearB }).IsAccepted, "can't pick the enemy's forward point");
        Check.False(h.Match.Apply(new PickSpawnCommand { playerId = p, zone = Mid }).IsAccepted, "the middle is no spawn");
        Check.True(h.Match.Apply(new PickSpawnCommand { playerId = p, zone = NearA }).IsAccepted, "own forward point");

        h.Match.EliminatePlayer(p);
        h.RunUntil(() => h.S.IsAlive(p), 30f);
        Check.Equal(ZoneId.None, h.Last<PlayerSpawnedEvent>().zone, "not held yet: base");

        h.Take(NearA, TeamId.A);
        h.Match.EliminatePlayer(p);
        h.RunUntil(() => h.S.IsAlive(p), 30f);
        Check.Equal(NearA, h.Last<PlayerSpawnedEvent>().zone, "held and quiet: forward point");

        h.World.Put(h.B[0], NearA);
        h.Match.EliminatePlayer(p);
        h.RunUntil(() => h.S.IsAlive(p), 30f);
        Check.Equal(ZoneId.None, h.Last<PlayerSpawnedEvent>().zone, "enemy on it: base");

        Check.True(h.Match.Apply(new PickSpawnCommand { playerId = p, zone = ZoneId.None }).IsAccepted, "back to base");
    }

    [Test]
    static void EliminationsOnlyCountDuringPlay()
    {
        var h = new MatchHarness(toPlay: false);
        h.Match.StartMatch();
        h.RunTo(MatchPhase.Deploy);
        Check.False(h.Match.EliminatePlayer(h.A[0]), "nobody fights in deploy");
        h.RunTo(MatchPhase.Play);
        Check.True(h.Match.EliminatePlayer(h.A[0]), "but they do in play");
        Check.False(h.Match.EliminatePlayer(h.A[0]), "not twice");
    }

    [Test]
    static void LateJoinAndLeave()
    {
        var h = new MatchHarness(perTeam: 2, rules: NoWaves());
        Check.True(h.Match.AddPlayer(3, TeamId.A).IsAccepted, "join mid-match");
        Check.False(h.S.IsAlive(3), "a late joiner waits");
        float t = h.RunUntil(() => h.S.IsAlive(3), 30f);
        Check.Near(8f, t, "a normal respawn wait", 0.06f);

        h.World.Put(3, Mid);
        h.Match.RemovePlayer(3);
        var e = h.Last<PlayerEliminatedEvent>();
        Check.True(e.disconnected && e.playerId == 3, "leaving counts as going down");
        Check.False(h.S.Player(3).connected, "listed but disconnected");
        h.Run(20f);
        Check.False(h.S.IsAlive(3), "a disconnected player never respawns");
        Check.Equal(0, h.Zone(Mid).countA, "and doesn't hold points");

        Check.False(h.Match.AddPlayer(3, TeamId.B).IsAccepted, "rejoining switches no teams");
        Check.True(h.Match.AddPlayer(3, TeamId.A).IsAccepted, "rejoin own team");
        h.RunUntil(() => h.S.IsAlive(3), 30f);
    }

    [Test]
    static void JoiningDuringDeploySpawnsAtOnce()
    {
        var h = new MatchHarness(perTeam: 1, toPlay: false);
        h.Match.StartMatch();
        h.RunTo(MatchPhase.Deploy);
        int spawnsBefore = h.Of<PlayerSpawnedEvent>().Count;
        h.Match.AddPlayer(2, TeamId.A);
        Check.True(h.S.IsAlive(2), "straight into the spawn room");
        Check.Equal(spawnsBefore + 1, h.Of<PlayerSpawnedEvent>().Count, "only the new player spawns");
    }
}

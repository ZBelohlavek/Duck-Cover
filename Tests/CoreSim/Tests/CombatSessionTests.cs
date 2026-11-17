using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>Movement and weapons through a real session: commands, events and state over the wire format (WP4).</summary>
static class CombatSessionTests
{
    static SessionHarness Playing(out SessionClient a, out SessionClient b, out CombatServer combat)
    {
        var h = new SessionHarness(scripted: false);
        combat = new CombatServer(h.Match, h.World, h.Events, h.Host.Settings.TickSeconds);
        combat.Attach(h.Host);
        a = h.Join();
        b = h.Join();
        h.Send(a, new StartMatchCommand());
        for (int i = 0; i < 400 && h.Match.State.phase != MatchPhase.Play; i++) h.Ticks(1);
        Check.Equal(MatchPhase.Play, h.Match.State.phase, "in play");
        return h;
    }

    [Test]
    static void TwoPlayersFightOverTheSession()
    {
        var h = Playing(out var a, out var b, out var combat);
        var rules = h.Match.Rules;
        var aSaw = new List<GameEvent>();
        var bSaw = new List<GameEvent>();
        a.Events.Subscribe(aSaw.Add);
        b.Events.Subscribe(bSaw.Add);

        // Both clients know where everyone is, what they hold and how healthy they are.
        var mine = a.Snapshot.Motion(a.PlayerId);
        var theirs = a.Snapshot.Motion(b.PlayerId);
        Check.True(mine != null && theirs != null, "positions are in the state");
        Check.True(Vec3.Distance(mine.position, theirs.position) > 30f, "the teams start at opposite ends");
        Check.Equal("rifle", a.Snapshot.Weapons(a.PlayerId).CurrentId, "weapons are in the state");
        Check.Near(rules.personalShield, a.Snapshot.abilities.Unit(b.PlayerId).Shield, "shields are in the state");

        // A walks a step: B sees it in the next state.
        h.Send(a, new MoveCommand { position = mine.position + new Vec3(0.3f, 0f, 0.2f), yaw = 12f, crouched = true, placement = mine.placement });
        h.Ticks(2);
        var seen = b.Snapshot.Motion(a.PlayerId);
        Check.True(Vec3.Approximately(seen.position, mine.position + new Vec3(0.3f, 0f, 0.2f)), "B sees A's new position");
        Check.True(seen.crouched && seen.yaw == 12f, "and stance and facing");

        // A shoots B in the chest until B goes down.
        int shots = 0;
        while (h.Match.State.IsAlive(b.PlayerId) && shots < 60)
        {
            var me = a.Snapshot.Motion(a.PlayerId);
            var them = a.Snapshot.Motion(b.PlayerId);
            Vec3 eye = me.position + Vec3.Up * Hitboxes.EyeHeight(rules, me.crouched);
            Vec3 chest = Hitboxes.Chest(rules, them.Pose(true));
            h.Send(a, new FireCommand { weaponId = "rifle", origin = eye, direction = (chest - eye).Normalized });
            shots++;
            h.Ticks(4);
        }
        Check.False(h.Match.State.IsAlive(b.PlayerId), "B went down");
        var rifle = StarterWeapons.Library().Weapon("rifle");
        float perShot = rifle.damage * rifle.Falloff(Vec3.Distance(mine.position, theirs.position));
        Check.True(shots >= (int)((200f + rules.personalShield) / rifle.damage) && shots <= (int)((200f + rules.personalShield) / perShot) + 2, "in the number of shots the weapon's numbers say (" + shots + ")");
        foreach (var saw in new[] { aSaw, bSaw })
        {
            Check.Equal(shots, saw.OfType<ShotFiredEvent>().Count(), "both machines saw every shot");
            Check.True(saw.OfType<ShotFiredEvent>().All(s => s.hitPlayers.Length == 1 && s.hitPlayers[0] == b.PlayerId && s.ends.Length == 1), "with what it hit");
            Check.Equal(shots, saw.OfType<DamagedEvent>().Count(), "and every hit");
            Check.Equal(1, saw.OfType<PlayerEliminatedEvent>().Count(e => e.playerId == b.PlayerId && e.killerId == a.PlayerId), "and the elimination");
            Check.True(saw.OfType<ReloadStartedEvent>().Any() == (shots >= rifle.magazine), "reloads are announced");
        }
        Check.Equal(1, a.State.Player(a.PlayerId).eliminations, "the scoreboard agrees");
    }

    [Test]
    static void RefusedMovesComeBackAsACorrection()
    {
        var h = Playing(out var a, out var b, out _);
        var aSaw = new List<GameEvent>();
        a.Events.Subscribe(aSaw.Add);
        var mine = a.Snapshot.Motion(a.PlayerId);
        h.Send(a, new MoveCommand { position = mine.position + new Vec3(0f, 0f, 30f), placement = mine.placement });
        var put = aSaw.OfType<PlayerPlacedEvent>().LastOrDefault();
        Check.True(put != null && put.correction && put.position == mine.position, "the client is told where it really is");
        Check.True(aSaw.OfType<CommandRejectedEvent>().Any(r => r.command == nameof(MoveCommand)), "and that the move was refused");
    }

    [Test]
    static void OnlyTheHostAddsDummies()
    {
        var h = new SessionHarness(scripted: false);
        var combat = new CombatServer(h.Match, h.World, h.Events, h.Host.Settings.TickSeconds);
        combat.Attach(h.Host);
        var a = h.Join();
        var b = h.Join();
        h.Send(b, new AddDummyCommand { team = TeamId.B });
        Check.Equal(0, combat.Dummies.Count, "a guest can't");
        h.Send(a, new AddDummyCommand { team = TeamId.B });
        h.Send(a, new AddDummyCommand { team = TeamId.B });
        Check.Equal(2, combat.Dummies.Count, "the host can");
        h.Send(a, new StartMatchCommand());
        for (int i = 0; i < 400 && h.Match.State.phase != MatchPhase.Play; i++) h.Ticks(1);
        Check.True(a.Snapshot.IsDummy(CombatServer.FirstDummyId), "clients know who is a dummy");
        var layout = ArenaLayout.Placeholder();
        Check.True(a.Snapshot.Motion(CombatServer.FirstDummyId).position == layout.dummies[0].position, "dummies stand on the dummy spots");
        Check.True(a.Snapshot.Motion(CombatServer.FirstDummyId + 1).position == layout.dummies[1].position, "each on its own");
        h.Send(a, new AddDummyCommand { team = TeamId.B });
        Check.Equal(2, combat.Dummies.Count, "not once the match runs");
    }
}

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DuckAndCover.Core;

/// <summary>Checks on the shared contracts and the layer rules from the bootstrap package.</summary>
static class ContractTests
{
    [Test]
    static void CoreHasNoEngineReferences()
    {
        // The asmdef enforces this in Unity (noEngineReferences); this catches it in the cloud too.
        var core = Path.Combine(Repo.Scripts, "Core");
        var banned = new Regex(@"\b(UnityEngine|UnityEditor|Unity\.Netcode)\b");
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//")) continue;
                if (banned.IsMatch(code)) offenders.Add(Path.GetRelativePath(Repo.Root, file) + ":" + (i + 1));
            }
        }
        Check.True(offenders.Count == 0, "Core must not use engine types: " + string.Join(", ", offenders));
    }

    [Test]
    static void Vec3Math()
    {
        var a = new Vec3(1, 2, 3);
        var b = new Vec3(4, 5, 6);
        Check.Equal(new Vec3(5, 7, 9), a + b, "add");
        Check.Equal(new Vec3(-3, -3, -3), a - b, "subtract");
        Check.Near(32f, Vec3.Dot(a, b), "dot");
        Check.Equal(Vec3.Up, Vec3.Cross(Vec3.Right, Vec3.Forward) * -1f, "cross follows Unity's left-handed axes");
        Check.Near(1f, new Vec3(3, 4, 0).Normalized.Magnitude, "normalized length");
        Check.Equal(Vec3.Zero, Vec3.Zero.Normalized, "normalizing zero stays zero");
        Check.Near(5f, Vec3.Distance(Vec3.Zero, new Vec3(3, 0, 4)), "distance");
    }

    [Test]
    static void IdsAndCells()
    {
        Check.Equal(TeamId.B, TeamId.A.Other(), "other team");
        Check.Equal(TeamId.None, TeamId.None.Other(), "no team has no other");
        Check.False(ZoneId.None.IsValid, "default zone is none");
        Check.True(new ZoneId(2) == new ZoneId(2), "zone equality");
        Check.False(SurfaceCell.None.IsValid, "no cell");
        Check.False(default(SurfaceCell).IsValid, "default cell is not valid (surface ids start at 1)");
        Check.True(new SurfaceCell(1, 0, 0).IsValid, "first cell of the first surface");
        Check.Equal(HitKind.Nothing, HitInfo.Miss.kind, "miss");
        Check.Equal(-1, HitInfo.Miss.playerId, "a miss hits no player");
    }

    sealed class TestEvent : GameEvent { public string label; }

    [Test]
    static void EventBus()
    {
        var bus = new GameEventBus { Record = true };
        int seen = 0;
        void Handler(GameEvent e) => seen++;
        bus.Subscribe(Handler);
        bus.Subscribe(Handler);
        bus.Publish(new TestEvent { tick = 3, label = "first" });
        Check.Equal(1, seen, "a handler subscribed twice runs once");
        bus.Unsubscribe(Handler);
        bus.Publish(new TestEvent { tick = 4, label = "second" });
        Check.Equal(1, seen, "unsubscribed handler stops");
        Check.Equal(2, bus.History.Count, "history records every event");
        Check.Equal("second", ((TestEvent)bus.History.Last()).label, "history keeps order");
    }
}

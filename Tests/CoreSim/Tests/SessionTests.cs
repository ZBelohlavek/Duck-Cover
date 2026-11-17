using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>A host and clients on a loopback network: every message goes through the real wire format.</summary>
sealed class SessionHarness
{
    public readonly ScriptedWorld World = new ScriptedWorld();
    public readonly GameEventBus Events = new GameEventBus { Record = true };
    public readonly MatchRules Match;
    public readonly LoopbackNetwork Net = new LoopbackNetwork();
    public readonly SessionHost Host;
    public readonly List<SessionClient> Clients = new List<SessionClient>();

    public SessionHarness(ModeDef mode = null, Ruleset rules = null, bool scripted = true, NetSettings settings = null)
    {
        Match = new MatchRules(rules ?? ScriptedPlay.SmokeRuleset(), mode ?? ModeDef.CaptureAndHold(), ScriptedPlay.PlaceholderMap(), World, Events);
        Host = new SessionHost(Match, Events, Net, settings);
        Net.Host = Host;
        if (scripted) ScriptedPlay.Attach(Host, World, Events);
    }

    public SessionClient Join(string name = null, TeamId team = TeamId.None, string token = null)
    {
        var c = Net.Connect(name ?? "P" + (Clients.Count + 1), token ?? "token-" + (Clients.Count + 1), team);
        Clients.Add(c);
        Net.Pump();
        return c;
    }

    public void Send(SessionClient c, MatchCommand cmd)
    {
        c.Send(cmd);
        Net.Pump();
    }

    public void Ticks(int n)
    {
        for (int i = 0; i < n; i++) { Host.Tick(); Net.Pump(); }
    }

    public void RunTo(MatchPhase phase, int limitTicks = 20000)
    {
        for (int i = 0; i < limitTicks && Match.State.phase != phase; i++) { Host.Tick(); Net.Pump(); }
        Check.Equal(phase, Match.State.phase, "reach " + phase);
    }
}

static class SessionTests
{
    [Test]
    static void JoinGivesIdsTeamsAndState()
    {
        var h = new SessionHarness();
        var a = h.Join("Ann");
        var b = h.Join("Bo");
        var c = h.Join("Cy");
        Check.Equal(1, a.PlayerId, "first player id");
        Check.Equal(TeamId.A, a.Team, "first player on A");
        Check.Equal(TeamId.B, b.Team, "second player balances to B");
        Check.Equal(TeamId.A, c.Team, "third back to A");
        Check.True(a.IsHostPlayer, "first connection is the host's player");
        Check.False(b.IsHostPlayer, "others are not");
        Check.Equal(1, b.HostPlayerId, "clients know who hosts");
        Check.True(c.State != null && c.State.players.Count == 3, "a joiner gets the state at once");
        h.Ticks(2);
        Check.Equal("Bo", a.NameOf(2), "names travel in the roster");
        Check.Equal(3, h.Events.History.OfType<PlayerJoinedEvent>().Count(), "a join event each");
    }

    [Test]
    static void PreferredTeamAndFullMatch()
    {
        var rules = ScriptedPlay.SmokeRuleset();
        rules.maxPlayersPerTeam = 2;
        var h = new SessionHarness(rules: rules);
        Check.Equal(TeamId.B, h.Join(team: TeamId.B).Team, "asked for B");
        Check.Equal(TeamId.B, h.Join(team: TeamId.B).Team, "asked for B again");
        Check.Equal(TeamId.A, h.Join(team: TeamId.B).Team, "B is full, so A");
        h.Join();
        var fifth = h.Join();
        Check.False(fifth.IsWelcomed, "fifth player of four is refused");
        Check.Equal("The match is full.", fifth.RefusedReason, "with a reason");
        Check.Equal(4, h.Match.State.players.Count, "and never enters the match");
    }

    [Test]
    static void DifferentBuildIsRefused()
    {
        var h = new SessionHarness();
        var hello = new HelloMessage { fingerprint = "deadbeef", token = "t", name = "Old" };
        Check.Equal("This game is a different version.", h.Host.Approve(9, WireCodec.Encode(hello)), "fingerprint mismatch");
        Check.Equal("This game is a different version.", h.Host.Approve(9, new byte[] { 1, 2, 3 }), "garbage hello");
        hello.fingerprint = WireCodec.Fingerprint;
        Check.Equal(null, h.Host.Approve(9, WireCodec.Encode(hello)), "matching build gets in");
        Check.Equal("This player is already connected.", h.Host.Approve(10, WireCodec.Encode(hello)), "same token twice");
        h.Host.Left(9);
        Check.Equal(null, h.Host.Approve(10, WireCodec.Encode(hello)), "a pending join that dropped frees the token");
    }

    [Test]
    static void CommandsAreRoutedWithTheRealSender()
    {
        var h = new SessionHarness();
        var a = h.Join();
        var b = h.Join();
        // A client lying about who it is: the server uses the connection's player.
        a.Send(new PickHeroCommand { heroId = "striker" });
        var forged = new PickHeroCommand { heroId = "support", playerId = 1 };
        h.Net.InjectToServer(b, WireCodec.Encode(new CommandMessage { command = forged }));
        h.Net.Pump();
        Check.Equal("striker", h.Match.State.Player(1).heroId, "player 1 keeps their own pick");
        Check.Equal("support", h.Match.State.Player(2).heroId, "the forged command counted for its real sender");
        h.Ticks(2);
        Check.Equal("striker", b.State.Player(1).heroId, "picks reach other clients");
    }

    [Test]
    static void RejectionsGoOnlyToTheSender()
    {
        var h = new SessionHarness();
        var a = h.Join();
        var b = h.Join();
        var c = h.Join(); // team A with a
        var aSaw = new List<CommandRejectedEvent>();
        var bSaw = new List<CommandRejectedEvent>();
        var cSaw = new List<CommandRejectedEvent>();
        a.Events.Subscribe(e => { if (e is CommandRejectedEvent r) aSaw.Add(r); });
        b.Events.Subscribe(e => { if (e is CommandRejectedEvent r) bSaw.Add(r); });
        c.Events.Subscribe(e => { if (e is CommandRejectedEvent r) cSaw.Add(r); });

        h.Send(a, new PickHeroCommand { heroId = "striker" });
        h.Send(c, new PickHeroCommand { heroId = "striker" });
        Check.Equal(1, cSaw.Count, "the refused player is told");
        Check.Equal("PickHeroCommand", cSaw[0].command, "which command");
        Check.True(cSaw[0].reason.Contains("already picked"), "and why: " + cSaw[0].reason);
        Check.Equal(0, aSaw.Count + bSaw.Count, "nobody else is");

        h.Send(b, new StartMatchCommand());
        Check.Equal(1, bSaw.Count, "only the host starts the match");
        Check.Equal(MatchPhase.Lobby, h.Match.State.phase, "still in the lobby");
        h.Send(b, new FireCommand { weaponId = "rifle" });
        Check.True(bSaw.Last().reason.Contains("nothing handles FireCommand"), "commands with no module yet are refused, not dropped");
        h.Send(a, new StartMatchCommand());
        Check.Equal(MatchPhase.HeroPick, h.Match.State.phase, "the host's player starts it");
    }

    [Test]
    static void ScriptedCommandsNeedScriptedPlay()
    {
        var h = new SessionHarness(scripted: false);
        var a = h.Join();
        h.Join();
        string reason = null;
        a.Events.Subscribe(e => { if (e is CommandRejectedEvent r) reason = r.reason; });
        h.Send(a, new StartMatchCommand());
        h.RunTo(MatchPhase.Play);
        h.Send(a, new ScriptedStandCommand { zone = new ZoneId(2) });
        Check.True(reason != null && reason.Contains("nothing handles"), "a normal host refuses scripted movement");
        Check.Equal(0, h.World.PlayersInZone(new ZoneId(2)).Length, "and nobody moved");
    }

    [Test]
    static void TeamChangeInLobbyOnly()
    {
        var h = new SessionHarness();
        var a = h.Join();
        var b = h.Join();
        h.Send(b, new ChooseTeamCommand { team = TeamId.A });
        Check.Equal(TeamId.A, h.Match.State.Player(2).team, "moved to A");
        h.Ticks(2);
        Check.Equal(TeamId.A, b.Team, "the client learns its new team");
        Check.Equal(1, h.Events.History.OfType<TeamChangedEvent>().Count(), "everyone is told");
        string reason = null;
        a.Events.Subscribe(e => { if (e is CommandRejectedEvent r) reason = r.reason; });
        h.Send(a, new StartMatchCommand());
        Check.True(reason != null && reason.Contains("team B needs"), "can't start with an empty team: " + reason);
        h.Send(b, new ChooseTeamCommand { team = TeamId.B });
        h.Send(a, new StartMatchCommand());
        Check.Equal(MatchPhase.HeroPick, h.Match.State.phase, "started");
        h.Send(b, new ChooseTeamCommand { team = TeamId.A });
        Check.Equal(TeamId.B, h.Match.State.Player(2).team, "teams are fixed after the lobby");
    }

    [Test]
    static void LeaveAndRejoinKeepsThePlayer()
    {
        var h = new SessionHarness();
        var a = h.Join();
        var b = h.Join(token: "bo-token");
        h.Send(b, new PickHeroCommand { heroId = "support" });
        h.Send(a, new StartMatchCommand());
        h.RunTo(MatchPhase.Play);
        h.Match.State.Player(2).level = 4;

        h.Net.Drop(b);
        h.Net.Pump();
        Check.False(h.Match.State.Player(2).connected, "marked as gone");
        Check.Equal(1, h.Events.History.OfType<PlayerLeftEvent>().Count(), "others are told");
        h.Ticks(40);

        var back = h.Net.Connect("Bo", "bo-token", TeamId.A);
        h.Net.Pump();
        Check.Equal(2, back.PlayerId, "same player id");
        Check.Equal(TeamId.B, back.Team, "same team, whatever they ask for");
        Check.Equal("support", back.Me.heroId, "same hero");
        Check.Equal(4, back.Me.level, "same level");
        Check.False(back.Me.alive, "and they wait for a respawn like anyone else");
        Check.True(h.Events.History.OfType<PlayerJoinedEvent>().Last().rejoined, "flagged as a rejoin");

        var stranger = h.Net.Connect("New", "new-token");
        h.Net.Pump();
        Check.Equal(3, stranger.PlayerId, "a late joiner gets a new id");
        Check.Equal(MatchPhase.Play, stranger.State.phase, "and the state as it is now");
        Check.False(stranger.Me.alive, "waiting to spawn");
    }

    [Test]
    static void BadBytesDropTheSender()
    {
        var h = new SessionHarness();
        var a = h.Join();
        var b = h.Join();
        h.Net.InjectToServer(b, new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0x7f });
        h.Net.Pump();
        Check.Equal(1, h.Host.ConnectedCount, "the sender of garbage is dropped");
        // A server-only message from a client is refused too.
        h.Net.InjectToServer(a, WireCodec.Encode(new WelcomeMessage { playerId = 7 }));
        h.Net.Pump();
        Check.Equal(0, h.Host.ConnectedCount, "so is one sending a message only servers send");
    }

    sealed class TeamNote : TeamEvent { public string text; }

    [Test]
    static void TeamEventsStayOnTheTeam()
    {
        var h = new SessionHarness();
        var a = h.Join();
        var b = h.Join();
        int aSaw = 0, bSaw = 0;
        a.Events.Subscribe(e => { if (e is PhaseChangedEvent) aSaw++; });
        b.Events.Subscribe(e => { if (e is PhaseChangedEvent) bSaw++; });
        h.Host.SendToTeam(TeamId.A, new PhaseChangedEvent());
        h.Net.Pump();
        Check.Equal(1, aSaw, "team A got it");
        Check.Equal(0, bSaw, "team B did not");
        // A TeamEvent published by the rules is routed to its audience without anyone calling SendToTeam.
        string aNote = null, bNote = null;
        a.Events.Subscribe(e => { if (e is TeamNote n) aNote = n.text; });
        b.Events.Subscribe(e => { if (e is TeamNote n) bNote = n.text; });
        h.Events.Publish(new TeamNote { audience = TeamId.B, text = "push mid" });
        h.Net.Pump();
        Check.Equal("push mid", bNote, "team B's note reached team B");
        Check.Equal(null, aNote, "and not team A");
    }

    [Test]
    static void FixedTickRate()
    {
        var h = new SessionHarness(settings: new NetSettings { tickRate = 20, snapshotEveryTicks = 2 });
        var a = h.Join();
        Check.Equal(0, h.Host.Update(0.04f), "not a full tick yet");
        Check.Equal(1, h.Host.Update(0.02f), "now one");
        Check.Equal(20, h.Host.Update(1f), "a second is 20 ticks");
        Check.Equal(200, h.Host.Update(1f, speed: 10f), "fast-forward");
        h.Net.Pump();
        int before = a.SnapshotsReceived;
        h.Ticks(10);
        Check.Equal(before + 5, a.SnapshotsReceived, "a state copy every second tick");
    }

    /// <summary>Scripted players on every client play a whole match; every machine must agree at the end.</summary>
    static void FullMatch(ModeDef mode, int players)
    {
        var h = new SessionHarness(mode);
        var scripts = new List<ScriptedPlayer>();
        for (int i = 0; i < players; i++)
        {
            var c = h.Join();
            Check.True(c.IsWelcomed, "player " + (i + 1) + " joined");
            var s = new ScriptedPlayer(c, players);
            scripts.Add(s);
            c.SnapshotReceived += s.Step;
        }
        int limit = (int)((mode.timeCapSeconds + 120f) * h.Host.Settings.tickRate);
        for (int i = 0; i < limit && h.Match.State.phase != MatchPhase.MatchEnd; i++) { h.Host.Tick(); h.Net.Pump(); }
        h.Ticks(4); // the last state copy goes out

        var s0 = h.Match.State;
        Check.Equal(MatchPhase.MatchEnd, s0.phase, mode.id + ": the match finished");
        Check.True(s0.winner != TeamId.None, mode.id + ": someone won");
        Check.True(s0.scoreA + s0.scoreB > 0, mode.id + ": points were scored");
        Check.True(h.Events.History.OfType<PlayerEliminatedEvent>().Any(), mode.id + ": players went down");
        Check.True(h.Events.History.OfType<PlayerSpawnedEvent>().Any(e => !e.initial), mode.id + ": and came back");
        Check.True(h.Events.History.OfType<ZoneCapturedEvent>().Any(), mode.id + ": zones were captured");
        Check.True(s0.players.All(p => p.heroId != ""), mode.id + ": everyone has a hero");

        string truth = SessionDigest.Of(s0);
        for (int i = 0; i < h.Clients.Count; i++)
        {
            Check.Equal(truth, SessionDigest.Of(h.Clients[i].State), mode.id + ": client " + (i + 1) + " agrees with the host");
            Check.True(scripts[i].Finished, mode.id + ": client " + (i + 1) + " saw the end");
        }
    }

    [Test]
    static void FullMatchCaptureAndHold10Players() => FullMatch(ModeDef.CaptureAndHold(), 10);

    [Test]
    static void FullMatchKingOfTheHill2Players() => FullMatch(ModeDef.KingOfTheHill(), 2);

    [Test]
    static void ClientsSeeTheSameEventsAsTheHost()
    {
        var h = new SessionHarness(ModeDef.KingOfTheHill());
        var a = h.Join();
        var b = h.Join();
        var seen = new List<string>();
        b.Events.Subscribe(e => seen.Add(e.tick + " " + e.GetType().Name));
        var sa = new ScriptedPlayer(a, 2);
        var sb = new ScriptedPlayer(b, 2);
        a.SnapshotReceived += sa.Step;
        b.SnapshotReceived += sb.Step;
        h.RunTo(MatchPhase.MatchEnd, 40000);
        h.Net.Pump();
        // Everything after b's own join (b subscribed after it), minus what was sent to one player only.
        var hostLog = h.Events.History.SkipWhile(e => !(e is PlayerJoinedEvent j && j.playerId == 2)).Skip(1)
            .Select(e => e.tick + " " + e.GetType().Name).ToList();
        var clientLog = seen.Where(s => !s.EndsWith(nameof(CommandRejectedEvent))).ToList();
        Check.Equal(hostLog.Count, clientLog.Count, "same number of events");
        Check.True(hostLog.SequenceEqual(clientLog), "same events in the same order");
    }

    [Test]
    static void SnapshotSizeIsSmall()
    {
        var h = new SessionHarness();
        for (int i = 0; i < 10; i++) h.Join("Player number " + i);
        int size = WireCodec.Encode(new SnapshotMessage { snapshot = h.Host.MakeSnapshot() }).Length;
        // 10 state copies a second to 10 players: keep one well under a network packet's worth.
        Check.True(size < 1000, "a 10-player state copy is " + size + " bytes");
    }
}

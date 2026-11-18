using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DuckAndCover.Core;

/// <summary>
/// The network smoke test without Unity. It starts one host process and the client processes,
/// connected by real TCP sockets on this machine, and has scripted players run a whole match. Every
/// process writes what it ended up believing (result, score, zones, players and levels); the run
/// fails unless they are all identical.
///
///   dotnet run --project Tools/netsmoke                       2 players, king of the hill
///   dotnet run --project Tools/netsmoke -- 8 capture_hold     8 players (4v4), capture and hold
///   dotnet run --project Tools/netsmoke -- 4 flag_rush        4 players, Flag Rush
///   dotnet run --project Tools/netsmoke -- 8 overrun          8 players (4v4), Overrun on Breakwater's lane (waves, camps, Bastions)
///
/// This checks the session code, the wire format and the rules across processes. It does not check
/// Netcode for GameObjects: that is Tools/unity netsmoke, which needs Unity.
/// </summary>
static class Program
{
    const float Speed = 40f; // match seconds per real second

    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "host") return RunHost(int.Parse(args[1]), int.Parse(args[2]), args[3], args[4]);
            if (args.Length > 0 && args[0] == "join") return RunClient(int.Parse(args[1]), args[2], args[3]);
            return Orchestrate(args.Length > 0 ? int.Parse(args[0]) : 2, args.Length > 1 ? args[1] : "king_of_the_hill");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("netsmoke: " + e);
            return 1;
        }
    }

    // ---- Starting the processes and comparing them ----

    static int Orchestrate(int players, string mode)
    {
        int most = 2 * Ruleset.Default.maxPlayersPerTeam;
        if (players > most)
        {
            Console.Error.WriteLine("netsmoke: teams are " + Ruleset.Default.maxPlayersPerTeam + " a side, so at most " + most + " players.");
            return 2;
        }
        string dir = Path.Combine(Path.GetTempPath(), "duck-netsmoke-" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(dir);
        int port = FreePort();
        string self = Environment.ProcessPath;
        var procs = new List<Process>();
        var outs = new List<string>();

        Process Launch(string name, string arguments)
        {
            string file = Path.Combine(dir, name + ".txt");
            outs.Add(file);
            var p = Process.Start(new ProcessStartInfo(self, arguments + " \"" + file + "\"") { UseShellExecute = false });
            procs.Add(p);
            return p;
        }

        Console.WriteLine("netsmoke: " + players + " players, " + mode + ", port " + port + ", " + (players + 0) + " processes");
        Launch("host", "host " + port + " " + players + " " + mode);
        Thread.Sleep(500);
        for (int i = 2; i <= players; i++) Launch("client" + i, "join " + port + " Client" + i);

        bool ok = true;
        foreach (var p in procs)
        {
            if (!p.WaitForExit(180000)) { p.Kill(); Console.WriteLine("netsmoke: process " + p.Id + " did not finish"); ok = false; }
            else if (p.ExitCode != 0) { Console.WriteLine("netsmoke: process " + p.Id + " exited with " + p.ExitCode); ok = false; }
        }

        string truth = File.Exists(outs[0]) ? File.ReadAllText(outs[0]) : null;
        Console.WriteLine("\nHost says:\n" + (truth ?? "(nothing)"));
        for (int i = 1; i < outs.Count; i++)
        {
            string theirs = File.Exists(outs[i]) ? File.ReadAllText(outs[i]) : null;
            if (theirs == null || theirs != truth)
            {
                ok = false;
                Console.WriteLine(Path.GetFileNameWithoutExtension(outs[i]) + " DISAGREES:\n" + (theirs ?? "(nothing)"));
            }
        }
        if (truth == null || !truth.Contains("phase=MatchEnd")) { ok = false; Console.WriteLine("The match never finished."); }
        try { Directory.Delete(dir, true); } catch (IOException) { }
        Console.WriteLine(ok ? "NETSMOKE PASSED: " + outs.Count + " processes agree." : "NETSMOKE FAILED");
        return ok ? 0 : 1;
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // ---- Framing: 4 bytes of length, then the message ----

    static void WriteFrame(NetworkStream stream, byte[] message)
    {
        var frame = new byte[message.Length + 4];
        BitConverter.TryWriteBytes(new Span<byte>(frame, 0, 4), message.Length);
        Buffer.BlockCopy(message, 0, frame, 4, message.Length);
        lock (stream) stream.Write(frame, 0, frame.Length);
    }

    static byte[] ReadFrame(NetworkStream stream)
    {
        var header = ReadExactly(stream, 4);
        if (header == null) return null;
        int length = BitConverter.ToInt32(header, 0);
        if (length < 0 || length > 1 << 20) return null;
        return ReadExactly(stream, length);
    }

    static byte[] ReadExactly(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n;
            try { n = stream.Read(buffer, got, count - got); }
            catch (IOException) { return null; }
            catch (ObjectDisposedException) { return null; }
            if (n <= 0) return null;
            got += n;
        }
        return buffer;
    }

    // ---- Host process ----

    sealed class Incoming
    {
        public ulong connection;
        public byte[] message; // null = the connection closed
        public bool hello;
    }

    sealed class TcpServer : IServerTransport
    {
        public readonly ConcurrentDictionary<ulong, TcpClient> sockets = new ConcurrentDictionary<ulong, TcpClient>();
        public readonly ConcurrentQueue<Incoming> inbox = new ConcurrentQueue<Incoming>();
        long next = 100;

        public void Listen(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            new Thread(() =>
            {
                while (true)
                {
                    var socket = listener.AcceptTcpClient();
                    socket.NoDelay = true;
                    ulong id = (ulong)Interlocked.Increment(ref next);
                    sockets[id] = socket;
                    new Thread(() =>
                    {
                        var stream = socket.GetStream();
                        bool first = true;
                        while (true)
                        {
                            var message = ReadFrame(stream);
                            inbox.Enqueue(new Incoming { connection = id, message = message, hello = first && message != null });
                            first = false;
                            if (message == null) return;
                        }
                    }) { IsBackground = true }.Start();
                }
            }) { IsBackground = true }.Start();
        }

        public void Send(ulong connection, byte[] message)
        {
            if (!sockets.TryGetValue(connection, out var socket)) return;
            try { WriteFrame(socket.GetStream(), message); }
            catch (IOException) { }
            catch (InvalidOperationException) { }
        }

        public void Disconnect(ulong connection, string reason)
        {
            if (sockets.TryRemove(connection, out var socket)) socket.Close();
        }
    }

    /// <summary>The host's own player, in-process, as it is in the game.</summary>
    sealed class LocalLink : IClientTransport
    {
        public const ulong Id = 1;
        public readonly Queue<byte[]> toHost = new Queue<byte[]>();
        public void Send(byte[] message) => toHost.Enqueue(message);
    }

    sealed class HostTransport : IServerTransport
    {
        public TcpServer tcp;
        public SessionClient local;
        public readonly Queue<byte[]> toLocal = new Queue<byte[]>();
        public void Send(ulong connection, byte[] message)
        {
            if (connection == LocalLink.Id) toLocal.Enqueue(message);
            else tcp.Send(connection, message);
        }
        public void Disconnect(ulong connection, string reason) => tcp.Disconnect(connection, reason);
    }

    static int RunHost(int port, int players, string modeId, string outFile)
    {
        var world = new ScriptedWorld();
        var events = new GameEventBus();
        var mode = modeId == "capture_hold" ? ModeDef.CaptureAndHold() : modeId == "flag_rush" ? ModeDef.FlagRush()
            : modeId == "overrun" ? ModeDef.Overrun() : ModeDef.KingOfTheHill();
        var match = new MatchRules(ScriptedPlay.SmokeRuleset(), mode, ScriptedPlay.PlaceholderMap(), world, events);
        var tcp = new TcpServer();
        var transport = new HostTransport { tcp = tcp };
        var host = new SessionHost(match, events, transport);
        var (heroes, abilities) = LoadHeroData();
        ScriptedPlay.Attach(host, world, events, heroes, abilities);

        var link = new LocalLink();
        var me = new SessionClient(link, "Host", "host-token");
        transport.local = me;
        var script = new ScriptedPlayer(me, players);
        me.SnapshotReceived += script.Step;
        string refusal = host.Approve(LocalLink.Id, me.HelloBytes(), isHost: true);
        if (refusal != null) throw new InvalidOperationException(refusal);
        host.Connected(LocalLink.Id);

        tcp.Listen(port);
        var clock = Stopwatch.StartNew();
        double last = 0, endedAt = -1;
        int mostUnits = 0;
        while (clock.Elapsed.TotalSeconds < 150)
        {
            while (tcp.inbox.TryDequeue(out var m))
            {
                if (m.message == null) host.Left(m.connection);
                else if (m.hello)
                {
                    string no = host.Approve(m.connection, m.message);
                    if (no == null) host.Connected(m.connection);
                    else { tcp.Send(m.connection, WireCodec.Encode(new RefusedMessage { reason = no })); tcp.Disconnect(m.connection, no); }
                }
                else host.Receive(m.connection, m.message);
            }
            for (int guard = 0; guard < 10000 && (transport.toLocal.Count > 0 || link.toHost.Count > 0); guard++)
            {
                if (transport.toLocal.Count > 0) me.Receive(transport.toLocal.Dequeue());
                else host.Receive(LocalLink.Id, link.toHost.Dequeue());
            }
            double now = clock.Elapsed.TotalSeconds;
            host.Update((float)(now - last), Speed);
            last = now;
            if (me.Snapshot != null && me.Snapshot.units != null && me.Snapshot.units.Count > mostUnits) mostUnits = me.Snapshot.units.Count;

            if (match.State.phase == MatchPhase.MatchEnd)
            {
                // Keep serving for a moment so the final state reaches every client.
                if (endedAt < 0) endedAt = now;
                if (now - endedAt > 1.5) break;
            }
            Thread.Sleep(1);
        }
        File.WriteAllText(outFile, SessionDigest.Of(match.State, me.Snapshot == null ? null : me.Snapshot.overrun));
        if (match.OverrunMode) Console.WriteLine("netsmoke host: most units in one state copy " + mostUnits + ", match time " + match.State.elapsed.ToString("0") + " s");
        return match.State.phase == MatchPhase.MatchEnd ? 0 : 1;
    }

    /// <summary>
    /// The hero and ability data the game ships (Data/Resources), read the way the tests read it, so
    /// scripted players use real abilities and upgrades. The test hero is the fallback.
    /// </summary>
    static (HeroCatalog, AbilityLibrary) LoadHeroData()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "DuckAndCover"))) dir = Path.GetDirectoryName(dir);
        if (dir == null) throw new InvalidOperationException("can't find the repository (Assets/DuckAndCover)");
        string data = Path.Combine(dir, "Assets", "DuckAndCover", "Data", "Resources");
        var opts = new JsonSerializerOptions { IncludeFields = true };
        var heroes = Directory.GetFiles(Path.Combine(data, "Heroes"), "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<HeroDef>(File.ReadAllText(f), opts));
        var files = Directory.GetFiles(Path.Combine(data, "Abilities"), "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => JsonSerializer.Deserialize<AbilityDataFile>(File.ReadAllText(f), opts)).ToArray();
        return (new HeroCatalog(heroes, "test_hero"), AbilityLibrary.From(files));
    }

    // ---- Client process ----

    sealed class TcpLink : IClientTransport
    {
        public NetworkStream stream;
        public void Send(byte[] message) => WriteFrame(stream, message);
    }

    static int RunClient(int port, string name, string outFile)
    {
        var socket = new TcpClient { NoDelay = true };
        socket.Connect(IPAddress.Loopback, port);
        var link = new TcpLink { stream = socket.GetStream() };
        var client = new SessionClient(link, name, "token-" + name + "-" + Process.GetCurrentProcess().Id);
        var script = new ScriptedPlayer(client, int.MaxValue); // only the host's player starts the match
        client.SnapshotReceived += script.Step;
        link.Send(client.HelloBytes());

        // After the end, wait for the host to stop sending: the last state is the one to report.
        while (true)
        {
            var message = ReadFrame(link.stream);
            if (message == null) break;
            client.Receive(message);
            if (client.RefusedReason != null) { Console.Error.WriteLine(name + " refused: " + client.RefusedReason); return 1; }
        }
        File.WriteAllText(outFile, SessionDigest.Of(client.State, client.Snapshot == null ? null : client.Snapshot.overrun));
        return script.Finished && script.CommandsSent > 0 ? 0 : 1;
    }
}

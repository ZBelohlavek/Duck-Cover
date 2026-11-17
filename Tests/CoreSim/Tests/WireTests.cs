using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DuckAndCover.Core;

static class WireTests
{
    [Test]
    static void NumbersAndStringsRoundTrip()
    {
        var w = new WireWriter(1);
        int[] ints = { 0, 1, -1, 63, -64, 64, 300, int.MaxValue, int.MinValue };
        foreach (var i in ints) w.VarInt(i);
        w.VarLong(long.MinValue);
        w.VarULong(ulong.MaxValue);
        float[] floats = { 0f, -0f, 1.5f, float.MaxValue, float.Epsilon, float.NaN, float.PositiveInfinity };
        foreach (var f in floats) w.Float(f);
        w.Double(Math.PI);
        w.String(null); w.String(""); w.String("Zoë 🦆");
        w.Bool(true); w.Bool(false);

        var r = new WireReader(w.ToArray());
        foreach (var i in ints) Check.Equal(i, r.VarInt(), "int");
        Check.Equal(long.MinValue, r.VarLong(), "long");
        Check.Equal(ulong.MaxValue, r.VarULong(), "ulong");
        foreach (var f in floats) Check.Equal(BitConverter.SingleToInt32Bits(f), BitConverter.SingleToInt32Bits(r.Float()), "float bits");
        Check.Equal(Math.PI, r.Double(), "double");
        Check.Equal(null, r.String(), "null string");
        Check.Equal("", r.String(), "empty string");
        Check.Equal("Zoë 🦆", r.String(), "unicode string");
        Check.True(r.Bool() && !r.Bool(), "bools");
        Check.True(r.AtEnd, "nothing left");
        var small = new WireWriter(); small.VarInt(5);
        Check.Equal(1, small.Length, "small numbers take one byte");
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (WireException) { return true; }
    }

    [Test]
    static void BadBytesThrowWireException()
    {
        Check.True(Throws(() => new WireReader(new byte[0]).Byte()), "reading past the end");
        Check.True(Throws(() => new WireReader(new byte[] { 0x80, 0x80 }).VarInt()), "unfinished varint");
        Check.True(Throws(() => WireCodec.Decode(new byte[] { 0xfe, 0x7f })), "unknown type id");
        var huge = new WireWriter(); huge.VarInt(int.MaxValue);
        Check.True(Throws(() => new WireReader(huge.ToArray()).String()), "absurd string length");
        var good = WireCodec.Encode(new CommandMessage { command = new PickHeroCommand { heroId = "striker" } });
        Check.True(Throws(() => WireCodec.Decode(good.Take(good.Length - 1).ToArray())), "cut-off message");
        Check.True(Throws(() => WireCodec.Decode(good.Concat(new byte[] { 0 }).ToArray())), "stray bytes");
        // A command slot holding an event is refused, not cast.
        var w = new WireWriter();
        w.VarInt(IdOf(typeof(CommandMessage)));
        WireCodec.WriteTagged(w, new MapShiftEvent());
        Check.True(Throws(() => WireCodec.Decode(w.ToArray())), "wrong kind of object in a field");
        // Random bytes never crash with anything but a WireException.
        var rng = new Random(12345);
        int other = 0;
        for (int i = 0; i < 20000; i++)
        {
            var junk = new byte[rng.Next(1, 40)];
            rng.NextBytes(junk);
            try { WireCodec.Decode(junk); }
            catch (WireException) { }
            catch (Exception) { other++; }
        }
        Check.Equal(0, other, "random input only ever fails as a WireException");
    }

    static int IdOf(Type t) => WireCodec.TaggedTypes.ToList().IndexOf(t) + 1;

    /// <summary>Fill every public field with a non-default value, so a field the codec skips shows up.</summary>
    static object Filled(Type t, int seed, int depth = 0)
    {
        if (t == typeof(int)) return 7 + seed;
        if (t == typeof(float)) return 1.25f + seed;
        if (t == typeof(bool)) return true;
        if (t == typeof(string)) return "s" + seed;
        if (t == typeof(byte)) return (byte)(3 + seed);
        if (t.IsEnum) { var v = Enum.GetValues(t); return v.GetValue(v.Length - 1); }
        if (t.IsArray)
        {
            var a = Array.CreateInstance(t.GetElementType(), 2);
            for (int i = 0; i < 2; i++) a.SetValue(Filled(t.GetElementType(), seed + i, depth + 1), i);
            return a;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(t);
            for (int i = 0; i < 2; i++) list.Add(Filled(t.GetGenericArguments()[0], seed + i, depth + 1));
            return list;
        }
        if (t == typeof(GameEvent)) return depth > 3 ? null : Filled(typeof(MatchEndedEvent), seed, depth + 1);
        if (t == typeof(MatchCommand)) return depth > 3 ? null : Filled(typeof(FireCommand), seed, depth + 1);
        if (t.IsAbstract || t.IsInterface) return null;
        var o = Activator.CreateInstance(t, true);
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.IsInitOnly || f.IsLiteral) continue;
            f.SetValue(o, Filled(f.FieldType, seed + 1, depth + 1));
        }
        return o;
    }

    /// <summary>Text of every public field, recursively: two objects with the same dump are the same data.</summary>
    static string Dump(object o)
    {
        if (o == null) return "null";
        var t = o.GetType();
        if (o is float f) return BitConverter.SingleToInt32Bits(f).ToString("x");
        if (t.IsPrimitive || t.IsEnum || o is string) return o.ToString();
        if (o is System.Collections.IEnumerable e) return "[" + string.Join(",", e.Cast<object>().Select(Dump)) + "]";
        return t.Name + "{" + string.Join(",", t.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => !x.IsInitOnly && !x.IsLiteral).OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Name + "=" + Dump(x.GetValue(o)))) + "}";
    }

    [Test]
    static void EveryEventCommandAndMessageRoundTrips()
    {
        Check.True(WireCodec.TaggedTypes.Count >= 30, "found the events, commands and messages: " + WireCodec.TaggedTypes.Count);
        foreach (var t in WireCodec.TaggedTypes)
        {
            var original = Filled(t, 1);
            object copy = null;
            try { copy = WireCodec.Decode(WireCodec.Encode(original)); }
            catch (Exception ex) { Check.Fail(t.Name + " can't travel: " + ex.Message); continue; }
            Check.Equal(Dump(original), Dump(copy), t.Name + " survives the wire");
        }
        // Every concrete event and command in Core must be reachable, or a module's messages would silently not send.
        foreach (var t in typeof(GameEvent).Assembly.GetTypes())
            if (!t.IsAbstract && (typeof(GameEvent).IsAssignableFrom(t) || typeof(MatchCommand).IsAssignableFrom(t)))
                Check.True(WireCodec.IsTagged(t), t.Name + " has a wire id");
    }

    [Test]
    static void SnapshotRoundTripsExactly()
    {
        var h = new MatchHarness(ModeDef.KingOfTheHill());
        h.Take(MatchHarness.Hills[0], TeamId.A, 2);
        h.Match.EliminatePlayer(101, 1);
        h.Run(7.3f);
        var snap = h.Match.TakeSnapshot();
        snap.roster.Add(new SessionPlayer { id = 1, name = "Ann", connected = true, host = true });
        var copy = ((SnapshotMessage)WireCodec.Decode(WireCodec.Encode(new SnapshotMessage { snapshot = snap }))).snapshot;
        Check.Equal(Dump(snap), Dump(copy), "a state copy is identical after the wire, float bits included");
        Check.Equal(Dump(snap.match), Dump(WireCodec.Copy(snap.match)), "Copy");
    }

    [Test]
    static void FingerprintIsStableAndBytesAreDeterministic()
    {
        Check.Equal(8, WireCodec.Fingerprint.Length, "fingerprint is 8 hex digits");
        Check.Equal(WireCodec.Fingerprint, WireCodec.Fingerprint, "and stable");
        var a = WireCodec.Encode(Filled(typeof(MatchEndedEvent), 3));
        var b = WireCodec.Encode(Filled(typeof(MatchEndedEvent), 3));
        Check.True(a.SequenceEqual(b), "same object, same bytes");
    }
}

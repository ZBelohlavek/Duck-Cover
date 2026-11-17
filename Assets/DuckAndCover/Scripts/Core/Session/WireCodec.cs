using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Turns events, commands, snapshots and session messages into bytes and back, by their public
    /// fields. Nothing has to be written per type: a module that adds an event class, a command or a
    /// field on a snapshot partial gets it sent for free.
    ///
    /// Supported field types: bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double,
    /// string, enums, arrays and List&lt;T&gt; of supported types, and classes or structs made of
    /// supported fields. A field declared as an abstract type (GameEvent, MatchCommand) is written
    /// with its type id. Readonly, const and [NonSerialized] fields are skipped. Fields go in name
    /// order, so both ends agree however the compiler orders partial classes.
    ///
    /// Every concrete GameEvent and MatchCommand in the Core assembly gets a type id, and
    /// <see cref="Fingerprint"/> hashes every type's shape: two builds that would read each other's
    /// bytes differently have different fingerprints and refuse to connect.
    /// </summary>
    public static class WireCodec
    {
        static readonly List<Type> types = new List<Type>();
        static readonly Dictionary<Type, int> typeIds = new Dictionary<Type, int>();
        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
        static readonly object gate = new object();
        static string fingerprint;

        static WireCodec()
        {
            var found = new List<Type>();
            foreach (var t in SafeTypes(typeof(GameEvent).Assembly))
            {
                if (t.IsAbstract || t.IsGenericTypeDefinition) continue;
                if (typeof(GameEvent).IsAssignableFrom(t) || typeof(MatchCommand).IsAssignableFrom(t) || typeof(SessionMessage).IsAssignableFrom(t))
                    found.Add(t);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            foreach (var t in found)
            {
                typeIds[t] = types.Count + 1; // 0 means null
                types.Add(t);
            }
        }

        static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                var ok = new List<Type>();
                foreach (var t in e.Types) if (t != null) ok.Add(t);
                return ok;
            }
        }

        /// <summary>Every type that can travel as a tagged object, in id order (id = index + 1).</summary>
        public static IReadOnlyList<Type> TaggedTypes => types;

        public static bool IsTagged(Type t) => typeIds.ContainsKey(t);

        /// <summary>
        /// A short hash of every tagged type's name and fields (recursively). Equal fingerprints mean
        /// two builds read each other's messages the same way.
        /// </summary>
        public static string Fingerprint
        {
            get
            {
                lock (gate)
                {
                    if (fingerprint != null) return fingerprint;
                    var sb = new StringBuilder();
                    var seen = new HashSet<Type>();
                    foreach (var t in types) Describe(t, sb, seen);
                    Describe(typeof(MatchSnapshot), sb, seen);
                    uint h = 2166136261;
                    foreach (char c in sb.ToString()) { h ^= c; h *= 16777619; }
                    fingerprint = h.ToString("x8");
                    return fingerprint;
                }
            }
        }

        static void Describe(Type t, StringBuilder sb, HashSet<Type> seen)
        {
            if (!seen.Add(t)) return;
            sb.Append(t.FullName).Append('{');
            var nested = new List<Type>();
            foreach (var f in Fields(t))
            {
                sb.Append(f.Name).Append(':').Append(f.FieldType.FullName).Append(';');
                var inner = Element(f.FieldType);
                if (IsComposite(inner)) nested.Add(inner);
            }
            sb.Append('}');
            foreach (var n in nested) Describe(n, sb, seen);
        }

        static Type Element(Type t)
        {
            if (t.IsArray) return Element(t.GetElementType());
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Element(t.GetGenericArguments()[0]);
            if (t.IsEnum) return Enum.GetUnderlyingType(t);
            return t;
        }

        static bool IsComposite(Type t) =>
            !t.IsPrimitive && t != typeof(string) && !t.IsEnum && !t.IsAbstract && t != typeof(object) && !t.IsArray;

        // ---- Public entry points ----

        /// <summary>Bytes for a tagged object (an event, a command or a session message).</summary>
        public static byte[] Encode(object value)
        {
            var w = new WireWriter();
            WriteTagged(w, value);
            return w.ToArray();
        }

        /// <summary>The object <see cref="Encode"/> wrote. Throws <see cref="WireException"/> on bad bytes.</summary>
        public static object Decode(byte[] data)
        {
            var r = new WireReader(data);
            var value = ReadTagged(r);
            if (!r.AtEnd) throw new WireException(r.Remaining + " stray bytes after the message");
            return value;
        }

        public static void WriteTagged(WireWriter w, object value)
        {
            if (value == null) { w.VarInt(0); return; }
            if (!typeIds.TryGetValue(value.GetType(), out int id))
                throw new ArgumentException(value.GetType().FullName + " can't travel: only GameEvents, MatchCommands and session messages in Core can.");
            w.VarInt(id);
            WriteFields(w, value, value.GetType());
        }

        public static object ReadTagged(WireReader r)
        {
            int id = r.VarInt();
            if (id == 0) return null;
            if (id < 1 || id > types.Count) throw new WireException("unknown type id " + id);
            var t = types[id - 1];
            return ReadFields(r, t);
        }

        /// <summary>Write any supported value whose type both ends know (a snapshot, a state).</summary>
        public static void Write(WireWriter w, object value, Type declared) => WriteValue(w, value, declared);

        public static object Read(WireReader r, Type declared) => ReadValue(r, declared);

        public static byte[] EncodeAs<T>(T value)
        {
            var w = new WireWriter();
            WriteValue(w, value, typeof(T));
            return w.ToArray();
        }

        public static T DecodeAs<T>(byte[] data)
        {
            var r = new WireReader(data);
            var value = (T)ReadValue(r, typeof(T));
            if (!r.AtEnd) throw new WireException("stray bytes");
            return value;
        }

        /// <summary>A deep copy made by a trip through the wire format.</summary>
        public static T Copy<T>(T value) => DecodeAs<T>(EncodeAs(value));

        // ---- Fields ----

        /// <summary>FieldAttributes.NotSerialized, by value: the named member is marked obsolete in newer .NET.</summary>
        const int NotSerializedFlag = 0x80;

        static FieldInfo[] Fields(Type t)
        {
            lock (gate)
            {
                if (fieldCache.TryGetValue(t, out var cached)) return cached;
                var list = new List<FieldInfo>();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsInitOnly || f.IsLiteral || ((int)f.Attributes & NotSerializedFlag) != 0) continue;
                    list.Add(f);
                }
                list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                var arr = list.ToArray();
                fieldCache[t] = arr;
                return arr;
            }
        }

        static void WriteFields(WireWriter w, object value, Type t)
        {
            foreach (var f in Fields(t)) WriteValue(w, f.GetValue(value), f.FieldType);
        }

        static object ReadFields(WireReader r, Type t)
        {
            object value;
            try { value = Activator.CreateInstance(t, true); }
            catch (Exception e) { throw new WireException("can't create " + t.Name + ": " + e.Message); }
            foreach (var f in Fields(t)) f.SetValue(value, ReadValue(r, f.FieldType));
            return value;
        }

        // ---- Values ----

        static void WriteValue(WireWriter w, object v, Type t)
        {
            if (t.IsEnum) { w.VarLong(Convert.ToInt64(v)); return; }
            if (t == typeof(int)) { w.VarInt((int)v); return; }
            if (t == typeof(float)) { w.Float((float)v); return; }
            if (t == typeof(bool)) { w.Bool((bool)v); return; }
            if (t == typeof(string)) { w.String((string)v); return; }
            if (t == typeof(byte)) { w.Byte((byte)v); return; }
            if (t == typeof(sbyte)) { w.VarInt((sbyte)v); return; }
            if (t == typeof(short)) { w.VarInt((short)v); return; }
            if (t == typeof(ushort)) { w.VarInt((ushort)v); return; }
            if (t == typeof(uint)) { w.VarULong((uint)v); return; }
            if (t == typeof(long)) { w.VarLong((long)v); return; }
            if (t == typeof(ulong)) { w.VarULong((ulong)v); return; }
            if (t == typeof(double)) { w.Double((double)v); return; }
            if (t.IsArray)
            {
                var a = (Array)v;
                if (a == null) { w.VarInt(-1); return; }
                var e = t.GetElementType();
                w.VarInt(a.Length);
                for (int i = 0; i < a.Length; i++) WriteValue(w, a.GetValue(i), e);
                return;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)v;
                if (list == null) { w.VarInt(-1); return; }
                var e = t.GetGenericArguments()[0];
                w.VarInt(list.Count);
                for (int i = 0; i < list.Count; i++) WriteValue(w, list[i], e);
                return;
            }
            if (t.IsAbstract || t == typeof(object)) { WriteTagged(w, v); return; }
            if (t.IsValueType) { WriteFields(w, v, t); return; }
            if (t.IsClass)
            {
                if (v == null) { w.Bool(false); return; }
                if (v.GetType() != t) throw new ArgumentException("field of type " + t.Name + " holds a " + v.GetType().Name + "; declare it abstract to send subclasses");
                w.Bool(true);
                WriteFields(w, v, t);
                return;
            }
            throw new ArgumentException("can't send a " + t.FullName);
        }

        static object ReadValue(WireReader r, Type t)
        {
            if (t.IsEnum)
            {
                long raw = r.VarLong();
                return Enum.ToObject(t, raw);
            }
            if (t == typeof(int)) return r.VarInt();
            if (t == typeof(float)) return r.Float();
            if (t == typeof(bool)) return r.Bool();
            if (t == typeof(string)) return r.String();
            if (t == typeof(byte)) return r.Byte();
            if (t == typeof(sbyte)) return checked((sbyte)r.VarInt());
            if (t == typeof(short)) return checked((short)r.VarInt());
            if (t == typeof(ushort)) return checked((ushort)r.VarInt());
            if (t == typeof(uint)) return checked((uint)r.VarULong());
            if (t == typeof(long)) return r.VarLong();
            if (t == typeof(ulong)) return r.VarULong();
            if (t == typeof(double)) return r.Double();
            if (t.IsArray)
            {
                int n = r.Count();
                if (n < 0) return null;
                var e = t.GetElementType();
                var a = Array.CreateInstance(e, n);
                for (int i = 0; i < n; i++) a.SetValue(ReadValue(r, e), i);
                return a;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                int n = r.Count();
                if (n < 0) return null;
                var e = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);
                for (int i = 0; i < n; i++) list.Add(ReadValue(r, e));
                return list;
            }
            if (t.IsAbstract || t == typeof(object))
            {
                var v = ReadTagged(r);
                if (v != null && !t.IsInstanceOfType(v)) throw new WireException("a " + v.GetType().Name + " where a " + t.Name + " belongs");
                return v;
            }
            if (t.IsValueType) return ReadFields(r, t);
            if (t.IsClass) return r.Bool() ? ReadFields(r, t) : null;
            throw new WireException("can't read a " + t.FullName);
        }
    }
}

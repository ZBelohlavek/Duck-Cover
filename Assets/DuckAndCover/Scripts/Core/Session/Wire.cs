using System;
using System.Collections.Generic;
using System.Text;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Thrown when bytes from the network can't be read: truncated, too large, an unknown type id, or
    /// a build that doesn't match. The session drops the peer that sent them.
    /// </summary>
    public sealed class WireException : Exception
    {
        public WireException(string message) : base(message) { }
    }

    /// <summary>
    /// Writes the compact binary format the session uses on the wire. Integers are zig-zag varints,
    /// floats are their exact 4 bytes (little-endian), strings are UTF-8 with a length.
    /// The same bytes come out on every platform, so both ends of a connection agree.
    /// </summary>
    public sealed class WireWriter
    {
        byte[] buffer;
        int length;

        public WireWriter(int capacity = 256) { buffer = new byte[Math.Max(16, capacity)]; }

        public int Length => length;

        public byte[] ToArray()
        {
            var copy = new byte[length];
            Buffer.BlockCopy(buffer, 0, copy, 0, length);
            return copy;
        }

        public void Clear() => length = 0;

        void Ensure(int extra)
        {
            if (length + extra <= buffer.Length) return;
            int size = buffer.Length * 2;
            while (size < length + extra) size *= 2;
            Array.Resize(ref buffer, size);
        }

        public void Byte(byte v)
        {
            Ensure(1);
            buffer[length++] = v;
        }

        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);

        /// <summary>Unsigned LEB128.</summary>
        public void VarULong(ulong v)
        {
            Ensure(10);
            while (v >= 0x80)
            {
                buffer[length++] = (byte)(v | 0x80);
                v >>= 7;
            }
            buffer[length++] = (byte)v;
        }

        /// <summary>Signed values zig-zag encoded, so small negatives stay small.</summary>
        public void VarLong(long v) => VarULong((ulong)((v << 1) ^ (v >> 63)));

        public void VarInt(int v) => VarLong(v);

        public void Fixed32(uint v)
        {
            Ensure(4);
            buffer[length++] = (byte)v;
            buffer[length++] = (byte)(v >> 8);
            buffer[length++] = (byte)(v >> 16);
            buffer[length++] = (byte)(v >> 24);
        }

        public void Fixed64(ulong v)
        {
            Fixed32((uint)v);
            Fixed32((uint)(v >> 32));
        }

        public void Float(float v) => Fixed32((uint)BitConverter.SingleToInt32Bits(v));

        public void Double(double v) => Fixed64((ulong)BitConverter.DoubleToInt64Bits(v));

        /// <summary>Null is written as length -1, so it survives the trip.</summary>
        public void String(string v)
        {
            if (v == null) { VarInt(-1); return; }
            var bytes = Encoding.UTF8.GetBytes(v);
            VarInt(bytes.Length);
            Bytes(bytes);
        }

        public void Bytes(byte[] v)
        {
            Ensure(v.Length);
            Buffer.BlockCopy(v, 0, buffer, length, v.Length);
            length += v.Length;
        }
    }

    /// <summary>Reads what <see cref="WireWriter"/> wrote. Every read is bounds-checked.</summary>
    public sealed class WireReader
    {
        /// <summary>Longest string or list the reader accepts, so a bad length can't allocate gigabytes.</summary>
        public const int MaxCount = 1 << 16;

        readonly byte[] buffer;
        int position;
        readonly int end;

        public WireReader(byte[] data) : this(data, 0, data == null ? 0 : data.Length) { }

        public WireReader(byte[] data, int offset, int count)
        {
            if (data == null) throw new WireException("no data");
            if (offset < 0 || count < 0 || offset + count > data.Length) throw new WireException("bad range");
            buffer = data;
            position = offset;
            end = offset + count;
        }

        public int Remaining => end - position;
        public bool AtEnd => position >= end;

        void Need(int n)
        {
            if (n < 0 || position + n > end) throw new WireException("message ended early");
        }

        public byte Byte()
        {
            Need(1);
            return buffer[position++];
        }

        public bool Bool()
        {
            byte b = Byte();
            if (b > 1) throw new WireException("bad bool");
            return b == 1;
        }

        public ulong VarULong()
        {
            ulong result = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                byte b = Byte();
                result |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return result;
            }
            throw new WireException("varint too long");
        }

        public long VarLong()
        {
            ulong v = VarULong();
            return (long)(v >> 1) ^ -(long)(v & 1);
        }

        public int VarInt()
        {
            long v = VarLong();
            if (v < int.MinValue || v > int.MaxValue) throw new WireException("int out of range");
            return (int)v;
        }

        public uint Fixed32()
        {
            Need(4);
            uint v = (uint)(buffer[position] | buffer[position + 1] << 8 | buffer[position + 2] << 16 | buffer[position + 3] << 24);
            position += 4;
            return v;
        }

        public ulong Fixed64()
        {
            ulong lo = Fixed32();
            ulong hi = Fixed32();
            return lo | hi << 32;
        }

        public float Float() => BitConverter.Int32BitsToSingle((int)Fixed32());

        public double Double() => BitConverter.Int64BitsToDouble((long)Fixed64());

        /// <summary>A length written before a string or list: -1 for null, otherwise 0..MaxCount.</summary>
        public int Count()
        {
            int n = VarInt();
            if (n < -1 || n > MaxCount) throw new WireException("bad length " + n);
            return n;
        }

        public string String()
        {
            int n = Count();
            if (n < 0) return null;
            Need(n);
            string s = Encoding.UTF8.GetString(buffer, position, n);
            position += n;
            return s;
        }
    }
}

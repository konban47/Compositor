using System.Buffers.Binary;
using Compositor.Core.Model;

namespace Compositor.Core.IO.PSD;

/// <summary>Seekable 64-bit PSD input; skips unknown blocks without materializing them.</summary>
internal sealed class PsdFileCursor(Stream stream, CancellationToken cancellation)
{
    public long Offset { get => stream.Position; set { if (value < 0 || value > Length) throw PsdFailure.Truncated; stream.Position = value; } }
    public long Length => stream.Length;
    public void Need(long count) { cancellation.ThrowIfCancellationRequested(); if (count < 0 || Offset > Length - count) throw PsdFailure.Truncated; }
    public void Skip(long count) { Need(count); Offset += count; }
    public void Read(Span<byte> target) { Need(target.Length); stream.ReadExactly(target); }
    public byte U8() { Need(1); var value = stream.ReadByte(); if (value < 0) throw PsdFailure.Truncated; return (byte)value; }
    public ushort U16() { Span<byte> b = stackalloc byte[2]; Read(b); return BinaryPrimitives.ReadUInt16BigEndian(b); }
    public short I16() => (short)U16();
    public uint U32() { Span<byte> b = stackalloc byte[4]; Read(b); return BinaryPrimitives.ReadUInt32BigEndian(b); }
    public int I32() => (int)U32();
    public ulong U64() { Span<byte> b = stackalloc byte[8]; Read(b); return BinaryPrimitives.ReadUInt64BigEndian(b); }
    public byte[] Bytes(int count) { Need(count); var bytes = MemoryBudget.Allocate<byte>(count); Read(bytes); return bytes; }
    public byte[] Block(long offset, long count)
    {
        // Only interpreted metadata reaches this method; large opaque blocks are skipped.
        if (count > 4 * 1024 * 1024) throw PsdFailure.TooLarge;
        var previous = Offset;
        try { Offset = offset; return Bytes(checked((int)count)); }
        finally { Offset = previous; }
    }
    public string Ascii(int count) => System.Text.Encoding.ASCII.GetString(Bytes(count));
}

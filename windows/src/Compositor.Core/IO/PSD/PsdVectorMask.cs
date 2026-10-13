using System.Buffers.Binary;
using SkiaSharp;

namespace Compositor.Core.IO.PSD;

/// <summary>PSD vmsk/vsms: 8.24 normalized coordinates and complete cubic closing segments.</summary>
internal static class PsdVectorMask
{
    internal sealed record Source(string Path, bool Enabled, bool Linked);
    public static Source? Read(ReadOnlySpan<byte> data, int width, int height)
    {
        if (data.Length < 8 || data.Length > 4_000_000 || BinaryPrimitives.ReadUInt32BigEndian(data) != 3) return null;
        var flags = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        using var builder = new SKPathBuilder(); builder.FillType = SKPathFillType.EvenOdd;
        var remaining = 0; var closed = false; var started = false; var initial = false;
        SKPoint first = default, firstIn = default, previousOut = default;
        void Finish()
        {
            if (started && closed) { builder.CubicTo(previousOut, firstIn, first); builder.Close(); }
            started = false;
        }
        for (var offset = 8; offset + 26 <= data.Length; offset += 26)
        {
            var kind = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            var body = data.Slice(offset + 2, 24);
            if (kind is 0 or 3)
            {
                if (remaining != 0) return null;
                Finish(); remaining = BinaryPrimitives.ReadUInt16BigEndian(body); closed = kind == 0;
            }
            else if (kind is 1 or 2 or 4 or 5)
            {
                if (remaining-- <= 0) return null;
                var incoming = Point(body, width, height); var anchor = Point(body[8..], width, height); var outgoing = Point(body[16..], width, height);
                if (!started) { builder.MoveTo(anchor); first = anchor; firstIn = incoming; started = true; }
                else builder.CubicTo(previousOut, incoming, anchor);
                previousOut = outgoing;
            }
            else if (kind == 8) initial = BinaryPrimitives.ReadUInt16BigEndian(body) != 0;
        }
        if (remaining != 0) return null;
        Finish();
        if (initial ^ ((flags & 1) != 0)) builder.AddRect(SKRect.Create(0, 0, width, height));
        using var path = builder.Detach();
        // An empty enabled vector mask legitimately hides all pixels.
        return new(path.IsEmpty ? "M0 0Z" : path.ToSvgPathData(), (flags & 4) == 0, (flags & 2) == 0);
    }
    private static SKPoint Point(ReadOnlySpan<byte> data, int width, int height) => new(
        (float)(BinaryPrimitives.ReadInt32BigEndian(data[4..]) / 16777216.0 * width),
        (float)(BinaryPrimitives.ReadInt32BigEndian(data) / 16777216.0 * height));
}

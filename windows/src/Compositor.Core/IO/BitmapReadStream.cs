using SkiaSharp;

namespace Compositor.Core.IO;

/// <summary>Read-only, tightly packed pixels without an extra whole-image managed copy.</summary>
internal sealed class BitmapReadStream(SKBitmap bitmap) : Stream
{
    private long _position;
    private readonly int _rowLength = checked(bitmap.Width * bitmap.BytesPerPixel);
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => (long)_rowLength * bitmap.Height;
    public override long Position { get => _position; set { if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value)); _position = value; } }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        var total = (int)Math.Min(buffer.Length, Length - Position); var copied = 0;
        while (copied < total)
        {
            var row = (int)(Position / _rowLength); var column = (int)(Position % _rowLength);
            var count = Math.Min(total - copied, _rowLength - column);
            bitmap.GetPixelSpan().Slice(row * bitmap.RowBytes + column, count).CopyTo(buffer[copied..]);
            copied += count; Position += count;
        }
        return copied;
    }
    public override long Seek(long offset, SeekOrigin origin) { Position = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? Position : Length) + offset); return Position; }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

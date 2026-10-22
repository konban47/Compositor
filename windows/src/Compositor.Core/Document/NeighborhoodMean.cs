using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Alpha-weighted, edge-clamped box means with linear work and bounded scratch space.</summary>
internal sealed class NeighborhoodMean
{
    private readonly SKRectI _bounds;
    private readonly SKColor[] _means;
    internal NeighborhoodMean(SKBitmap image, SKRectI bounds, int radius)
    {
        _bounds = bounds;
        _means = MemoryBudget.Allocate<SKColor>(checked(bounds.Width * bounds.Height));
        var columns = MemoryBudget.Allocate<int>(checked((bounds.Width + radius * 2) * 4));
        var width = columns.Length / 4;
        void Row(int row, int sign)
        {
            var y = Math.Clamp(row, 0, image.Height - 1);
            for (var i = 0; i < width; i++)
            {
                var c = image.GetPixel(Math.Clamp(bounds.Left - radius + i, 0, image.Width - 1), y);
                var a = c.Alpha * sign; var offset = i * 4;
                columns[offset] += c.Red * a; columns[offset + 1] += c.Green * a;
                columns[offset + 2] += c.Blue * a; columns[offset + 3] += a;
            }
        }
        for (var y = bounds.Top - radius; y <= bounds.Top + radius; y++) Row(y, 1);
        for (var y = 0; y < bounds.Height; y++)
        {
            if (y > 0) { Row(bounds.Top + y - radius - 1, -1); Row(bounds.Top + y + radius, 1); }
            long r = 0, g = 0, b = 0, a = 0;
            for (var i = 0; i <= radius * 2; i++) { var n = i * 4; r += columns[n]; g += columns[n + 1]; b += columns[n + 2]; a += columns[n + 3]; }
            for (var x = 0; x < bounds.Width; x++)
            {
                _means[y * bounds.Width + x] = a == 0 ? SKColors.Transparent : new SKColor((byte)Math.Round((double)r / a), (byte)Math.Round((double)g / a), (byte)Math.Round((double)b / a));
                if (x + 1 == bounds.Width) continue;
                var old = x * 4; var next = (x + radius * 2 + 1) * 4;
                r += columns[next] - columns[old]; g += columns[next + 1] - columns[old + 1];
                b += columns[next + 2] - columns[old + 2]; a += columns[next + 3] - columns[old + 3];
            }
        }
    }
    internal SKColor At(int x, int y) => _means[(y - _bounds.Top) * _bounds.Width + x - _bounds.Left];
}

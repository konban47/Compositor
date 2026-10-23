using SkiaSharp;

namespace Compositor.Core.Model;

/// <summary>
/// Pixels a layer or a mask holds, with the small copy the layer panel draws. Pixels are immutable while
/// they stand for a saved asset: anything that changes them makes a new instance, so reference equality
/// means "the same pixels", as CGImage identity does in the Swift build.
/// </summary>
public sealed class ImportedImage : IDisposable
{
    public ImportedImage(SKBitmap image, SKBitmap thumbnail, string name)
    {
        // Assets are copy-on-write throughout the document/history model. Tell Skia too, so cached
        // SKImages can share the pixel reference instead of copying hundreds of megabytes per scene.
        image.SetImmutable();
        thumbnail.SetImmutable();
        Image = image;
        Thumbnail = thumbnail;
        Name = name;
    }

    public GifAnimation? Animation { get; internal set; }
    public SKBitmap Image { get; private set; }
    public SKBitmap Thumbnail { get; private set; }
    public string Name { get; private set; }
    public Compositor.Core.Format.ImageMode ColorMode { get; set; } = Compositor.Core.Format.ImageMode.RGB;
    public SKBitmap? Deep { get; private init; }
    public byte[]? CmykProfile { get; set; }
    public int BitDepth { get; private init; } = 8;
    public SKBitmap WorkingImage => Deep ?? Image;

    public int Width => Image.Width;
    public int Height => Image.Height;

    /// <summary>Thumbnail size: at most 96 pixels on the longer side, as the Mac build uses.</summary>
    public static (int Width, int Height) ThumbnailSize(int width, int height)
    {
        var factor = Math.Min(1, 96.0 / Math.Max(width, height));
        return (Math.Max(1, (int)(width * factor)), Math.Max(1, (int)(height * factor)));
    }

    public static ImportedImage Create(SKBitmap image, string name) =>
        new(image, Bitmaps.Thumbnail(image), name);

    public static ImportedImage CreateDeep(SKBitmap deep, string name, int depth = 16)
    {
        if (depth is not (16 or 32) || deep.ColorType != SKColorType.RgbaF32) throw new ArgumentException("Invalid deep image.");
        if (depth == 32) FloatPixels.ValidateSize(deep.Width, deep.Height);
        DeepPixels.Quantize(deep, depth);
        var preview = DeepPixels.Preview(deep);
        return new ImportedImage(preview, Bitmaps.Thumbnail(preview), name) { Deep = deep, BitDepth = depth };
    }

    private readonly object _lifetime = new();
    private int _references = 1;
    private bool _ownerDisposed;

    /// <summary>Keeps immutable pixels alive while a background snapshot is rendering.</summary>
    public IDisposable Retain()
    {
        lock (_lifetime)
        {
            if (_references == 0) throw new ObjectDisposedException(nameof(ImportedImage));
            _references++;
        }
        return new PixelLease(this);
    }
    private sealed class PixelLease(ImportedImage image) : IDisposable
    {
        private ImportedImage? _image = image;
        public void Dispose() => Interlocked.Exchange(ref _image, null)?.Release();
    }
    private void Release()
    {
        lock (_lifetime)
        {
            if (--_references != 0) return;
            Image.Dispose(); Thumbnail.Dispose(); Deep?.Dispose();
        }
    }
    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_ownerDisposed) return;
            _ownerDisposed = true; Release();
        }
    }
}

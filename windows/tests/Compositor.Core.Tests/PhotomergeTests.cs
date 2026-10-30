using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>Photomerge on overlapping views cut from one made-up scene, so where each belongs is known.</summary>
public sealed class PhotomergeTests
{
    /// <summary>A busy scene: shapes of many colors and sizes, full of corners to match.</summary>
    private static SKBitmap Scene(int width = 640, int height = 320)
    {
        var scene = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height));
        var random = new Random(11);
        using var canvas = new SKCanvas(scene);
        canvas.Clear(new SKColor(120, 130, 140));
        using var paint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < 260; i++)
        {
            paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            var x = random.Next(width); var y = random.Next(height); var s = random.Next(6, 40);
            if (i % 3 == 0) canvas.DrawCircle(x, y, s / 2f, paint);
            else canvas.DrawRect(x, y, s, s * (float)(.4 + random.NextDouble()), paint);
        }
        return scene;
    }

    /// <summary>A view of the scene: <paramref name="map"/> takes each of the view's pixels to the scene's.</summary>
    private static SKBitmap View(SKBitmap scene, int width, int height, Func<double, double, (double X, double Y)> map, double gain = 1, double vignette = 0)
    {
        var view = Bitmaps.Allocate(Bitmaps.ColorInfo(width, height));
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var (sx, sy) = map(x + .5, y + .5);
            var ix = (int)Math.Floor(sx); var iy = (int)Math.Floor(sy);
            if (ix < 0 || iy < 0 || ix >= scene.Width || iy >= scene.Height) { view.SetPixel(x, y, SKColors.Black); continue; }
            var c = scene.GetPixel(ix, iy);
            var dx = (x - width / 2.0) / (Math.Max(width, height) / 2.0); var dy = (y - height / 2.0) / (Math.Max(width, height) / 2.0);
            var f = gain * Math.Exp(vignette * (dx * dx + dy * dy));
            byte Scale(byte v) => (byte)Math.Clamp(Math.Round(Gamma(Linear(v / 255.0) * f) * 255), 0, 255);
            view.SetPixel(x, y, new SKColor(Scale(c.Red), Scale(c.Green), Scale(c.Blue)));
        }
        return view;
    }
    private static double Linear(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
    private static double Gamma(double v) => v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055;

    private static (double X, double Y) Center(ImageLayer layer) => (layer.Transform.X + layer.Transform.Width / 2, layer.Transform.Y + layer.Transform.Height / 2);

    [Fact]
    public void RepositionFindsHowFarEachViewIsShifted()
    {
        using var scene = Scene();
        using var left = View(scene, 320, 240, (x, y) => (x + 20, y + 30));
        using var right = View(scene, 320, 240, (x, y) => (x + 230, y + 50));
        var result = Photomerge.Merge([("Left", left), ("Right", right)], new PhotomergeOptions(PhotomergeLayout.Reposition, BlendImages: false));
        using var document = result.Document;
        Assert.Equal(PhotomergeLayout.Reposition, result.Layout);
        Assert.Empty(result.Unplaced);
        Assert.InRange(document.Width, 528, 532);
        Assert.InRange(document.Height, 258, 262);
        var a = document.Layers.Single(l => l.Name == "Left"); var b = document.Layers.Single(l => l.Name == "Right");
        Assert.InRange(b.Transform.X - a.Transform.X, 209, 211);
        Assert.InRange(b.Transform.Y - a.Transform.Y, 19, 21);
        Assert.All(document.Layers, l => Assert.Null(l.Mask));
    }

    [Fact]
    public void CollageUndoesATurnAndPerspectiveATilt()
    {
        using var scene = Scene();
        using var first = View(scene, 320, 240, (x, y) => (x + 40, y + 40));
        var angle = 8 * Math.PI / 180;
        using var turned = View(scene, 320, 240, (x, y) => (Math.Cos(angle) * (x - 160) - Math.Sin(angle) * (y - 120) + 360, Math.Sin(angle) * (x - 160) + Math.Cos(angle) * (y - 120) + 170));
        var collage = Photomerge.Merge([("First", first), ("Turned", turned)], new PhotomergeOptions(PhotomergeLayout.Collage, BlendImages: false));
        using (collage.Document)
        {
            Assert.Empty(collage.Unplaced);
            // The turned view's middle (scene 360, 170) lands 160 to the right of the first's (scene 200, 160), 10 down.
            var a = Center(collage.Document.Layers.Single(l => l.Name == "First")); var b = Center(collage.Document.Layers.Single(l => l.Name == "Turned"));
            Assert.InRange(b.X - a.X, 152, 168);
            Assert.InRange(b.Y - a.Y, 4, 16);
        }
        using var tilted = View(scene, 320, 240, (x, y) => { var w = 1 + .0006 * (x - 160); return ((x - 160) / w + 380, (y - 120) / w + 160); });
        var perspective = Photomerge.Merge([("First", first), ("Tilted", tilted)], new PhotomergeOptions(PhotomergeLayout.Perspective, BlendImages: false));
        using (perspective.Document) Assert.Empty(perspective.Unplaced);
    }

    [Fact]
    public void BlendingMatchesExposureAndMasksTheSeam()
    {
        using var scene = Scene();
        using var left = View(scene, 320, 240, (x, y) => (x + 20, y + 30));
        using var right = View(scene, 320, 240, (x, y) => (x + 200, y + 30), gain: .6);
        var result = Photomerge.Merge([("Left", left), ("Right", right)], new PhotomergeOptions(PhotomergeLayout.Reposition, BlendImages: true));
        using var document = result.Document;
        var a = document.Layers.Single(l => l.Name == "Left"); var b = document.Layers.Single(l => l.Name == "Right");
        Assert.NotNull(a.Mask); Assert.NotNull(b.Mask);
        // Where both show the scene, the darker view has been brought up to the other.
        double Mean(ImageLayer layer, int fromX, int toX)
        {
            double sum = 0; var count = 0;
            for (var y = 20; y < 200; y += 4) for (var x = fromX; x < toX; x += 4)
            {
                var c = layer.Asset!.Image.GetPixel(x - (int)layer.Transform.X, y - (int)layer.Transform.Y);
                sum += c.Red + c.Green + c.Blue; count++;
            }
            return sum / count;
        }
        var overlap = (from: 190, to: 310);
        Assert.InRange(Mean(b, overlap.from, overlap.to) / Mean(a, overlap.from, overlap.to), .93, 1.07);
        // Each view's mask shows it on its own side of the seam.
        Assert.True(a.Mask!.Asset.Image.GetPixel(20, 120).Red > 200 && a.Mask.Asset.Image.GetPixel(310, 120).Red < 60);
        Assert.True(b.Mask!.Asset.Image.GetPixel(300, 120).Red > 200 && b.Mask.Asset.Image.GetPixel(5, 120).Red < 60);
    }

    [Fact]
    public void VignetteRemovalLiftsTheCorners()
    {
        using var scene = Scene();
        var views = Enumerable.Range(0, 3).Select(i => View(scene, 300, 220, (x, y) => (x + 20 + i * 140, y + 40), vignette: -.7)).ToArray();
        try
        {
            var plain = Photomerge.Merge([.. views.Select((v, i) => ($"V{i}", v))], new PhotomergeOptions(PhotomergeLayout.Reposition, BlendImages: true));
            var lifted = Photomerge.Merge([.. views.Select((v, i) => ($"V{i}", v))], new PhotomergeOptions(PhotomergeLayout.Reposition, BlendImages: true, VignetteRemoval: true));
            using (plain.Document) using (lifted.Document)
            {
                SKColor Corner(CanvasDocument d) => d.Layers.Single(l => l.Name == "V0").Asset!.Image.GetPixel(4, 4);
                SKColor Middle(CanvasDocument d) => d.Layers.Single(l => l.Name == "V0").Asset!.Image.GetPixel(150, 110);
                double Ratio(CanvasDocument d) => (Corner(d).Red + Corner(d).Green + Corner(d).Blue + 1.0) / (Middle(d).Red + Middle(d).Green + Middle(d).Blue + 1.0);
                var before = (scene.GetPixel(24, 44).Red + scene.GetPixel(24, 44).Green + scene.GetPixel(24, 44).Blue + 1.0) / (scene.GetPixel(170, 150).Red + scene.GetPixel(170, 150).Green + scene.GetPixel(170, 150).Blue + 1.0);
                Assert.True(Math.Abs(Ratio(lifted.Document) - before) < Math.Abs(Ratio(plain.Document) - before),
                    $"corner/middle: scene {before:0.00}, plain {Ratio(plain.Document):0.00}, lifted {Ratio(lifted.Document):0.00}");
            }
        }
        finally { foreach (var view in views) view.Dispose(); }
    }

    [Fact]
    public void GeometricDistortionCorrectionFindsTheBarrel()
    {
        using var scene = Scene();
        const double k = -.12;
        (double, double) Barrel(double x, double y, double ox, double oy)
        {
            // The same model the merge undoes: an ideal point at (1 + k r²) times the distorted one's offset.
            var dx = (x - 160) / 160; var dy = (y - 120) / 160; var f = 1 + k * (dx * dx + dy * dy);
            return (160 + dx * f * 160 + ox, 120 + dy * f * 160 + oy);
        }
        using var left = View(scene, 320, 240, (x, y) => Barrel(x, y, 40, 40));
        using var right = View(scene, 320, 240, (x, y) => Barrel(x, y, 220, 50));
        var result = Photomerge.Merge([("Left", left), ("Right", right)], new PhotomergeOptions(PhotomergeLayout.Perspective, BlendImages: false, GeometricDistortion: true));
        using var _ = result.Document;
        Assert.InRange(result.DistortionK1, k - .05, k + .05);
    }

    [Fact]
    public void CylindricalAndAutoLayOutAWideRow()
    {
        // A camera turning on the spot: the scene is unrolled around it at 300 pixels per radian.
        using var scene = Scene(900, 300);
        const double focal = 300;
        (double, double) Turned(double x, double y, double yaw)
        {
            var u = x - 130; var v = y - 110;
            return (focal * (yaw + Math.Atan2(u, focal)) + 120, focal * v / Math.Sqrt(u * u + focal * focal) + 150);
        }
        var views = Enumerable.Range(0, 4).Select(i => View(scene, 260, 220, (x, y) => Turned(x, y, i * .6))).ToArray();
        try
        {
            var cylinder = Photomerge.Merge([.. views.Select((v, i) => ($"V{i}", v))], new PhotomergeOptions(PhotomergeLayout.Cylindrical));
            using (cylinder.Document)
            {
                Assert.Empty(cylinder.Unplaced);
                Assert.Equal(4, cylinder.Document.Layers.Count);
                var xs = cylinder.Document.Layers.OrderBy(l => l.Name).Select(l => Center(l).X).ToArray();
                Assert.True(xs.Zip(xs.Skip(1)).All(p => p.Second > p.First + 100), string.Join(", ", xs));
            }
            var auto = Photomerge.Merge([.. views.Take(2).Select((v, i) => ($"V{i}", v))], new PhotomergeOptions());
            using (auto.Document) Assert.Equal(PhotomergeLayout.Perspective, auto.Layout);
        }
        finally { foreach (var view in views) view.Dispose(); }
    }

    [Fact]
    public void FillingTransparentAreasLeavesNoHoles()
    {
        using var scene = Scene();
        using var top = View(scene, 300, 200, (x, y) => (x + 20, y + 20));
        using var lower = View(scene, 300, 200, (x, y) => (x + 180, y + 70));
        var result = Photomerge.Merge([("Top", top), ("Lower", lower)], new PhotomergeOptions(PhotomergeLayout.Reposition, FillTransparent: true));
        using var document = result.Document;
        var fill = document.Layers.Last();
        Assert.Equal("Content-Aware Fill", fill.Name);
        var image = fill.Asset!.Image;
        Assert.True(image.GetPixel(document.Width - 3, 3).Alpha > 200 && image.GetPixel(3, document.Height - 3).Alpha > 200, "the corners no photo covers");
    }

    [Fact]
    public void PhotosThatShareNothingAreReported()
    {
        using var scene = Scene();
        using var a = View(scene, 300, 200, (x, y) => (x + 20, y + 20));
        using var b = View(scene, 300, 200, (x, y) => (x + 150, y + 40));
        using var noise = Bitmaps.Allocate(Bitmaps.ColorInfo(300, 200));
        var random = new Random(5);
        for (var y = 0; y < 200; y++) for (var x = 0; x < 300; x++) noise.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        var result = Photomerge.Merge([("A", a), ("B", b), ("Noise", noise)], new PhotomergeOptions(PhotomergeLayout.Reposition, BlendImages: false));
        using var _ = result.Document;
        Assert.Equal(["Noise"], result.Unplaced);
        Assert.Equal(2, result.Document.Layers.Count);
        Assert.Throws<ArgumentException>(() => Photomerge.Merge([("A", a)], new PhotomergeOptions()));
    }
}

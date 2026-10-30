using System.Runtime.InteropServices;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

/// <summary>File › Automate › Lens Correction with the bundled lensfun profiles.</summary>
public sealed class LensCorrectionTests
{
    private const string Sample = """
        <lensdatabase version="2">
          <camera><maker>Canon</maker><model>Canon EOS 5D Mark II</model><mount>Canon EF</mount><cropfactor>1</cropfactor></camera>
          <lens>
            <maker>Canon</maker><model>Canon EF 24-105mm f/4L IS USM</model><mount>Canon EF</mount><cropfactor>1</cropfactor>
            <calibration>
              <distortion model="poly3" focal="24" k1="-0.04"/>
              <distortion model="poly3" focal="70" k1="0.02"/>
              <tca model="linear" focal="24" kr="1.001" kb="0.999"/>
              <vignetting model="pa" focal="24" aperture="4" distance="10" k1="-0.6" k2="0.2" k3="-0.05"/>
            </calibration>
          </lens>
          <lens><maker>Sigma</maker><model>10-20mm f/4-5.6 EX DC</model><mount>Canon EF</mount><cropfactor>1.6</cropfactor></lens>
        </lensdatabase>
        """;

    [Fact]
    public void TheBundledDatabaseHoldsThousandsOfLensesAndCameras()
    {
        var database = LensDatabase.Bundled();
        Assert.True(database.Lenses.Count > 1000, $"{database.Lenses.Count} lenses");
        Assert.True(database.Cameras.Count > 1000, $"{database.Cameras.Count} cameras");
        Assert.Contains(database.Lenses, l => l.Distortion.Count > 0 && l.Tca.Count > 0 && l.Vignetting.Count > 0);
    }

    [Fact]
    public void CalibrationsAreInterpolatedBetweenFocalLengthsAndMatchedByName()
    {
        var database = LensDatabase.Parse(Sample);
        var lens = database.Lenses[0];
        Assert.Equal(-.01, LensCorrection.At(lens.Distortion, 47)!.Terms[0], 6);
        Assert.Equal(-.04, LensCorrection.At(lens.Distortion, 12)!.Terms[0], 6);
        Assert.Equal(lens, database.Match(new LensExif("Canon", "Canon EOS 5D Mark II", "EF24-105mm f/4L IS USM", 50, 8)));
        Assert.Null(database.Match(new LensExif("Canon", "Canon EOS 5D Mark II", "EF24-105mm f/4L IS USM", 200, 8)));
        Assert.Null(database.Match(new LensExif("Nikon", "D850", "AF-S NIKKOR 14-24mm f/2.8G ED", 20, 8)));
        Assert.Null(database.Match(new LensExif("", "", "", 0, 0)));
    }

    /// <summary>A bowed line comes out straight: the source is the ideal picture seen through the profile's barrel.</summary>
    [Fact]
    public void DistortionCorrectionStraightensALine()
    {
        const int w = 300, h = 200; const double k1 = -.08;
        var lens = new LensProfile("Test", "Test 20mm", [], 1, [new LensCalibration(20, "poly3", [k1])], [], []);
        double Distorted(double r) => r * (1 - k1 + k1 * r * r);
        double Undistort(double rd) { var r = rd; for (var i = 0; i < 30; i++) r -= (Distorted(r) - rd) / (1 - k1 + 3 * k1 * r * r); return r; }
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(w, h));
        double unit = Math.Min(w, h) / 2.0, cx = w / 2.0, cy = h / 2.0;
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var dx = (x + .5 - cx) / unit; var dy = (y + .5 - cy) / unit; var rd = Math.Sqrt(dx * dx + dy * dy);
            var f = rd > 1e-9 ? Undistort(rd) / rd : 1;
            var ux = cx + dx * f * unit;
            // An ideal vertical line at x = 250.
            source.SetPixel(x, y, Math.Abs(ux - 250) < 1.5 ? SKColors.Black : SKColors.White);
        }
        double LineAt(SKBitmap image, int y)
        {
            double sum = 0, weight = 0;
            for (var x = 150; x < w; x++) { var dark = 255 - image.GetPixel(x, y).Red; sum += x * dark; weight += dark; }
            return sum / weight;
        }
        Assert.True(Math.Abs(LineAt(source, 5) - LineAt(source, 100)) > 3, "the source line bows");
        using var corrected = LensCorrection.Apply(source, lens, 20, 8, new LensCorrectionOptions(AutoScale: false));
        using var shown = DeepPixels.Preview(corrected);
        Assert.True(Math.Abs(LineAt(shown, 5) - LineAt(shown, 100)) < 1, $"{LineAt(shown, 5):0.0} vs {LineAt(shown, 100):0.0}");
        Assert.InRange(LineAt(shown, 100), 248.5, 251.5);
    }

    [Fact]
    public void VignetteCorrectionLiftsTheCorners()
    {
        var lens = LensDatabase.Parse(Sample).Lenses[0];
        const int w = 120, h = 80;
        using var source = Bitmaps.Allocate(Bitmaps.ColorInfo(w, h));
        double diagonal = Math.Sqrt(w * w + h * h) / 2;
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var r = Math.Sqrt((x + .5 - w / 2.0) * (x + .5 - w / 2.0) + (y + .5 - h / 2.0) * (y + .5 - h / 2.0)) / diagonal; var r2 = r * r;
            var lit = .2 * (1 - .6 * r2 + .2 * r2 * r2 - .05 * r2 * r2 * r2);
            var v = (byte)Math.Round(LinearToGamma(lit) * 255);
            source.SetPixel(x, y, new SKColor(v, v, v));
        }
        using var corrected = LensCorrection.Apply(source, lens, 24, 4, new LensCorrectionOptions(Distortion: false, Vignette: true));
        var floats = MemoryMarshal.Cast<byte, float>(corrected.GetPixelSpan()).ToArray();
        float At(int x, int y) => floats[(y * w + x) * 4];
        Assert.InRange(At(1, 1) / At(60, 40), .96, 1.04);
        static double LinearToGamma(double v) => v <= .0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - .055;
    }

    [Fact]
    public void BatchWritesMatchedPhotosAndListsTheRest()
    {
        var folder = Directory.CreateTempSubdirectory("lens-batch-").FullName;
        try
        {
            using var document = new CanvasDocument(Guid.NewGuid(), 40, 30);
            var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(40, 30)); bitmap.Erase(SKColors.Teal);
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Photo"), new Model.LayerTransform(0, 0, 40, 30), "Photo"));
            var database = LensDatabase.Parse(Sample);
            var sources = new[] { new AutomationSource("Photo.jpg", null, document) };
            var (written, unmatched) = LensCorrection.Batch(sources, folder, ExportFormat.Png, database, null, new LensCorrectionOptions());
            Assert.Empty(written); Assert.Equal(["Photo.jpg"], unmatched);
            (written, unmatched) = LensCorrection.Batch(sources, folder, ExportFormat.Png, database, database.Lenses[0], new LensCorrectionOptions());
            Assert.Equal(Path.Combine(folder, "Photo.png"), Assert.Single(written));
            (written, _) = LensCorrection.Batch(sources, folder, null, database, database.Lenses[0], new LensCorrectionOptions());
            Assert.EndsWith(".psd", Assert.Single(written));
            Assert.Equal((40, 30), (SKBitmap.Decode(Path.Combine(folder, "Photo.png")).Width, 30));
        }
        finally { Directory.Delete(folder, true); }
    }
}

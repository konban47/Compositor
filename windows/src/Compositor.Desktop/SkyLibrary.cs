using System.Text.Json;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>One sky in the library: its group (a folder) and the picture file.</summary>
internal sealed record SkyEntry(string Group, string Name, string Path);

/// <summary>
/// The skies Sky Replacement offers, kept as pictures in folders beside the settings, one folder a group. The default skies are
/// drawn by Compositor itself the first time the library is opened: gradients for clear and sunset skies, with clouds made of
/// fractal noise. Imported pictures are copied in as JPEG, so the library never points at a file that may move.
/// </summary>
internal static class SkyLibrary
{
    internal static string Root => System.IO.Path.Combine(AppPaths.SettingsDirectory, "Skies");
    private static string RecentPath => System.IO.Path.Combine(Root, "recent.json");
    internal static readonly string[] DefaultGroups = ["Blue Skies", "Spectacular", "Sunsets"];

    internal static IReadOnlyList<string> Groups()
    {
        EnsureDefaults();
        try { return Directory.EnumerateDirectories(Root).Select(System.IO.Path.GetFileName).OfType<string>().OrderBy(g => Array.IndexOf(DefaultGroups, g) is var i and >= 0 ? i : 99).ThenBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        catch (IOException) { return []; }
    }
    internal static IReadOnlyList<SkyEntry> Skies(string group)
    {
        var folder = System.IO.Path.Combine(Root, group);
        try { return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.jpg").Order(StringComparer.CurrentCultureIgnoreCase).Select(p => new SkyEntry(group, System.IO.Path.GetFileNameWithoutExtension(p), p)).ToArray() : []; }
        catch (IOException) { return []; }
    }
    internal static IReadOnlyList<SkyEntry> All() => Groups().SelectMany(Skies).ToArray();
    internal static IReadOnlyList<SkyEntry> Recent()
    {
        try { var paths = File.Exists(RecentPath) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(RecentPath)) ?? [] : []; return paths.Where(File.Exists).Select(p => new SkyEntry(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p)!), System.IO.Path.GetFileNameWithoutExtension(p), p)).ToArray(); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
    internal static void Used(SkyEntry sky)
    {
        try { var paths = Recent().Select(r => r.Path).Where(p => p != sky.Path).Prepend(sky.Path).Take(6).ToArray(); AtomicFile.Write(RecentPath, stream => JsonSerializer.Serialize(stream, paths)); }
        catch (IOException) { }
    }
    internal static bool ValidName(string name) => name.Length is > 0 and <= 100 && name.Trim() == name && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
    internal static void CreateGroup(string name) { if (!ValidName(name)) throw new ArgumentException("Enter a name without \\ / : * ? \" < > |."); Directory.CreateDirectory(System.IO.Path.Combine(Root, name)); }
    /// <summary>Copies a picture into a group as a JPEG at most 4096 pixels across, under a name no other sky in the group has.</summary>
    internal static SkyEntry Import(string group, string file)
    {
        if (new FileInfo(file).Length > 256L * 1024 * 1024) throw new InvalidDataException("The image is too large.");
        using var decoded = SKBitmap.Decode(file) ?? throw new InvalidDataException("The file is not an image Compositor can read.");
        var scale = Math.Min(1, 4096d / Math.Max(decoded.Width, decoded.Height));
        using var sized = scale < 1 ? decoded.Resize(new SKImageInfo((int)(decoded.Width * scale), (int)(decoded.Height * scale)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : null;
        var stem = System.IO.Path.GetFileNameWithoutExtension(file); if (!ValidName(stem)) stem = "Sky";
        return Write(group, stem, sized ?? decoded);
    }
    internal static void Rename(SkyEntry sky, string name)
    {
        if (!ValidName(name)) throw new ArgumentException("Enter a name without \\ / : * ? \" < > |.");
        var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sky.Path)!, name + ".jpg");
        if (File.Exists(target)) throw new IOException("A sky with that name already exists.");
        File.Move(sky.Path, target);
    }
    internal static void Delete(SkyEntry sky) { if (File.Exists(sky.Path)) File.Delete(sky.Path); }
    internal static void DeleteGroup(string group) { var folder = System.IO.Path.Combine(Root, group); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    /// <summary>Puts any missing default sky back, which is Append Default Skies.</summary>
    internal static void AppendDefaults()
    {
        for (var g = 0; g < DefaultGroups.Length; g++)
            for (var i = 0; i < 5; i++)
            {
                var name = $"{DefaultGroups[g]} {i + 1}"; var path = System.IO.Path.Combine(Root, DefaultGroups[g], name + ".jpg");
                if (File.Exists(path)) continue;
                using var sky = Draw(g, i); Write(DefaultGroups[g], name, sky, exact: true);
            }
    }
    private static void EnsureDefaults()
    {
        if (Directory.Exists(Root)) return;
        try { Directory.CreateDirectory(Root); AppendDefaults(); } catch (IOException) { }
    }
    private static SkyEntry Write(string group, string stem, SKBitmap bitmap, bool exact = false)
    {
        var folder = System.IO.Path.Combine(Root, group); Directory.CreateDirectory(folder);
        var name = stem; for (var n = 2; !exact && File.Exists(System.IO.Path.Combine(folder, name + ".jpg")); n++) name = $"{stem} {n}";
        var path = System.IO.Path.Combine(folder, name + ".jpg");
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 92) ?? throw new IOException("The sky could not be saved.");
        AtomicFile.Write(path, stream => data.SaveTo(stream));
        return new SkyEntry(group, name, path);
    }

    /// <summary>A default sky: <paramref name="kind"/> 0 is clear blue with fair-weather clouds, 1 is dramatic cloud, 2 is a sunset.</summary>
    internal static SKBitmap Draw(int kind, int variant)
    {
        const int width = 1200, height = 800;
        var bitmap = new SKBitmap(width, height); using var canvas = new SKCanvas(bitmap);
        SKColor[] colors = kind switch
        {
            0 => [new(0x1E, 0x5F, 0xB4), new(0x5A, 0x9B, 0xDC), new(0xBF, 0xDC, 0xF2)],
            1 => [new(0x2A, 0x3A, 0x5A), new(0x6A, 0x7E, 0xA2), new(0xD8, 0xC8, 0xB0)],
            _ => [new(0x13, 0x3C, 0x78), new(0x7A, 0x6E, 0x9E), new(0xF5, 0xA0, 0x46)],
        };
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), colors, [0, .55f, 1], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader }) canvas.DrawRect(0, 0, width, height, paint);
        if (kind == 2)
        {
            using var glow = SKShader.CreateRadialGradient(new SKPoint(width * (.3f + variant * .1f), height * .95f), height * .7f, [new SKColor(0xFF, 0xD2, 0x7A, 0xD0), new SKColor(0xFF, 0x8C, 0x3C, 0x00)], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = glow }; canvas.DrawRect(0, 0, width, height, paint);
        }
        if (kind == 0 && variant == 0) return bitmap;
        // Clouds: fractal noise, thinned toward the horizon, kept only where it is dense enough to read as cloud.
        var frequency = kind == 1 ? .0045f : .006f + variant * .0012f;
        using var noise = SKShader.CreatePerlinNoiseFractalNoise(frequency, frequency * 1.8f, 5, 11 + variant * 7 + kind * 31);
        using var clouds = new SKBitmap(width, height);
        using (var cloudCanvas = new SKCanvas(clouds)) using (var paint = new SKPaint { Shader = noise }) cloudCanvas.DrawRect(0, 0, width, height, paint);
        var cover = kind == 1 ? .42f : .52f - variant * .02f; var cloud = kind == 2 ? new SKColor(0xFF, 0xC8, 0xA0) : kind == 1 ? new SKColor(0xE8, 0xE8, 0xEC) : SKColors.White;
        var shade = kind == 1 ? new SKColor(0x55, 0x5E, 0x70) : kind == 2 ? new SKColor(0x6A, 0x3C, 0x5A) : new SKColor(0xA8, 0xB8, 0xCC);
        var sky = bitmap.GetPixelSpan(); var source = clouds.GetPixelSpan(); var info = bitmap.Info;
        for (var y = 0; y < height; y++)
        {
            var fade = 1 - Math.Clamp((y / (float)height - .55f) / .45f, 0, 1) * .7f;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4; var density = source[i + 3] / 255f * (source[i] / 255f + .2f);
                var a = Math.Clamp((density - cover) / .18f, 0, 1) * fade; if (a <= 0) continue;
                var lit = Math.Clamp((source[i + 1] / 255f - .3f) * 2.2f, 0, 1);
                byte Mix(byte under, byte lightTone, byte darkTone) => (byte)Math.Round(under + ((darkTone + (lightTone - darkTone) * lit) - under) * a);
                // SKBitmap's native order is BGRA on Windows and RGBA elsewhere; write in whichever it is.
                var bgra = info.ColorType == SKColorType.Bgra8888;
                sky[i + (bgra ? 2 : 0)] = Mix(sky[i + (bgra ? 2 : 0)], cloud.Red, shade.Red); sky[i + 1] = Mix(sky[i + 1], cloud.Green, shade.Green); sky[i + (bgra ? 0 : 2)] = Mix(sky[i + (bgra ? 0 : 2)], cloud.Blue, shade.Blue);
            }
        }
        bitmap.NotifyPixelsChanged();
        return bitmap;
    }
}

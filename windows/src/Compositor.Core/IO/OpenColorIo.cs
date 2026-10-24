using System.Diagnostics;
using System.Text.Json;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record OcioSettings
{
    public const string Builtin = "ocio://cg-config-v2.2.0_aces-v1.3_ocio-v2.4";
    public bool Enabled { get; init; }
    public string RootFolder { get; init; } = "";
    public bool OverrideEnvironment { get; init; }
    public string Configuration { get; init; } = Builtin;
    public string WorkingSpace { get; init; } = "matte_paint";
    public int Depth { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string EffectiveConfig => !OverrideEnvironment && Environment.GetEnvironmentVariable("OCIO") is { Length: > 0 } env ? env : Configuration;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => Depth is 0 or 8 or 16 or 32 && Configuration is { Length: > 0 and < 32768 } && WorkingSpace is { Length: > 0 and < 1024 } && RootFolder is { Length: < 32768 };
}
public sealed record OcioSpace(string Name, string Family, string Encoding, string Description);
public sealed record OcioRole(string Name, string Space);
public sealed record OcioBuiltin(string Name, string Description);
public sealed record OcioDisplay(string Name, string[] Views);
public sealed record OcioConfigInfo(string Version, string Description, OcioRole[] Roles, OcioSpace[] Spaces, OcioDisplay[] Displays, OcioBuiltin[] Builtins)
{
    public OcioSpace? Find(string name) => Spaces.FirstOrDefault(s => s.Name == name || Roles.Any(r => r.Name == name && r.Space == s.Name));
    public string EditorSpace => Spaces.FirstOrDefault(s => s.Name == "sRGB Encoded Rec.709 (sRGB)")?.Name
        ?? Roles.FirstOrDefault(r => r.Name == "color_picking")?.Space
        ?? throw new InvalidDataException("The OCIO configuration needs an sRGB color_picking role for the editor.");
}
public static class OpenColorIo
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static string Runtime => Path.Combine(AppContext.BaseDirectory, "color-runtime");
    private static JsonElement Run(object request)
    {
        var start = new ProcessStartInfo(Path.Combine(Runtime, "python.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Runtime };
        start.ArgumentList.Add("-I"); start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ocio_worker.py"));
        using var process = Process.Start(start) ?? throw new IOException("Could not start OpenColorIO.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(JsonSerializer.Serialize(request)); process.StandardInput.Close();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new IOException("OpenColorIO timed out."); }
        if (process.ExitCode != 0) throw new InvalidDataException(error.GetAwaiter().GetResult());
        return JsonSerializer.Deserialize<JsonElement>(output.GetAwaiter().GetResult());
    }
    public static OcioConfigInfo Inspect(string config) => Run(new { action = "inspect", config }).Deserialize<OcioConfigInfo>(Json) ?? throw new InvalidDataException("Invalid OCIO metadata.");
    public static void Validate(OcioSettings settings)
    {
        if (!settings.IsValid) throw new InvalidDataException("Invalid OpenColorIO settings.");
        if (!settings.Enabled) return;
        var info = Inspect(settings.EffectiveConfig);
        if (info.Find(settings.WorkingSpace) is null) throw new InvalidDataException("Choose a color space in this configuration.");
        Run(new { action = "validate", config = settings.EffectiveConfig, source = settings.WorkingSpace, target = info.EditorSpace });
        Run(new { action = "validate", config = settings.EffectiveConfig, source = info.EditorSpace, target = settings.WorkingSpace });
    }
    public static SKBitmap Convert(SKBitmap image, string config, string source, string target, bool displayView = false)
    {
        MemoryBudget.Current.Check(checked((long)image.Width * image.Height * 32));
        var folder = Path.Combine(Path.GetTempPath(), "Compositor-OCIO-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            using var input = DeepPixels.From(image); var inputPath = Path.Combine(folder, "input.rgba"); var outputPath = Path.Combine(folder, "output.rgba");
            using (var stream = File.Create(inputPath)) for (var y = 0; y < input.Height; y++) stream.Write(input.GetPixelSpan().Slice(y * input.RowBytes, input.Width * 16));
            Run(new { action = "convert", config, source, target, display_view = displayView, input = inputPath, output = outputPath, width = input.Width, height = input.Height });
            if (new FileInfo(outputPath).Length != (long)input.Width * input.Height * 16) throw new InvalidDataException("OpenColorIO changed the image dimensions.");
            var result = Bitmaps.Allocate(DeepPixels.Info(input.Width, input.Height));
            try { using var stream = File.OpenRead(outputPath); for (var y = 0; y < result.Height; y++) stream.ReadExactly(result.GetPixelSpan().Slice(y * result.RowBytes, result.Width * 16)); return result; }
            catch { result.Dispose(); throw; }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}

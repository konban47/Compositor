using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Compositor.Core.Model;

namespace Compositor.Core.IO;

public sealed record PluginCommand(string ID, string Title, string Result = "layer");
public sealed record PluginManifest(int ApiVersion, string ID, string Name, string Version, string Entry, PluginCommand[] Commands)
{
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public bool IsValid => ApiVersion == 1 && ValidId(ID) && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 128
        && Version is { Length: > 0 and <= 64 } && Description is { Length: <= 4096 } && Author is { Length: <= 256 }
        && Entry is { Length: > 0 and <= 240 } && !Path.IsPathRooted(Entry) && Path.GetExtension(Entry).Equals(".exe", StringComparison.OrdinalIgnoreCase)
        && Commands is { Length: > 0 and <= 64 } && Commands.All(c => c is not null && ValidId(c.ID) && c.Title is { Length: > 0 and <= 128 } && c.Result is "layer" or "replace" or "message")
        && Commands.Select(c => c.ID).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Commands.Length;
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 128 } && Regex.IsMatch(id, "^[a-zA-Z][a-zA-Z0-9._-]*$");
}
public sealed record InstalledPlugin(PluginManifest Manifest, string Directory, bool Enabled);
public sealed record PluginRequest(int ApiVersion, string Command, string Input, string OutputDirectory, int Width, int Height,
    Guid? LayerID, string Language, int BitDepth);
public sealed record PluginResponse(int ApiVersion, string? Image, string? Name, string? Message);
public sealed record PluginResult(PluginResponse Response, byte[]? Image);

/// <summary>Versioned, out-of-process PNG/JSON interface. Discovery never executes plugin code.</summary>
public static class PluginHost
{
    public const string ManifestName = "compositor-plugin.json";
    public static string DefaultDirectory => Path.Combine(Environment.GetEnvironmentVariable("COMPOSITOR_SETTINGS_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Compositor"), "Plugins");
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 32 };
    public static PluginManifest Read(string directory)
    {
        var file = SafePath(directory, ManifestName); if (new FileInfo(file).Length > 256 * 1024) throw new InvalidDataException("Plugin manifest is too large.");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file), Json);
        if (manifest is null || !manifest.IsValid) throw new InvalidDataException("Unsupported or invalid plugin manifest.");
        _ = SafePath(directory, manifest.Entry); return manifest;
    }
    public static IReadOnlyList<InstalledPlugin> List(string? root = null)
    {
        root ??= DefaultDirectory; if (!Directory.Exists(root)) return [];
        var found = new List<InstalledPlugin>();
        foreach (var directory in Directory.EnumerateDirectories(root).Take(256))
        {
            try { found.Add(new(Read(directory), directory, File.Exists(Path.Combine(directory, "enabled")))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return found;
    }
    public static InstalledPlugin Install(string sourceDirectory, string? root = null)
    {
        var manifest = Read(sourceDirectory); root ??= DefaultDirectory; Directory.CreateDirectory(root);
        var target = Path.Combine(Path.GetFullPath(root), manifest.ID);
        if (Directory.Exists(target)) throw new IOException("This plugin is already installed. Remove it before installing another version.");
        var files = Directory.GetFiles(sourceDirectory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false });
        if (files.Length > 2048) throw new InvalidDataException("Plugin package has too many files.");
        long bytes = 0; foreach (var file in files) { _ = SafePath(sourceDirectory, Path.GetRelativePath(sourceDirectory, file)); bytes += new FileInfo(file).Length; }
        if (bytes > 512L * 1024 * 1024) throw new InvalidDataException("Plugin package is too large.");
        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(sourceDirectory, file); if (relative.Equals("enabled", StringComparison.OrdinalIgnoreCase)) continue;
                var destination = Path.Combine(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            return new(manifest, target, false);
        }
        catch { if (Directory.Exists(target)) Directory.Delete(target, true); throw; }
    }
    public static void Enable(InstalledPlugin plugin, bool enabled)
    {
        _ = Read(plugin.Directory); var marker = Path.Combine(plugin.Directory, "enabled");
        if (enabled) File.WriteAllText(marker, "API 1"); else if (File.Exists(marker)) File.Delete(marker);
    }
    public static void Remove(InstalledPlugin plugin, string? root = null)
    {
        root ??= DefaultDirectory; var expected = Path.GetFullPath(Path.Combine(root, plugin.Manifest.ID));
        if (!expected.Equals(Path.GetFullPath(plugin.Directory), StringComparison.OrdinalIgnoreCase) || File.GetAttributes(expected).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Invalid plugin directory.");
        Directory.Delete(expected, true);
    }
    public static async Task<PluginResult> Run(InstalledPlugin plugin, string commandID, byte[] input, int width, int height,
        Guid? layerID, string language, int bitDepth, CancellationToken cancellation = default)
    {
        if (!plugin.Enabled) throw new InvalidOperationException("Enable the plugin before running it.");
        var manifest = Read(plugin.Directory); if (!manifest.Commands.Any(c => c.ID == commandID)) throw new InvalidDataException("Unknown plugin command.");
        var directory = Path.Combine(Path.GetTempPath(), "Compositor-plugin-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var inputPath = Path.Combine(directory, "input.png"); var outputDirectory = Path.Combine(directory, "output"); Directory.CreateDirectory(outputDirectory);
            await File.WriteAllBytesAsync(inputPath, input, cancellation);
            var request = new PluginRequest(1, commandID, inputPath, outputDirectory, width, height, layerID, language, bitDepth);
            using var process = new Process { StartInfo = new ProcessStartInfo(SafePath(plugin.Directory, manifest.Entry))
                { WorkingDirectory = plugin.Directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            async Task<string> ReadOutput(StreamReader reader)
            {
                try { return await ReadBounded(reader, timeout.Token); }
                catch { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } throw; }
            }
            var outputTask = ReadOutput(process.StandardOutput); var errorTask = ReadOutput(process.StandardError);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, new JsonSerializerOptions(Json) { WriteIndented = false }).AsMemory(), timeout.Token); process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), outputTask, errorTask); var output = await outputTask;
            if (process.ExitCode != 0) throw new InvalidOperationException("The plugin exited with an error.");
            var response = JsonSerializer.Deserialize<PluginResponse>(output, Json);
            if (response is null || response.ApiVersion != 1 || response.Name?.Length > 1024 || response.Message?.Length > 8192) throw new InvalidDataException("Invalid plugin response.");
            byte[]? image = null;
            if (response.Image is { } relative)
            {
                var path = SafePath(outputDirectory, relative); if (new FileInfo(path).Length > DocumentLimits.MaxAssetBytes) throw new InvalidDataException("Plugin image exceeds the asset budget.");
                image = await File.ReadAllBytesAsync(path, cancellation); var header = PngCodec.ReadHeader(image);
                if (header.Width > DocumentLimits.MaxSide || header.Height > DocumentLimits.MaxSide || (long)header.Width * header.Height > DocumentLimits.MaxSurfacePixels) throw new InvalidDataException("Plugin image exceeds the pixel budget.");
            }
            if (manifest.Commands.First(c => c.ID == commandID).Result != "message" && image is null) throw new InvalidDataException("The plugin did not return an image.");
            return new(response, image);
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellation)
    {
        var buffer = new char[4096]; var result = new System.Text.StringBuilder();
        while (true) { var read = await reader.ReadAsync(buffer, cancellation); if (read == 0) break; if (result.Length + read > 65536) throw new InvalidDataException("Plugin output exceeds 64 KiB."); result.Append(buffer, 0, read); }
        return result.ToString();
    }
    internal static string SafePath(string directory, string relative)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Plugin paths must be relative.");
        var path = Path.GetFullPath(Path.Combine(root, relative)); if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Plugin path escapes its directory.");
        var current = directory; if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Plugin symbolic links are not supported.");
        foreach (var part in Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        { current = Path.Combine(current, part); if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Plugin symbolic links are not supported."); }
        if (!File.Exists(path)) throw new FileNotFoundException("Plugin file does not exist."); return path;
    }
}

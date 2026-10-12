using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;
using System.Text.Json;

namespace Compositor.Core.Tests;

public sealed class PluginHostTests
{
    [Fact] public async Task ExamplePluginInstallsRunsAndCanBeRemoved()
    {
        if (!OperatingSystem.IsWindows()) return; // Manifest API v1 executes Windows .exe plugins.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var example = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../examples/Compositor.SamplePlugin/bin", configuration, "net10.0"));
        var root = Path.Combine(Path.GetTempPath(), "Compositor-plugins-test-" + Guid.NewGuid());
        try
        {
            var plugin = PluginHost.Install(example, root); Assert.False(plugin.Enabled); Assert.Single(PluginHost.List(root));
            using var pixel = new SKBitmap(4, 3); pixel.Erase(SKColors.Coral); var input = PngCodec.Encode(pixel);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PluginHost.Run(plugin, "duplicate", input, 4, 3, null, "zh-CN", 8));
            PluginHost.Enable(plugin, true); plugin = Assert.Single(PluginHost.List(root)); Assert.True(plugin.Enabled);
            var copied = await PluginHost.Run(plugin, "duplicate", input, 4, 3, null, "zh-CN", 8); Assert.Equal(input, copied.Image); Assert.Equal("Plugin copy", copied.Response.Name);
            var info = await PluginHost.Run(plugin, "describe", input, 4, 3, null, "en", 8); Assert.Contains("4 × 3", info.Response.Message); Assert.Null(info.Image);
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginHost.Run(plugin, "missing", input, 4, 3, null, "en", 8));
            PluginHost.Remove(plugin, root); Assert.Empty(PluginHost.List(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory] [InlineData("../outside.exe")] [InlineData("C:/outside.exe")]
    public void ManifestCannotEscapeItsPackage(string entry)
    {
        var root = Path.Combine(Path.GetTempPath(), "Compositor-plugin-path-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, PluginHost.ManifestName), JsonSerializer.Serialize(new PluginManifest(1, "test.safe", "test", "1", entry, [new("run", "Run")])));
            Assert.Throws<InvalidDataException>(() => PluginHost.Read(root));
        }
        finally { Directory.Delete(root, true); }
    }
}

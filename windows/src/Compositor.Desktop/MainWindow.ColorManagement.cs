using System.Diagnostics;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private async Task ColorSettings()
    {
        var form = new FormDialog("Color Settings", 560, 430);
        form.Description("The editing space is sRGB. Assign Profile interprets the selected layer with a source ICC profile and converts it to sRGB. Convert to Profile exports a color-managed TIFF with an embedded ICC profile.");
        form.Description("CMYK and Lab documents use RGB previews. Native channels are available through color-mode TIFF export. HDR processing uses extended sRGB; ICC conversion is intended for SDR images.");
        form.Fields.Children.Add(FormDialog.ActionButton("Assign Profile…", async () => { form.Close(false); await AssignColorProfile(); }));
        form.Fields.Children.Add(FormDialog.ActionButton("Convert to Profile…", async () => { form.Close(false); await ConvertColorProfile(); }));
        await form.ShowDialog<bool>(this);
    }
    private async Task<IColorProfile?> ChooseIcc(string title)
    {
        var form = new FormDialog(title, 500, 330);
        var profile = form.Choice("ICC Profile", ["sRGB IEC61966-2.1", "Adobe RGB (1998)", "Custom ICC Profile…"], 0);
        if (!await form.ShowDialog<bool>(this)) return null;
        if (profile.SelectedIndex < 2) return profile.SelectedIndex == 0 ? ColorProfiles.SRGB : ColorProfiles.AdobeRGB1998;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("ICC Profile"), FileTypeFilter = [new FilePickerFileType("ICC Profile") { Patterns = ["*.icc", "*.icm"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("ICC profile is too large.");
        return new ColorProfile(await File.ReadAllBytesAsync(path));
    }
    private async Task AssignColorProfile()
    {
        if (_document is not { } doc || PropertyLayer is not { Asset: { } asset } layer || !LayerProtection.CanPaint(doc, layer.ID)) return;
        var revision = _history.CurrentRevision;
        try
        {
            if (await ChooseIcc("Assign Source Profile to Layer") is not { } profile || !ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            using var converted = DeepImageCodec.Magick(asset.WorkingImage);
            converted.TransformColorSpace(profile, ColorProfiles.SRGB);
            using var pixels = AdvancedEdit.Pixels(converted);
            Edit("Assign Profile", () => ImageAdjustments.SetPixels(doc, layer, pixels, selection: false));
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task ConvertColorProfile()
    {
        if (_document is not { } doc) return;
        try
        {
            if (await ChooseIcc("Convert and Export ICC TIFF") is not { } profile || !ReferenceEquals(doc, _document)) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "color-managed.tif", DefaultExtension = "tif" });
            if (file?.TryGetLocalPath() is not { } path) return;
            using var composite = DocumentRenderer.RenderDeep(doc); using var converted = DeepImageCodec.Magick(composite);
            converted.TransformColorSpace(ColorProfiles.SRGB, profile); converted.Depth = 16;
            converted.Density = new Density(doc.Resolution, doc.Resolution, DensityUnit.PixelsPerInch);
            await WorkScheduler.Run(_ => { AtomicFile.Write(path, output => converted.Write(output, MagickFormat.Tiff)); return true; }); Say("Color-managed TIFF exported.");
        }
        catch (Exception error) { Say(error.Message); }
    }
    private async Task OpenColorIoSettings()
    {
        var p = EditorPreferences.Current; var form = new FormDialog("OpenColorIO Settings", 640, 590);
        form.Description("Choose an installed ocioconvert executable and a trusted OCIO configuration. Apply runs it locally on the selected layer. Choose an sRGB display output for the editor preview.");
        var executable = form.Text("ocioconvert Executable", p.OcioExecutable); var config = form.Text("OCIO Configuration", p.OcioConfiguration);
        var source = form.Text("Input Color Space", p.OcioSource); var destination = form.Text("Output Color Space", p.OcioDestination);
        var apply = form.Check("Apply to Selected Layer", false);
        form.Validate = () => apply.IsChecked == true && (!File.Exists(executable.Text) || !File.Exists(config.Text) || PropertyLayer?.Asset is null) ? "Choose an existing executable, configuration and pixel layer." : null;
        if (!await form.ShowDialog<bool>(this)) return;
        p.OcioExecutable = executable.Text ?? ""; p.OcioConfiguration = config.Text ?? ""; p.OcioSource = source.Text ?? ""; p.OcioDestination = destination.Text ?? "";
        try { p.Save(); } catch (Exception error) { Say(error.Message); }
        if (apply.IsChecked != true || _document is not { } doc || PropertyLayer is not { Asset: { } asset } layer || !LayerProtection.CanPaint(doc, layer.ID)) return;
        var revision = _history.CurrentRevision;
        var folder = Path.Combine(Path.GetTempPath(), "Compositor-OCIO-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "input.tif"); var output = Path.Combine(folder, "output.tif");
            await File.WriteAllBytesAsync(input, DeepImageCodec.EncodeMode(asset.WorkingImage, new ImageSettings { Depth = 32 }, true, doc.Resolution));
            var start = new ProcessStartInfo(Path.GetFullPath(p.OcioExecutable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
            foreach (var argument in new[] { "--iconfig", Path.GetFullPath(p.OcioConfiguration), "--bitdepth", "float", input, p.OcioSource, output, p.OcioDestination }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start OpenColorIO.");
            // Drain output without retaining unbounded logs from external programs.
            process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { }; process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new IOException("OpenColorIO timed out."); }
            if (process.ExitCode != 0 || !File.Exists(output)) throw new IOException("OpenColorIO conversion failed. Check the configuration and color-space names.");
            if (new FileInfo(output).Length > DocumentLimits.MaxAssetBytes) throw new IOException("OpenColorIO output is too large.");
            using var result = DeepImageCodec.Decode(await File.ReadAllBytesAsync(output), "OpenColorIO");
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision) return;
            if (result.Width != asset.Width || result.Height != asset.Height) throw new IOException("OpenColorIO changed the image dimensions.");
            Edit("OpenColorIO", () => ImageAdjustments.SetPixels(doc, layer, result.WorkingImage));
        }
        catch (Exception error) { Say(error.Message); }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}

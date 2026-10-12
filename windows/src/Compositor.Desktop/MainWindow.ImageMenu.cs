using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private static string ImageAdjustmentTitle(ImageAdjustmentKind kind) => kind switch
    {
        ImageAdjustmentKind.BrightnessContrast => "Brightness/Contrast", ImageAdjustmentKind.PhotoFilter => "Photo Filter",
        ImageAdjustmentKind.ChannelMixer => "Channel Mixer", ImageAdjustmentKind.ColorLookup => "Color Lookup",
        ImageAdjustmentKind.SelectiveColor => "Selective Color", ImageAdjustmentKind.ShadowsHighlights => "Shadows/Highlights",
        ImageAdjustmentKind.HdrToning => "HDR Toning", ImageAdjustmentKind.MatchColor => "Match Color", ImageAdjustmentKind.ReplaceColor => "Replace Color", _ => kind.ToString(),
    };
    private static string ModeTitle(ImageMode mode) => mode switch
    { ImageMode.RGB => "RGB Color", ImageMode.CMYK => "CMYK Color", ImageMode.Lab => "Lab Color", ImageMode.Indexed => "Indexed Color", _ => mode.ToString() };

    private MenuItem BuildImageMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("_Image") };
        var modes = new MenuItem { Header = Localize.Text("Mode") };
        var modeRows = new Dictionary<ImageMode, MenuItem>(); var depthRows = new Dictionary<int, MenuItem>();
        foreach (var mode in Enum.GetValues<ImageMode>())
        {
            var item = Command(ModeTitle(mode), () => _ = ConvertImageMode(mode)); item.ToggleType = MenuItemToggleType.Radio;
            modes.Items.Add(item); modeRows[mode] = item;
        }
        modes.Items.Add(new Separator());
        foreach (var bits in new[] { 8, 16, 32 })
        {
            var item = Command($"{bits} Bits/Channel", () => _ = ConvertImageDepth(bits)); item.ToggleType = MenuItemToggleType.Radio;
            modes.Items.Add(item); depthRows[bits] = item;
        }
        modes.Items.Add(new Separator()); modes.Items.Add(Command("Color Table…", () => _ = ColorTable()));
        modes.SubmenuOpened += (_, _) =>
        {
            foreach (var pair in modeRows) pair.Value.IsChecked = _document?.Image.Mode == pair.Key;
            foreach (var pair in depthRows) { pair.Value.IsChecked = _document?.Image.Depth == pair.Key; pair.Value.IsEnabled = _document is { } d && (pair.Key == 8 || d.Image.Mode is not (ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone)); }
        };
        menu.Items.Add(modes);
        var adjustments = new MenuItem { Header = Localize.Text("Adjustments") };
        void Existing(string title, AdjustmentKind kind, string? key = null) => adjustments.Items.Add(Command(title, () => _ = ImageAdjustment(kind), key));
        void Added(ImageAdjustmentKind kind, bool immediate = false, string? key = null) => adjustments.Items.Add(Command(ImageAdjustmentTitle(kind) + (immediate ? "" : "…"), () => _ = ExtendedImageAdjustment(kind, immediate), key));
        Added(ImageAdjustmentKind.BrightnessContrast); Existing("Levels…", AdjustmentKind.Levels, "Levels"); Existing("Curves…", AdjustmentKind.Curves, "Curves"); Existing("Exposure…", AdjustmentKind.Exposure);
        adjustments.Items.Add(new Separator()); Added(ImageAdjustmentKind.Vibrance); Existing("Hue/Saturation…", AdjustmentKind.HueSaturation, "Hue/Saturation"); Existing("Color Balance…", AdjustmentKind.ColorBalance, "Color Balance");
        Existing("Black & White…", AdjustmentKind.BlackWhite, "Black & White"); Added(ImageAdjustmentKind.PhotoFilter); Added(ImageAdjustmentKind.ChannelMixer); Added(ImageAdjustmentKind.ColorLookup);
        adjustments.Items.Add(new Separator()); Existing("Invert", AdjustmentKind.Invert, "Invert"); Added(ImageAdjustmentKind.Posterize); Added(ImageAdjustmentKind.Threshold); Existing("Gradient Map…", AdjustmentKind.GradientMap); Added(ImageAdjustmentKind.SelectiveColor);
        adjustments.Items.Add(new Separator()); Added(ImageAdjustmentKind.ShadowsHighlights); Added(ImageAdjustmentKind.HdrToning);
        adjustments.Items.Add(new Separator()); Added(ImageAdjustmentKind.Desaturate, true, "Desaturate"); Added(ImageAdjustmentKind.MatchColor); Added(ImageAdjustmentKind.ReplaceColor); Added(ImageAdjustmentKind.Equalize, true);
        menu.Items.Add(adjustments); menu.Items.Add(new Separator());
        menu.Items.Add(Command("Auto Tone", () => AutoLevels(LevelsAuto.Color), "Auto Tone"));
        menu.Items.Add(Command("Auto Contrast", () => AutoLevels(LevelsAuto.Contrast), "Auto Contrast"));
        menu.Items.Add(Command("Auto Color", () => AutoLevels(LevelsAuto.Neutral), "Auto Color"));
        menu.Items.Add(new Separator()); menu.Items.Add(Command("Image Size…", () => _ = ImageSize(), "Image Size"));
        menu.Items.Add(Command("Generative Upscale…", () => _ = GenerativeUpscale()));
        menu.Items.Add(Command("Canvas Size…", () => _ = CanvasSize(), "Canvas Size"));
        var rotation = new MenuItem { Header = Localize.Text("Image Rotation") };
        rotation.Items.Add(Command("180°", () => Change("Rotate Canvas", d => ImageGeometry.Rotate(d, 180))));
        rotation.Items.Add(Command("90° Clockwise", () => RotateCanvas(true))); rotation.Items.Add(Command("90° Counterclockwise", () => RotateCanvas(false)));
        rotation.Items.Add(Command("Arbitrary…", () => _ = RotateImageArbitrary())); rotation.Items.Add(new Separator());
        rotation.Items.Add(Command("Flip Canvas Horizontal", () => Flip(true, true))); rotation.Items.Add(Command("Flip Canvas Vertical", () => Flip(false, true))); menu.Items.Add(rotation);
        menu.Items.Add(Command("Crop", CropImageSelection)); menu.Items.Add(Command("Trim…", () => _ = Trim())); menu.Items.Add(Command("Reveal All", () => Change("Reveal All", ImageGeometry.RevealAll)));
        menu.Items.Add(new Separator()); menu.Items.Add(Command("Duplicate Image…", () => _ = DuplicateImage()));
        menu.Items.Add(Command("Apply Image…", () => _ = ChannelCalculation(false))); menu.Items.Add(Command("Calculations…", () => _ = ChannelCalculation(true)));
        menu.Items.Add(new Separator()); var variables = new MenuItem { Header = Localize.Text("Variables") };
        variables.Items.Add(Command("Define…", () => _ = DefineImageVariables())); variables.Items.Add(Command("Data Sets…", () => _ = ImageDataSets(false)));
        menu.Items.Add(variables); menu.Items.Add(Command("Apply Data Set…", () => _ = ImageDataSets(true)));
        menu.Items.Add(new Separator()); menu.Items.Add(Command("Trap…", () => _ = TrapImage())); menu.Items.Add(new Separator()); menu.Items.Add(BuildAnalysisMenu());
        menu.SubmenuOpened += (_, _) => { foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = _document is not null; };
        return menu;
    }

    private async Task ExtendedImageAdjustment(ImageAdjustmentKind kind, bool immediate = false, bool adjustmentLayer = false, Guid? editingLayer = null)
    {
        if (_document is not { } doc) return;
        var layer = PropertyLayer;
        if (!adjustmentLayer && (layer?.Asset is null || !LayerProtection.CanPaint(doc, layer.ID))) { Say("Select an unlocked pixel layer first."); return; }
        var settings = editingLayer is { } existing ? doc.Layers.First(l => l.ID == existing).Adjustment!.Extended!.Copy() : new ImageAdjustmentSettings { Kind = kind }; var title = ImageAdjustmentTitle(kind); var revision = _history.CurrentRevision;
        if (!immediate)
        {
            var dialog = new FormDialog(title, 540, kind is ImageAdjustmentKind.ChannelMixer or ImageAdjustmentKind.SelectiveColor ? 670 : 540);
            void Number(string key, double min, double max, double value = 0, string? label = null) { value = settings.Get(key, value); settings.Values[key] = value; dialog.Number(label ?? key, value, min, max, n => settings.Values[key] = n); }
            switch (kind)
            {
                case ImageAdjustmentKind.BrightnessContrast: Number("Brightness", -150, 150); Number("Contrast", -100, 100); break;
                case ImageAdjustmentKind.Vibrance: Number("Vibrance", -100, 100); Number("Saturation", -100, 100); break;
                case ImageAdjustmentKind.PhotoFilter:
                    Number("Density", 0, 100, 25); ColorInput(dialog, "Filter Color", settings.Color, c => settings.Color = c);
                    dialog.Check("Preserve Luminosity", settings.PreserveLuminosity, on => settings.PreserveLuminosity = on); break;
                case ImageAdjustmentKind.ChannelMixer:
                    for (var channel = 0; channel < 3; channel++)
                    {
                        dialog.Description(new[] { "Output Red", "Output Green", "Output Blue" }[channel]);
                        foreach (var (input, index) in new[] { ("R", 0), ("G", 1), ("B", 2) }) Number($"{channel}{input}", -200, 200, channel == index ? 100 : 0, new[] { "Red (%)", "Green (%)", "Blue (%)" }[index]);
                        Number($"{channel}Constant", -200, 200, 0, "Constant (%)");
                    }
                    dialog.Check("Monochrome", settings.Monochrome, on => settings.Monochrome = on); break;
                case ImageAdjustmentKind.ColorLookup:
                    dialog.Description("Load a .cube 3D color lookup file.");
                    var lutLabel = new TextBlock { Text = Localize.Text(settings.Cube is null ? "No color lookup loaded" : "Embedded color lookup loaded"), TextWrapping = Avalonia.Media.TextWrapping.Wrap }; dialog.Fields.Children.Add(lutLabel);
                    dialog.Fields.Children.Add(FormDialog.ActionButton("Load Color Lookup…", async () =>
                    {
                        var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Color Lookup…"), FileTypeFilter = [new FilePickerFileType("Color lookup") { Patterns = ["*.cube"] }] });
                        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                        try { settings.Cube = ColorCube.Parse(await File.ReadAllTextAsync(path)); lutLabel.Text = Path.GetFileName(path); dialog.Changed?.Invoke(); }
                        catch (Exception e) { lutLabel.Text = e.Message; }
                    })); dialog.Validate = () => settings.Cube is null ? "Load a color lookup first." : null; break;
                case ImageAdjustmentKind.Posterize: Number("Levels", 2, 256, 4); break;
                case ImageAdjustmentKind.Threshold: Number("Threshold", 0, 255, 128); break;
                case ImageAdjustmentKind.SelectiveColor:
                    var fields = new Dictionary<string, NumericUpDown>(); var switchingRange = false;
                    dialog.Choice("Colors", ["Reds", "Yellows", "Greens", "Cyans", "Blues", "Magentas", "Whites", "Neutrals", "Blacks"], settings.Range, selected =>
                    { switchingRange = true; settings.Range = selected; foreach (var p in fields) p.Value.Value = (decimal)settings.Get($"{selected}{p.Key}"); switchingRange = false; });
                    foreach (var (key, label) in new[] { ("C", "Cyan"), ("M", "Magenta"), ("Y", "Yellow"), ("K", "Black") })
                        fields[key] = dialog.Number(label, settings.Get($"{settings.Range}{key}"), -100, 100, v => { if (!switchingRange) settings.Values[$"{settings.Range}{key}"] = v; });
                    dialog.Check("Absolute", settings.Absolute, on => settings.Absolute = on); break;
                case ImageAdjustmentKind.ShadowsHighlights:
                    Number("Shadows", 0, 100, 35); Number("Shadow Width", 1, 100, 50); Number("Highlights", 0, 100); Number("Highlight Width", 1, 100, 50); Number("Radius", 1, 250, 30); Number("Midtone Contrast", -100, 100); break;
                case ImageAdjustmentKind.HdrToning:
                    Number("Exposure", -20, 20); Number("Gamma", .1, 10, 1); Number("Detail", -100, 100, 30); Number("Radius", 1, 250, 30); Number("Saturation", -100, 100); break;
                case ImageAdjustmentKind.MatchColor:
                    var sources = _tabs.Where(t => t.Document is not null).ToArray();
                    void Source(int index) { using var image = DocumentRenderer.Render(sources[index].Document!); var stats = ImageAdjustments.Statistics(image); settings.ReferenceMean = stats.Mean; settings.ReferenceDeviation = stats.Deviation; }
                    Source(0); dialog.Choice("Source Document", sources.Select(t => t.Document!.ID == doc.ID ? Localize.Text("Current Document") : t.Name), 0, Source);
                    Number("Luminance", 1, 200, 100); Number("Color Intensity", 0, 200, 100); Number("Fade", 0, 100); break;
                case ImageAdjustmentKind.ReplaceColor:
                    ColorInput(dialog, "Sample Color", settings.Color, c => settings.Color = c); ColorInput(dialog, "Replacement Color", settings.Replacement, c => settings.Replacement = c); Number("Fuzziness", 0, 255, 40); break;
            }
            if (!adjustmentLayer)
            {
                dialog.Preview(); StartPreview(doc, layer!.ID);
                dialog.Changed = () => { var copy = settings.Copy(); RequestPreview((target, id) => !dialog.PreviewEnabled || ImageAdjustments.Apply(target, id, copy)); };
            }
            bool accepted; try { accepted = await dialog.ShowDialog<bool>(this); } finally { StopPreview(); }
            if (!accepted || !ReferenceEquals(_document, doc) || revision != _history.CurrentRevision) return;
        }
        if (adjustmentLayer)
        {
            Guid? made = editingLayer;
            Edit(title, () =>
            {
                made ??= LayerPlacement.AddAdjustment(doc, AdjustmentKind.Extended, Selected);
                if (made is null) return false;
                var target = doc.Layers.First(l => l.ID == made); target.Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Extended, Extended = settings.Copy() };
                if (editingLayer is null) target.Name = Localize.Text(title.Replace("…", ""));
                return true;
            }); Reselect(made);
        }
        else { Edit(title, () => ImageAdjustments.Apply(doc, layer!.ID, settings)); Reselect(layer!.ID); }
    }

    private static void ColorInput(FormDialog dialog, string title, uint value, Action<uint> changed)
    {
        var input = dialog.Text(title + " (#RRGGBB)", $"#{value & 0xFFFFFF:X6}");
        input.TextChanged += (_, _) => { if (SKColor.TryParse(input.Text, out var color)) { changed((uint)color); dialog.Changed?.Invoke(); } };
    }
}

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

    /// <summary>A row of the Image menu: <paramref name="header"/> carries its Alt key ("_Levels…"), shown in Chinese as
    /// "色阶(L)…"; the command keeps its own name (<paramref name="id"/>), which shortcuts and customized menus key on.</summary>
    private MenuItem Keyed(string header, Action action, string? key = null, string? id = null)
    {
        var item = Command(id ?? header.Replace("_", ""), action, key);
        item.Header = Localize.Text(header);
        return item;
    }

    /// <summary>
    /// The Image menu, in the order, groups and Alt keys of Photoshop's screenshots: Mode and Adjustments; Auto Tone, Auto
    /// Contrast and Auto Color; Image Size, Generative Upscale, Canvas Size, Image Rotation, Crop, Trim and Reveal All;
    /// Duplicate, Apply Image and Calculations; Variables and Apply Data Set; Trap; Analysis. Rows that cannot act on the
    /// document in front are greyed as Photoshop greys them: Crop without a selection, Apply Data Set without data sets,
    /// Trap outside CMYK, Bitmap and Duotone outside Grayscale, Color Table outside Indexed Color.
    /// </summary>
    private MenuItem BuildImageMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("_Image"), Tag = "Image" };
        var modes = new MenuItem { Header = Localize.Text("_Mode"), Tag = "Mode" };
        var modeRows = new Dictionary<ImageMode, MenuItem>(); var depthRows = new Dictionary<int, MenuItem>();
        var modeHeaders = new Dictionary<ImageMode, string>
        {
            [ImageMode.Bitmap] = "_Bitmap…", [ImageMode.Grayscale] = "_Grayscale", [ImageMode.Duotone] = "_Duotone…", [ImageMode.Indexed] = "_Indexed Color…",
            [ImageMode.RGB] = "_RGB Color", [ImageMode.CMYK] = "_CMYK Color", [ImageMode.Lab] = "_Lab Color", [ImageMode.Multichannel] = "_Multichannel",
        };
        foreach (var mode in Enum.GetValues<ImageMode>())
        {
            // Modes and depths are two radio groups: without their own names Avalonia makes one group of the whole submenu, and
            // ticking the depth would untick the mode.
            var item = Keyed(modeHeaders[mode], () => _ = ConvertImageMode(mode), id: ModeTitle(mode)); item.ToggleType = MenuItemToggleType.Radio; item.GroupName = "image-mode";
            modes.Items.Add(item); modeRows[mode] = item;
        }
        modes.Items.Add(new Separator());
        foreach (var (bits, header) in new[] { (8, "8 Bits/Ch_annel"), (16, "16 Bits/Cha_nnel"), (32, "32 Bits/C_hannel") })
        {
            var item = Keyed(header, () => _ = ConvertImageDepth(bits), id: $"{bits} Bits/Channel"); item.ToggleType = MenuItemToggleType.Radio; item.GroupName = "image-depth";
            modes.Items.Add(item); depthRows[bits] = item;
        }
        var colorTable = Keyed("Color _Table…", () => _ = ColorTable(), id: "Color Table…");
        modes.Items.Add(new Separator()); modes.Items.Add(colorTable);
        modes.SubmenuOpened += (_, _) =>
        {
            var current = _document?.Image.Mode;
            foreach (var pair in modeRows)
            {
                pair.Value.IsChecked = current == pair.Key;
                // Bitmap and Duotone are reached from Grayscale, as in Photoshop.
                pair.Value.IsEnabled = _document is not null && (pair.Key is not (ImageMode.Bitmap or ImageMode.Duotone) || current is ImageMode.Grayscale || current == pair.Key);
            }
            foreach (var pair in depthRows) { pair.Value.IsChecked = _document?.Image.Depth == pair.Key; pair.Value.IsEnabled = _document is { } d && (pair.Key == 8 || d.Image.Mode is not (ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone)); }
            colorTable.IsEnabled = current == ImageMode.Indexed;
        };
        menu.Items.Add(modes); menu.Items.Add(new Separator());

        var adjustments = new MenuItem { Header = Localize.Text("Ad_justments"), Tag = "Adjustments" };
        void Existing(string header, AdjustmentKind kind, string? key = null) => adjustments.Items.Add(Keyed(header, () => _ = ImageAdjustment(kind), key, header.Replace("_", "")));
        void Added(string header, ImageAdjustmentKind kind, bool immediate = false, string? key = null) =>
            adjustments.Items.Add(Keyed(header, () => _ = ExtendedImageAdjustment(kind, immediate), key, ImageAdjustmentTitle(kind) + (immediate ? "" : "…")));
        Added("Brightness/_Contrast…", ImageAdjustmentKind.BrightnessContrast); Existing("_Levels…", AdjustmentKind.Levels, "Levels"); Existing("C_urves…", AdjustmentKind.Curves, "Curves"); Existing("_Exposure…", AdjustmentKind.Exposure);
        adjustments.Items.Add(new Separator());
        Added("_Vibrance…", ImageAdjustmentKind.Vibrance); Existing("_Hue/Saturation…", AdjustmentKind.HueSaturation, "Hue/Saturation"); Existing("Color _Balance…", AdjustmentKind.ColorBalance, "Color Balance");
        Existing("Blac_k & White…", AdjustmentKind.BlackWhite, "Black & White"); Added("Photo _Filter…", ImageAdjustmentKind.PhotoFilter); Added("Channel Mi_xer…", ImageAdjustmentKind.ChannelMixer); Added("Color Lookup…", ImageAdjustmentKind.ColorLookup);
        adjustments.Items.Add(new Separator());
        Existing("_Invert", AdjustmentKind.Invert, "Invert"); Added("_Posterize…", ImageAdjustmentKind.Posterize); Added("_Threshold…", ImageAdjustmentKind.Threshold); Existing("_Gradient Map…", AdjustmentKind.GradientMap); Added("_Selective Color…", ImageAdjustmentKind.SelectiveColor);
        adjustments.Items.Add(new Separator());
        Added("Shado_ws/Highlights…", ImageAdjustmentKind.ShadowsHighlights); Added("HDR Toning…", ImageAdjustmentKind.HdrToning);
        adjustments.Items.Add(new Separator());
        Added("_Desaturate", ImageAdjustmentKind.Desaturate, true, "Desaturate"); Added("_Match Color…", ImageAdjustmentKind.MatchColor); Added("_Replace Color…", ImageAdjustmentKind.ReplaceColor); Added("E_qualize", ImageAdjustmentKind.Equalize, true);
        menu.Items.Add(adjustments); menu.Items.Add(new Separator());

        menu.Items.Add(Keyed("Auto To_ne", () => AutoLevels(LevelsAuto.Color), "Auto Tone"));
        menu.Items.Add(Keyed("A_uto Contrast", () => AutoLevels(LevelsAuto.Contrast), "Auto Contrast"));
        menu.Items.Add(Keyed("Auto C_olor", () => AutoLevels(LevelsAuto.Neutral), "Auto Color"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Keyed("_Image Size…", () => _ = ImageSize(), "Image Size", "Image Size…"));
        menu.Items.Add(Keyed("Generative Upscale…", () => _ = GenerativeUpscale(), "Generative Upscale"));
        menu.Items.Add(Keyed("Canva_s Size…", () => _ = CanvasSize(), "Canvas Size", "Canvas Size…"));
        var rotation = new MenuItem { Header = Localize.Text("Ima_ge Rotation"), Tag = "Image Rotation" };
        rotation.Items.Add(Keyed("_180°", () => Change("Rotate Canvas", d => ImageGeometry.Rotate(d, 180)), id: "180°"));
        rotation.Items.Add(Keyed("_90° Clockwise", () => RotateCanvas(true), id: "90° Clockwise")); rotation.Items.Add(Keyed("9_0° Counterclockwise", () => RotateCanvas(false), id: "90° Counterclockwise"));
        rotation.Items.Add(Keyed("_Arbitrary…", () => _ = RotateImageArbitrary(), id: "Arbitrary…")); rotation.Items.Add(new Separator());
        rotation.Items.Add(Keyed("Flip Canvas _Horizontal", () => Flip(true, true), id: "Flip Canvas Horizontal")); rotation.Items.Add(Keyed("Flip Canvas _Vertical", () => Flip(false, true), id: "Flip Canvas Vertical"));
        menu.Items.Add(rotation);
        var crop = Keyed("Cro_p", CropImageSelection, id: "Crop"); menu.Items.Add(crop);
        menu.Items.Add(Keyed("T_rim…", () => _ = Trim(), id: "Trim…")); menu.Items.Add(Keyed("Re_veal All", () => Change("Reveal All", ImageGeometry.RevealAll), id: "Reveal All"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Keyed("_Duplicate…", () => _ = DuplicateImage(), id: "Duplicate Image…"));
        menu.Items.Add(Keyed("Appl_y Image…", () => _ = ChannelCalculation(false), id: "Apply Image…")); menu.Items.Add(Keyed("_Calculations…", () => _ = ChannelCalculation(true), id: "Calculations…"));
        menu.Items.Add(new Separator());
        var variables = new MenuItem { Header = Localize.Text("Varia_bles"), Tag = "Variables" };
        var dataSets = Keyed("Data _Sets…", () => _ = ImageDataSets(false), id: "Data Sets…");
        variables.Items.Add(Keyed("_Define…", () => _ = DefineImageVariables(), id: "Define…")); variables.Items.Add(dataSets);
        var applySet = Keyed("App_ly Data Set…", () => _ = ImageDataSets(true), id: "Apply Data Set…");
        menu.Items.Add(variables); menu.Items.Add(applySet);
        menu.Items.Add(new Separator());
        var trap = Keyed("_Trap…", () => _ = TrapImage(), id: "Trap…"); menu.Items.Add(trap);
        ToolTip.SetTip(trap, Localize.Text("Trap is for CMYK documents. Convert with Image ▸ Mode ▸ CMYK Color first."));
        menu.Items.Add(new Separator()); menu.Items.Add(BuildAnalysisMenu());
        menu.SubmenuOpened += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = _document is not null;
            // Crop needs a selection; data sets need variables and sets; Trap needs CMYK, as Photoshop greys them.
            crop.IsEnabled = _document?.Selection.Path is not null;
            dataSets.IsEnabled = _document?.Variables.Count > 0;
            applySet.IsEnabled = _document?.DataSets.Count > 0;
            trap.IsEnabled = _document?.Image.Mode == ImageMode.CMYK;
        };
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
            var documents = _tabs.Where(t => t.Document is not null && !ReferenceEquals(t.Document, doc)).Select(t => (t.Name, t.Document!)).ToArray();
            var context = new ImageAdjustmentContext(AdjustmentPicture(doc, adjustmentLayer ? null : layer?.ID), doc, _open.Name, documents);
            var dialog = new ImageAdjustmentDialog(title, settings, context);
            if (!adjustmentLayer)
            {
                StartPreview(doc, layer!.ID);
                dialog.Changed = () => { var copy = settings.Copy(); RequestPreview((target, id) => !dialog.PreviewEnabled || ImageAdjustments.Apply(target, id, copy)); };
            }
            try { await dialog.ShowDialog<bool>(this); } finally { if (!adjustmentLayer) StopPreview(); }
            if (!dialog.Accepted || !ReferenceEquals(_document, doc) || revision != _history.CurrentRevision) return;
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

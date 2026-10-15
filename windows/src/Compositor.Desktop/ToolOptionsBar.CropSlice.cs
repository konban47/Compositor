using Avalonia.Controls;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    private void BuildCropSliceOptions()
    {
        var c = _options.Crop;
        var presets = OptionAction("cropAdvanced", "Crop Presets"); presets.Width = 165;
        _optionRefreshers.Add(() => presets.Content = Localize.Text(c.Mode == 0 ? "Ratio" : "W × H × Resolution") + "  ⌄");
        OptionNumber("cropDimensions", "W:", () => c.Width, v => c.Width = v); OptionAction("cropDimensions", "Swap Crop Dimensions", "swap"); OptionNumber("cropDimensions", "H:", () => c.Height, v => c.Height = v);
        var resolution = OptionNumber("cropResolution", "Resolution:", () => c.Resolution, v => c.Resolution = v, 0, 9600);
        _optionRefreshers.Add(() => { resolution.Maximum = (decimal)(c.ResolutionUnit == 0 ? 9600 : 9600 / 2.54); resolution.Value = (decimal)c.Resolution; });
        OptionChoice("cropResolution", "", ["Pixels/Inch", "Pixels/Centimeter"], () => c.ResolutionUnit, v => { if (v != c.ResolutionUnit) c.Resolution *= v == 0 ? 2.54 : 1 / 2.54; c.Resolution = Math.Min(c.Resolution, v == 0 ? 9600 : 9600 / 2.54); c.ResolutionUnit = v; }, 130);
        OptionAction("perspectiveAdvanced", "Front Image"); OptionAction("cropClear", "Clear Crop");
        OptionAction("cropSettings", "Straighten", "Ruler"); OptionAction("cropSettings", "Crop Overlay", "grid"); OptionAction("cropSettings", "Crop Options", "settings");
        OptionCheck("cropSettings", "Delete Cropped Pixels", () => c.DeletePixels, v => c.DeletePixels = v);
        OptionChoice("cropSettings", "Fill:", ["Transparent (Default)", "Generative Expand", "Content-Aware Fill"], () => c.Fill, v => c.Fill = v, 170);
        OptionAction("cropSettings", "Fill Options", "info"); OptionAction("cropSettings", "Reset Crop", "reset");
        OptionCheck("perspectiveAdvanced", "Show Grid", () => c.PerspectiveGrid, v => c.PerspectiveGrid = v);
        OptionAction("cropCommit", "Apply Crop", "check"); OptionAction("cropCommit", "Cancel Crop", "close");
        OptionChoice("sliceCreate", "Style:", ["Normal", "Fixed Ratio", "Fixed Size"], () => c.SliceStyle, v => c.SliceStyle = v);
        var w = OptionNumber("sliceCreate", "Width:", () => c.SliceWidth, v => c.SliceWidth = v, 1); var h = OptionNumber("sliceCreate", "Height:", () => c.SliceHeight, v => c.SliceHeight = v, 1);
        _optionRefreshers.Add(() => w.IsEnabled = h.IsEnabled = c.SliceStyle > 0); OptionAction("sliceCreate", "Slices from Guides");
        foreach (var title in new[] { "Bring Slice to Front", "Bring Slice Forward", "Send Slice Backward", "Send Slice to Back" }) OptionAction("sliceArrange", title, title.Contains("Front") || title.Contains("Forward") ? "up" : "down");
        OptionAction("sliceArrange", "Promote Slice"); OptionAction("sliceArrange", "Divide Slice…");
        foreach (var (title, icon) in new[] { ("Align Slice Left", "align-left"), ("Align Slice Center", "align-center"), ("Align Slice Right", "align-right"), ("Align Slice Top", "align-top"), ("Align Slice Middle", "align-middle"), ("Align Slice Bottom", "align-bottom") }) OptionAction("sliceArrange", title, icon);
        OptionAction("sliceArrange", "Slice Alignment Options", "more");
        var auto = OptionAction("sliceArrange", "Hide Auto Slices"); _optionRefreshers.Add(() => auto.Content = Localize.Text(c.HideAutoSlices ? "Show Auto Slices" : "Hide Auto Slices"));
        OptionAction("sliceArrange", "Slice Options", "properties"); OptionAction("sliceExport", "Export Slices…");
    }
    private void ShowCropSliceOptions(Tool tool)
    {
        var crop = tool is Tool.Crop or Tool.PerspectiveCrop; var slice = tool is Tool.Slice or Tool.SliceSelect;
        if (crop || slice) foreach (var group in new[] { "crop", "perspective", "slice", "marks", "proApply", "proReadout" }) On(group, false);
        On("cropAdvanced", tool == Tool.Crop); On("cropDimensions", crop); On("cropResolution", crop && (_options.Crop.Mode == 1 || tool == Tool.PerspectiveCrop));
        On("cropClear", crop); On("cropSettings", tool == Tool.Crop); On("perspectiveAdvanced", tool == Tool.PerspectiveCrop); On("cropCommit", crop);
        On("sliceCreate", tool == Tool.Slice); On("sliceArrange", tool == Tool.SliceSelect); On("sliceExport", slice);
    }
}

using Avalonia.Controls;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public partial class MainWindow
{
    private async Task ConditionalModeChange()
    {
        if (_document is not { } document) return;
        CommitText();
        var revision = _history.CurrentRevision;
        var dialog = new FormDialog("Conditional Mode Change", 540, 570);
        dialog.Description("Convert only when the document matches a selected source mode.");
        var modes = Enum.GetValues<ImageMode>();
        var sources = new List<CheckBox>();
        foreach (var mode in modes) sources.Add(dialog.Check(ModeTitle(mode), mode == document.Image.Mode));
        dialog.Row("", FormDialog.ActionButton("All", () => sources.ForEach(check => check.IsChecked = true)));
        dialog.Row("", FormDialog.ActionButton("None", () => sources.ForEach(check => check.IsChecked = false)));
        var target = dialog.Choice("Target Mode", modes.Select(ModeTitle).ToArray(), Array.IndexOf(modes, document.Image.Mode));
        dialog.Description("Bitmap, indexed color, duotone and multichannel conversions combine visible layers. Undo restores the original document.");
        if (!await dialog.ShowDialog<bool>(this) || _document != document || revision != _history.CurrentRevision) return;
        if (sources[Array.IndexOf(modes, document.Image.Mode)].IsChecked != true) return;
        var destination = modes[target.SelectedIndex];
        if (destination == document.Image.Mode) return;
        var settings = document.Image with
        {
            Mode = destination,
            Depth = destination is ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone ? 8 : document.Image.Depth,
            Palette = null,
        };
        Edit("Conditional Mode Change", () => ImageModeEdits.Convert(document, settings));
        _open.ActiveAlpha = null;
        _open.VisibleChannels = ColorChannels.RGB;
        document.EditChannels = ColorChannels.RGB;
        Refresh();
    }
}

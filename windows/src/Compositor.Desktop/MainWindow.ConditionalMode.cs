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
        var dialog = new FormDialog("Conditional Mode Change", 560, 390); dialog.SideActions();
        var modes = Enum.GetValues<ImageMode>();
        var sources = modes.Select(mode => AutomationDialog.Check(ModeTitle(mode), mode == document.Image.Mode)).ToArray();
        // Down the first column then the second, as Photoshop lists them.
        (ImageMode Mode, char Key)[] order = [(ImageMode.Bitmap, 'B'), (ImageMode.RGB, 'R'), (ImageMode.Grayscale, 'G'), (ImageMode.CMYK, 'C'), (ImageMode.Duotone, 'D'), (ImageMode.Lab, 'L'), (ImageMode.Indexed, 'I'), (ImageMode.Multichannel, 'U')];
        var checks = DialogKit.Columns("*,*", 16, [.. order.Select(item => (Control)dialog.Mnemonic(sources[Array.IndexOf(modes, item.Mode)], item.Key))]);
        Button Pick(string title, bool on, char key)
        {
            var button = FormDialog.ActionButton(title, () => { foreach (var check in sources) check.IsChecked = on; }); button.Margin = default; button.Width = 110; return dialog.Mnemonic(button, key);
        }
        var source = new StackPanel { Spacing = 8, Children = { checks, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { Pick("All", true, 'A'), Pick("None", false, 'N') } } } };
        dialog.Fields.Children.Add(DialogKit.Group("Source Mode", source));
        var target = AutomationDialog.Choice([.. modes.Select(ModeTitle)], Array.IndexOf(modes, document.Image.Mode), "Target Mode");
        target.Width = 190; target.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        ToolTip.SetTip(target, Localize.Text("Bitmap, indexed color, duotone and multichannel conversions combine visible layers. Undo restores the original document."));
        dialog.Fields.Children.Add(DialogKit.Group("Target Mode", DialogKit.Row("Mode", target, 56)));        if (!await dialog.ShowDialog<bool>(this) || _document != document || revision != _history.CurrentRevision) return;
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed class ClippingDeleteDialog : DialogWindow
{
    private ClippingDeleteChoice _choice;
    private ClippingDeleteDialog()
    {
        Title = Localize.Text("Delete clipping source"); Width = 560;
        SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var bake = new Button { Content = Localize.Text("Bake and Delete") };
        var release = new Button { Content = Localize.Text("Remove Links and Delete") };
        var cancel = new Button { Content = Localize.Text("Cancel"), IsCancel = true };
        bake.Click += (_, _) => { _choice = ClippingDeleteChoice.Bake; Close(); };
        release.Click += (_, _) => { _choice = ClippingDeleteChoice.Release; Close(); };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 16,
            Children =
            {
                new TextBlock { Text = Localize.Text("Other layers use this layer as a clipping mask. Bake preserves their current clipped pixels; Remove Links reveals the original pixels. Both choices can be undone."), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, release, bake } },
            },
        };
    }

    internal static async Task<ClippingDeleteChoice> Ask(Window owner)
    {
        var dialog = new ClippingDeleteDialog();
        await dialog.ShowDialog(owner);
        return dialog._choice;
    }
}

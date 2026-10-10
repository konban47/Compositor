using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed class LayerOperationDialog : DialogWindow
{
    internal sealed record ContainerResult(string Name, SKRect Bounds, bool Ellipse);
    private readonly StackPanel _body = new() { Spacing = 12, Margin = new Thickness(20) };
    private bool _accepted;
    private LayerOperationDialog(string title)
    { Title = Localize.Text(title); Width = 430; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; Content = _body; }
    private void Buttons(Func<bool>? validate = null)
    {
        var error = new TextBlock { Text = Localize.Text("Enter a name and valid dimensions."), IsVisible = false };
        var ok = new Button { Content = Localize.Text("OK"), IsDefault = true, MinWidth = 85 };
        var cancel = new Button { Content = Localize.Text("Cancel"), IsCancel = true, MinWidth = 85 };
        ok.Click += (_, _) => { if (validate is not null && !validate()) { error.IsVisible = true; return; } _accepted = true; Close(); };
        cancel.Click += (_, _) => Close(); _body.Children.Add(error);
        _body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { cancel, ok } });
    }
    internal static async Task<LayerLocks?> Locks(Window owner, LayerLocks initial)
    {
        var dialog = new LayerOperationDialog("Lock Layers"); var boxes = new Dictionary<LayerLocks, CheckBox>();
        foreach (var (flag, title) in new[] { (LayerLocks.Transparency, "Lock Transparent Pixels"), (LayerLocks.Pixels, "Lock Image Pixels"), (LayerLocks.Position, "Lock Position"), (LayerLocks.All, "Lock All") })
        { var box = new CheckBox { Content = Localize.Text(title), IsChecked = initial.HasFlag(flag) }; boxes[flag] = box; dialog._body.Children.Add(box); }
        dialog.Buttons(); await dialog.ShowDialog(owner);
        return dialog._accepted ? boxes.Where(p => p.Value.IsChecked == true).Aggregate(LayerLocks.None, (a, p) => a | p.Key) : null;
    }
    internal static async Task<ContainerResult?> Container(Window owner, string title, string name, SKRect bounds, bool frame)
    {
        var dialog = new LayerOperationDialog(title); var text = new TextBox { Text = name };
        dialog._body.Children.Add(new TextBlock { Text = Localize.Text("Name") }); dialog._body.Children.Add(text);
        NumericUpDown Field(string label, double value, int min)
        {
            var field = new NumericUpDown { Minimum = min, Maximum = DocumentLimits.MaxSide, Value = (decimal)Math.Clamp(value, min, DocumentLimits.MaxSide), Increment = 1, FormatString = "0.##" };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") }; row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(field, 1); row.Children.Add(field); dialog._body.Children.Add(row); return field;
        }
        var x = Field("X (px)", bounds.Left, 0); var y = Field("Y (px)", bounds.Top, 0);
        var width = Field("Width (px)", bounds.Width, 1); var height = Field("Height (px)", bounds.Height, 1);
        var ellipse = new CheckBox { Content = Localize.Text("Elliptical Frame"), IsVisible = frame }; dialog._body.Children.Add(ellipse);
        dialog.Buttons(() => !string.IsNullOrWhiteSpace(text.Text) && text.Text.Length <= 256 && x.Value + width.Value <= DocumentLimits.MaxSide
            && y.Value + height.Value <= DocumentLimits.MaxSide && width.Value * height.Value <= DocumentLimits.MaxSurfacePixels);
        await dialog.ShowDialog(owner);
        return dialog._accepted ? new ContainerResult(text.Text!.Trim(), SKRect.Create((float)x.Value!, (float)y.Value!, (float)width.Value!, (float)height.Value!), ellipse.IsChecked == true) : null;
    }
}

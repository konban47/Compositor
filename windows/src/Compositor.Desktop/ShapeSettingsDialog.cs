using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed class ShapeSettingsDialog : DialogWindow
{
    internal sealed record Result(int Sides, double Inner, string Path);
    internal ShapeSettingsDialog(int sides, double inner, string path, string? selected)
    {
        Title = Localize.Text("Shape Tool Options"); Width = 540; Height = 580;
        var polygon = new NumericUpDown { Minimum = 3, Maximum = 100, Value = sides };
        var star = new NumericUpDown { Minimum = 1, Maximum = 99, Value = (decimal)(inner * 100) };
        var data = new TextBox { Text = path, AcceptsReturn = true, Height = 95, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var presets = new ComboBox { ItemsSource = ShapeTemplates.Presets.Keys.Select(Localize.Text).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex >= 0) data.Text = ShapeTemplates.Presets.Values.ElementAt(presets.SelectedIndex); };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var use = new Button { Content = Localize.Text("Use Selected Shape / Selection"), IsEnabled = selected is not null };
        use.Click += (_, _) => data.Text = selected;
        var apply = new Button { Content = Localize.Text("Apply") };
        apply.Click += (_, _) =>
        {
            try { Close(new Result((int)(polygon.Value ?? 5), (double)(star.Value ?? 50) / 100, ShapeTemplates.Normalize(data.Text ?? ""))); }
            catch { error.Text = Localize.Text("Enter a valid SVG path enclosing a non-empty shape."); }
        };
        var cancel = new Button { Content = Localize.Text("Cancel"), IsCancel = true }; cancel.Click += (_, _) => Close(null);
        Content = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children = {
            new TextBlock { Text = Localize.Text("Polygon / star sides") }, polygon,
            new TextBlock { Text = Localize.Text("Star inner radius (%)") }, star,
            new TextBlock { Text = Localize.Text("Custom Shape Preset") }, presets, use,
            new TextBlock { Text = Localize.Text("Custom shape SVG path") }, data, error,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } }
        } };
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Model;

namespace Compositor.Desktop;

internal sealed class MaskRefineDialog : DialogWindow
{
    internal sealed record Settings(double Density, double Feather, int Shift, double Smooth);
    private readonly NumericUpDown _density, _feather, _shift, _smooth;
    private Settings? _result;
    private MaskRefineDialog(LayerMask mask, Action<Settings> preview)
    {
        Title = Localize.Text("Select and Mask"); Width = 390; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        NumericUpDown Field(string name, double value, double min, double max)
        {
            var input = new NumericUpDown
            {
                Value = (decimal)value,
                Minimum = (decimal)min,
                Maximum = (decimal)max,
                FormatString = "0.##",
                Height = 24,
                FontSize = 11.5,
                CornerRadius = new CornerRadius(2),
                Background = Skin.SurfaceDarkBrush,
                BorderBrush = Skin.BorderControlBrush,
                Foreground = Skin.LabelBrush,
                Tag = name,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(new TextBlock { Text = Localize.Text(name), FontSize = 12, Foreground = Skin.LabelBrush });
            content.Children.Add(input);
            return input;
        }
        _density = Field("Density (%)", mask.Density * 100, 0, 100);
        _feather = Field("Feather (px)", mask.Feather, 0, 1000);
        _shift = Field("Shift Edge (px)", 0, -100, 100);
        _smooth = Field("Smooth (px)", 0, 0, 100);
        var show = new Button
        {
            Content = Localize.Text("Preview"),
            MinWidth = 72,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(2),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var apply = new Button
        {
            Content = Localize.Text("Apply"),
            IsDefault = true,
            MinWidth = 72,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(2),
            Background = Skin.AccentBrush,
            BorderBrush = Skin.AccentBrush,
            BorderThickness = new Thickness(1),
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var cancel = new Button
        {
            Content = Localize.Text("Cancel"),
            IsCancel = true,
            MinWidth = 72,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(2),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        show.Click += (_, _) => preview(Read());
        apply.Click += (_, _) => { _result = Read(); Close(); };
        cancel.Click += (_, _) => Close();
        content.Children.Add(new TextBlock { Text = Localize.Text("Preview updates the canvas. Edge shift and smoothing rasterize vector masks."), FontSize = 11, Foreground = Skin.SecondaryBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { show, cancel, apply } });
        Content = content;
    }
    private Settings Read() => new((double)(_density.Value ?? 100) / 100, (double)(_feather.Value ?? 0), (int)(_shift.Value ?? 0), (double)(_smooth.Value ?? 0));
    public static async Task<Settings?> Ask(Window owner, LayerMask mask, Action<Settings> preview)
    {
        var dialog = new MaskRefineDialog(mask, preview); await dialog.ShowDialog(owner); return dialog._result;
    }
}

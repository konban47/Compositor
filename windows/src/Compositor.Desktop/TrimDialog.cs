using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;

namespace Compositor.Desktop;

/// <summary>
/// Image ▸ Trim: which edges to take away, and what counts as border. Avalonia ships no such dialog, so this
/// is one.
/// </summary>
internal sealed class TrimDialog : DialogWindow
{
    private readonly ComboBox _basedOn = new();
    private readonly CheckBox _top = new() { Content = Localize.Text("Top") };
    private readonly CheckBox _bottom = new() { Content = Localize.Text("Bottom") };
    private readonly CheckBox _left = new() { Content = Localize.Text("Left") };
    private readonly CheckBox _right = new() { Content = Localize.Text("Right") };
    private readonly Slider _tolerance = new() { Minimum = 0, Maximum = 255, Width = 200 };
    private TrimOptions? _result;

    private TrimDialog(TrimOptions start)
    {
        Title = Localize.Text("Trim");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _basedOn.ItemsSource = new[] { "Transparent pixels", "Top-left pixel color", "Bottom-right pixel color" };
        _basedOn.SelectedIndex = (int)start.BasedOn;
        _basedOn.Width = 200;
        _basedOn.Height = 24;
        _basedOn.FontSize = 11.5;
        _basedOn.CornerRadius = new CornerRadius(7);
        _basedOn.Background = Skin.SurfaceControlBrush;
        _basedOn.BorderBrush = Skin.BorderControlBrush;
        _basedOn.VerticalContentAlignment = VerticalAlignment.Center;
        _top.IsChecked = start.Top;
        _top.FontSize = 12;
        _bottom.IsChecked = start.Bottom;
        _bottom.FontSize = 12;
        _left.IsChecked = start.Left;
        _left.FontSize = 12;
        _right.IsChecked = start.Right;
        _right.FontSize = 12;
        _tolerance.Value = start.Tolerance;
        var readout = new TextBlock { Text = start.Tolerance.ToString(), Width = 40, FontSize = 11, Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
        _tolerance.PropertyChanged += (_, change) =>
        {
            if (change.Property == Slider.ValueProperty) readout.Text = ((int)_tolerance.Value).ToString();
        };

        var ok = new Button
        {
            Content = Localize.Text("OK"),
            IsDefault = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
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
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                Row("Based on", _basedOn),
                new TextBlock { Text = Localize.Text("Trim away"), FontSize = 12, Foreground = Skin.LabelBrush },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children = { _top, _bottom, _left, _right },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = Localize.Text("Tolerance"), Width = 120, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center },
                        _tolerance,
                        readout,
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
    }

    private static Control Row(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = Localize.Text(label), Width = 120, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center },
            control,
        },
    };

    private void Accept()
    {
        _result = new TrimOptions(
            (TrimBasedOn)Math.Max(0, _basedOn.SelectedIndex),
            _top.IsChecked == true,
            _bottom.IsChecked == true,
            _left.IsChecked == true,
            _right.IsChecked == true,
            (byte)Math.Clamp((int)_tolerance.Value, 0, 255));
        Close();
    }

    /// <summary>What was asked for, or null when the dialog was dismissed.</summary>
    public static async Task<TrimOptions?> Ask(Window owner, TrimOptions start)
    {
        var dialog = new TrimDialog(start);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}

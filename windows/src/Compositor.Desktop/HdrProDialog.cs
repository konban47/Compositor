using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// Merge to HDR Pro's second step: the merged light previewed on the left, and on the right the mode, Remove Ghosts,
/// the toning method and its Edge Glow, Tone and Detail and Advanced amounts. A 32-bit result keeps the light as it is,
/// so its toning is unavailable.
/// </summary>
internal sealed class HdrProDialog : DialogWindow
{
    private readonly float[] _radiance;
    private readonly int _width, _height;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly ComboBox _mode = Combo(["32 Bit", "16 Bit", "8 Bit"], 1);
    private readonly ComboBox _method = Combo(["Local Adaptation", "Equalize Histogram", "Exposure and Gamma", "Highlight Compression"], 0);
    private readonly CheckBox _ghosts = new() { Content = Localize.Text("Remove ghosts") };
    private readonly List<(string Name, Slider Slider)> _amounts = [];
    private readonly StackPanel _toning = new() { Spacing = 6 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private WriteableBitmap? _shown;
    private HdrOptions? _result;
    private readonly bool _align;

    private HdrProDialog(float[] radiance, int width, int height, bool align)
    {
        _radiance = radiance; _width = width; _height = height; _align = align;
        Title = Localize.Text("Merge to HDR Pro"); Width = 980; Height = 640; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { ColumnDefinitions = new("*,390"), Margin = new Thickness(14) };
        layout.Children.Add(new Border { Background = Skin.SurfaceDarkBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Child = _preview });
        var side = new StackPanel { Spacing = 8, Margin = new Thickness(14, 0, 18, 0) };
        side.Children.Add(Row("Mode", _mode)); side.Children.Add(_ghosts);
        side.Children.Add(_toning);
        _toning.Children.Add(Row("Method", _method));
        var defaults = new HdrToning();
        Header("Edge Glow");
        Amount("Radius", 1, 500, defaults.Radius, "px"); Amount("Strength", .1, 4, defaults.Strength, "");
        Header("Tone and Detail");
        Amount("Gamma", .1, 2, defaults.Gamma, ""); Amount("Exposure", -5, 5, defaults.Exposure, ""); Amount("Detail", -100, 300, defaults.Detail, "%");
        Header("Advanced");
        Amount("Shadow", -100, 100, defaults.Shadow, "%"); Amount("Highlight", -100, 100, defaults.Highlight, "%");
        Amount("Vibrance", -100, 100, defaults.Vibrance, "%"); Amount("Saturation", -100, 100, defaults.Saturation, "%");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(FormDialog.ActionButton("Cancel", Close));
        var ok = FormDialog.ActionButton("OK", Accept); ok.IsDefault = true; ok.Background = Skin.AccentBrush; ok.Foreground = Brushes.White;
        actions.Children.Add(ok);
        side.Children.Add(actions);
        var scroll = new ScrollViewer { Content = side }; Grid.SetColumn(scroll, 1); layout.Children.Add(scroll);
        Content = layout;
        _mode.SelectionChanged += (_, _) => { _toning.IsEnabled = _mode.SelectedIndex != 0; Queue(); };
        _method.SelectionChanged += (_, _) => { Enable(); Queue(); };
        _timer.Tick += (_, _) => { _timer.Stop(); ShowPreview(); };
        Opened += (_, _) => { Enable(); ShowPreview(); };
        Closed += (_, _) => { _timer.Stop(); _shown?.Dispose(); };

        void Header(string text) => _toning.Children.Add(new TextBlock { Text = Localize.Text(text), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        void Amount(string name, double least, double most, double value, string unit)
        {
            var slider = new Slider { Minimum = least, Maximum = most, Value = value };
            var readout = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush };
            void Show() => readout.Text = slider.Value.ToString(most <= 5 ? "0.00" : "0") + unit;
            slider.PropertyChanged += (_, change) => { if (change.Property == Slider.ValueProperty) { Show(); Queue(); } };
            Show();
            var row = new Grid { ColumnDefinitions = new("90,*,Auto") };
            row.Children.Add(new TextBlock { Text = Localize.Text(name), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slider, 1); row.Children.Add(slider); Grid.SetColumn(readout, 2); row.Children.Add(readout);
            _toning.Children.Add(row); _amounts.Add((name, slider));
        }
    }

    private static ComboBox Combo(string[] items, int selected) =>
        new() { ItemsSource = items.Select(Localize.Text).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 28 };
    private static Grid Row(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new("90,*") };
        row.Children.Add(new TextBlock { Text = Localize.Text(label), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1); row.Children.Add(control);
        return row;
    }

    /// <summary>Only Local Adaptation has Edge Glow and the detail amounts; Exposure and Gamma has those two.</summary>
    private void Enable()
    {
        var method = (HdrToneMethod)Math.Max(0, _method.SelectedIndex);
        foreach (var (name, slider) in _amounts)
            slider.IsEnabled = method == HdrToneMethod.LocalAdaptation || method == HdrToneMethod.ExposureAndGamma && name is "Gamma" or "Exposure";
    }

    private double Value(string name) => _amounts.First(a => a.Name == name).Slider.Value;

    internal HdrToning Toning() => new()
    {
        Method = (HdrToneMethod)Math.Max(0, _method.SelectedIndex), Radius = Value("Radius"), Strength = Value("Strength"), Gamma = Value("Gamma"),
        Exposure = Value("Exposure"), Detail = Value("Detail"), Shadow = Value("Shadow"), Highlight = Value("Highlight"),
        Vibrance = Value("Vibrance"), Saturation = Value("Saturation"),
    };

    internal HdrOptions Options() => new((HdrMode)Math.Max(0, _mode.SelectedIndex), _ghosts.IsChecked == true, _align, Toning());

    private void Queue() { _timer.Stop(); _timer.Start(); }

    /// <summary>The preview toned as the panel stands; a 32-bit result shows the light at its own exposure.</summary>
    internal void ShowPreview()
    {
        var toning = _mode.SelectedIndex == 0 ? new HdrToning { Method = HdrToneMethod.ExposureAndGamma } : Toning();
        using var toned = HdrMerge.Tone(_radiance, _width, _height, toning);
        var floats = MemoryMarshal.Cast<byte, float>(toned.GetPixelSpan());
        var bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
        {
            var bytes = new byte[_width * 4];
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width * 4; x++) bytes[x] = (byte)Math.Clamp(Math.Round(floats[y * toned.RowBytes / 4 + x] * 255), 0, 255);
                Marshal.Copy(bytes, 0, buffer.Address + y * buffer.RowBytes, bytes.Length);
            }
        }
        _preview.Source = bitmap; _shown?.Dispose(); _shown = bitmap;
    }

    private void Accept() { _result = Options(); Close(); }

    /// <summary>The merge's options, or null when cancelled. <paramref name="radiance"/> is a small merged copy for the preview.</summary>
    public static async Task<HdrOptions?> Ask(Window owner, float[] radiance, int width, int height, bool align)
    {
        var dialog = new HdrProDialog(radiance, width, height, align);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    /// <summary>For the self check: the dialog shown but not waited on.</summary>
    internal static HdrProDialog Open(Window owner, float[] radiance, int width, int height, out Task closed)
    {
        var dialog = new HdrProDialog(radiance, width, height, false);
        closed = dialog.ShowDialog(owner);
        return dialog;
    }
    internal void SetMode(int mode) => _mode.SelectedIndex = mode;
    internal void SetMethod(int method) => _method.SelectedIndex = method;
    internal bool ToningEnabled => _toning.IsEnabled;
    internal bool SliderEnabled(string name) => _amounts.First(a => a.Name == name).Slider.IsEnabled;
    internal void Confirm() => Accept();
    internal HdrOptions? Result => _result;
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed class ExportAsDialog : DialogWindow
{
    private readonly CanvasDocument _document;
    private readonly ComboBox _format = new()
    {
        ItemsSource = new[] { "PNG", "JPEG", "PDF" },
        Width = 170,
        Height = 24,
        FontSize = 11.5,
        CornerRadius = new CornerRadius(7),
        Background = Skin.SurfaceControlBrush,
        BorderBrush = Skin.BorderControlBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly NumericUpDown _width = Dimension();
    private readonly NumericUpDown _height = Dimension();
    private readonly CheckBox _linked = new() { Content = Localize.Text("Constrain Proportions"), FontSize = 11.5, IsChecked = true };
    private readonly NumericUpDown _quality = new()
    {
        Minimum = 1,
        Maximum = 100,
        Value = 90,
        Width = 170,
        Height = 24,
        FontSize = 11.5,
        CornerRadius = new CornerRadius(7),
        Background = Skin.SurfaceDarkBrush,
        BorderBrush = Skin.BorderControlBrush,
        Foreground = Skin.LabelBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox _transparent = new() { Content = Localize.Text("Transparency"), FontSize = 11.5, IsChecked = true };
    private readonly ComboBox _background = new()
    {
        ItemsSource = new[] { Localize.Text("White"), Localize.Text("Black") },
        SelectedIndex = 0,
        Width = 170,
        Height = 24,
        FontSize = 11.5,
        CornerRadius = new CornerRadius(7),
        Background = Skin.SurfaceControlBrush,
        BorderBrush = Skin.BorderControlBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    private readonly TextBlock _size = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Skin.SecondaryBrush };
    private readonly Button _export = new()
    {
        Content = Localize.Text("Export"),
        IsDefault = true,
        IsEnabled = false,
        MinWidth = 80,
        Height = 26,
        FontSize = 12,
        CornerRadius = new CornerRadius(7),
        Background = Skin.AccentBrush,
        BorderBrush = Skin.AccentBrush,
        BorderThickness = new Thickness(1),
        Foreground = Brushes.White,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private ExportResult? _encoded;
    private WriteableBitmap? _bitmap;
    private bool _changing, _closed;
    private int _revision;
    internal (ExportOptions Options, byte[] Bytes)? Result { get; private set; }

    internal ExportAsDialog(CanvasDocument document, ExportFormat format = ExportFormat.Png)
    {
        _document = document.Clone();
        Title = Localize.Text("Export As"); Width = 860; Height = 610; MinWidth = 660; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _width.Value = document.Width; _height.Value = document.Height; _format.SelectedIndex = (int)format;
        var controls = new StackPanel { Spacing = 9, Margin = new Thickness(18), Width = 230 };
        void Add(string label, Control control) { controls.Children.Add(new TextBlock { Text = Localize.Text(label), FontSize = 11.5, Foreground = Skin.LabelBrush }); controls.Children.Add(control); }
        Add("Format", _format); Add("Width, pixels", _width); Add("Height, pixels", _height);
        controls.Children.Add(_linked); Add("Quality", _quality); controls.Children.Add(_transparent); Add("Background", _background);
        controls.Children.Add(_size);
        var cancel = new Button
        {
            Content = Localize.Text("Cancel"),
            IsCancel = true,
            MinWidth = 80,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cancel.Click += (_, _) => Close();
        _export.Click += (_, _) => { if (_encoded is null) return; Result = (Options(), _encoded.Bytes); Close(); };
        controls.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { cancel, _export } });
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,266") };
        layout.Children.Add(new Border { Background = Skin.Checker, Margin = new Thickness(18), Child = _preview });
        Grid.SetColumn(controls, 1); layout.Children.Add(new ScrollViewer { Content = controls, [Grid.ColumnProperty] = 1 }); Content = layout;
        _width.ValueChanged += (_, _) => DimensionChanged(true);
        _height.ValueChanged += (_, _) => DimensionChanged(false);
        _format.SelectionChanged += (_, _) => Queue(); _quality.ValueChanged += (_, _) => Queue();
        _transparent.IsCheckedChanged += (_, _) => Queue(); _background.SelectionChanged += (_, _) => Queue();
        _timer.Tick += async (_, _) => { _timer.Stop(); await RenderPreview(); };
        Opened += (_, _) => Queue();
        Closed += (_, _) => { _closed = true; _revision++; _timer.Stop(); _bitmap?.Dispose(); _encoded?.Dispose(); _document.Dispose(); };
    }

    private static NumericUpDown Dimension() => new()
    {
        Minimum = 1,
        Maximum = DocumentLimits.MaxSide,
        Increment = 1,
        FormatString = "0",
        Width = 170,
        Height = 24,
        FontSize = 11.5,
        CornerRadius = new CornerRadius(7),
        Background = Skin.SurfaceDarkBrush,
        BorderBrush = Skin.BorderControlBrush,
        Foreground = Skin.LabelBrush,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private ExportOptions Options() => new((ExportFormat)_format.SelectedIndex, (int)(_width.Value ?? 1), (int)(_height.Value ?? 1),
        (int)(_quality.Value ?? 90), _transparent.IsChecked == true, _background.SelectedIndex == 1 ? SKColors.Black : SKColors.White, _document.Resolution);
    private void DimensionChanged(bool width)
    {
        if (_changing) return;
        _changing = true;
        if (_linked.IsChecked == true)
        {
            if (width) _height.Value = Math.Clamp(Math.Round((_width.Value ?? 1) * _document.Height / _document.Width), 1, DocumentLimits.MaxSide);
            else _width.Value = Math.Clamp(Math.Round((_height.Value ?? 1) * _document.Width / _document.Height), 1, DocumentLimits.MaxSide);
        }
        _changing = false; Queue();
    }
    private void Queue()
    {
        if (_closed) return;
        _revision++; _export.IsEnabled = false; _quality.IsEnabled = _format.SelectedIndex == 1;
        _transparent.IsEnabled = _format.SelectedIndex == 0;
        _background.IsEnabled = _format.SelectedIndex != 0 || _transparent.IsChecked != true;
        _size.Text = Localize.Text("Rendering preview…"); _timer.Stop(); _timer.Start();
    }
    private bool _rendering;
    internal async Task RenderPreview()
    {
        if (_rendering || _closed) return;
        _rendering = true; var revision = _revision; var options = Options();
        try
        {
            var encoded = await Task.Run(() => ExportImage.Encode(_document, options));
            if (_closed || revision != _revision) { encoded.Dispose(); return; }
            _encoded?.Dispose(); _encoded = encoded; _bitmap?.Dispose(); _bitmap = CanvasView.ToImage(encoded.Preview);
            _preview.Source = _bitmap; _size.Text = Localize.Format($"File size: {encoded.Bytes.Length / 1024.0:0.0} KB"); _export.IsEnabled = true;
        }
        catch (Exception error) { if (!_closed && revision == _revision) _size.Text = Localize.Text(error.Message); }
        finally { _rendering = false; if (!_closed && revision != _revision) { _timer.Stop(); _timer.Start(); } }
    }
    internal static async Task<(ExportOptions Options, byte[] Bytes)?> Ask(Window owner, CanvasDocument document, ExportFormat format)
    {
        var dialog = new ExportAsDialog(document, format); await dialog.ShowDialog(owner); return dialog.Result;
    }
}

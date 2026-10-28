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
    private readonly DocumentLease _lease;
    private readonly ComboBox _format = new()
    {
        ItemsSource = new[] { "PNG", "JPEG", "PDF", "TIFF", "EXR", "GIF" },
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
    private readonly ComboBox _colorSpace = new() { ItemsSource = new[] { "RGB", "CMYK", Localize.Text("Document Mode (TIFF)") }, SelectedIndex = 0, Height = 24, Width = 170 };
    private readonly ComboBox _metadata = new() { ItemsSource = new[] { "None", "Copyright", "Copyright and Contact", "All Except Camera Information", "All" }.Select(Localize.Text).ToArray(), SelectedIndex = 2, Height = 24, Width = 170 };
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
        _lease = new DocumentLease(document);
        _document = _lease.Document;
        Title = Localize.Text("Export As"); Width = 860; Height = 610; MinWidth = 660; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _width.Value = document.Width; _height.Value = document.Height; _format.SelectedIndex = (int)format;
        var controls = new StackPanel { Spacing = 9, Margin = new Thickness(18), Width = 230 };
        void Add(string label, Control control) { controls.Children.Add(new TextBlock { Text = Localize.Text(label), FontSize = 11.5, Foreground = Skin.LabelBrush }); controls.Children.Add(control); }
        _quality.Value = EditorPreferences.Current.ExportQuality;
        _transparent.IsChecked = EditorPreferences.Current.ExportTransparency;
        var currentFormat = new MenuItem { Header = ((string[])_format.ItemsSource!)[(int)format] + " ▾" };
        var more = new MenuItem { Header = Localize.Text("More") };
        foreach (var (label, value) in new[] { ("JPEG", ExportFormat.Jpeg), ("PNG", ExportFormat.Png), ("PDF", ExportFormat.Pdf), ("TIFF", ExportFormat.Tiff), ("EXR", ExportFormat.Exr), ("GIF", ExportFormat.Gif) })
        {
            var choice = new MenuItem { Header = label };
            choice.Click += (_, _) => { _format.SelectedIndex = (int)value; currentFormat.Header = label + " ▾"; };
            if (value is ExportFormat.Jpeg or ExportFormat.Png) currentFormat.Items.Add(choice); else more.Items.Add(choice);
        }
        currentFormat.Items.Add(more);
        var formatMenu = new Menu { Width = 170, Height = 30, Background = Skin.SurfaceControlBrush }; formatMenu.Items.Add(currentFormat);
        Add("Format", formatMenu); Add("Color Space", _colorSpace); Add("Width, pixels", _width); Add("Height, pixels", _height);
        controls.Children.Add(_linked); Add("Quality", _quality); controls.Children.Add(_transparent); Add("Background", _background);
        Add("Metadata", _metadata);
        controls.Children.Add(_size);
        if (document.Image.Depth > 8)
            controls.Children.Add(new TextBlock { Text = Localize.Text(Compositor.Core.Rendering.DocumentRenderer.SupportsDeepComposite(document)
                ? "PNG supports up to 16 bits. Use TIFF for 32-bit floating-point output or EXR for HDR highlights."
                : "This document uses compositing effects rendered at 8-bit precision. Original deep layer data is preserved in the project."), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Skin.SecondaryBrush });
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
        _colorSpace.SelectionChanged += (_, _) => Queue();
        _metadata.SelectionChanged += (_, _) => Queue();
        _format.SelectionChanged += (_, _) => Queue(); _quality.ValueChanged += (_, _) => Queue();
        _transparent.IsCheckedChanged += (_, _) => Queue(); _background.SelectionChanged += (_, _) => Queue();
        _timer.Tick += async (_, _) => { _timer.Stop(); await RenderPreview(); };
        Opened += (_, _) => Queue();
        Closed += (_, _) => { _closed = true; _revision++; _timer.Stop(); _bitmap?.Dispose(); _encoded?.Dispose(); if (!_rendering) _lease.Dispose(); };
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
        (int)(_quality.Value ?? 90), _transparent.IsChecked == true, _background.SelectedIndex == 1 ? SKColors.Black : SKColors.White, _format.SelectedIndex == 2 ? EditorPreferences.Current.PdfResolution : _document.Resolution, _colorSpace.SelectedIndex == 1 && _format.SelectedIndex is 1 or 2 or 3 ? _document.Image.CmykProfile ?? CmykColor.DefaultProfile : null, _colorSpace.SelectedIndex == 2 && _format.SelectedIndex == 3, _format.SelectedIndex is 2 or 4 ? MetadataPolicy.None : (MetadataPolicy)_metadata.SelectedIndex);
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
        _colorSpace.IsEnabled = _format.SelectedIndex is 1 or 2 or 3; if (!_colorSpace.IsEnabled || _colorSpace.SelectedIndex == 2 && _format.SelectedIndex != 3) _colorSpace.SelectedIndex = 0;
        _revision++; _export.IsEnabled = false; _quality.IsEnabled = _format.SelectedIndex == 1;
        _transparent.IsEnabled = _colorSpace.SelectedIndex == 0 && _format.SelectedIndex is 0 or 3 or 4 or 5;
        _background.IsEnabled = _format.SelectedIndex is not (0 or 3 or 4) || _transparent.IsChecked != true;
        _metadata.IsEnabled = _format.SelectedIndex is not (2 or 4);
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
        finally { _rendering = false; if (_closed) _lease.Dispose(); else if (revision != _revision) { _timer.Stop(); _timer.Start(); } }
    }
    internal static async Task<(ExportOptions Options, byte[] Bytes)?> Ask(Window owner, CanvasDocument document, ExportFormat format)
    {
        var dialog = new ExportAsDialog(document, format); await dialog.ShowDialog(owner); return dialog.Result;
    }
}

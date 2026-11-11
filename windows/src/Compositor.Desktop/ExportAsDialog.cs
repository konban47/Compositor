using System.Globalization;
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

/// <summary>Export As, laid out as Photoshop's: the sizes to export and the list on the left, the preview in the middle, the settings on the right.</summary>
internal sealed class ExportAsDialog : DialogWindow
{
    private static readonly ExportFormat[] Formats = [ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.Gif, ExportFormat.Pdf, ExportFormat.Tiff, ExportFormat.Exr];
    private static readonly double[] Scales = [.25, .5, .75, 1, 1.5, 2, 3, 4];
    private readonly CanvasDocument _document;
    private readonly DocumentLease _lease;
    private readonly ComboBox _format = new() { ItemsSource = new[] { "PNG", "JPG", "GIF", "PDF", "TIFF", "EXR" }, Width = 170, Tag = "export-format" };
    private readonly NumericUpDown _width = Number(1, 1, DocumentLimits.MaxSide), _height = Number(1, 1, DocumentLimits.MaxSide), _percent = Number(100, 1, 1600);
    private readonly NumericUpDown _canvasWidth = Number(1, 1, DocumentLimits.MaxSide), _canvasHeight = Number(1, 1, DocumentLimits.MaxSide);
    private readonly Slider _quality = new() { Minimum = 1, Maximum = 100, Value = 90, Width = 170, Tag = "export-quality" };
    private readonly TextBlock _qualityValue = new() { VerticalAlignment = VerticalAlignment.Center, Width = 28, TextAlignment = TextAlignment.Right };
    private readonly Control _qualityRow, _qualityScale, _pdfRow;
    private readonly ComboBox _pdfPreset = new() { Width = 200, Tag = "export-pdf-preset" };
    private readonly IReadOnlyList<PdfPreset> _pdfPresets = PdfPresets.All();
    private readonly CheckBox _transparent = new() { Content = Localize.Text("Transparency"), IsChecked = true, Tag = "export-transparency" };
    private readonly ComboBox _background = new() { ItemsSource = new[] { Localize.Text("White"), Localize.Text("Black") }, SelectedIndex = 0, Width = 150 };
    private readonly ComboBox _colorSpace = new() { ItemsSource = new[] { "RGB", "CMYK", Localize.Text("Document Mode (TIFF)") }, SelectedIndex = 0, Width = 150 };
    private readonly ComboBox _resample = new() { ItemsSource = new[] { "Nearest Neighbor", "Bilinear", "Bicubic", "Bicubic Smoother", "Bicubic Sharper" }.Select(Localize.Text).ToArray(), SelectedIndex = EditorPreferences.DefaultResample, Width = 170 };
    private readonly RadioButton _metaNone = new() { Content = Localize.Text("None"), GroupName = "export-metadata" }, _metaCopyright = new() { Content = Localize.Text("Copyright and Contact"), GroupName = "export-metadata", IsChecked = true };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform }, _before = new() { Stretch = Stretch.Uniform };
    private readonly Border _beforePane = new() { Background = Skin.Checker, IsVisible = false };
    private readonly TextBlock _size = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _itemDetails = new() { Foreground = Skin.SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Image _thumbnail = new() { Width = 60, Height = 36, Stretch = Stretch.Uniform };
    private readonly NumericUpDown _zoom = Number(100, 6, 1600);
    private readonly Button _export;
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly List<(ComboBox Size, TextBox Suffix, Control Row)> _scaleRows = [];
    private readonly Button[] _tabs = new Button[2];
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private ExportResult? _encoded;
    private WriteableBitmap? _bitmap, _original;
    private bool _changing, _closed, _rendering;
    private int _revision;
    internal (ExportOptions Options, byte[] Bytes)? Result { get; private set; }
    /// <summary>The further sizes asked for: each exports beside the main file with its suffix.</summary>
    internal IReadOnlyList<(double Scale, string Suffix)> ExtraSizes => [.. _scaleRows.Skip(1).Select(r => (Scales[Math.Max(0, r.Size.SelectedIndex)], r.Suffix.Text ?? ""))];

    internal ExportAsDialog(CanvasDocument document, ExportFormat format = ExportFormat.Png)
    {
        _lease = new DocumentLease(document);
        _document = _lease.Document;
        Title = Localize.Text("Export As"); Width = 1140; Height = 800; MinWidth = 960; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _width.Value = _canvasWidth.Value = document.Width; _height.Value = _canvasHeight.Value = document.Height; _format.SelectedIndex = Math.Max(0, Array.IndexOf(Formats, format));
        _quality.Value = EditorPreferences.Current.ExportQuality; _transparent.IsChecked = EditorPreferences.Current.ExportTransparency;

        Button Plain(Control content, string tip, Action action, double size = 24)
        {
            var button = new Button { Content = content, Width = size, Height = size, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(button, Localize.Text(tip)); button.Click += (_, _) => action(); return button;
        }
        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,30"), ColumnSpacing = 6, Children = { DialogKit.Label("Size") } };
        var suffixLabel = DialogKit.Label("Suffix"); Grid.SetColumn(suffixLabel, 1); labels.Children.Add(suffixLabel);
        var add = Plain(new InspectorGlyph("add") { Width = 18, Height = 18, IsHitTestVisible = false }, "Add a Scale", () => AddScale()); add.Tag = "export-add-scale"; Grid.SetColumn(add, 2); labels.Children.Add(add);
        AddScale();
        var scale = DialogKit.Group("Scale All", new StackPanel { Spacing = 6, Children = { labels, _rows } });
        var selectAll = new CheckBox { Content = Localize.Text("Select All"), IsChecked = true, IsEnabled = false };
        var check = new CheckBox { IsChecked = true, IsEnabled = false, MinWidth = 0 };
        var details = new StackPanel { Children = { new TextBlock { Text = Localize.Text("Export"), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, _itemDetails } };
        var item = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8, Children = { check, _thumbnail } }; Grid.SetColumn(_thumbnail, 1); Grid.SetColumn(details, 2); item.Children.Add(details);
        var list = new StackPanel { Children = { new Border { Padding = new Thickness(12, 6), Child = selectAll }, new Border { Padding = new Thickness(12, 8), Background = Skin.SurfaceDarkBrush, Child = item } } };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8, Children = { scale, new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), [Grid.RowProperty] = 1, Child = list } } };

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (label, index) in new[] { "Preview", "2-Up" }.Select((label, index) => (label, index)))
        {
            var tab = new Button { Content = Localize.Text(label), MinWidth = 72, Height = 30, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0), Tag = "export-view-" + label, HorizontalContentAlignment = HorizontalAlignment.Center };
            tab.Click += (_, _) => { _beforePane.IsVisible = index == 1; ShowTabs(index); }; _tabs[index] = tab; strip.Children.Add(tab);
        }
        ShowTabs(0);
        _beforePane.Child = _before;
        var pictures = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 4, Children = { _beforePane } };
        var surface = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Skin.Checker, Child = _preview };
        _scroll = new ScrollViewer { Content = surface, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Background = Skin.SurfaceDarkBrush }; Grid.SetColumn(_scroll, 1); pictures.Children.Add(_scroll);
        _zoom.Width = 64; _zoom.Height = 24; _zoom.MinHeight = 0; _zoom.ValueChanged += (_, _) => ShowZoom();
        var zoom = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                Plain(new TextBlock { Text = "−", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "Zoom Out", () => _zoom.Value = Math.Max(6, (_zoom.Value ?? 100) / 1.25m)),
                _zoom, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center },
                Plain(new TextBlock { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "Zoom In", () => _zoom.Value = Math.Min(1600, (_zoom.Value ?? 100) * 1.25m)),
            },
        };
        var middle = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Children = { strip, new Border { [Grid.RowProperty] = 1, Child = pictures }, new Border { [Grid.RowProperty] = 2, Child = zoom } } };

        var low = new TextBlock { Text = Localize.Text("Low"), Foreground = Skin.SecondaryBrush }; var high = new TextBlock { Text = Localize.Text("High"), Foreground = Skin.SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Right };
        _qualityRow = Row("Quality", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _quality, _qualityValue } });
        _qualityScale = new Grid { Margin = new Thickness(92, 0, 36, 0), ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { low, new Border { [Grid.ColumnProperty] = 1, Child = high } } };
        _pdfPreset.ItemsSource = _pdfPresets.Select(p => p.BuiltIn ? Localize.Text(p.Name) : p.Name).ToArray(); _pdfPreset.SelectedIndex = 0;
        _pdfPreset.SelectionChanged += (_, _) => { if (SelectedPdf is { ColorConversion: PdfColorConversion.WorkingCmyk }) _colorSpace.SelectedIndex = 1; Queue(); };
        _pdfRow = Row("PDF Preset", _pdfPreset);
        var settings = new StackPanel { Spacing = 6, Children = { Row("Format", _format), _qualityRow, _qualityScale, _pdfRow } };
        var imageSize = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Row("Width", Field(_width, true)), Row("Height", Field(_height, true)),
                Row("Scale", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _percent, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center } } }),
                Row("Resample", _resample),
            },
        };
        _percent.Width = 90; _percent.Height = 26; _percent.MinHeight = 0;
        var resetCanvas = FormDialog.ActionButton("Reset", () => { _canvasWidth.Value = _width.Value; _canvasHeight.Value = _height.Value; }); resetCanvas.Margin = default; resetCanvas.MinHeight = 0; resetCanvas.Height = 24; resetCanvas.HorizontalAlignment = HorizontalAlignment.Right;
        var canvasSize = new StackPanel { Spacing = 6, Children = { Row("Width", Field(_canvasWidth, true)), Row("Height", Field(_canvasHeight, true)), resetCanvas } };
        var metadata = new StackPanel { Spacing = 4, Children = { _metaNone, _metaCopyright } };
        var color = new StackPanel { Spacing = 6, Children = { Row("Mode", _colorSpace), _transparent, Row("Background", _background) } };
        var column = new StackPanel { Spacing = 4, Children = { settings, DialogKit.Group("Image Size", imageSize), DialogKit.Group("Canvas Size", canvasSize), DialogKit.Group("Metadata", metadata), DialogKit.Group("Color Space", color) } };
        if (document.Image.Depth > 8)
            column.Children.Add(new TextBlock { Text = Localize.Text(Compositor.Core.Rendering.DocumentRenderer.SupportsDeepComposite(document)
                ? "PNG supports up to 16 bits. Use TIFF for 32-bit floating-point output or EXR for HDR highlights."
                : "This document uses compositing effects rendered at 8-bit precision. Original deep layer data is preserved in the project."), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Skin.SecondaryBrush });
        var cancel = FormDialog.ActionButton("Cancel", Close); cancel.IsCancel = true; cancel.Margin = default;
        _export = FormDialog.ActionButton("Export", () => { if (_encoded is null) return; Result = (Options(), _encoded.Bytes); Close(); }); _export.IsDefault = true; _export.IsEnabled = false; _export.Margin = default;
        _export.Background = _export.BorderBrush = Skin.AccentBrush; _export.Foreground = Brushes.White;
        var version = new TextBlock { Text = "v" + typeof(ExportAsDialog).Assembly.GetName().Version?.ToString(3), Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { version, _size } };
        Grid.SetColumn(_size, 1); Grid.SetColumn(cancel, 2); Grid.SetColumn(_export, 3); footer.Children.Add(cancel); footer.Children.Add(_export);

        var settingsScroll = new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Tag = "export-settings" };
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { settingsScroll, new Border { [Grid.RowProperty] = 1, Child = footer } } };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*,340"), ColumnSpacing = 12, Margin = new Thickness(12, 8, 12, 12), Children = { left } };
        Grid.SetColumn(middle, 1); layout.Children.Add(middle); Grid.SetColumn(right, 2); layout.Children.Add(right);
        Content = layout;

        _width.ValueChanged += (_, _) => DimensionChanged(true);
        _height.ValueChanged += (_, _) => DimensionChanged(false);
        _percent.ValueChanged += (_, _) => { if (_changing || _percent.Value is not { } value) return; _changing = true; _width.Value = Fit(_document.Width * value / 100); _height.Value = Fit(_document.Height * value / 100); FollowCanvas(); _changing = false; Queue(); };
        _canvasWidth.ValueChanged += (_, _) => Queue(); _canvasHeight.ValueChanged += (_, _) => Queue();
        _colorSpace.SelectionChanged += (_, _) => Queue(); _resample.SelectionChanged += (_, _) => Queue();
        _metaNone.IsCheckedChanged += (_, _) => Queue();
        _format.SelectionChanged += (_, _) => Queue(); _quality.ValueChanged += (_, _) => { _qualityValue.Text = ((int)_quality.Value).ToString(CultureInfo.InvariantCulture); Queue(); };
        _qualityValue.Text = ((int)_quality.Value).ToString(CultureInfo.InvariantCulture);
        _transparent.IsCheckedChanged += (_, _) => Queue(); _background.SelectionChanged += (_, _) => Queue();
        _timer.Tick += async (_, _) => { _timer.Stop(); await RenderPreview(); };
        Opened += (_, _) => Queue();
        Closed += (_, _) => { _closed = true; _revision++; _timer.Stop(); _bitmap?.Dispose(); _original?.Dispose(); _encoded?.Dispose(); if (!_rendering) _lease.Dispose(); };
    }
    private void ShowTabs(int chosen) { for (var i = 0; i < _tabs.Length; i++) if (_tabs[i] is { } tab) { tab.Background = i == chosen ? Skin.SurfaceControlBrush : Brushes.Transparent; tab.Foreground = i == chosen ? Skin.LabelBrush : Skin.SecondaryBrush; } }
    private void AddScale()
    {
        var size = new ComboBox { ItemsSource = Scales.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture) + "x").ToArray(), SelectedIndex = 3, Tag = "export-scale" };
        var suffix = DialogKit.Line("export-suffix");
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,30"), ColumnSpacing = 6, Children = { size } }; Grid.SetColumn(suffix, 1); line.Children.Add(suffix);
        var delete = new Button { Content = new InspectorGlyph("delete") { Width = 18, Height = 18, IsHitTestVisible = false }, Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0), IsEnabled = _scaleRows.Count > 0 };
        ToolTip.SetTip(delete, Localize.Text("Delete")); Grid.SetColumn(delete, 2); line.Children.Add(delete);
        (ComboBox, TextBox, Control) row = (size, suffix, line);
        delete.Click += (_, _) => { _scaleRows.Remove(row); _rows.Children.Remove(line); };
        if (_scaleRows.Count > 0) { size.SelectedIndex = Math.Min(Scales.Length - 1, 3 + _scaleRows.Count); suffix.Text = "@" + Scales[size.SelectedIndex].ToString("0.##", CultureInfo.InvariantCulture) + "x"; }
        _scaleRows.Add(row); _rows.Children.Add(line);
    }
    private static decimal Fit(decimal value) => Math.Clamp(Math.Round(value), 1, DocumentLimits.MaxSide);
    private void FollowCanvas() { _canvasWidth.Value = _width.Value; _canvasHeight.Value = _height.Value; }
    private static Control Row(string label, Control control) { control.HorizontalAlignment = HorizontalAlignment.Left; return DialogKit.Row(label, control, 84); }
    private static Control Field(NumericUpDown field, bool pixels)
    {
        field.Width = 110; field.Height = 26; field.MinHeight = 0;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { field, new TextBlock { Text = Localize.Text(pixels ? "Pixels" : "%"), VerticalAlignment = VerticalAlignment.Center, Foreground = Skin.SecondaryBrush } } };
    }
    private static NumericUpDown Number(int value, int least, int most) => new() { Value = value, Minimum = least, Maximum = most, Increment = 1, FormatString = "0", ShowButtonSpinner = false, ClipValueToMinMax = true };

    private ExportFormat Format => Formats[Math.Max(0, _format.SelectedIndex)];
    private ExportOptions Options()
    {
        var pdf = Format == ExportFormat.Pdf; var noMetadata = Format is ExportFormat.Pdf or ExportFormat.Exr;
        int width = (int)(_width.Value ?? 1), height = (int)(_height.Value ?? 1), canvasWidth = (int)(_canvasWidth.Value ?? width), canvasHeight = (int)(_canvasHeight.Value ?? height);
        return new(Format, width, height, (int)_quality.Value, _transparent.IsChecked == true, _background.SelectedIndex == 1 ? SKColors.Black : SKColors.White, pdf ? EditorPreferences.Current.PdfResolution : _document.Resolution,
            _colorSpace.SelectedIndex == 1 && Format is ExportFormat.Jpeg or ExportFormat.Pdf or ExportFormat.Tiff ? _document.Image.CmykProfile ?? CmykColor.DefaultProfile : null,
            _colorSpace.SelectedIndex == 2 && Format == ExportFormat.Tiff, noMetadata || _metaNone.IsChecked == true ? MetadataPolicy.None : MetadataPolicy.CopyrightAndContact,
            canvasWidth == width && canvasHeight == height ? 0 : canvasWidth, canvasWidth == width && canvasHeight == height ? 0 : canvasHeight, _resample.SelectedIndex, pdf ? SelectedPdf : null);
    }
    private PdfPreset? SelectedPdf => _pdfPresets.ElementAtOrDefault(_pdfPreset.SelectedIndex);
    private void DimensionChanged(bool width)
    {
        if (_changing) return;
        _changing = true;
        if (width) _height.Value = Fit((_width.Value ?? 1) * _document.Height / _document.Width);
        else _width.Value = Fit((_height.Value ?? 1) * _document.Width / _document.Height);
        _percent.Value = Math.Clamp(Math.Round((_width.Value ?? 1) * 100 / _document.Width), 1, 1600); FollowCanvas();
        _changing = false; Queue();
    }
    private void Queue()
    {
        if (_closed || _export is null) return;
        var format = Format;
        _colorSpace.IsEnabled = format is ExportFormat.Jpeg or ExportFormat.Pdf or ExportFormat.Tiff; if (!_colorSpace.IsEnabled || _colorSpace.SelectedIndex == 2 && format != ExportFormat.Tiff) _colorSpace.SelectedIndex = 0;
        _revision++; _export.IsEnabled = false; _qualityRow.IsVisible = _qualityScale.IsVisible = format == ExportFormat.Jpeg; _pdfRow.IsVisible = format == ExportFormat.Pdf;
        _transparent.IsEnabled = _colorSpace.SelectedIndex == 0 && format is ExportFormat.Png or ExportFormat.Tiff or ExportFormat.Exr or ExportFormat.Gif;
        _background.IsEnabled = format is not (ExportFormat.Png or ExportFormat.Tiff or ExportFormat.Exr) || _transparent.IsChecked != true;
        _metaNone.IsEnabled = _metaCopyright.IsEnabled = format is not (ExportFormat.Pdf or ExportFormat.Exr);
        _size.Text = Localize.Text("Rendering preview…"); _timer.Stop(); _timer.Start();
    }
    private void ShowZoom()
    {
        if (_encoded is null) return;
        var zoom = (double)(_zoom.Value ?? 100) / 100;
        _preview.Width = _encoded.Preview.Width * zoom; _preview.Height = _encoded.Preview.Height * zoom;
        _before.Width = Math.Min(360, _document.Width * zoom); _before.Height = _before.Width * _document.Height / _document.Width;
    }
    internal async Task RenderPreview()
    {
        if (_rendering || _closed) return;
        _rendering = true; var revision = _revision; var options = Options();
        try
        {
            var encoded = await Task.Run(() => ExportImage.Encode(_document, options));
            if (_closed || revision != _revision) { encoded.Dispose(); return; }
            _encoded?.Dispose(); _encoded = encoded; _bitmap?.Dispose(); _bitmap = CanvasView.ToImage(encoded.Preview);
            _preview.Source = _bitmap; _thumbnail.Source = _bitmap;
            if (_original is null) { using var flat = await Task.Run(() => Compositor.Core.Rendering.DocumentRenderer.Render(_document)); if (!_closed) { _original = CanvasView.ToImage(flat); _before.Source = _original; } }
            ShowZoom();
            var text = (((string[])_format.ItemsSource!)[Math.Max(0, _format.SelectedIndex)]) + $"   {encoded.Preview.Width} x {encoded.Preview.Height}   " + (encoded.Bytes.Length < 1024 ? encoded.Bytes.Length + " " + Localize.Text("bytes") : (encoded.Bytes.Length / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB");
            _itemDetails.Text = text; _size.Text = ""; _export.IsEnabled = true;
        }
        catch (Exception error) { if (!_closed && revision == _revision) _size.Text = Localize.Text(error.Message); }
        finally { _rendering = false; if (_closed) _lease.Dispose(); else if (revision != _revision) { _timer.Stop(); _timer.Start(); } }
    }
    internal static async Task<(ExportOptions Options, byte[] Bytes, IReadOnlyList<(double Scale, string Suffix)> Extras)?> Ask(Window owner, CanvasDocument document, ExportFormat format)
    {
        var dialog = new ExportAsDialog(document, format); await dialog.ShowDialog(owner);
        return dialog.Result is { } result ? (result.Options, result.Bytes, dialog.ExtraSizes) : null;
    }
}

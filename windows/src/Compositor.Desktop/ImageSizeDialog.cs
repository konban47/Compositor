using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>What Image Size asks for: the new pixels and resolution, how to resample, whether layer styles scale, or that the
/// picture is to go to Generative Upscale instead.</summary>
internal sealed record ImageSizeResult(int Width, int Height, double Resolution, LayerSampling Sampling, bool ScaleStyles, bool Upscale = false);

/// <summary>
/// Image ▸ Image Size, laid out as Photoshop's: the picture on the left; on the right Image Size and Dimensions with the
/// ⚙ menu, Fit To with its presets, Width, Height and Resolution with their units and the chain that keeps them in
/// proportion, and Resample with its eight methods (Alt+1…8); OK and Cancel along the bottom. Without Resample the pixels
/// stay and only the resolution and print size change, as in Photoshop.
/// </summary>
internal sealed class ImageSizeDialog : DialogWindow
{
    internal static readonly string[] Units = ["Percent", "Pixels", "Inches", "Centimeters", "Millimeters", "Points", "Picas", "Columns"];
    private static readonly double[] PerInch = [0, 0, 1, 2.54, 25.4, 72, 6, 0];
    internal static readonly (string Name, LayerSampling Sampling)[] Methods =
    [
        ("Automatic", LayerSampling.HighQuality), ("Preserve Details (enlargement)", LayerSampling.HighQuality), ("Preserve Details 2.0", LayerSampling.HighQuality),
        ("Bicubic Smoother (enlargement)", LayerSampling.HighQuality), ("Bicubic Sharper (reduction)", LayerSampling.HighQuality), ("Bicubic (smooth gradients)", LayerSampling.HighQuality),
        ("Nearest Neighbor (hard edges)", LayerSampling.Nearest), ("Bilinear", LayerSampling.Smooth),
    ];
    private static int _widthUnit = 3, _heightUnit = 3, _resolutionUnit, _dimensionUnit = 1, _method;
    private static bool _scaleStyles = true;
    private readonly int _startWidth, _startHeight;
    private readonly double _startResolution;
    private readonly int _channels, _bytes;
    private double _width, _height, _resolution;
    private bool _syncing;
    private ImageSizeResult? _result;
    internal ValueBox WidthBox { get; } = new(0, 1e9, 0, "0.##", tag: "Width") { Width = 104 };
    internal ValueBox HeightBox { get; } = new(0, 1e9, 0, "0.##", tag: "Height") { Width = 104 };
    internal ValueBox ResolutionBox { get; } = new(1, 9600, 72, "0.##", tag: "Resolution") { Width = 104 };
    internal ComboBox WidthUnit { get; } = new() { Width = 196, Tag = "Width unit" };
    internal ComboBox HeightUnit { get; } = new() { Width = 196, Tag = "Height unit" };
    internal ComboBox ResolutionUnit { get; } = new() { Width = 196, Tag = "Resolution unit" };
    internal CheckBox Resample { get; } = new() { Tag = "Resample", IsChecked = true };
    internal ComboBox Method { get; } = new() { Width = 314, Tag = "Resample method" };
    internal ComboBox FitTo { get; } = new() { Width = 314, Tag = "Fit To" };
    internal ToggleButton Constrain { get; } = new() { Width = 26, Height = 30, Padding = default, IsChecked = true, Tag = "Constrain Proportions", Background = Brushes.Transparent, BorderThickness = default };
    private readonly TextBlock _size = new() { Tag = "image size", VerticalAlignment = VerticalAlignment.Center }, _dimensions = new() { Tag = "dimensions", VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(string Name, double Width, double Height, double Resolution, int Unit, string? Path)> _fits = [];

    private ImageSizeDialog(int width, int height, double resolution, ImageSettings image, Func<SKBitmap?> picture)
    {
        Title = Localize.Text("Image Size"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _startWidth = width; _width = width; _startHeight = height; _height = height; _startResolution = _resolution = resolution;
        _channels = image.Mode switch { ImageMode.Grayscale or ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone => 1, ImageMode.CMYK => 4, _ => 3 };
        _bytes = Math.Max(1, image.Depth / 8);

        // The picture, fitted into the left of the dialog.
        var preview = new Image { Width = 410, Height = 445, Stretch = Stretch.Uniform };
        using (var small = picture()) if (small is not null) preview.Source = Eyedroppers.Thumbnail(small);
        var previewFrame = new Border { Width = 412, Height = 447, Background = Skin.SurfaceDarkBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = preview };

        // The ⚙ menu: Scale Styles.
        var scale = new MenuItem { Header = Localize.Text("Scale Styles"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = _scaleStyles, Tag = "Scale Styles" };
        scale.Click += (_, _) => { _scaleStyles = !_scaleStyles; scale.IsChecked = _scaleStyles; };
        var gearMenu = new ContextMenu { Items = { scale } };
        var gear = new Button { Content = "⚙▾", Width = 36, Height = 28, Padding = default, Background = Brushes.Transparent, BorderThickness = default, Tag = "image size menu", HorizontalContentAlignment = HorizontalAlignment.Center };
        gear.Click += (_, _) => gearMenu.Open(gear);

        // Dimensions, shown in the unit its ⌄ chooses.
        var dimensionMenu = new ContextMenu();
        foreach (var (unit, index) in Units.Take(7).Select((u, i) => (u, i)))
        {
            var item = new MenuItem { Header = Localize.Text(unit), ToggleType = MenuItemToggleType.Radio, Tag = unit };
            item.Click += (_, _) => { _dimensionUnit = index; Refresh(); }; dimensionMenu.Items.Add(item);
        }
        var dimensionButton = new Button { Content = "⌄", Width = 24, Height = 24, Padding = default, Tag = "dimensions unit", HorizontalContentAlignment = HorizontalAlignment.Center };
        dimensionButton.Click += (_, _) => { foreach (var item in dimensionMenu.Items.OfType<MenuItem>()) item.IsChecked = Equals(item.Tag, Units[_dimensionUnit]); dimensionMenu.Open(dimensionButton); };

        // Fit To: Original Size, Auto Resolution…, the screen and print sizes, the presets saved here, and Custom.
        LoadFits();
        FitTo.SelectionChanged += async (_, _) => await Fit();

        WidthUnit.ItemsSource = Units.Select(UnitName).ToArray(); HeightUnit.ItemsSource = Units.Take(7).Select(UnitName).ToArray();
        ResolutionUnit.ItemsSource = new[] { "Pixels/Inch", "Pixels/Centimeter" }.Select(Localize.Text).ToArray();
        WidthUnit.SelectedIndex = _widthUnit; HeightUnit.SelectedIndex = _heightUnit; ResolutionUnit.SelectedIndex = _resolutionUnit;
        Method.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((index, _) => new Grid
        {
            ColumnDefinitions = new("*,Auto"), MinWidth = 280,
            Children = { new TextBlock { Text = Localize.Text(Methods[index].Name) }, new TextBlock { Text = $"Alt+{index + 1}", [Grid.ColumnProperty] = 1, Margin = new Thickness(18, 0, 0, 0) } },
        });
        Method.SelectionBoxItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((index, _) => new TextBlock { Text = Localize.Text(Methods[index].Name) });
        Method.ItemsSource = Enumerable.Range(0, Methods.Length).ToArray(); Method.SelectedIndex = _method;
        ToolTip.SetTip(Method, Localize.Text("Each method resamples with the nearest of this app's three kernels: high-quality bicubic, bilinear, or nearest neighbor."));

        WidthUnit.SelectionChanged += (_, _) => { if (_syncing) return; _widthUnit = Math.Max(0, WidthUnit.SelectedIndex); if (Constrain.IsChecked == true && WidthUnit.SelectedIndex < 7) { _syncing = true; HeightUnit.SelectedIndex = WidthUnit.SelectedIndex; _heightUnit = _widthUnit; _syncing = false; } Refresh(); };
        HeightUnit.SelectionChanged += (_, _) => { if (_syncing) return; _heightUnit = Math.Max(0, HeightUnit.SelectedIndex); if (Constrain.IsChecked == true) { _syncing = true; WidthUnit.SelectedIndex = HeightUnit.SelectedIndex; _widthUnit = _heightUnit; _syncing = false; } Refresh(); };
        ResolutionUnit.SelectionChanged += (_, _) => { if (_syncing) return; _resolutionUnit = Math.Max(0, ResolutionUnit.SelectedIndex); Refresh(); };
        WidthBox.ValueChanged += () => { if (!_syncing) Typed(true, WidthBox.Value); };
        HeightBox.ValueChanged += () => { if (!_syncing) Typed(false, HeightBox.Value); };
        ResolutionBox.ValueChanged += () => { if (_syncing) return; SetResolution(ResolutionBox.Value * (_resolutionUnit == 1 ? 2.54 : 1)); };
        Resample.IsCheckedChanged += (_, _) => { if (Resample.IsChecked != true) Constrain.IsChecked = true; Refresh(); };
        Method.SelectionChanged += (_, _) => _method = Math.Max(0, Method.SelectedIndex);
        Constrain.Click += (_, _) => { if (Resample.IsChecked != true) Constrain.IsChecked = true; Refresh(); };
        Constrain.Content = new ChainGlyph(Constrain); Constrain.IsCheckedChanged += (_, _) => (Constrain.Content as Control)?.InvalidateVisual();
        ToolTip.SetTip(Constrain, Localize.Text("Constrain aspect ratio"));
        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Alt && e.Key is >= Key.D1 and <= Key.D8) { Resample.IsChecked = true; Method.SelectedIndex = e.Key - Key.D1; e.Handled = true; }
        };
        LayerDialogControls.Key(this, 'D', WidthBox); LayerDialogControls.Key(this, 'G', HeightBox); LayerDialogControls.Key(this, 'R', ResolutionBox); LayerDialogControls.Key(this, 'S', Resample);

        // The right side: captions right-aligned against their fields, as in the screenshots.
        TextBlock Right(string text, char? key = null) => new() { Text = Localize.Text(text) + (key is { } k ? $"({k})" : "") + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var form = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 12 };
        void Put(Control c, int row, int column, int span = 1) { while (form.RowDefinitions.Count <= row) form.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Grid.SetRow(c, row); Grid.SetColumn(c, column); Grid.SetColumnSpan(c, span); c.VerticalAlignment = VerticalAlignment.Center; form.Children.Add(c); }
        var sizeRow = new Grid { ColumnDefinitions = new("*,Auto"), Children = { _size } }; Grid.SetColumn(gear, 1); sizeRow.Children.Add(gear);
        Put(Right("Image Size"), 0, 1); Put(sizeRow, 0, 2, 2);
        Put(Right("Dimensions"), 1, 1); Put(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { dimensionButton, _dimensions } }, 1, 2, 2);
        Put(Right("Fit To"), 2, 1); Put(FitTo, 2, 2, 2);
        Put(Right("Width", 'D'), 3, 1); Put(WidthBox, 3, 2); Put(WidthUnit, 3, 3);
        Put(Right("Height", 'G'), 4, 1); Put(HeightBox, 4, 2); Put(HeightUnit, 4, 3);
        Put(Right("Resolution", 'R'), 5, 1); Put(ResolutionBox, 5, 2); Put(ResolutionUnit, 5, 3);
        // The chain and its bracket join Width and Height (and Resolution too when nothing is resampled).
        var bracket = new Canvas { Width = 40, Tag = "constrain bracket" };
        var link = new Grid { Children = { bracket, Constrain } }; Constrain.HorizontalAlignment = HorizontalAlignment.Left; Constrain.Margin = new Thickness(-2, 0, 0, 0);
        Put(link, 3, 0); Grid.SetRowSpan(link, 3); link.VerticalAlignment = VerticalAlignment.Stretch;
        bracket.SizeChanged += (_, _) => Bracket(bracket);
        Resample.Content = new TextBlock { Text = AdjustmentKit.Word("Resample", "重新采样") + "(S):" };
        Put(Resample, 6, 1); Resample.HorizontalAlignment = HorizontalAlignment.Right; Put(Method, 6, 2, 2);
        var upscale = new TextBlock { Text = Localize.Text("Open in Generative Upscale…"), Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xA0, 0xF0)), TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand), Tag = "generative upscale" };
        upscale.PointerPressed += (_, _) => { _result = new ImageSizeResult(_startWidth, _startHeight, _startResolution, LayerSampling.HighQuality, _scaleStyles, true); Close(); };
        var right = new StackPanel
        {
            Width = 520, Spacing = 4, Margin = new Thickness(26, 0, 0, 0),
            Children = { form, new StackPanel { Margin = new Thickness(0, 34, 0, 0), Spacing = 4, Children = { new TextBlock { Text = Localize.Text("Create a new document that is larger and holds more detail") }, upscale } } },
        };
        var ok = FormDialog.ActionButton("OK", Accept); ok.IsDefault = true; ok.Width = 236; ok.Tag = "OK";
        var cancel = FormDialog.ActionButton("Cancel", Close); cancel.IsCancel = true; cancel.Width = 236; cancel.Tag = "Cancel";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0), Children = { ok, cancel } };
        var rightColumn = new Grid { RowDefinitions = new("*,Auto"), Children = { right } }; Grid.SetRow(buttons, 1); rightColumn.Children.Add(buttons);
        var root = new Grid { ColumnDefinitions = new("Auto,*"), Margin = new Thickness(16), Children = { previewFrame } }; Grid.SetColumn(rightColumn, 1); root.Children.Add(rightColumn);
        Content = root;
        Refresh();
    }

    /// <summary>The bracket from the chain to Width and Height, reaching Resolution as well when Resample is off.</summary>
    private void Bracket(Canvas bracket)
    {
        bracket.Children.Clear(); var rows = Resample.IsChecked == true ? 2 : 3; var rowHeight = (bracket.Bounds.Height + 12) / 3;
        var pen = Skin.SecondaryBrush; var top = rowHeight / 2 - 6; var bottom = rowHeight * (rows - .5) - 6;
        bracket.Children.Add(new Line { StartPoint = new Point(14, top), EndPoint = new Point(38, top), Stroke = pen, StrokeThickness = 2 });
        bracket.Children.Add(new Line { StartPoint = new Point(14, top), EndPoint = new Point(14, bottom), Stroke = pen, StrokeThickness = 2 });
        bracket.Children.Add(new Line { StartPoint = new Point(14, bottom), EndPoint = new Point(38, bottom), Stroke = pen, StrokeThickness = 2 });
        if (rows == 3) bracket.Children.Add(new Line { StartPoint = new Point(14, rowHeight * 1.5 - 6), EndPoint = new Point(38, rowHeight * 1.5 - 6), Stroke = pen, StrokeThickness = 2 });
        Constrain.Margin = new Thickness(2, (top + bottom) / 2 - 15, 0, 0); Constrain.VerticalAlignment = VerticalAlignment.Top;
    }

    private static string UnitName(string unit) => unit == "Columns" ? AdjustmentKit.Word("Columns", "列") : Localize.Text(unit);
    private static double Inches(double value, int unit) => unit == 7
        ? value * ColumnInches() + Math.Max(0, value - 1) * GutterInches() : value / PerInch[unit];
    private static double FromInches(double inches, int unit) => unit == 7
        ? (inches + GutterInches()) / Math.Max(1e-6, ColumnInches() + GutterInches()) : inches * PerInch[unit];
    private static double ColumnInches() => EditorPreferences.Current.ColumnWidth / new[] { 1, 2.54, 25.4, 72, 6 }[Math.Clamp(EditorPreferences.Current.ColumnUnit, 0, 4)];
    private static double GutterInches() => EditorPreferences.Current.ColumnGutter / new[] { 1, 2.54, 25.4, 72, 6 }[Math.Clamp(EditorPreferences.Current.GutterUnit, 0, 4)];

    /// <summary>A size in pixels shown in a unit.</summary>
    private double Show(double pixels, double start, int unit) => unit switch { 0 => pixels / start * 100, 1 => pixels, _ => FromInches(pixels / _resolution, unit) };

    /// <summary>A width or height typed in its unit: with Resample the pixels change; without it the resolution does.</summary>
    private void Typed(bool width, double value)
    {
        if (value <= 0) { Refresh(); return; }
        var unit = width ? _widthUnit : _heightUnit; var start = width ? _startWidth : _startHeight; var current = width ? _width : _height;
        if (Resample.IsChecked == true)
        {
            var pixels = unit switch { 0 => start * value / 100, 1 => value, _ => Inches(value, unit) * _resolution };
            var ratio = pixels / Math.Max(1e-6, current);
            if (width) { _width = pixels; if (Constrain.IsChecked == true) _height *= ratio; }
            else { _height = pixels; if (Constrain.IsChecked == true) _width *= ratio; }
        }
        else
        {
            // Without resampling the pixels stay: a new print size is a new resolution.
            var inches = unit == 0 ? start / _startResolution * value / 100 : Inches(value, unit);
            if (inches > 0) _resolution = Math.Clamp(current / inches, 1, 9600);
        }
        MarkCustom(); Refresh();
    }

    private void SetResolution(double resolution)
    {
        if (resolution is < 1 or > 9600) { Refresh(); return; }
        if (Resample.IsChecked == true) { _width = _width / _resolution * resolution; _height = _height / _resolution * resolution; }
        _resolution = resolution; MarkCustom(); Refresh();
    }

    private void MarkCustom() { _syncing = true; FitTo.SelectedIndex = _fits.Count - 1; _syncing = false; }

    private void Refresh()
    {
        _syncing = true;
        var units = Units.Select(UnitName).ToArray();
        // Pixels cannot be typed when nothing is resampled; Photoshop grays the entry.
        WidthUnit.ItemsSource = units.Select((u, i) => (object)new ComboBoxItem { Content = u, IsEnabled = i != 1 || Resample.IsChecked == true }).ToArray();
        HeightUnit.ItemsSource = units.Take(7).Select((u, i) => (object)new ComboBoxItem { Content = u, IsEnabled = i != 1 || Resample.IsChecked == true }).ToArray();
        if (Resample.IsChecked != true && _widthUnit == 1) _widthUnit = _heightUnit = 3;
        WidthUnit.SelectedIndex = _widthUnit; HeightUnit.SelectedIndex = _heightUnit;
        WidthBox.Value = Math.Round(Show(_width, _startWidth, _widthUnit), 2); HeightBox.Value = Math.Round(Show(_height, _startHeight, _heightUnit), 2);
        ResolutionBox.Value = Math.Round(_resolution / (_resolutionUnit == 1 ? 2.54 : 1), 2);
        Method.IsEnabled = Resample.IsChecked == true;
        var bytes = Math.Round(_width) * Math.Round(_height) * _channels * _bytes; var before = (double)_startWidth * _startHeight * _channels * _bytes;
        string Megabytes(double b) => b >= 1024 * 1024 * 1024 ? (b / 1024 / 1024 / 1024).ToString("0.0G", CultureInfo.InvariantCulture) : b >= 1024 * 1024 ? (b / 1024 / 1024).ToString("0.0M", CultureInfo.InvariantCulture) : (b / 1024).ToString("0.0K", CultureInfo.InvariantCulture);
        _size.Text = Megabytes(bytes) + (Math.Abs(bytes - before) > .5 ? " (" + Localize.Text("was") + " " + Megabytes(before) + ")" : "");
        var unitName = Localize.Text(Units[_dimensionUnit]);
        string Dimension(double pixels, int start) => Show(pixels, start, _dimensionUnit).ToString(_dimensionUnit == 1 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + (_dimensionUnit == 0 ? "%" : unitName);
        _dimensions.Text = $"{Dimension(Math.Round(_width), _startWidth)}  ×  {Dimension(Math.Round(_height), _startHeight)}";
        if (Content is Grid root && root.GetLogicalDescendantsOfTag("constrain bracket") is Canvas bracket) Bracket(bracket);
        _syncing = false;
    }

    // ------------------------------------------------------------------------------------------------ Fit To

    private static string PresetFolder => System.IO.Path.Combine(AppPaths.SettingsDirectory, "Image Size Presets");
    private void LoadFits()
    {
        _fits.Clear();
        _fits.Add(("Original Size", 0, 0, 0, -1, null)); _fits.Add(("Auto Resolution…", 0, 0, 0, -2, null)); _fits.Add(("", 0, 0, 0, -9, null));
        foreach (var (w, h, r) in new[] { (960.0, 640.0, 144.0), (1024, 768, 72), (1136, 640, 144), (1366, 768, 72) }) _fits.Add(($"{w} x {h} {Localize.Text("Pixels")} {r} ppi", w, h, r, 1, null));
        _fits.Add(("", 0, 0, 0, -9, null));
        _fits.Add(("A4 210 x 297 " + Localize.Text("Millimeters") + " 300 dpi", 210, 297, 300, 4, null)); _fits.Add(("A6 105 x 148 " + Localize.Text("Millimeters") + " 300 dpi", 105, 148, 300, 4, null));
        _fits.Add((Localize.Text("Legal") + " 8.5 x 14 " + Localize.Text("Inches") + " 300 dpi", 8.5, 14, 300, 2, null)); _fits.Add((Localize.Text("Letter") + " 8.5 x 11 " + Localize.Text("Inches") + " 300 dpi", 8.5, 11, 300, 2, null));
        _fits.Add(("", 0, 0, 0, -9, null));
        foreach (var (w, h) in new[] { (4.0, 6.0), (5, 7), (8, 10), (11, 14) }) _fits.Add(($"{w} x {h} {Localize.Text("Inches")} 300 dpi", w, h, 300, 2, null));
        try
        {
            if (Directory.Exists(PresetFolder))
                foreach (var path in Directory.GetFiles(PresetFolder, "*.json").Take(100))
                    if (PresetBar.Read(path) is { } v && v.TryGetValue("Width", out var w) && v.TryGetValue("Height", out var h) && v.TryGetValue("Resolution", out var r))
                        _fits.Add((System.IO.Path.GetFileNameWithoutExtension(path), w, h, r, (int)v.GetValueOrDefault("Unit", 1), path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _fits.Add(("", 0, 0, 0, -9, null)); _fits.Add(("Load Preset…", 0, 0, 0, -3, null)); _fits.Add(("Save Preset…", 0, 0, 0, -4, null)); _fits.Add(("Delete Preset…", 0, 0, 0, -5, null));
        _fits.Add(("", 0, 0, 0, -9, null)); _fits.Add(("Custom", 0, 0, 0, -6, null));
        _syncing = true;
        FitTo.ItemsSource = _fits.Select(f => (object)(f.Unit == -9 ? new ComboBoxItem { Content = new Separator(), IsEnabled = false, Height = 9, Padding = default }
            : new ComboBoxItem { Content = Localize.Text(f.Name), Tag = f.Name, IsEnabled = f.Unit != -5 || _fits.Any(x => x.Path is not null) })).ToArray();
        FitTo.SelectedIndex = 0; _syncing = false;
    }

    private async Task Fit()
    {
        if (_syncing || FitTo.SelectedIndex < 0) return;
        var fit = _fits[FitTo.SelectedIndex];
        switch (fit.Unit)
        {
            case -1: _width = _startWidth; _height = _startHeight; _resolution = _startResolution; Refresh(); return;
            case -2:
                // Auto Resolution: the screen frequency the picture will print at, and the quality, give the resolution.
                var dialog = new FormDialog("Auto Resolution", 420, 300); var screen = dialog.Number("Screen (lines/inch)", 133, 1, 999);
                var quality = dialog.Choice("Quality", ["Draft", "Good", "Best"], 1);
                if (await dialog.ShowDialog<bool>(this)) SetResolution(Math.Clamp((double)(screen.Value ?? 133) * new[] { 1, 1.5, 2 }[Math.Max(0, quality.SelectedIndex)], 72, 9600));
                MarkCustom(); return;
            case -3:
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Preset…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Image Size preset")) { Patterns = ["*.json"] }] });
                if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path && PresetBar.Read(path) is { } values && values.ContainsKey("Width"))
                {
                    try { Directory.CreateDirectory(PresetFolder); File.Copy(path, System.IO.Path.Combine(PresetFolder, System.IO.Path.GetFileName(path)), true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    LoadFits(); var index = _fits.FindIndex(f => f.Name == System.IO.Path.GetFileNameWithoutExtension(path)); if (index >= 0) FitTo.SelectedIndex = index;
                }
                else MarkCustom();
                return;
            case -4:
                Directory.CreateDirectory(PresetFolder);
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Preset…"), SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(PresetFolder), SuggestedFileName = $"{Math.Round(_width)} x {Math.Round(_height)}.json", DefaultExtension = ".json" });
                if (file?.TryGetLocalPath() is { } saved)
                    try { await AtomicFile.WriteBytesAsync(saved, JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, double> { ["Width"] = Math.Round(_width), ["Height"] = Math.Round(_height), ["Resolution"] = _resolution, ["Unit"] = 1 })); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                LoadFits(); MarkCustom(); return;
            case -5:
                var user = _fits.Where(f => f.Path is not null).ToList();
                if (user.Count == 0) { MarkCustom(); return; }
                var delete = new FormDialog("Delete Preset", 420, 240); var which = delete.Choice("Preset", user.Select(u => u.Name), 0);
                if (await delete.ShowDialog<bool>(this) && which.SelectedIndex >= 0) try { File.Delete(user[which.SelectedIndex].Path!); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                LoadFits(); MarkCustom(); return;
            case -6: case -9: return;
        }
        // A size to fit within: the picture keeps its proportions and is made as large as fits.
        Resample.IsChecked = true; _resolution = fit.Resolution;
        var boxWidth = fit.Unit == 1 ? fit.Width : Inches(fit.Width, fit.Unit) * fit.Resolution; var boxHeight = fit.Unit == 1 ? fit.Height : Inches(fit.Height, fit.Unit) * fit.Resolution;
        if (_startWidth > _startHeight != boxWidth > boxHeight) (boxWidth, boxHeight) = (boxHeight, boxWidth);
        var factor = Math.Min(boxWidth / _startWidth, boxHeight / _startHeight); _width = _startWidth * factor; _height = _startHeight * factor;
        Refresh();
    }

    private void Accept()
    {
        WidthBox.Commit(); HeightBox.Commit(); ResolutionBox.Commit();
        var width = (int)Math.Round(_width); var height = (int)Math.Round(_height);
        if (width < 1 || height < 1 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide) return;
        _result = new ImageSizeResult(width, height, Math.Round(_resolution, 3), Methods[Math.Max(0, Method.SelectedIndex)].Sampling, _scaleStyles);
        Close();
    }

    internal static async Task<ImageSizeResult?> Ask(Window owner, int width, int height, double resolution, ImageSettings image, Func<SKBitmap?> picture)
    {
        var dialog = new ImageSizeDialog(width, height, resolution, image, picture);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}

/// <summary>The chain between Width and Height: closed while they keep their proportion.</summary>
internal sealed class ChainGlyph(ToggleButton owner) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(16, 24);
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(owner.IsChecked == true ? Skin.LabelBrush : Skin.SecondaryBrush, 1.6);
        context.DrawRectangle(null, pen, new Rect(4, 2, 8, 10), 4, 4);
        context.DrawRectangle(null, pen, new Rect(4, 12, 8, 10), 4, 4);
        if (owner.IsChecked == true) context.DrawLine(pen, new Point(8, 8), new Point(8, 16));
    }
}

internal static class LogicalTagSearch
{
    /// <summary>The first descendant with the given tag, searching the logical tree.</summary>
    internal static Control? GetLogicalDescendantsOfTag(this Control root, string tag) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<Control>().FirstOrDefault(c => Equals(c.Tag, tag));
}

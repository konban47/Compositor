using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>What the adjustment dialogs need to know beyond their own settings: the picture to count and sample, the
/// document being adjusted and the other open ones Match Color can take statistics from.</summary>
internal sealed record ImageAdjustmentContext(Func<SKBitmap?> Picture, CanvasDocument Target, string TargetName, IReadOnlyList<(string Name, CanvasDocument Document)> Documents);

/// <summary>
/// Image ▸ Adjustments' Brightness/Contrast, Vibrance, Photo Filter, Channel Mixer, Color Lookup, Posterize, Threshold,
/// Selective Color, Shadows/Highlights, HDR Toning, Match Color and Replace Color, each laid out as Photoshop's dialog is
/// in its screenshots. The settings are amounts by name on <see cref="ImageAdjustmentSettings"/>, which the image and an
/// adjustment layer of these kinds both carry.
/// </summary>
internal sealed class ImageAdjustmentDialog : DialogWindow
{
    protected override bool SharesEditPosition => true;
    private readonly ImageAdjustmentSettings _settings;
    private readonly ImageAdjustmentContext _context;
    private readonly CheckBox _previewBox;
    private PresetBar? _presets;
    private Action? _refresh;
    private bool _loading;
    internal bool PreviewEnabled => _previewBox.IsChecked == true;
    internal Action? Changed { get; set; }
    internal bool Accepted { get; private set; }
    internal ImageAdjustmentSettings Settings => _settings;

    internal static readonly (string Name, uint Color)[] PhotoFilters =
    [
        ("Warming Filter (85)", 0xFFEC8A00), ("Warming Filter (LBA)", 0xFFFA9600), ("Warming Filter (81)", 0xFFEBB113), ("Cooling Filter (80)", 0xFF006DFF),
        ("Cooling Filter (LBB)", 0xFF005DFF), ("Cooling Filter (82)", 0xFF00B5FF), ("Red", 0xFFEA1A1A), ("Orange", 0xFFF28A00), ("Yellow", 0xFFF9E31C), ("Green", 0xFF19C919),
        ("Cyan", 0xFF1DCBEA), ("Blue", 0xFF1D35EA), ("Violet", 0xFF9B1DEA), ("Magenta", 0xFFE318E3), ("Sepia", 0xFFAC7A33), ("Deep Red", 0xFFFF0000), ("Deep Blue", 0xFF0022CD),
        ("Deep Emerald", 0xFF008C00), ("Deep Yellow", 0xFFFFD500), ("Underwater", 0xFF00C1B1),
    ];

    internal ImageAdjustmentDialog(string title, ImageAdjustmentSettings settings, ImageAdjustmentContext context)
    {
        _settings = settings; _context = context;
        Title = Localize.Text(title); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var older = settings.Kind is ImageAdjustmentKind.Threshold or ImageAdjustmentKind.HdrToning or ImageAdjustmentKind.MatchColor or ImageAdjustmentKind.ReplaceColor;
        _previewBox = AdjustmentKit.Preview(!older);
        _previewBox.IsCheckedChanged += (_, _) => Changed?.Invoke();
        var extras = new List<Control>();
        Control left = settings.Kind switch
        {
            ImageAdjustmentKind.BrightnessContrast => BrightnessBody(extras),
            ImageAdjustmentKind.Vibrance => Sliders(("Vibrance", null, -100, 100, 0), ("Saturation", null, -100, 100, 0)),
            ImageAdjustmentKind.PhotoFilter => PhotoFilterBody(),
            ImageAdjustmentKind.ChannelMixer => ChannelMixerBody(),
            ImageAdjustmentKind.ColorLookup => ColorLookupBody(),
            ImageAdjustmentKind.Posterize => Sliders(("Levels", 'L', 2, 255, 4)),
            ImageAdjustmentKind.Threshold => ThresholdBody(),
            ImageAdjustmentKind.SelectiveColor => SelectiveColorBody(),
            ImageAdjustmentKind.ShadowsHighlights => ShadowsBody(extras),
            ImageAdjustmentKind.HdrToning => HdrBody(),
            ImageAdjustmentKind.MatchColor => MatchColorBody(),
            ImageAdjustmentKind.ReplaceColor => ReplaceColorBody(extras),
            _ => Sliders(("Clarity", null, -100, 100, 25), ("Dehaze", null, -100, 100, 15)),
        };
        // Photoshop's newer dialogs keep Preview at the foot of the right column; Threshold and the older ones under the buttons.
        var actions = AdjustmentKit.Actions(this, () => { foreach (var box in this.GetVisualDescendants().OfType<ValueBox>()) box.Commit(); if (Problem is not null) return; Accepted = true; Close(true); }, () => Close(false), _previewBox, extras.ToArray());
        Content = AdjustmentKit.Body(left, actions);
    }

    private double V(string key, double fallback = 0) => _settings.Get(key, fallback);
    private void Set(string key, double value) { _settings.Values[key] = value; Moved(); }
    private void Moved() { if (_loading) return; _presets?.MarkCustom(); Changed?.Invoke(); }
    private void Reload(Action load) { _loading = true; try { load(); _refresh?.Invoke(); } finally { _loading = false; } Changed?.Invoke(); }

    /// <summary>A column of slider rows, each writing its amount under its own name.</summary>
    private StackPanel Sliders(params (string Key, char? Letter, double Min, double Max, double Default)[] rows)
    {
        var panel = new StackPanel { Width = 362, Spacing = 8 }; var made = new List<(SliderRow Row, string Key, double Default)>();
        foreach (var (key, letter, min, max, fallback) in rows)
        {
            _settings.Values[key] = V(key, fallback);
            var row = new SliderRow(this, key, letter, min, max, V(key, fallback), tag: key); row.Changed += () => Set(key, row.Value);
            panel.Children.Add(row); made.Add((row, key, fallback));
        }
        _refresh = () => { foreach (var (row, key, fallback) in made) row.Value = V(key, fallback); };
        return panel;
    }

    // ------------------------------------------------------------------------------------------------ Brightness/Contrast

    private Control BrightnessBody(List<Control> extras)
    {
        var panel = Sliders(("Brightness", null, -150, 150, 0), ("Contrast", null, -50, 100, 0));
        var legacy = new CheckBox { Content = new TextBlock { Text = Localize.Text("Use Legacy") + "(L)" }, IsChecked = V("Legacy") != 0, Tag = "Use Legacy", Margin = new Thickness(0, 8, 0, 0) };
        legacy.IsCheckedChanged += (_, _) => Set("Legacy", legacy.IsChecked == true ? 1 : 0);
        LayerDialogControls.Key(this, 'L', legacy); panel.Children.Add(legacy);
        extras.Add(AdjustmentKit.Action(this, "Auto", null, () => Reload(() =>
        {
            // Auto: the contrast that stretches the picture's range to full, and the brightness that brings its mean to the middle.
            using var picture = _context.Picture(); if (picture is null) return;
            var levels = LevelsEdits.Automatic(picture, LevelsAuto.Brightness); var range = levels.Ranges[0];
            _settings.Values["Contrast"] = Math.Round(Math.Clamp(Math.Log2(255 / Math.Max(1, range.White - range.Black)) * 100, -50, 100));
            _settings.Values["Brightness"] = Math.Round(Math.Clamp((range.Gamma - 1) * 60, -150, 150));
        })));
        return panel;
    }

    // ------------------------------------------------------------------------------------------------ Photo Filter

    private Control PhotoFilterBody()
    {
        _settings.Values["Density"] = V("Density", 25);
        var index = (int)V("Filter", 0);
        var useFilter = new RadioButton { Content = new TextBlock { Text = Localize.Text("Filter") + "(F)" }, GroupName = "photo", IsChecked = index >= 0, Tag = "Filter" };
        var useColor = new RadioButton { Content = new TextBlock { Text = Localize.Text("Color") + "(C)" }, GroupName = "photo", IsChecked = index < 0, Tag = "Color" };
        // The filters keep their English names in either language, as Photoshop's Chinese screenshots show them.
        var filters = new ComboBox { ItemsSource = PhotoFilters.Select(f => f.Name).ToArray(), SelectedIndex = Math.Max(0, index), Width = 240, Height = 32, Tag = "filters" };
        ToolTip.SetTip(filters, Localize.Text("Use a predefined filter color"));
        var swatch = new Button { Width = 70, Height = 34, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "filter color" };
        void Paint() => swatch.Background = new SolidColorBrush(Color.FromUInt32(_settings.Color));
        filters.SelectionChanged += (_, _) => { if (filters.SelectedIndex < 0) return; useFilter.IsChecked = true; _settings.Color = PhotoFilters[filters.SelectedIndex].Color; Paint(); Set("Filter", filters.SelectedIndex); };
        useFilter.IsCheckedChanged += (_, _) => { if (useFilter.IsChecked == true && filters.SelectedIndex >= 0) { _settings.Color = PhotoFilters[filters.SelectedIndex].Color; Paint(); Set("Filter", filters.SelectedIndex); } };
        useColor.IsCheckedChanged += (_, _) => { if (useColor.IsChecked == true) Set("Filter", -1); };
        swatch.Click += async (_, _) =>
        {
            var c = Color.FromUInt32(_settings.Color); useColor.IsChecked = true;
            await ColorPickerDialog.Pick(this, "Color Picker (Photo Filter Color)", (c.R / 255.0, c.G / 255.0, c.B / 255.0), moved =>
            { _settings.Color = 0xff000000u | (uint)Math.Round(moved.Red * 255) << 16 | (uint)Math.Round(moved.Green * 255) << 8 | (uint)Math.Round(moved.Blue * 255); Paint(); Moved(); });
        };
        if (index >= 0) _settings.Color = PhotoFilters[Math.Min(index, PhotoFilters.Length - 1)].Color;
        Paint();
        var density = new SliderRow(this, "Density", 'D', 0, 100, V("Density", 25), suffix: "%", tag: "Density"); density.Changed += () => Set("Density", density.Value);
        var preserve = new CheckBox { Content = new TextBlock { Text = Localize.Text("Preserve Luminosity") + "(L)" }, IsChecked = _settings.PreserveLuminosity, Tag = "Preserve Luminosity" };
        preserve.IsCheckedChanged += (_, _) => { _settings.PreserveLuminosity = preserve.IsChecked == true; Moved(); };
        LayerDialogControls.Key(this, 'F', useFilter); LayerDialogControls.Key(this, 'C', useColor); LayerDialogControls.Key(this, 'L', preserve);
        var choices = new Grid { ColumnDefinitions = new("Auto,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 18, RowSpacing = 12 };
        choices.Children.Add(useFilter); Grid.SetColumn(filters, 1); choices.Children.Add(filters);
        Grid.SetRow(useColor, 1); choices.Children.Add(useColor); Grid.SetRow(swatch, 1); Grid.SetColumn(swatch, 1); swatch.HorizontalAlignment = HorizontalAlignment.Left; choices.Children.Add(swatch);
        return new StackPanel { Width = 362, Spacing = 14, Children = { choices, density, preserve } };
    }

    // ------------------------------------------------------------------------------------------------ Channel Mixer

    private Control ChannelMixerBody()
    {
        var output = 0;
        var dots = new ColorDots([(Colors.Red, "Red"), (Colors.Lime, "Green"), (Colors.Blue, "Blue")], 32) { Tag = "output channels" };
        var mono = new CheckBox { Content = new TextBlock { Text = Localize.Text("Monochrome") + "(H)" }, IsChecked = _settings.Monochrome, Tag = "Monochrome" };
        var rows = new[]
        {
            new SliderRow(this, AdjustmentKit.Word("Red", "红色"), 'R', -200, 200, 0, track: AdjustmentKit.Track(Colors.Black, Colors.Red, Colors.White), tag: "Red"),
            new SliderRow(this, AdjustmentKit.Word("Green", "绿色"), 'G', -200, 200, 0, track: AdjustmentKit.Track(Colors.Black, Colors.Lime, Colors.White), tag: "Green"),
            new SliderRow(this, AdjustmentKit.Word("Blue", "蓝色"), 'B', -200, 200, 0, track: AdjustmentKit.Track(Colors.Black, Colors.Blue, Colors.White), tag: "Blue"),
        };
        var constant = new SliderRow(this, AdjustmentKit.Word("Constant", "常量"), 'N', -200, 200, 0, track: AdjustmentKit.Track(Colors.Black, Colors.White), tag: "Constant");
        var total = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
        string[] inputs = ["R", "G", "B"];
        var syncing = false;
        void Fill()
        {
            syncing = true; var channel = _settings.Monochrome ? 0 : output;
            for (var i = 0; i < 3; i++) rows[i].Value = V($"{channel}{inputs[i]}", channel == i ? 100 : 0);
            constant.Value = V($"{channel}Constant"); mono.IsChecked = _settings.Monochrome; dots.IsEnabled = !_settings.Monochrome; dots.Opacity = _settings.Monochrome ? .45 : 1;
            var sum = Enumerable.Range(0, 3).Sum(i => V($"{channel}{inputs[i]}", channel == i ? 100 : 0)); total.Text = $"{sum:0}  %";
            total.Foreground = sum > 100 ? Brushes.Orange : Skin.LabelBrush; syncing = false;
        }
        for (var i = 0; i < 3; i++) { var input = inputs[i]; var row = rows[i]; row.Changed += () => { if (syncing) return; Set($"{(_settings.Monochrome ? 0 : output)}{input}", row.Value); Fill(); }; }
        constant.Changed += () => { if (!syncing) Set($"{(_settings.Monochrome ? 0 : output)}Constant", constant.Value); };
        dots.Selected += i => { output = i; Fill(); };
        mono.IsCheckedChanged += (_, _) => { if (syncing) return; _settings.Monochrome = mono.IsChecked == true; Fill(); Moved(); };
        LayerDialogControls.Key(this, 'H', mono);
        _refresh = Fill; Fill();
        _presets = new PresetBar(this, "Channel Mixer", 'S', AdjustmentPresets.ChannelMixerList, () =>
        {
            var values = new Dictionary<string, double> { ["Monochrome"] = _settings.Monochrome ? 1 : 0 };
            for (var c = 0; c < 3; c++) { foreach (var input in inputs) values[$"{c}{input}"] = V($"{c}{input}", c == Array.IndexOf(inputs, input) ? 100 : 0); values[$"{c}Constant"] = V($"{c}Constant"); }
            return values;
        }, values => Reload(() => { _settings.Monochrome = values.GetValueOrDefault("Monochrome") != 0; foreach (var pair in values.Where(v => v.Key != "Monochrome")) _settings.Values[pair.Key] = pair.Value; }),
            tooltip: "Choose a Channel Mixer preset");
        var outputRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { new TextBlock { Text = Localize.Text("Output Channel"), VerticalAlignment = VerticalAlignment.Center }, dots } };
        var totalRow = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new TextBlock { Text = Localize.Text("Total") } } }; Grid.SetColumn(total, 1); totalRow.Children.Add(total);
        var panel = new StackPanel { Width = 362, Spacing = 8, Children = { _presets, outputRow, mono } };
        foreach (var row in rows) panel.Children.Add(row);
        panel.Children.Add(totalRow); panel.Children.Add(new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 4) }); panel.Children.Add(constant);
        return panel;
    }

    // ------------------------------------------------------------------------------------------------ Color Lookup

    /// <summary>Where the 3D LUT list comes from: .cube files kept in the settings folder's Color Lookup, where Load 3D LUT… copies them.</summary>
    internal static string LookupFolder => Path.Combine(AppPaths.SettingsDirectory, "Color Lookup");

    private Control ColorLookupBody()
    {
        var file = new RadioButton { Content = Localize.Text("3DLUT File"), GroupName = "lookup", IsChecked = true, Tag = "3DLUT File" };
        var abstractRadio = new RadioButton { Content = Localize.Text("Abstract"), GroupName = "lookup", IsEnabled = false, Tag = "Abstract" };
        var device = new RadioButton { Content = Localize.Text("Device Link"), GroupName = "lookup", IsEnabled = false, Tag = "Device Link" };
        foreach (var radio in new[] { abstractRadio, device }) ToolTip.SetTip(radio, Localize.Text("ICC abstract and device link profiles are not supported; use a 3D LUT (.cube)."));
        IEnumerable<string> Luts() { try { return Directory.Exists(LookupFolder) ? Directory.GetFiles(LookupFolder, "*.cube").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Take(500) : []; } catch (IOException) { return []; } catch (UnauthorizedAccessException) { return []; } }
        var luts = new ComboBox { Width = 400, Height = 32, Tag = "3DLUT list" };
        var status = new TextBlock { Foreground = Skin.SecondaryBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
        var paths = new List<string?>();
        void Fill(string? chosen)
        {
            paths.Clear(); var items = new List<object> { Localize.Text("Load 3D LUT…"), new ComboBoxItem { Content = Localize.Text("Other"), IsEnabled = false } };
            paths.Add(null); paths.Add(null);
            var found = Luts().ToList();
            if (found.Count > 0) { items.Add(new ComboBoxItem { Content = new Separator(), IsEnabled = false, Height = 9, Padding = default }); paths.Add(null); }
            foreach (var path in found) { items.Add(Path.GetFileName(path)); paths.Add(path); }
            luts.ItemsSource = items; luts.SelectedIndex = chosen is null ? 0 : Math.Max(0, paths.FindIndex(p => p is not null && string.Equals(p, chosen, StringComparison.OrdinalIgnoreCase)));
        }
        void Take(string path)
        {
            try { _settings.Cube = ColorCube.Parse(File.ReadAllText(path)); status.Text = Path.GetFileName(path); Moved(); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { status.Text = e.Message; }
        }
        var choosing = false;
        luts.SelectionChanged += async (_, _) =>
        {
            if (choosing || luts.SelectedIndex < 0) return;
            if (luts.SelectedIndex < paths.Count && paths[luts.SelectedIndex] is { } path) { Take(path); return; }
            if (luts.SelectedIndex != 0 || !IsVisible) return;
            choosing = true;
            try
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load 3D LUT…"), FileTypeFilter = [new FilePickerFileType("3D LUT") { Patterns = ["*.cube"] }] });
                if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } chosen) return;
                // The LUT is kept with the others, as Photoshop lists the ones it has loaded.
                var kept = chosen;
                try { Directory.CreateDirectory(LookupFolder); kept = Path.Combine(LookupFolder, Path.GetFileName(chosen)); if (!string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase)) File.Copy(chosen, kept, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { kept = chosen; }
                Fill(kept); Take(kept);
            }
            finally { choosing = false; }
        };
        Fill(null);
        status.Text = Localize.Text(_settings.Cube is null ? "No color lookup loaded" : "Embedded color lookup loaded");
        ComboBox Off(string text) { var box = new ComboBox { ItemsSource = new[] { Localize.Text(text), Localize.Text("Other") }, SelectedIndex = 0, Width = 400, Height = 32, IsEnabled = false }; return box; }
        var dither = new CheckBox { Content = Localize.Text("Dither"), IsChecked = true, IsEnabled = false, Tag = "Dither" };
        ToolTip.SetTip(dither, Localize.Text("The lookup is applied in floating point, so there is no banding for dithering to hide."));
        var grid = new Grid { ColumnDefinitions = new("120,Auto"), RowDefinitions = new("Auto,Auto,Auto,Auto"), RowSpacing = 10 };
        void Put(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); c.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(c); }
        Put(file, 0, 0); Put(luts, 0, 1); Put(abstractRadio, 1, 0); Put(Off("Load Abstract Profile…"), 1, 1); Put(device, 2, 0); Put(Off("Load Device Link Profile…"), 2, 1);
        return new StackPanel { Spacing = 12, Children = { grid, dither, status } };
    }

    /// <summary>Whether the dialog may close: Color Lookup needs a lookup.</summary>
    internal string? Problem => _settings.Kind == ImageAdjustmentKind.ColorLookup && _settings.Cube is null ? "Load a color lookup first." : null;

    // ------------------------------------------------------------------------------------------------ Threshold

    private Control ThresholdBody()
    {
        _settings.Values["Threshold"] = V("Threshold", 128);
        var level = new ValueBox(1, 255, V("Threshold", 128), tag: "Threshold Level");
        var histogram = new HistogramView(V("Threshold", 128)) { Width = 384, Height = 170, Tag = "threshold histogram" };
        using (var picture = _context.Picture())
        {
            var bins = new double[256];
            if (picture is not null) for (var y = 0; y < picture.Height; y++) for (var x = 0; x < picture.Width; x++) { var c = picture.GetPixel(x, y); if (c.Alpha > 0) bins[Math.Clamp((int)Math.Round(.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue), 0, 255)]++; }
            histogram.Bins = bins;
        }
        var syncing = false;
        level.ValueChanged += () => { if (syncing) return; syncing = true; histogram.Points[0] = level.Value; histogram.InvalidateVisual(); syncing = false; Set("Threshold", level.Value); };
        histogram.Moved += () => { if (syncing) return; syncing = true; level.Value = Math.Max(1, histogram.Points[0]); syncing = false; Set("Threshold", level.Value); };
        LayerDialogControls.Key(this, 'T', level);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { AdjustmentKit.Caption("Threshold Level", 'T', true), level } };
        return new StackPanel { Spacing = 10, Children = { row, histogram } };
    }

    // ------------------------------------------------------------------------------------------------ Selective Color

    private Control SelectiveColorBody()
    {
        var dots = new ColorDots([(Colors.Red, "Reds"), (Colors.Yellow, "Yellows"), (Colors.Lime, "Greens"), (Colors.Cyan, "Cyans"), (Colors.Blue, "Blues"), (Colors.Magenta, "Magentas"),
            (Colors.White, "Whites"), (Color.FromRgb(0x80, 0x80, 0x80), "Neutrals"), (Colors.Black, "Blacks")], 36) { Tag = "colors" };
        dots.Index = _settings.Range;
        var keys = new[] { ("C", "Cyan", 'C', Colors.Red, Colors.Cyan), ("M", "Magenta", 'M', Colors.Lime, Colors.Magenta), ("Y", "Yellow", 'Y', Colors.Blue, Colors.Yellow), ("K", "Black", 'B', Colors.White, Colors.Black) };
        var rows = keys.Select(k => new SliderRow(this, k.Item2, k.Item3, -100, 100, 0, suffix: "%", track: AdjustmentKit.Track(k.Item4, Color.FromRgb(0x80, 0x80, 0x80), k.Item5), tag: k.Item2)).ToArray();
        var syncing = false;
        void Fill() { syncing = true; for (var i = 0; i < 4; i++) rows[i].Value = V($"{_settings.Range}{keys[i].Item1}"); syncing = false; }
        for (var i = 0; i < 4; i++) { var key = keys[i].Item1; var row = rows[i]; row.Changed += () => { if (!syncing) Set($"{_settings.Range}{key}", row.Value); }; }
        dots.Selected += i => { _settings.Range = i; Fill(); };
        var relative = new RadioButton { Content = new TextBlock { Text = Localize.Text("Relative") + "(R)" }, GroupName = "method", IsChecked = !_settings.Absolute, Tag = "Relative" };
        var absolute = new RadioButton { Content = new TextBlock { Text = Localize.Text("Absolute") + "(A)" }, GroupName = "method", IsChecked = _settings.Absolute, Tag = "Absolute" };
        absolute.IsCheckedChanged += (_, _) => { _settings.Absolute = absolute.IsChecked == true; Moved(); };
        LayerDialogControls.Key(this, 'R', relative); LayerDialogControls.Key(this, 'A', absolute);
        _refresh = () => { relative.IsChecked = !_settings.Absolute; absolute.IsChecked = _settings.Absolute; Fill(); }; Fill();
        _presets = new PresetBar(this, "Selective Color", 'R', AdjustmentPresets.SelectiveColorList, () =>
        {
            var values = _settings.Values.Where(v => v.Key.Length == 2 && char.IsDigit(v.Key[0])).ToDictionary(v => v.Key, v => v.Value); values["Absolute"] = _settings.Absolute ? 1 : 0; return values;
        }, values => Reload(() =>
        {
            foreach (var key in _settings.Values.Keys.Where(k => k.Length == 2 && char.IsDigit(k[0])).ToList()) _settings.Values.Remove(key);
            foreach (var pair in values.Where(v => v.Key != "Absolute")) _settings.Values[pair.Key] = pair.Value; _settings.Absolute = values.GetValueOrDefault("Absolute") != 0;
        }), tooltip: "Choose a Selective Color preset");
        var panel = new StackPanel { Width = 470, Spacing = 8, Children = { _presets, dots } };
        foreach (var row in rows) panel.Children.Add(row);
        panel.Children.Add(new TextBlock { Text = Localize.Text("Method"), Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { relative, absolute } });
        return panel;
    }

    // ------------------------------------------------------------------------------------------------ Shadows/Highlights

    private Control ShadowsBody(List<Control> extras)
    {
        foreach (var (key, value) in new[] { ("Shadows", 35.0), ("Highlights", 0.0), ("Shadow Width", 50.0), ("Highlight Width", 50.0), ("Radius", 30.0), ("Midtone Contrast", 0.0) }) _settings.Values[key] = V(key, value);
        SliderRow Inline(string label, char? key, string valueKey, double min, double max, string suffix) { var row = new SliderRow(this, label, key, min, max, V(valueKey), suffix: suffix, inline: true, tag: valueKey); row.Changed += () => Set(valueKey, row.Value); return row; }
        var shadows = Inline("Shadows", 'S', "Shadows", 0, 100, "%"); var highlights = Inline("Highlights", 'H', "Highlights", 0, 100, "%");
        var shadowTone = Inline("Tone", null, "Shadow Width", 1, 100, "%"); var highlightTone = Inline("Tone", null, "Highlight Width", 1, 100, "%");
        var radius = Inline("Radius", null, "Radius", 1, 250, " px"); var midtone = Inline("Midtone", null, "Midtone Contrast", -100, 100, "");
        var more = new StackPanel
        {
            Spacing = 4, IsVisible = false, Tag = "more options",
            Children = { AdjustmentKit.Group("Shadows", new StackPanel { Spacing = 6, Children = { shadowTone } }), AdjustmentKit.Group("Highlights", new StackPanel { Spacing = 6, Children = { highlightTone } }),
                AdjustmentKit.Group("Adjustments", new StackPanel { Spacing = 6, Children = { radius, midtone } }) },
        };
        var show = new CheckBox { Content = new TextBlock { Text = Localize.Text("Show More Options") + "(O)" }, Tag = "Show More Options" };
        show.IsCheckedChanged += (_, _) => more.IsVisible = show.IsChecked == true;
        LayerDialogControls.Key(this, 'O', show);
        _refresh = () => { foreach (var row in new[] { shadows, highlights, shadowTone, highlightTone, radius, midtone }) row.Value = V((string)row.Tag!); };
        extras.Add(AdjustmentKit.Action(this, "Load…", 'L', async () =>
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Adjustment preset")) { Patterns = ["*.json"] }] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path && PresetBar.Read(path) is { } values) Reload(() => { foreach (var pair in values) _settings.Values[pair.Key] = pair.Value; });
        }));
        extras.Add(AdjustmentKit.Action(this, "Save…", 'V', async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save…"), SuggestedFileName = Localize.Text("Shadows/Highlights").Replace('/', '-') + ".json", DefaultExtension = ".json" });
            if (file?.TryGetLocalPath() is { } path) try { await AtomicFile.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(_settings.Values)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }));
        return new StackPanel { Width = 450, Spacing = 10, Children = { shadows, highlights, show, more } };
    }

    // ------------------------------------------------------------------------------------------------ HDR Toning

    private Control HdrBody()
    {
        foreach (var (key, value) in AdjustmentPresets.HdrList[0].Item2) if (!_settings.Values.ContainsKey(key)) _settings.Values[key] = value;
        var rows = new List<SliderRow>();
        SliderRow Row(string label, char key, string valueKey, double min, double max, string format, string suffix = "", bool signed = false)
        {
            var row = new SliderRow(this, label, key, min, max, V(valueKey), format, suffix, signed: signed, inline: true, triangle: true, tag: valueKey);
            row.Label.MinWidth = 120; row.Label.TextAlignment = TextAlignment.Right; row.Label.Text += ":";
            row.Changed += () => Set(valueKey, row.Value); rows.Add(row); return row;
        }
        Grid WithUnit(SliderRow row, string unit) { var grid = new Grid { ColumnDefinitions = new("*,34"), Children = { row } }; var u = new TextBlock { Text = Localize.Text(unit), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(u, 1); grid.Children.Add(u); return grid; }
        var smooth = new CheckBox { Content = new TextBlock { Text = Localize.Text("Smooth Edges") + "(M)" }, IsChecked = V("Smooth") != 0, Margin = new Thickness(130, 0, 0, 0), Tag = "Smooth Edges" };
        smooth.IsCheckedChanged += (_, _) => Set("Smooth", smooth.IsChecked == true ? 1 : 0); LayerDialogControls.Key(this, 'M', smooth);
        var edge = Section("Edge Glow", true, WithUnit(Row("Radius", 'R', "Radius", 1, 500, "0"), "Pixels"), WithUnit(Row("Strength", 'S', "Strength", .1, 4, "0.00"), ""), smooth);
        var gammaRow = WithUnit(Row("Gamma", 'G', "Gamma", .1, 2, "0.00"), ""); var exposureRow = WithUnit(Row("Exposure", 'X', "Exposure", -5, 5, "0.00", signed: true), "");
        var detailRow = WithUnit(Row("Detail", 'D', "Detail", -100, 300, "0", signed: true), "%");
        var tone = Section("Tone and Detail", true, gammaRow, exposureRow, detailRow);
        var advanced = Section("Advanced", true, WithUnit(Row("Shadow", 'W', "Shadow", -100, 100, "0", signed: true), "%"), WithUnit(Row("Highlight", 'H', "Highlight", -100, 100, "0", signed: true), "%"),
            WithUnit(Row("Vibrance", 'V', "Vibrance", -100, 100, "0", signed: true), "%"), WithUnit(Row("Saturation", 'A', "Saturation", -100, 100, "0", signed: true), "%"));
        // The toning curve, input and output in percent, kept as handles on the master channel of a curve.
        var curves = new Compositor.Core.Format.CurvesSettings();
        void CurveFromValues()
        {
            var count = (int)V("CurveCount"); curves.Channels[0] = count is >= 2 and <= 32
                ? Enumerable.Range(0, count).Select(i => new Compositor.Core.Format.CurvePoint { X = Math.Round(V($"Curve{i}X") * 2.55), Y = Math.Round(V($"Curve{i}Y") * 2.55) }).OrderBy(p => p.X).ToList()
                : [new() { X = 0, Y = 0 }, new() { X = 255, Y = 255 }];
        }
        CurveFromValues();
        var curve = new CurveEditor { Curves = curves, Channel = 0, Width = 384, Height = 384, ShowOverlays = false, Tag = "toning curve" };
        var input = new ValueBox(0, 100, 0, tag: "Curve input"); var outputBox = new ValueBox(0, 100, 0, tag: "Curve output");
        curve.Changed += () =>
        {
            var points = curves.Channels[0]; _settings.Values["CurveCount"] = points.Count;
            for (var i = 0; i < points.Count; i++) { _settings.Values[$"Curve{i}X"] = Math.Round(points[i].X / 2.55, 1); _settings.Values[$"Curve{i}Y"] = Math.Round(points[i].Y / 2.55, 1); }
            var last = points[^1]; input.Value = Math.Round(last.X / 2.55); outputBox.Value = Math.Round(last.Y / 2.55); Moved();
        };
        var corner = new CheckBox { Content = Localize.Text("Corner"), IsEnabled = false, Tag = "Corner" };
        ToolTip.SetTip(corner, Localize.Text("Handles always join smoothly in this curve."));
        var reset = new Button { Content = "↺", Width = 30, Height = 28, Padding = default, Tag = "reset curve", HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(reset, Localize.Text("Reset the curve")); reset.Click += (_, _) => { _settings.Values["CurveCount"] = 0; CurveFromValues(); curve.Curves = curves; Moved(); };
        var underCurve = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0),
            Children = { AdjustmentKit.Caption("Input", 'I', true), input, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center }, AdjustmentKit.Caption("Output", 'O', true), outputBox, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center }, corner, reset },
        };
        var toning = Section("Toning Curve and Histogram", false, curve, underCurve);
        using (var picture = _context.Picture())
        {
            var bins = new double[256];
            if (picture is not null) for (var y = 0; y < picture.Height; y++) for (var x = 0; x < picture.Width; x++) { var c = picture.GetPixel(x, y); if (c.Alpha > 0) bins[Math.Clamp((int)Math.Round(.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue), 0, 255)]++; }
            curve.Histogram = bins;
        }
        var methods = new[] { "Exposure and Gamma", "Highlight Compression", "Equalize Histogram", "Local Adaptation" };
        var method = new ComboBox { ItemsSource = methods.Select(Localize.Text).ToArray(), SelectedIndex = Math.Clamp((int)V("Method", 3), 0, 3), Width = 190, Height = 30, Tag = "Method" };
        void Sections()
        {
            // Each method shows only the amounts it reads, as Photoshop's dialog does.
            var m = method.SelectedIndex; edge.IsVisible = advanced.IsVisible = toning.IsVisible = detailRow.IsVisible = m == 3; tone.IsVisible = m is 0 or 3;
        }
        method.SelectionChanged += (_, _) => { if (method.SelectedIndex < 0) return; Sections(); Set("Method", method.SelectedIndex); };
        Sections();
        _refresh = () => { foreach (var row in rows) row.Value = V((string)row.Tag!); smooth.IsChecked = V("Smooth") != 0; method.SelectedIndex = Math.Clamp((int)V("Method", 3), 0, 3); CurveFromValues(); curve.Curves = curves; };
        _presets = new PresetBar(this, "HDR Toning", 'E', AdjustmentPresets.HdrList, () => new(_settings.Values),
            values => Reload(() => { foreach (var key in _settings.Values.Keys.Where(k => k.StartsWith("Curve")).ToList()) _settings.Values.Remove(key); foreach (var pair in values) _settings.Values[pair.Key] = pair.Value; }),
            gear: true, newer: false);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { AdjustmentKit.Caption("Method", null, true), method } };
        var body = new StackPanel { Spacing = 4, Children = { edge, tone, advanced, toning } };
        return new StackPanel { Width = 520, Spacing = 4, Children = { _presets, AdjustmentKit.Group("", body, heading) } };
    }

    /// <summary>One of HDR Toning's sections, opened and closed by its heading's arrow.</summary>
    private static Control Section(string title, bool open, params Control[] rows)
    {
        var content = new StackPanel { Spacing = 8, IsVisible = open };
        foreach (var row in rows) content.Children.Add(row);
        var arrow = new TextBlock { Text = open ? "⌄" : "›", Width = 14 };
        var heading = new Button { Background = Brushes.Transparent, BorderThickness = default, Padding = new Thickness(2), Tag = "section " + title, Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { arrow, new TextBlock { Text = Localize.Text(title) } } } };
        heading.Click += (_, _) => { content.IsVisible = !content.IsVisible; arrow.Text = content.IsVisible ? "⌄" : "›"; };
        return AdjustmentKit.Group("", content, heading);
    }

    // ------------------------------------------------------------------------------------------------ Match Color

    private Control MatchColorBody()
    {
        foreach (var (key, value) in new[] { ("Luminance", 100.0), ("Color Intensity", 100.0), ("Fade", 0.0) }) _settings.Values[key] = V(key, value);
        var target = _context.Target; var hasSelection = target.Selection.Path is not null;
        var ignore = new CheckBox { Content = new TextBlock { Text = Localize.Text("Ignore Selection when Applying Adjustment") + "(I)" }, IsEnabled = hasSelection, IsChecked = V("IgnoreSelection") != 0, Tag = "Ignore Selection" };
        ignore.IsCheckedChanged += (_, _) => Set("IgnoreSelection", ignore.IsChecked == true ? 1 : 0);
        var targetLine = new TextBlock { Text = $"{Localize.Text("Target")}:  {_context.TargetName}({target.Image.Mode}/{target.Image.Depth})" };
        var luminance = new SliderRow(this, "Luminance", 'L', 1, 200, V("Luminance", 100), triangle: true, tag: "Luminance"); luminance.Changed += () => Set("Luminance", luminance.Value);
        var intensity = new SliderRow(this, "Color Intensity", 'C', 1, 200, V("Color Intensity", 100), triangle: true, tag: "Color Intensity"); intensity.Changed += () => Set("Color Intensity", intensity.Value);
        var fade = new SliderRow(this, "Fade", 'F', 0, 100, V("Fade"), triangle: true, tag: "Fade"); fade.Changed += () => Set("Fade", fade.Value);
        var neutralize = new CheckBox { Content = new TextBlock { Text = Localize.Text("Neutralize") + "(N)" }, IsChecked = V("Neutralize") != 0, Tag = "Neutralize" };
        neutralize.IsCheckedChanged += (_, _) => Set("Neutralize", neutralize.IsChecked == true ? 1 : 0);
        LayerDialogControls.Key(this, 'I', ignore); LayerDialogControls.Key(this, 'N', neutralize);
        var options = new StackPanel { Spacing = 6, Margin = new Thickness(90, 0, 0, 0), Children = { luminance, intensity, fade, neutralize } };

        // Image Statistics: the source document and layer whose colors the target is matched to, or statistics loaded from a file.
        var documents = _context.Documents;
        var sourceSelection = new CheckBox { Content = new TextBlock { Text = Localize.Text("Use Selection in Source to Calculate Colors") + "(R)" }, IsEnabled = false, Tag = "Use Selection in Source" };
        var targetSelection = new CheckBox { Content = new TextBlock { Text = Localize.Text("Use Selection in Target to Calculate Adjustment") + "(T)" }, IsEnabled = false, Tag = "Use Selection in Target" };
        ToolTip.SetTip(targetSelection, Localize.Text("The target's statistics are taken from its whole layer."));
        var source = new ComboBox { ItemsSource = new[] { Localize.Text("None") }.Concat(documents.Select(d => d.Name)).ToArray(), SelectedIndex = 0, Width = 220, Tag = "Source" };
        var layer = new ComboBox { Width = 220, IsEnabled = false, Tag = "Layer" };
        var thumbnail = new Image { Width = 150, Height = 150, Stretch = Stretch.Uniform };
        var layerIDs = new List<Guid?>();
        void Statistics()
        {
            if (source.SelectedIndex <= 0) { _settings.ReferenceMean = null; _settings.ReferenceDeviation = null; thumbnail.Source = null; Moved(); return; }
            var doc = documents[source.SelectedIndex - 1].Document; var id = layer.SelectedIndex >= 0 && layer.SelectedIndex < layerIDs.Count ? layerIDs[layer.SelectedIndex] : null;
            using var image = id is { } layerID && doc.Layers.FirstOrDefault(l => l.ID == layerID)?.Asset?.Image is { } pixels ? pixels.Copy() : DocumentRenderer.Render(doc);
            if (sourceSelection.IsChecked == true && doc.Selection.Path is not null && id is null)
            {
                // Only the selected part of the source counts: everything else is made transparent before counting.
                using var mask = doc.Selection.Coverage(SKRectI.Create(doc.Width, doc.Height));
                if (mask is not null) for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++) if (mask.GetPixelSpan()[y * mask.RowBytes + x] < 128) image.SetPixel(x, y, SKColors.Transparent);
            }
            var stats = ImageAdjustments.Statistics(image); _settings.ReferenceMean = stats.Mean; _settings.ReferenceDeviation = stats.Deviation;
            var scale = Math.Min(1, 150.0 / Math.Max(image.Width, image.Height));
            using var small = image.Resize(new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear));
            if (small is not null) thumbnail.Source = Eyedroppers.Thumbnail(small);
            Moved();
        }
        source.SelectionChanged += (_, _) =>
        {
            layerIDs.Clear(); var names = new List<string>();
            if (source.SelectedIndex > 0)
            {
                var doc = documents[source.SelectedIndex - 1].Document;
                layerIDs.Add(null); names.Add(Localize.Text("Merged"));
                foreach (var l in doc.Layers.Where(l => l.Asset is not null).Reverse()) { layerIDs.Add(l.ID); names.Add(l.Name); }
                sourceSelection.IsEnabled = doc.Selection.Path is not null;
            }
            else sourceSelection.IsEnabled = false;
            layer.ItemsSource = names; layer.IsEnabled = names.Count > 0; layer.SelectedIndex = names.Count > 0 ? 0 : -1; Statistics();
        };
        layer.SelectionChanged += (_, _) => Statistics();
        sourceSelection.IsCheckedChanged += (_, _) => Statistics();
        var load = AdjustmentKit.Action(this, "Load Statistics…", 'O', async () =>
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load Statistics…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Statistics")) { Patterns = ["*.json"] }] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path || PresetBar.Read(path) is not { } values) return;
            if (Enumerable.Range(0, 3).All(c => values.ContainsKey($"Mean{c}") && values.ContainsKey($"Deviation{c}")))
            { _settings.ReferenceMean = [.. Enumerable.Range(0, 3).Select(c => values[$"Mean{c}"])]; _settings.ReferenceDeviation = [.. Enumerable.Range(0, 3).Select(c => Math.Max(0, values[$"Deviation{c}"]))]; Moved(); }
        });
        var save = AdjustmentKit.Action(this, "Save Statistics…", 'V', async () =>
        {
            if (_settings.ReferenceMean is not { } mean || _settings.ReferenceDeviation is not { } deviation) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save Statistics…"), SuggestedFileName = "statistics.json", DefaultExtension = ".json" });
            if (file?.TryGetLocalPath() is not { } path) return;
            var values = new Dictionary<string, double>(); for (var c = 0; c < 3; c++) { values[$"Mean{c}"] = mean[c]; values[$"Deviation{c}"] = deviation[c]; }
            try { await AtomicFile.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(values)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });
        load.HorizontalAlignment = save.HorizontalAlignment = HorizontalAlignment.Left; load.Width = save.Width = 220;
        TextBlock Right(string text) => new() { Text = Localize.Text(text) + ":", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var picker = new Grid { ColumnDefinitions = new("80,Auto"), RowDefinitions = new("Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        void Put(Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); picker.Children.Add(c); }
        Put(Right("Source"), 0, 0); Put(source, 0, 1); Put(Right("Layer"), 1, 0); Put(layer, 1, 1); Put(load, 2, 1); Put(save, 3, 1);
        var lower = new Grid { ColumnDefinitions = new("*,Auto"), Children = { picker } }; Grid.SetColumn(thumbnail, 1); thumbnail.VerticalAlignment = VerticalAlignment.Bottom; lower.Children.Add(thumbnail);
        var statistics = new StackPanel { Spacing = 10, Children = { sourceSelection, targetSelection, lower } };
        return new StackPanel
        {
            Width = 540,
            Children =
            {
                AdjustmentKit.Group("Destination Image", new StackPanel { Spacing = 8, Children = { targetLine, ignore,
                    AdjustmentKit.Group("Image Options", options) } }),
                AdjustmentKit.Group("Image Statistics", statistics),
            },
        };
    }

    // ------------------------------------------------------------------------------------------------ Replace Color

    private Control ReplaceColorBody(List<Control> extras)
    {
        _settings.Values["Shift"] = 1; _settings.Values["Fuzziness"] = V("Fuzziness", 40);
        foreach (var key in new[] { "Hue", "Saturation", "Lightness" }) _settings.Values[key] = V(key);
        using var start = _context.Picture(); var picture = start?.Copy();
        Closed += (_, _) => picture?.Dispose();
        var preview = new Image { Width = 300, Height = 300, Stretch = Stretch.Uniform, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross), Tag = "replace preview" };
        var showImage = new RadioButton { Content = new TextBlock { Text = Localize.Text("Image") + "(M)" }, GroupName = "show", Tag = "Image" };
        var showSelection = new RadioButton { Content = new TextBlock { Text = Localize.Text("Selection") + "(C)" }, GroupName = "show", IsChecked = true, Tag = "Selection" };
        var sample = new Border { Width = 60, Height = 60, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "sample color" };
        var result = new Button { Width = 60, Height = 60, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "result color" };
        var mode = 0; // 0 sample, 1 add, 2 subtract
        void Draw()
        {
            sample.Background = new SolidColorBrush(Color.FromUInt32(_settings.Color));
            var shifted = ImageAdjustments.ShiftHsl(Color3.From(_settings.Color), V("Hue"), V("Saturation"), V("Lightness"));
            result.Background = new SolidColorBrush(Color.FromRgb((byte)Math.Round(shifted.R * 255), (byte)Math.Round(shifted.G * 255), (byte)Math.Round(shifted.B * 255)));
            if (picture is null) return;
            using var shown = new SKBitmap(picture.Width, picture.Height);
            for (var y = 0; y < picture.Height; y++) for (var x = 0; x < picture.Width; x++)
            {
                var c = picture.GetPixel(x, y);
                if (showImage.IsChecked == true) { shown.SetPixel(x, y, c); continue; }
                var w = (byte)Math.Round(ImageAdjustments.ReplaceWeight(new Color3(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0), _settings) * 255 * c.Alpha / 255);
                shown.SetPixel(x, y, new SKColor(w, w, w));
            }
            preview.Source = Eyedroppers.Thumbnail(shown);
        }
        void Take(SKColor c)
        {
            var color = new Color3(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0);
            if (mode == 0) _settings.Color = 0xff000000u | (uint)c.Red << 16 | (uint)c.Green << 8 | c.Blue;
            else
            {
                // Add widens the fuzziness to take the sample in, Subtract narrows it to leave the sample out.
                var delta = color - Color3.From(_settings.Color); var distance = Math.Sqrt(delta.R * delta.R + delta.G * delta.G + delta.B * delta.B) / Math.Sqrt(3) * 255;
                _settings.Values["Fuzziness"] = Math.Clamp(Math.Round(mode == 1 ? Math.Max(V("Fuzziness"), distance + 4) : Math.Min(V("Fuzziness"), distance - 4)), 0, 200);
                _refresh?.Invoke();
            }
            Draw(); Moved();
        }
        preview.PointerPressed += (_, e) =>
        {
            if (picture is null || preview.Bounds.Width <= 0) return; var at = e.GetPosition(preview);
            // The image is drawn uniformly inside the box: find where in it the click fell.
            var scale = Math.Min(preview.Bounds.Width / picture.Width, preview.Bounds.Height / picture.Height);
            var x = (int)((at.X - (preview.Bounds.Width - picture.Width * scale) / 2) / scale); var y = (int)((at.Y - (preview.Bounds.Height - picture.Height * scale) / 2) / scale);
            if (x < 0 || y < 0 || x >= picture.Width || y >= picture.Height) return;
            Take(picture.GetPixel(x, y));
        };
        showImage.IsCheckedChanged += (_, _) => Draw();
        ToggleButton Dropper(int index, string? mark, string tip)
        {
            var button = new ToggleButton { Content = new EyedropperGlyph(Colors.Transparent, mark), Width = 36, Height = 32, Padding = new Thickness(4), IsChecked = index == 0, Tag = tip };
            ToolTip.SetTip(button, Localize.Text(tip)); return button;
        }
        var droppers = new[] { Dropper(0, null, "Eyedropper Tool"), Dropper(1, "+", "Add to Sample"), Dropper(2, "-", "Subtract from Sample") };
        for (var i = 0; i < 3; i++) { var index = i; droppers[i].Click += (_, _) => { mode = index; for (var j = 0; j < 3; j++) droppers[j].IsChecked = j == index; }; }
        var clusters = new CheckBox { Content = new TextBlock { Text = Localize.Text("Localized Color Clusters") + "(Z)" }, IsEnabled = false, Tag = "Localized Color Clusters" };
        ToolTip.SetTip(clusters, Localize.Text("Colors are chosen by how near they are to the sample, wherever they are in the image."));
        var fuzziness = new SliderRow(this, "Fuzziness", 'F', 0, 200, V("Fuzziness", 40), inline: true, triangle: true, tag: "Fuzziness");
        fuzziness.Changed += () => { _settings.Values["Fuzziness"] = fuzziness.Value; Draw(); Moved(); };
        SliderRow Shift(string label, char key, string valueKey, double min, double max, IBrush track)
        { var row = new SliderRow(this, label, key, min, max, V(valueKey), inline: true, triangle: true, track: track, tag: valueKey); row.Label.MinWidth = 100; row.Changed += () => { _settings.Values[valueKey] = row.Value; Draw(); Moved(); }; return row; }
        var hue = Shift("Hue", 'H', "Hue", -180, 180, AdjustmentKit.Spectrum()); var saturation = Shift("Saturation", 'A', "Saturation", -100, 100, AdjustmentKit.Track(Colors.Gray, Colors.Red));
        var lightness = Shift("Lightness", 'G', "Lightness", -100, 100, AdjustmentKit.Track(Colors.Black, Colors.White));
        result.Click += async (_, _) =>
        {
            // Picking the result color sets the shifts that take the sampled color to it.
            var shifted = ImageAdjustments.ShiftHsl(Color3.From(_settings.Color), V("Hue"), V("Saturation"), V("Lightness"));
            await ColorPickerDialog.Pick(this, "Color Picker (Result Color)", (shifted.R, shifted.G, shifted.B), moved =>
            {
                var from = new SKColor(_settings.Color); var to = new SKColor((byte)Math.Round(moved.Red * 255), (byte)Math.Round(moved.Green * 255), (byte)Math.Round(moved.Blue * 255));
                from.ToHsl(out var h0, out var s0, out var l0); to.ToHsl(out var h1, out var s1, out var l1);
                _settings.Values["Hue"] = Math.Round(((h1 - h0 + 540) % 360) - 180);
                _settings.Values["Saturation"] = Math.Round(Math.Clamp(s1 >= s0 ? (s0 >= 100 ? 0 : (s1 - s0) / (100 - s0) * 100) : (s0 <= 0 ? 0 : (s1 - s0) / s0 * 100), -100, 100));
                _settings.Values["Lightness"] = Math.Round(Math.Clamp(l1 >= l0 ? (l0 >= 100 ? 0 : (l1 - l0) / (100 - l0) * 100) : (l0 <= 0 ? 0 : (l1 - l0) / l0 * 100), -100, 100));
                _refresh?.Invoke(); Draw(); Moved();
            });
        };
        _refresh = () => { _loading = true; fuzziness.Value = V("Fuzziness", 40); hue.Value = V("Hue"); saturation.Value = V("Saturation"); lightness.Value = V("Lightness"); _loading = false; };
        LayerDialogControls.Key(this, 'M', showImage); LayerDialogControls.Key(this, 'C', showSelection);
        Draw();
        extras.Add(AdjustmentKit.Action(this, "Load…", 'L', async () =>
        {
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localize.Text("Load…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Adjustment preset")) { Patterns = ["*.json"] }] });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path || PresetBar.Read(path) is not { } values) return;
            foreach (var pair in values.Where(v => v.Key != "Color")) _settings.Values[pair.Key] = pair.Value;
            if (values.TryGetValue("Color", out var color)) _settings.Color = 0xff000000u | (uint)color & 0xffffff;
            _refresh?.Invoke(); Draw(); Moved();
        }));
        extras.Add(AdjustmentKit.Action(this, "Save…", 'S', async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localize.Text("Save…"), SuggestedFileName = Localize.Text("Replace Color") + ".json", DefaultExtension = ".json" });
            if (file?.TryGetLocalPath() is not { } path) return;
            var values = new Dictionary<string, double>(_settings.Values) { ["Color"] = _settings.Color & 0xffffff };
            try { await AtomicFile.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(values)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }));
        var top = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto") };
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { droppers[0], droppers[1], droppers[2] } });
        var colorLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = Localize.Text("Color") + ":", VerticalAlignment = VerticalAlignment.Center }, sample } };
        Grid.SetColumn(colorLabel, 1); Grid.SetRowSpan(colorLabel, 2); top.Children.Add(colorLabel);
        Grid.SetRow(clusters, 1); top.Children.Add(clusters);
        var shifts = new Grid { ColumnDefinitions = new("*,Auto"), Children = { new StackPanel { Spacing = 8, Children = { hue, saturation, lightness } } } };
        var resultColumn = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 0, 0), Children = { result, new TextBlock { Text = Localize.Text("Result"), HorizontalAlignment = HorizontalAlignment.Center } } };
        Grid.SetColumn(resultColumn, 1); shifts.Children.Add(resultColumn);
        return new StackPanel
        {
            Width = 500, Spacing = 10,
            Children =
            {
                top, fuzziness, new Border { Child = preview, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center, Children = { showSelection, showImage } },
                new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 6) }, AdjustmentKit.Group("Replacement", shifts),
            },
        };
    }
}

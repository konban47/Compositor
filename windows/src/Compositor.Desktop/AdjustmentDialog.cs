using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// Image ▸ Adjustments' Levels, Curves, Exposure, Hue/Saturation, Color Balance, Black &amp; White and Gradient Map, laid
/// out as Photoshop's dialogs are in its screenshots: presets with their ≡ or ⚙ menu at the top, the controls, and OK,
/// Cancel, Auto, Options and the eyedroppers down the right with Preview last. The same dialog edits an adjustment layer
/// of these kinds; the blur, noise and grain layers keep a plain list of sliders.
/// </summary>
internal sealed class AdjustmentDialog : DialogWindow
{
    protected override bool SharesEditPosition => true;
    private readonly LayerAdjustment _start;
    private readonly LayerAdjustment _work;
    private readonly Func<SKBitmap?> _picture;
    private readonly CheckBox _previewBox;
    private PresetBar? _presets;
    private Action? _refresh;
    private LayerAdjustment? _result;
    private bool _loading;
    private double[][]? _histograms;

    /// <summary>Asks for the picture to be shown with these settings as they stand.</summary>
    public Action<LayerAdjustment>? Preview { get; set; }
    internal LayerAdjustment Work => _work;
    internal CurveEditor? Curve { get; private set; }

    private static LayerAdjustment Clone(LayerAdjustment value) =>
        JsonSerializer.Deserialize<LayerAdjustment>(JsonSerializer.Serialize(value, ManifestJson.Options), ManifestJson.Options)!;

    internal AdjustmentDialog(LayerAdjustment start, Func<SKBitmap?>? picture = null)
    {
        _start = start; _work = Clone(start); _picture = picture ?? (() => null);
        var kind = start.Kind;
        Title = Localize.Text(Heading(kind));
        SizeToContent = SizeToContent.WidthAndHeight; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var newer = kind is not (AdjustmentKind.Levels or AdjustmentKind.Curves or AdjustmentKind.GradientMap or AdjustmentKind.ColorBalance);
        _previewBox = AdjustmentKit.Preview(newer);
        _previewBox.IsCheckedChanged += (_, _) => ShowPreview();
        var extras = new List<Control>();
        Control left = kind switch
        {
            AdjustmentKind.Levels => LevelsBody(extras),
            AdjustmentKind.Curves => CurvesBody(extras),
            AdjustmentKind.Exposure => ExposureBody(extras),
            AdjustmentKind.HueSaturation => HueBody(extras),
            AdjustmentKind.BlackWhite => BlackWhiteBody(extras),
            AdjustmentKind.GradientMap => GradientMapBody(),
            AdjustmentKind.ColorBalance => BalanceBody(),
            AdjustmentKind.Invert => new TextBlock { Text = Localize.Text("Invert has no settings: it turns every pixel over."), Width = 320, TextWrapping = TextWrapping.Wrap },
            _ => FilterBody(),
        };
        var actions = AdjustmentKit.Actions(this, Accept, Close, _previewBox, extras.ToArray());
        if (kind == AdjustmentKind.Curves) { actions.Margin = new Thickness(18, 0, 0, 0); }
        Content = AdjustmentKit.Body(left, actions);
    }

    private static string Heading(AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.HueSaturation => "Hue/Saturation", AdjustmentKind.BlackWhite => "Black & White", AdjustmentKind.ColorBalance => "Color Balance",
        AdjustmentKind.GradientMap => "Gradient Map", _ => LayerPlacement.Name(kind),
    };

    /// <summary>The settings moved: the preset list reads Custom and the picture is shown with them.</summary>
    private void Changed() { if (_loading) return; _presets?.MarkCustom(); ShowPreview(); }
    private void ShowPreview() => Preview?.Invoke(_previewBox.IsChecked == true ? Clone(_work) : _start);
    private void Reload(Action load) { _loading = true; try { load(); _refresh?.Invoke(); } finally { _loading = false; } ShowPreview(); }

    /// <summary>The layer's own histograms (composite, red, green, blue), from the picture the dialog was given.</summary>
    private double[][] Histograms()
    {
        if (_histograms is not null) return _histograms;
        var bins = Enumerable.Range(0, 4).Select(_ => new double[256]).ToArray();
        using var picture = _picture();
        if (picture is not null)
            for (var y = 0; y < picture.Height; y++) for (var x = 0; x < picture.Width; x++)
            {
                var c = picture.GetPixel(x, y); if (c.Alpha == 0) continue;
                bins[0][Math.Clamp((int)Math.Round(.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue), 0, 255)]++; bins[1][c.Red]++; bins[2][c.Green]++; bins[3][c.Blue]++;
            }
        return _histograms = bins;
    }

    private Eyedroppers PointDroppers() => new(_picture, ["Sample in image to set black point", "Sample in image to set gray point", "Sample in image to set white point"],
        [Colors.Black, Color.FromRgb(0x80, 0x80, 0x80), Colors.White]);

    private Button AutoButton(bool key = true) => AdjustmentKit.Action(this, "Auto", key ? 'A' : null, () => Auto());
    private Button OptionsButton() => AdjustmentKit.Action(this, "Options…", 'T', async () =>
    {
        if (await AutoColorCorrectionDialog.Ask(this) is not null) Auto();
    });

    /// <summary>Auto: what Auto Color Correction Options ask for, worked out from the picture.</summary>
    private void Auto()
    {
        using var picture = _picture(); if (picture is null) return;
        var options = AutoColorCorrectionDialog.Current;
        var levels = LevelsEdits.Automatic(picture, options.Mode, options.ShadowClip / 100, options.HighlightClip / 100, options.SnapNeutral);
        if (options.Mode != LevelsAuto.Brightness && (options.Shadows != 0xff000000 || options.Highlights != 0xffffffff))
            for (var c = 1; c <= 3; c++)
            {
                // The target colors: the darkest pixels are taken to the shadow color and the lightest to the highlight color.
                var shift = 16 - (c - 1) * 8;
                levels.Ranges[c].OutputBlack = options.Shadows >> shift & 255; levels.Ranges[c].OutputWhite = options.Highlights >> shift & 255;
            }
        Reload(() =>
        {
            if (_work.Kind == AdjustmentKind.Levels) { levels.Channel = _work.Levels.Channel; _work.Levels = levels; }
            else if (_work.Kind == AdjustmentKind.Curves)
            {
                // The levels made into curves: each channel's black and white points, and a middle point for its gamma.
                for (var c = 0; c < 4; c++)
                {
                    var range = levels.Ranges[c];
                    var points = new List<CurvePoint> { new() { X = Math.Round(range.Black), Y = 0 }, new() { X = Math.Round(range.White), Y = 255 } };
                    if (Math.Abs(range.Gamma - 1) > .01)
                    {
                        var mid = Math.Round((range.Black + range.White) / 2);
                        points.Insert(1, new CurvePoint { X = mid, Y = Math.Round(255 * Math.Pow(.5, 1 / range.Gamma)) });
                    }
                    _work.Curves.Channels[c] = points;
                }
            }
        });
        _presets?.MarkCustom();
    }

    // ------------------------------------------------------------------------------------------------ Levels

    private Control LevelsBody(List<Control> extras)
    {
        var channel = ChannelChoice(_work.Levels.Channel == LevelsChannel.Rgb ? 0 : (int)_work.Levels.Channel);
        LevelsChannelRange Range() => _work.Levels.Ranges[Math.Max(0, channel.SelectedIndex)];
        var histogram = new HistogramView(0, 127.5, 255) { Width = 386, Tag = "levels histogram" };
        var black = new ValueBox(0, 253, 0, tag: "Input black"); var gamma = new ValueBox(.01, 9.99, 1, "0.00", tag: "Input gamma"); var white = new ValueBox(2, 255, 255, tag: "Input white");
        var output = new OutputRamp { Width = 386, Tag = "levels output" };
        var outBlack = new ValueBox(0, 255, 0, tag: "Output black"); var outWhite = new ValueBox(0, 255, 255, tag: "Output white");
        var syncing = false;
        void Fill()
        {
            syncing = true; var r = Range();
            black.Value = r.Black; gamma.Value = r.Gamma; white.Value = r.White; outBlack.Value = r.OutputBlack; outWhite.Value = r.OutputWhite;
            histogram.Points[0] = r.Black; histogram.Points[2] = r.White; histogram.Points[1] = HistogramView.GammaPosition(r.Black, r.White, r.Gamma);
            output.Points[0] = r.OutputBlack; output.Points[1] = r.OutputWhite;
            histogram.Bins = Histograms()[Math.Max(0, channel.SelectedIndex)]; histogram.InvalidateVisual(); output.InvalidateVisual(); syncing = false;
        }
        void Set(Action<LevelsChannelRange> change) { if (syncing) return; var r = Range(); change(r); if (r.Black >= r.White - 1) r.White = Math.Min(255, r.Black + 2); Fill(); Changed(); }
        black.ValueChanged += () => Set(r => r.Black = black.Value); gamma.ValueChanged += () => Set(r => r.Gamma = gamma.Value); white.ValueChanged += () => Set(r => r.White = white.Value);
        outBlack.ValueChanged += () => Set(r => r.OutputBlack = outBlack.Value); outWhite.ValueChanged += () => Set(r => r.OutputWhite = outWhite.Value);
        histogram.Moved += () => Set(r => { r.Black = histogram.Points[0]; r.White = histogram.Points[2]; r.Gamma = Math.Round(HistogramView.GammaFrom(histogram.Points[0], histogram.Points[2], histogram.Points[1]), 2); });
        output.Moved += () => Set(r => { r.OutputBlack = output.Points[0]; r.OutputWhite = output.Points[1]; });
        channel.SelectionChanged += (_, _) => { _work.Levels.Channel = (LevelsChannel)Math.Max(0, channel.SelectedIndex); Fill(); };
        _refresh = Fill; Fill();
        ChannelKeys(channel);
        _presets = new PresetBar(this, "Levels", 'E', AdjustmentPresets.LevelsList, () =>
        {
            var values = new Dictionary<string, double>();
            for (var c = 0; c < 4; c++) { var r = _work.Levels.Ranges[c]; values[$"L{c}B"] = r.Black; values[$"L{c}G"] = r.Gamma; values[$"L{c}W"] = r.White; values[$"L{c}OB"] = r.OutputBlack; values[$"L{c}OW"] = r.OutputWhite; }
            return values;
        }, values => Reload(() =>
        {
            for (var c = 0; c < 4; c++)
                _work.Levels.Ranges[c] = new LevelsChannelRange
                {
                    Black = values.GetValueOrDefault($"L{c}B", 0), Gamma = values.GetValueOrDefault($"L{c}G", 1), White = values.GetValueOrDefault($"L{c}W", 255),
                    OutputBlack = values.GetValueOrDefault($"L{c}OB", 0), OutputWhite = values.GetValueOrDefault($"L{c}OW", 255),
                };
        }), gear: true, newer: false);
        var droppers = PointDroppers();
        droppers.Picked += (which, color) => Reload(() =>
        {
            // Each color channel's point is set from the sample, as Photoshop's eyedroppers do.
            var sample = new[] { color.Red, color.Green, color.Blue }; var target = .2126 * color.Red + .7152 * color.Green + .0722 * color.Blue;
            for (var c = 1; c <= 3; c++)
            {
                var r = _work.Levels.Ranges[c]; var s = (double)sample[c - 1];
                if (which == 0) r.Black = Math.Min(s, r.White - 2);
                else if (which == 2) r.White = Math.Max(s, r.Black + 2);
                else if (s > r.Black && s < r.White && target is > 0 and < 255)
                    r.Gamma = Math.Clamp(Math.Log((s - r.Black) / (r.White - r.Black)) / Math.Log(.5), .1, 9.99) * Math.Log(.5) / Math.Log(target / 255);
                r.Gamma = Math.Clamp(r.Gamma, .1, 9.99);
            }
        });
        extras.Add(AutoButton()); extras.Add(OptionsButton()); extras.Add(droppers);
        var boxes = new Grid { ColumnDefinitions = new("Auto,*,Auto,*,Auto"), Margin = new Thickness(60, 8, 10, 0) };
        boxes.Children.Add(black); Grid.SetColumn(gamma, 2); boxes.Children.Add(gamma); Grid.SetColumn(white, 4); boxes.Children.Add(white);
        var outBoxes = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(60, 8, 10, 0) };
        outBoxes.Children.Add(outBlack); Grid.SetColumn(outWhite, 2); outBoxes.Children.Add(outWhite);
        var inner = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                AdjustmentKit.Caption("Input Levels", 'I', true), histogram, boxes,
                new Border { Height = 1, Background = Skin.BorderControlBrush, Margin = new Thickness(0, 10, 0, 6) },
                AdjustmentKit.Caption("Output Levels", 'O', true), output, outBoxes,
            },
        };
        LayerDialogControls.Key(this, 'I', black); LayerDialogControls.Key(this, 'O', outBlack);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { AdjustmentKit.Caption("Channel", null, true), channel } };
        return new StackPanel { Width = 430, Spacing = 4, Children = { _presets, AdjustmentKit.Group("", inner, heading) } };
    }

    /// <summary>RGB, Red, Green, Blue with their Alt+2…5 keys, as Levels and Curves list them.</summary>
    private ComboBox ChannelChoice(int selected)
    {
        var rows = new[] { ("RGB", "Alt+2"), ("Red", "Alt+3"), ("Green", "Alt+4"), ("Blue", "Alt+5") };
        var combo = new ComboBox { Width = 220, Height = 30, Tag = "channel", SelectedIndex = -1 };
        combo.ItemTemplate = new FuncDataTemplate<(string, string)>((row, _) => new Grid
        {
            ColumnDefinitions = new("*,Auto"), MinWidth = 180,
            Children = { new TextBlock { Text = Localize.Text(row.Item1) }, new TextBlock { Text = row.Item2, [Grid.ColumnProperty] = 1, Margin = new Thickness(24, 0, 0, 0) } },
        });
        combo.SelectionBoxItemTemplate = new FuncDataTemplate<(string, string)>((row, _) => new TextBlock { Text = Localize.Text(row.Item1) });
        combo.ItemsSource = rows; combo.SelectedIndex = Math.Clamp(selected, 0, 3);
        return combo;
    }
    private void ChannelKeys(ComboBox channel) => KeyDown += (_, e) =>
    {
        if (e.KeyModifiers != Avalonia.Input.KeyModifiers.Alt || e.Key is < Avalonia.Input.Key.D2 or > Avalonia.Input.Key.D5) return;
        channel.SelectedIndex = e.Key - Avalonia.Input.Key.D2; e.Handled = true;
    };

    // ------------------------------------------------------------------------------------------------ Curves

    private Control CurvesBody(List<Control> extras)
    {
        var channel = ChannelChoice(Math.Clamp((int)_work.Curves.Channel, 0, 3)); channel.Width = 150;
        var curve = Curve = new CurveEditor { Curves = _work.Curves, Channel = channel.SelectedIndex, Width = 384, Height = 384, Tag = "curve" };
        curve.Histogram = Histograms()[channel.SelectedIndex];
        curve.Changed += Changed;
        channel.SelectionChanged += (_, _) => { curve.Channel = Math.Max(0, channel.SelectedIndex); _work.Curves.Channel = (LevelsChannel)curve.Channel; curve.Histogram = Histograms()[curve.Channel]; };
        ChannelKeys(channel);
        _refresh = () => { curve.Curves = _work.Curves; curve.InvalidateVisual(); };
        _presets = new PresetBar(this, "Curves", 'R', AdjustmentPresets.CurvesList, () =>
        {
            var values = new Dictionary<string, double>();
            for (var c = 0; c < 4; c++)
            {
                var points = _work.Curves.Channels[c]; values[$"C{c}N"] = points.Count;
                for (var i = 0; i < points.Count; i++) { values[$"C{c}X{i}"] = points[i].X; values[$"C{c}Y{i}"] = points[i].Y; }
            }
            return values;
        }, values => Reload(() =>
        {
            for (var c = 0; c < 4; c++)
            {
                var count = (int)Math.Clamp(values.GetValueOrDefault($"C{c}N", 2), 2, 32);
                _work.Curves.Channels[c] = Enumerable.Range(0, count).Select(i => new CurvePoint { X = Math.Clamp(values.GetValueOrDefault($"C{c}X{i}", i == 0 ? 0 : 255), 0, 255), Y = Math.Clamp(values.GetValueOrDefault($"C{c}Y{i}", i == 0 ? 0 : 255), 0, 255) })
                    .OrderBy(p => p.X).ToList();
            }
        }), gear: true, newer: false);

        // The curve and pencil tools, the graph with its two ramps, and what is under it.
        var smooth = AdjustmentKit.Action(this, "Smooth", 'M', () => curve.Smooth()); smooth.IsEnabled = false;
        ToggleButton Tool(string kind, bool on) => new() { Content = new CurveToolGlyph(kind), Width = 36, Height = 32, Padding = new Thickness(4), IsChecked = on, Tag = kind };
        var points = Tool("curve-points", true); var pencil = Tool("curve-pencil", false);
        points.Click += (_, _) => { points.IsChecked = true; pencil.IsChecked = false; curve.Pencil = false; smooth.IsEnabled = false; };
        pencil.Click += (_, _) => { pencil.IsChecked = true; points.IsChecked = false; curve.Pencil = true; smooth.IsEnabled = true; };
        ToolTip.SetTip(points, Localize.Text("Edit points to modify the curve")); ToolTip.SetTip(pencil, Localize.Text("Draw to modify the curve"));
        var vertical = new Border { Width = 8, Height = 384, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Colors.White, 1) } } };
        var horizontal = new Border { Width = 384, Height = 8, Background = AdjustmentKit.Track(Colors.Black, Colors.White) };
        var graph = new Grid { ColumnDefinitions = new("Auto,Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 4, RowSpacing = 4 };
        var tools = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { points, pencil } } } };
        var outputLabel = AdjustmentKit.Caption("Output", 'O', true); outputLabel.VerticalAlignment = VerticalAlignment.Bottom; outputLabel.Margin = new Thickness(0, 0, 6, 30);
        var leftColumn = new Grid { RowDefinitions = new("Auto,*"), Children = { tools, outputLabel } }; Grid.SetRow(outputLabel, 1);
        graph.Children.Add(leftColumn); Grid.SetColumn(vertical, 1); graph.Children.Add(vertical); Grid.SetColumn(curve, 2); graph.Children.Add(curve);
        Grid.SetRow(horizontal, 1); Grid.SetColumn(horizontal, 2); graph.Children.Add(horizontal);
        var inputLabel = AdjustmentKit.Caption("Input", 'I', true); Grid.SetRow(inputLabel, 2); Grid.SetColumn(inputLabel, 2); graph.Children.Add(inputLabel);
        var droppers = PointDroppers();
        droppers.Picked += (which, color) => Reload(() =>
        {
            var sample = new[] { color.Red, color.Green, color.Blue }; var target = Math.Round(.2126 * color.Red + .7152 * color.Green + .0722 * color.Blue);
            for (var c = 1; c <= 3; c++)
            {
                var list = _work.Curves.Channels[c]; var s = (double)sample[c - 1];
                if (which == 0) { list[0].X = Math.Min(s, list[^1].X - 2); list[0].Y = 0; }
                else if (which == 2) { list[^1].X = Math.Max(s, list[0].X + 2); list[^1].Y = 255; }
                else if (s > list[0].X && s < list[^1].X && list.Count < 32) { list.RemoveAll(p => p != list[0] && p != list[^1] && Math.Abs(p.X - s) < 4); list.Add(new CurvePoint { X = s, Y = target }); list.Sort((a, b) => a.X.CompareTo(b.X)); }
                list.RemoveAll(p => p != list[0] && p != list[^1] && (p.X <= list[0].X || p.X >= list[^1].X));
            }
        });
        var onImage = new Button { Content = new CurveToolGlyph("on-image"), Width = 36, Height = 32, Padding = new Thickness(4), IsEnabled = false, Background = Brushes.Transparent, BorderThickness = default, Tag = "on-image" };
        ToolTip.SetTip(onImage, Localize.Text("Click and drag in the image needs the canvas, which this dialog's modal window keeps closed; drag on the curve instead."));
        var clipping = new CheckBox { Content = new TextBlock { Text = Localize.Text("Show Clipping") + "(W)" }, IsEnabled = false, Tag = "Show Clipping" };
        ToolTip.SetTip(clipping, Localize.Text("Clipping display is not available; watch the histogram behind the curve instead."));
        var under = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 14 };
        under.Children.Add(onImage); Grid.SetColumn(droppers, 2); under.Children.Add(droppers); Grid.SetColumn(clipping, 3); clipping.VerticalAlignment = VerticalAlignment.Center; under.Children.Add(clipping);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { AdjustmentKit.Caption("Channel", null, true), channel } };
        var plot = AdjustmentKit.Group("", new StackPanel { Children = { graph, under } }, heading);

        // Curve Display Options, as the middle column.
        var light = new RadioButton { Content = new TextBlock { Text = Localize.Text("Light (0-255)") + "(L)" }, IsChecked = true, GroupName = "amount", Tag = "Light" };
        var pigment = new RadioButton { Content = new TextBlock { Text = Localize.Text("Pigment/Ink %") + "(G)" }, GroupName = "amount", Tag = "Pigment" };
        light.IsCheckedChanged += (_, _) => curve.Pigment = pigment.IsChecked == true; pigment.IsCheckedChanged += (_, _) => curve.Pigment = pigment.IsChecked == true;
        ToggleButton Grid4(string kind, int divisions) { var button = new ToggleButton { Content = new CurveToolGlyph(kind), Width = 34, Height = 32, Padding = new Thickness(4), Tag = kind, IsChecked = divisions == 4 }; ToolTip.SetTip(button, Localize.Text(divisions == 4 ? "Simple grid" : "Detailed grid")); return button; }
        var simple = Grid4("grid-4", 4); var detailed = Grid4("grid-10", 10);
        simple.Click += (_, _) => { simple.IsChecked = true; detailed.IsChecked = false; curve.Divisions = 4; };
        detailed.Click += (_, _) => { detailed.IsChecked = true; simple.IsChecked = false; curve.Divisions = 10; };
        CheckBox Show(string text, char key, Action<bool> set) { var box = new CheckBox { Content = new TextBlock { Text = Localize.Text(text) + $"({key})" }, IsChecked = true, Tag = text }; box.IsCheckedChanged += (_, _) => set(box.IsChecked == true); LayerDialogControls.Key(this, key, box); return box; }
        LayerDialogControls.Key(this, 'L', light); LayerDialogControls.Key(this, 'G', pigment);
        var middle = new StackPanel
        {
            Width = 226, Margin = new Thickness(26, 0, 0, 0), Spacing = 2,
            Children =
            {
                AdjustmentKit.Group("Show Amount for:", new StackPanel { Spacing = 10, Children = { light, pigment } }),
                AdjustmentKit.Group("Grid size:", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { simple, detailed } }),
                AdjustmentKit.Group("Show:", new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        Show("Channel Overlays", 'V', on => curve.ShowOverlays = on), Show("Histogram", 'H', on => curve.ShowHistogram = on),
                        Show("Baseline", 'B', on => curve.ShowBaseline = on), Show("Intersection Line", 'N', on => curve.ShowIntersection = on),
                    },
                }),
            },
        };
        extras.Add(smooth); extras.Add(AutoButton()); extras.Add(OptionsButton());
        var left = new StackPanel { Spacing = 6, Children = { _presets, plot } };
        _presets.Width = 520;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { left, middle } };
    }

    // ------------------------------------------------------------------------------------------------ Exposure

    private Control ExposureBody(List<Control> extras)
    {
        var e = _work.ExposureSettings ??= new ExposureSettings();
        var exposure = new SliderRow(this, "Exposure", 'E', -20, 20, e.Exposure, "0.00", signed: true, tag: "Exposure");
        var offset = new SliderRow(this, "Offset", 'O', -.5, .5, e.Offset, "0.0000", signed: true, tag: "Offset");
        var gamma = new SliderRow(this, "Gamma Correction", 'G', .01, 9.99, e.Gamma, "0.00", tag: "Gamma");
        exposure.Changed += () => { e.Exposure = exposure.Value; Changed(); }; offset.Changed += () => { e.Offset = offset.Value; Changed(); }; gamma.Changed += () => { e.Gamma = gamma.Value; Changed(); };
        _refresh = () => { exposure.Value = e.Exposure; offset.Value = e.Offset; gamma.Value = e.Gamma; };
        _presets = new PresetBar(this, "Exposure", 'R', AdjustmentPresets.ExposureList, () => new() { ["Exposure"] = e.Exposure, ["Offset"] = e.Offset, ["Gamma"] = e.Gamma },
            values => Reload(() => { e.Exposure = values.GetValueOrDefault("Exposure"); e.Offset = values.GetValueOrDefault("Offset"); e.Gamma = values.GetValueOrDefault("Gamma", 1); }),
            tooltip: "Choose an Exposure preset");
        var droppers = PointDroppers();
        droppers.Picked += (which, color) => Reload(() =>
        {
            // Black sets the offset that takes the sample to black; gray and white the exposure that takes it to middle gray or white.
            static double Linear(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
            var level = Linear((.2126 * color.Red + .7152 * color.Green + .0722 * color.Blue) / 255);
            if (which == 0) e.Offset = Math.Clamp(-level * Math.Pow(2, e.Exposure), -.5, .5);
            else if (level > 1e-6) e.Exposure = Math.Clamp(Math.Log2((which == 1 ? Linear(.5) : 1) / level), -20, 20);
        });
        extras.Add(droppers);
        return new StackPanel { Width = 360, Spacing = 8, Children = { _presets, exposure, offset, gamma } };
    }

    // ------------------------------------------------------------------------------------------------ Hue/Saturation

    private Control HueBody(List<Control> extras)
    {
        var settings = _work.HsvSettings ??= _work.ResolvedHSV;
        var dots = new ColorDots([(null, "Master"), (Colors.Red, "Reds"), (Colors.Yellow, "Yellows"), (Colors.Lime, "Greens"), (Colors.Cyan, "Cyans"), (Colors.Blue, "Blues"), (Colors.Magenta, "Magentas")]) { Tag = "ranges" };
        dots.Index = HueBand.Ranges.IndexOf(settings.Range);
        var hue = new SliderRow(this, "Hue", 'H', -180, 180, 0, track: AdjustmentKit.Track(Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red), tag: "Hue");
        var saturation = new SliderRow(this, "Saturation", 'A', -100, 100, 0, track: AdjustmentKit.Track(Color.FromRgb(0x80, 0x80, 0x80), Colors.Red), tag: "Saturation");
        var lightness = new SliderRow(this, "Lightness", 'I', -100, 100, 0, track: AdjustmentKit.Track(Colors.Black, Color.FromRgb(0x80, 0x80, 0x80), Colors.White), tag: "Lightness");
        var colorize = new CheckBox { Content = new TextBlock { Text = Localize.Text("Colorize") + "(O)" }, IsChecked = settings.Colorize, Tag = "Colorize" };
        var invert = new ToggleButton { Content = Localize.Text("Invert") + "(I)", MinWidth = 110, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "Invert Range" };
        ToolTip.SetTip(invert, Localize.Text("Adjusts every color except the chosen range"));
        var before = new Border { Height = 18, Background = AdjustmentKit.Spectrum(), Tag = "before" };
        var after = new Image { Height = 18, Stretch = Stretch.Fill, Tag = "after" };
        var degrees = new Grid { ColumnDefinitions = new("*,Auto") }; var leftDegrees = new TextBlock { Foreground = Skin.SecondaryBrush }; var rightDegrees = new TextBlock { Foreground = Skin.SecondaryBrush };
        degrees.Children.Add(leftDegrees); Grid.SetColumn(rightDegrees, 1); degrees.Children.Add(rightDegrees);
        var syncing = false;
        void Fill()
        {
            syncing = true;
            var current = settings.Current; var master = settings.Range == ColorRange.Master;
            colorize.IsChecked = settings.Colorize; dots.IsEnabled = !settings.Colorize; invert.IsEnabled = !master && !settings.Colorize; invert.IsChecked = settings.InvertRange;
            hue.Box.IsEnabled = hue.Slider.IsEnabled = true;
            hue.Value = current.Hue; saturation.Value = current.Saturation; lightness.Value = current.Lightness;
            var band = settings.Band; leftDegrees.Text = master ? "0° / 0°" : $"{band.FalloffStart:0}° / {band.RangeStart:0}°"; rightDegrees.Text = master ? "0° / 0°" : $"{band.RangeEnd:0}° / {band.FalloffEnd:0}°";
            syncing = false; DrawAfter();
        }
        void DrawAfter()
        {
            // The spectrum as the settings leave it, under the spectrum as it was.
            using var bitmap = new SKBitmap(360, 1);
            for (var x = 0; x < 360; x++) { var c = SKColor.FromHsv((x + 180) % 360, 100, 100); bitmap.SetPixel(x, 0, c); }
            var row = new byte[360 * 4];
            for (var x = 0; x < 360; x++) { var c = bitmap.GetPixel(x, 0); row[x * 4] = c.Red; row[x * 4 + 1] = c.Green; row[x * 4 + 2] = c.Blue; row[x * 4 + 3] = 255; }
            AdjustmentOperators.Apply(_work, 1, row, 360, 1, 360 * 4);
            for (var x = 0; x < 360; x++) bitmap.SetPixel(x, 0, new SKColor(row[x * 4], row[x * 4 + 1], row[x * 4 + 2]));
            after.Source = Eyedroppers.Thumbnail(bitmap);
        }
        before.Background = AdjustmentKit.Track(Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan);
        void Values() { if (syncing) return; settings.Set(hue.Value, saturation.Value, lightness.Value); DrawAfter(); Changed(); }
        hue.Changed += Values; saturation.Changed += Values; lightness.Changed += Values;
        dots.Selected += index => { settings.Range = HueBand.Ranges[index]; Fill(); };
        colorize.IsCheckedChanged += (_, _) =>
        {
            if (syncing) return; settings.Colorize = colorize.IsChecked == true;
            if (settings.Colorize) { settings.Range = ColorRange.Master; dots.Index = 0; if (settings.Current.IsIdentity) settings.Set(0, 25, 0); }
            Fill(); Changed();
        };
        invert.IsCheckedChanged += (_, _) => { if (syncing) return; settings.InvertRange = invert.IsChecked == true; DrawAfter(); Changed(); };
        _refresh = () => { settings = _work.HsvSettings ??= _work.ResolvedHSV; dots.Index = HueBand.Ranges.IndexOf(settings.Range); Fill(); };
        Fill();
        _presets = new PresetBar(this, "Hue Saturation", 'E', AdjustmentPresets.HueSaturationList, () =>
        {
            var values = new Dictionary<string, double> { ["Colorize"] = settings.Colorize ? 1 : 0 };
            for (var r = 0; r < 7; r++) { var a = settings.Adjustments.Find(HueBand.Ranges[r]) ?? new RangeAdjustment(); values[$"H{r}"] = a.Hue; values[$"S{r}"] = a.Saturation; values[$"L{r}"] = a.Lightness; }
            return values;
        }, values => Reload(() =>
        {
            var next = new HueSaturationSettings { Colorize = values.GetValueOrDefault("Colorize") != 0 };
            for (var r = 0; r < 7; r++) { next.Range = HueBand.Ranges[r]; next.Set(values.GetValueOrDefault($"H{r}"), values.GetValueOrDefault($"S{r}"), values.GetValueOrDefault($"L{r}")); }
            next.Range = ColorRange.Master; _work.HsvSettings = next;
        }), tooltip: "Choose a Hue/Saturation preset");
        var hand = new Button { Content = new CurveToolGlyph("on-image"), Width = 34, Height = 34, Padding = new Thickness(4), IsEnabled = false, Background = Brushes.Transparent, BorderThickness = default, Tag = "on-image" };
        ToolTip.SetTip(hand, Localize.Text("Click and drag in the image needs the canvas, which this dialog's modal window keeps closed; use the eyedroppers instead."));
        var droppers = new Eyedroppers(_picture, ["Choose a range from the image", "Add to the range", "Subtract from the range"], [Colors.Transparent, Colors.Transparent, Colors.Transparent], [null!, "+", "-"]);
        droppers.Picked += (which, color) => Reload(() =>
        {
            color.ToHsv(out var h, out var s, out _); if (s < 5) return;
            if (settings.Range == ColorRange.Master || which == 0)
            {
                // The range whose band holds the sample's hue, as Photoshop's first eyedropper picks one.
                var nearest = HueBand.Ranges.Skip(1).OrderBy(r => Math.Abs(((HueBand.Default(r).RangeStart + HueBand.Default(r).RangeEnd + (r == ColorRange.Reds ? 360 : 0)) / 2 % 360 - h + 540) % 360 - 180)).First();
                settings.Range = nearest; dots.Index = HueBand.Ranges.IndexOf(nearest);
                if (which == 0) return;
            }
            var band = settings.Band; double Distance(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);
            if (which == 1)
            {
                // Add: the band's nearer edge moves out to take the sample in.
                if (Distance(h, band.RangeStart) < Distance(h, band.RangeEnd)) { band.RangeStart = h; band.FalloffStart = (h - 30 + 360) % 360; } else { band.RangeEnd = h; band.FalloffEnd = (h + 30) % 360; }
            }
            else if (Distance(h, band.RangeStart) < Distance(h, band.RangeEnd)) band.RangeStart = (h + 5) % 360; else band.RangeEnd = (h - 5 + 360) % 360;
            settings.Bands.Set(settings.Range, band);
        });
        LayerDialogControls.Key(this, 'O', colorize);
        var dotRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { hand, dots } };
        var tools = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 10 };
        tools.Children.Add(colorize); Grid.SetColumn(droppers, 2); tools.Children.Add(droppers); Grid.SetColumn(invert, 3); tools.Children.Add(invert);
        return new StackPanel
        {
            Width = 416, Spacing = 10,
            Children =
            {
                _presets, dotRow, hue, saturation, lightness, tools,
                new TextBlock { Text = Localize.Text("Before - After") }, new StackPanel { Spacing = 2, Children = { before, after } }, degrees,
            },
        };
    }

    // ------------------------------------------------------------------------------------------------ Black & White

    private Control BlackWhiteBody(List<Control> extras)
    {
        var mix = _work.BlackWhiteSettings ??= new BlackWhiteSettings();
        SliderRow Row(string label, char key, double value, Color ink) =>
            new(this, label, key, -200, 300, value, suffix: "%", track: AdjustmentKit.Track(Colors.Black, ink, Colors.White), tag: label);
        var reds = Row("Reds", 'R', mix.Reds, Colors.Red); var yellows = Row("Yellows", 'Y', mix.Yellows, Colors.Yellow); var greens = Row("Greens", 'G', mix.Greens, Colors.Lime);
        var cyans = Row("Cyans", 'C', mix.Cyans, Colors.Cyan); var blues = Row("Blues", 'B', mix.Blues, Colors.Blue); var magentas = Row("Magentas", 'M', mix.Magentas, Colors.Magenta);
        var tint = new CheckBox { Content = new TextBlock { Text = Localize.Text("Tint") + "(T)" }, IsChecked = mix.Tint, Tag = "Tint" };
        var swatch = new Button { Width = 70, Height = 34, Tag = "tint swatch", BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1) };
        var hue = new SliderRow(this, "Hue", 'H', 0, 360, mix.TintHue, suffix: "°", track: AdjustmentKit.Spectrum(), tag: "Tint Hue");
        var saturation = new SliderRow(this, "Saturation", 'S', 0, 100, mix.TintSaturation, suffix: "%", track: AdjustmentKit.Track(Color.FromRgb(0x80, 0x80, 0x80), Colors.Orange), tag: "Tint Saturation");
        var rows = new[] { reds, yellows, greens, cyans, blues, magentas };
        var syncing = false;
        void Swatch()
        {
            var color = SKColor.FromHsl((float)mix.TintHue, (float)mix.TintSaturation, 50);
            swatch.Background = new SolidColorBrush(mix.Tint ? Color.FromRgb(color.Red, color.Green, color.Blue) : Color.FromRgb(0x77, 0x72, 0x6C));
            hue.IsEnabled = saturation.IsEnabled = swatch.IsEnabled = mix.Tint;
        }
        void Fill()
        {
            syncing = true; reds.Value = mix.Reds; yellows.Value = mix.Yellows; greens.Value = mix.Greens; cyans.Value = mix.Cyans; blues.Value = mix.Blues; magentas.Value = mix.Magentas;
            tint.IsChecked = mix.Tint; hue.Value = mix.TintHue; saturation.Value = mix.TintSaturation; syncing = false; Swatch();
        }
        void Read()
        {
            if (syncing) return;
            mix.Reds = reds.Value; mix.Yellows = yellows.Value; mix.Greens = greens.Value; mix.Cyans = cyans.Value; mix.Blues = blues.Value; mix.Magentas = magentas.Value;
            mix.Tint = tint.IsChecked == true; mix.TintHue = hue.Value; mix.TintSaturation = saturation.Value; Swatch(); Changed();
        }
        foreach (var row in rows.Append(hue).Append(saturation)) row.Changed += Read;
        tint.IsCheckedChanged += (_, _) => Read();
        swatch.Click += async (_, _) =>
        {
            var start = SKColor.FromHsl((float)mix.TintHue, (float)mix.TintSaturation, 50);
            // The tint follows the picker as it moves, and a Cancel reports the color it opened on.
            await ColorPickerDialog.Pick(this, "Color Picker (Tint Color)", (start.Red / 255.0, start.Green / 255.0, start.Blue / 255.0), c =>
            {
                new SKColor((byte)Math.Round(c.Red * 255), (byte)Math.Round(c.Green * 255), (byte)Math.Round(c.Blue * 255)).ToHsl(out var h, out var s, out _);
                mix.TintHue = Math.Round(h); mix.TintSaturation = Math.Round(s); Fill(); Changed();
            });
        };
        mix = _work.BlackWhiteSettings; _refresh = () => { mix = _work.BlackWhiteSettings ??= new BlackWhiteSettings(); Fill(); }; Fill();
        LayerDialogControls.Key(this, 'T', tint);
        _presets = new PresetBar(this, "Black and White", 'E', AdjustmentPresets.BlackWhiteList, () => new()
        {
            ["Reds"] = mix.Reds, ["Yellows"] = mix.Yellows, ["Greens"] = mix.Greens, ["Cyans"] = mix.Cyans, ["Blues"] = mix.Blues, ["Magentas"] = mix.Magentas,
            ["Tint"] = mix.Tint ? 1 : 0, ["TintHue"] = mix.TintHue, ["TintSaturation"] = mix.TintSaturation,
        }, values => Reload(() => _work.BlackWhiteSettings = new BlackWhiteSettings
        {
            Reds = values.GetValueOrDefault("Reds", 40), Yellows = values.GetValueOrDefault("Yellows", 60), Greens = values.GetValueOrDefault("Greens", 40), Cyans = values.GetValueOrDefault("Cyans", 60),
            Blues = values.GetValueOrDefault("Blues", 20), Magentas = values.GetValueOrDefault("Magentas", 80), Tint = values.GetValueOrDefault("Tint") != 0,
            TintHue = values.GetValueOrDefault("TintHue", 42), TintSaturation = values.GetValueOrDefault("TintSaturation", 20),
        }), tooltip: "Choose a Black & White preset");
        extras.Add(AdjustmentKit.Action(this, "Auto", null, () => Reload(() =>
        {
            // Auto: each hue's slider from how bright that hue already is against the picture's mean, so the grays spread out.
            using var picture = _picture(); if (picture is null) return;
            var sums = new double[6]; var counts = new double[6]; double all = 0, n = 0;
            for (var y = 0; y < picture.Height; y++) for (var x = 0; x < picture.Width; x++)
            {
                var c = picture.GetPixel(x, y); if (c.Alpha == 0) continue; c.ToHsv(out var h, out var s, out var v);
                var luma = (.2126 * c.Red + .7152 * c.Green + .0722 * c.Blue) / 255; all += luma; n++;
                if (s < 10) continue; var band = (int)((h + 30) % 360 / 60); sums[band] += luma * s / 100; counts[band] += s / 100;
            }
            if (n == 0) return; var mean = all / n; var values = new double[6];
            for (var i = 0; i < 6; i++) values[i] = counts[i] > 0 ? Math.Round(Math.Clamp(50 + (sums[i] / counts[i] - mean) * 200, -200, 300)) : new[] { 40, 60, 40, 60, 20, 80 }[i];
            var target = _work.BlackWhiteSettings!; target.Reds = values[0]; target.Yellows = values[1]; target.Greens = values[2]; target.Cyans = values[3]; target.Blues = values[4]; target.Magentas = values[5];
        })));
        var tintRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { tint, swatch } };
        var panel = new StackPanel { Width = 362, Spacing = 4, Children = { _presets } };
        foreach (var row in rows) panel.Children.Add(row);
        panel.Children.Add(tintRow); panel.Children.Add(hue); panel.Children.Add(saturation);
        return panel;
    }

    // ------------------------------------------------------------------------------------------------ Color Balance

    private Control BalanceBody()
    {
        var b = _work.ColorBalanceSettings ??= new ColorBalanceSettings();
        var tone = 1;
        var boxes = new[] { new ValueBox(-100, 100, 0, tag: "Cyan Red"), new ValueBox(-100, 100, 0, tag: "Magenta Green"), new ValueBox(-100, 100, 0, tag: "Yellow Blue") };
        ToneSlider Slider(Color from, Color to, int i) => new() { Minimum = -100, Maximum = 100, Width = 260, TrackBrush = AdjustmentKit.Track(from, to), Tag = "balance " + i };
        var sliders = new[] { Slider(Colors.Cyan, Colors.Red, 0), Slider(Colors.Magenta, Colors.Lime, 1), Slider(Colors.Yellow, Colors.Blue, 2) };
        double Get(int i) => (tone, i) switch
        {
            (0, 0) => b.ShadowCyanRed, (0, 1) => b.ShadowMagentaGreen, (0, 2) => b.ShadowYellowBlue,
            (1, 0) => b.MidCyanRed, (1, 1) => b.MidMagentaGreen, (1, 2) => b.MidYellowBlue,
            (2, 0) => b.HighlightCyanRed, (2, 1) => b.HighlightMagentaGreen, _ => b.HighlightYellowBlue,
        };
        void Put(int i, double v)
        {
            switch (tone, i)
            {
                case (0, 0): b.ShadowCyanRed = v; break; case (0, 1): b.ShadowMagentaGreen = v; break; case (0, 2): b.ShadowYellowBlue = v; break;
                case (1, 0): b.MidCyanRed = v; break; case (1, 1): b.MidMagentaGreen = v; break; case (1, 2): b.MidYellowBlue = v; break;
                case (2, 0): b.HighlightCyanRed = v; break; case (2, 1): b.HighlightMagentaGreen = v; break; default: b.HighlightYellowBlue = v; break;
            }
        }
        var syncing = false;
        void Fill() { syncing = true; for (var i = 0; i < 3; i++) { boxes[i].Value = Get(i); sliders[i].Value = Get(i); } syncing = false; }
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            boxes[i].ValueChanged += () => { if (syncing) return; Put(index, boxes[index].Value); Fill(); Changed(); };
            sliders[i].ValueChanged += () => { if (syncing) return; Put(index, Math.Round(sliders[index].Value)); Fill(); Changed(); };
        }
        var levels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { AdjustmentKit.Caption("Color Levels", 'L', true) } };
        foreach (var box in boxes) levels.Children.Add(box);
        LayerDialogControls.Key(this, 'L', boxes[0]);
        Grid Scale(string from, string to, ToneSlider slider)
        {
            var grid = new Grid { ColumnDefinitions = new("70,*,70"), ColumnSpacing = 8 };
            grid.Children.Add(new TextBlock { Text = Localize.Text(from), HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(slider, 1); grid.Children.Add(slider); var right = new TextBlock { Text = Localize.Text(to) }; Grid.SetColumn(right, 2); grid.Children.Add(right); return grid;
        }
        var balance = new StackPanel { Spacing = 12, Children = { levels, Scale("Cyan", "Red", sliders[0]), Scale("Magenta", "Green", sliders[1]), Scale("Yellow", "Blue", sliders[2]) } };
        RadioButton Tone(string text, char key, int value) { var radio = new RadioButton { Content = new TextBlock { Text = Localize.Text(text) + $"({key})" }, GroupName = "tone", IsChecked = value == tone, Tag = text }; radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { tone = value; Fill(); } }; LayerDialogControls.Key(this, key, radio); return radio; }
        var preserve = new CheckBox { Content = new TextBlock { Text = Localize.Text("Preserve Luminosity") + "(V)" }, IsChecked = b.PreserveLuminosity, Tag = "Preserve Luminosity" };
        preserve.IsCheckedChanged += (_, _) => { b.PreserveLuminosity = preserve.IsChecked == true; Changed(); }; LayerDialogControls.Key(this, 'V', preserve);
        var tones = new StackPanel { Spacing = 10, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { Tone("Shadows", 'S', 0), Tone("Midtones", 'D', 1), Tone("Highlights", 'H', 2) } }, preserve } };
        _refresh = Fill; Fill();
        return new StackPanel { Width = 460, Children = { AdjustmentKit.Group("Color Balance", balance), AdjustmentKit.Group("Tone Balance", tones) } };
    }

    // ------------------------------------------------------------------------------------------------ Gradient Map

    private Control GradientMapBody()
    {
        // A new map starts Smooth, as Photoshop's does; one already saved keeps its own method.
        var map = _work.GradientMapSettings ??= new GradientMapSettings { Method = GradientMethod.Smooth };
        var swatch = new Border { Height = 30, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "gradient swatch", Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        void Paint()
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            for (var i = 0; i <= 32; i++) { var c = map.Sample(i / 32.0); brush.GradientStops.Add(new GradientStop(Color.FromRgb((byte)Math.Round(c.Red * 255), (byte)Math.Round(c.Green * 255), (byte)Math.Round(c.Blue * 255)), i / 32.0)); }
            swatch.Background = brush;
        }
        var picker = new GradientPresetPicker(this, preset =>
        {
            map.Stops = preset.Stops.Select(s => new GradientStopValue(s.Position, s.Color | 0xff000000)).ToArray();
            var first = new SKColor(preset.Stops[0].Color); var last = new SKColor(preset.Stops[^1].Color);
            map.Shadows = AdjustmentColor.From(first.Red / 255.0, first.Green / 255.0, first.Blue / 255.0); map.Highlights = AdjustmentColor.From(last.Red / 255.0, last.Green / 255.0, last.Blue / 255.0);
            Paint(); Changed();
        }, () => map.Stops ?? [new(0, GradientMapSettings.Pack(map.Shadows)), new(1, GradientMapSettings.Pack(map.Highlights))]);
        var open = new Button { Content = "⌄", Width = 22, Height = 30, Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "gradient picker" };
        open.Click += (_, _) => picker.ShowAt(open);
        swatch.PointerPressed += (_, _) => picker.ShowAt(swatch);
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Children = { swatch } }; Grid.SetColumn(open, 1); row.Children.Add(open);
        var dither = new CheckBox { Content = new TextBlock { Text = Localize.Text("Dither") + "(D)" }, IsChecked = map.Dither == true, Tag = "Dither" };
        var reverse = new CheckBox { Content = new TextBlock { Text = Localize.Text("Reverse") + "(R)" }, IsChecked = map.Reversed, Tag = "Reverse" };
        var methods = new[] { "Perceptual", "Linear", "Classic", "Smooth", "Stripes" };
        var method = new ComboBox { ItemsSource = methods.Select(Localize.Text).ToArray(), SelectedIndex = (int)(map.Method ?? GradientMethod.Linear), Width = 100, Tag = "Method" };
        dither.IsCheckedChanged += (_, _) => { map.Dither = dither.IsChecked == true ? true : null; Changed(); };
        reverse.IsCheckedChanged += (_, _) => { map.Reversed = reverse.IsChecked == true; Paint(); Changed(); };
        method.SelectionChanged += (_, _) => { map.Method = method.SelectedIndex == (int)GradientMethod.Linear && map.Stops is null ? null : (GradientMethod)method.SelectedIndex; Paint(); Changed(); };
        LayerDialogControls.Key(this, 'D', dither); LayerDialogControls.Key(this, 'R', reverse);
        Paint();
        var options = new StackPanel { Spacing = 10, Children = { dither, reverse, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { AdjustmentKit.Caption("Method", null, true), method } } } };
        return new StackPanel { Width = 432, Children = { AdjustmentKit.Group("Gradient Used for Grayscale Mapping", row), AdjustmentKit.Group("Gradient Options", options) } };
    }

    // ------------------------------------------------------------------------------------------------ Blur, noise, grain layers

    private Control FilterBody()
    {
        var panel = new StackPanel { Width = 360, Spacing = 6 };
        void Row(string label, double min, double max, double value, string format, Action<double> set)
        { var row = new SliderRow(this, label, null, min, max, value, format, tag: label); row.Changed += () => { set(row.Value); Changed(); }; panel.Children.Add(row); }
        void Check(string label, bool value, Action<bool> set)
        { var box = new CheckBox { Content = Localize.Text(label), IsChecked = value, Tag = label }; box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Changed(); }; panel.Children.Add(box); }
        switch (_work.Kind)
        {
            case AdjustmentKind.Grain:
                var g = _work.GrainSettings ??= new GrainSettings();
                Row("Amount", 0, 100, g.Amount, "0", v => g.Amount = v); Row("Size", .5, 20, g.Size, "0.0", v => g.Size = v); Row("Roughness", 0, 100, g.Roughness, "0", v => g.Roughness = v); break;
            case AdjustmentKind.AddNoise:
                Row("Amount", .1, 400, _work.ResolvedNoiseAmount, "0.0", v => _work.NoiseAmount = v);
                Check("Gaussian", _work.ResolvedNoiseGaussian, v => _work.NoiseGaussian = v); Check("Monochromatic", _work.ResolvedNoiseMonochromatic, v => _work.NoiseMonochromatic = v); break;
            case AdjustmentKind.GaussianBlur: Row("Radius", .1, 250, _work.GaussianRadius, "0.0", v => _work.BlurRadius = v); break;
            case AdjustmentKind.MotionBlur:
                Row("Angle", -90, 90, _work.ResolvedMotionAngle, "0", v => _work.MotionAngle = v); Row("Distance", 1, 2000, _work.ResolvedMotionDistance, "0", v => _work.MotionDistance = v); break;
        }
        return panel;
    }

    private void Accept()
    {
        // A value typed and not yet entered is read first.
        foreach (var box in this.GetVisualDescendants().OfType<ValueBox>()) box.Commit();
        _result = _work.IsValid ? Clone(_work) : null;
        Close();
    }

    /// <summary>The settings to put back on the layer, or null when the dialog was dismissed.</summary>
    public static async Task<LayerAdjustment?> Ask(Window owner, LayerAdjustment start, Action<LayerAdjustment>? preview = null, Func<SKBitmap?>? picture = null)
    {
        var dialog = new AdjustmentDialog(start, picture) { Preview = preview };
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}

/// <summary>The curve tools, grid sizes and on-image tool that Curves and Hue/Saturation draw as small pictures.</summary>
internal sealed class CurveToolGlyph(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (kind)
        {
            case "curve-points":
                var wave = new StreamGeometry();
                using (var g = wave.Open()) { g.BeginFigure(new Point(2, 15), false); g.CubicBezierTo(new Point(8, 2), new Point(13, 20), new Point(20, 7)); }
                context.DrawGeometry(null, pen, wave);
                foreach (var p in new[] { new Point(2, 15), new Point(11, 11), new Point(20, 7) }) context.FillRectangle(Skin.LabelBrush, new Rect(p.X - 2, p.Y - 2, 4, 4));
                break;
            case "curve-pencil":
                context.DrawLine(pen, new Point(5, 17), new Point(16, 6)); context.DrawLine(pen, new Point(8, 19), new Point(19, 8));
                context.DrawLine(pen, new Point(5, 17), new Point(4, 20)); context.DrawLine(pen, new Point(4, 20), new Point(8, 19)); context.DrawLine(pen, new Point(16, 6), new Point(19, 8));
                break;
            case "grid-4": case "grid-10":
                var n = kind == "grid-4" ? 2 : 4; context.DrawRectangle(null, pen, new Rect(3, 3, 16, 16));
                for (var i = 1; i < n; i++) { var at = 3 + 16.0 * i / n; context.DrawLine(pen, new Point(at, 3), new Point(at, 19)); context.DrawLine(pen, new Point(3, at), new Point(19, at)); }
                break;
            default:
                // A hand with a two-way arrow: click and drag in the image.
                context.DrawEllipse(null, pen, new Point(13, 13), 5, 5);
                context.DrawLine(pen, new Point(3, 6), new Point(9, 6)); context.DrawLine(pen, new Point(3, 6), new Point(5, 4)); context.DrawLine(pen, new Point(3, 6), new Point(5, 8));
                context.DrawLine(pen, new Point(3, 11), new Point(8, 11)); context.DrawLine(pen, new Point(8, 11), new Point(6, 9)); context.DrawLine(pen, new Point(8, 11), new Point(6, 13));
                break;
        }
    }
}

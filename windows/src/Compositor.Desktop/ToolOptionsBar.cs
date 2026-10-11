using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;

namespace Compositor.Desktop;

/// <summary>Which of the Shape tool's amounts was asked for.</summary>
internal enum ShapeSetting
{
    CornerRadius,
    LineWidth,
}

/// <summary>Which of the magic wand's amounts was asked for.</summary>
internal enum WandSetting
{
    Tolerance,
    SampleSize,
}

/// <summary>
/// The strip of options above the canvas, as the Mac keeps above its own: what the tool in hand can be told.
/// The port's amounts are reached from the Tools menu and answered through prompts, so the bar shows each one
/// as its name and its value and hands the asking back to the window, rather than growing a second set of
/// sliders beside the first.
/// </summary>
internal sealed partial class ToolOptionsBar : Border
{
    /// <summary>How tall the strip is, matching Photoshop's compact tool options bar (36 points).</summary>
    private const double StripHeight = 36;

    private readonly ToolOptions _options;
    private readonly TextBlock _title = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeight.SemiBold,
        FontSize = 11.5,
        Foreground = Skin.LabelBrush,
        Margin = new Thickness(0, 0, 14, 0),
    };
    private readonly TextBlock _zoom = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 11,
        Foreground = Skin.SecondaryBrush,
    };
    private readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal, Spacing = 12 };

    /// <summary>Every row, by the name of the tools it belongs to: a name may cover several controls.</summary>
    private readonly Dictionary<string, List<Control>> _named = [];

    /// <summary>A setting was changed by the bar, so the window pushes it to the canvas and says what it is.</summary>
    public event Action? Changed;

    /// <summary>One of the brush's amounts was asked for, by the name the window's own prompt knows it by.</summary>
    public event Action<BrushSetting>? BrushSettingAsked;

    /// <summary>One of the magic wand's amounts was asked for.</summary>
    public event Action<WandSetting>? WandSettingAsked;

    /// <summary>One of the Shape tool's amounts was asked for.</summary>
    public event Action<ShapeSetting>? ShapeSettingAsked;

    /// <summary>A colour swatch was clicked: true for the foreground, false for the gradient's background.</summary>
    public event Action<bool>? ColourAsked;

    /// <summary>A layer is to be flipped, across its own middle, one way or the other.</summary>
    public event Action<bool>? FlipAsked;

    /// <summary>Which of the crop ratios was chosen, by its place in the window's own list.</summary>
    public event Action<int>? CropRatioChosen;
    public event Action? CropApplied;
    public event Action? CropCancelled;

    /// <summary>The Type tool's text is to be edited.</summary>
    public event Action? TextAsked;

    public ToolOptionsBar(ToolOptions options)
    {
        _options = options;
        Height = StripHeight;
        Background = Skin.ChromeBrush;
        BorderBrush = Skin.BorderSubtleBrush;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(10, 0, 10, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(_title);
        row.Children.Add(_cells);
        Child = new ScrollViewer
        {
            Content = row, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Build(); BuildExtended(); BuildProfessional();
    }

    /// <summary>
    /// Shows the rows the tool in hand has. Called when the tool or the panel selection changes and never on a
    /// repaint: a row rebuilt under the pointer's own drag would drop the drag.
    /// </summary>
    public void Show(Tool tool, bool hasDocument, bool maskSelected)
    {
        var brush = ToolCatalog.HasRoundCursor(tool);
        _loading = true;
        try
        {
            On("brush", brush && hasDocument);
            _hardness.IsVisible = ToolCatalog.IsBrush(tool) && tool != Tool.Pencil && hasDocument;
            _fill.IsVisible = ToolCatalog.IsBrush(tool) && hasDocument;
            On("opacity", (brush && tool != Tool.QuickSelection || tool is Tool.Bucket or Tool.MagicEraser) && hasDocument);
            On("mode", tool == Tool.Brush);
            On("heal", tool == Tool.Heal);
            On("clone", tool is Tool.Clone or Tool.HealingBrush);
            On("mask", tool == Tool.Brush && maskSelected);
            On("blur", tool == Tool.Blur);
            On("marqueeShape", tool is Tool.Marquee or Tool.Ellipse);
            On("lasso", tool is Tool.Lasso or Tool.Polygon);
            On("eye", tool == Tool.Eyedropper);
            On("wand", tool is Tool.Wand or Tool.QuickSelection);
            _sampleSize.IsVisible = _contiguous.IsVisible = tool == Tool.Wand;
            On("selectionSample", tool is Tool.Wand or Tool.Object);
                On("gradient", tool == Tool.Gradient);
            On("shape", ToolCatalog.IsShape(tool));
            On("viewRotation", tool == Tool.RotateView);
            On("corner", ToolCatalog.IsShape(tool) && _options.Shape == ShapeKind.Rectangle);
            On("linewidth", ToolCatalog.IsPen(tool) || ToolCatalog.IsShape(tool) && _options.Shape == ShapeKind.Line);
            On("crop", tool == Tool.Crop);
            On("transform", tool == Tool.Move && hasDocument);
            On("type", ToolCatalog.IsType(tool)); ShowExtended(tool); ShowProfessional(tool);
            On("history", tool is Tool.HistoryBrush or Tool.ArtHistory && hasDocument);
            On("path", ToolCatalog.IsPathEditor(tool) && hasDocument);
            On("zoom", tool is Tool.Pan or Tool.Zoom or Tool.RotateView);
            _title.Text = Localize.Text(Names.TryGetValue(tool, out var name) ? name : ToolCatalog.Title(tool));
            // The marquee's shape and the lasso's kind *are* the tool in hand, so the bar follows the tool
            // rather than the other way round: picking one here asks for the tool the window already has.
            _marqueeShape.SelectedIndex = tool == Tool.Ellipse ? 1 : 0;
            _lassoKind.SelectedIndex = tool == Tool.Polygon ? 1 : 0;
            Refresh();
            _shapeKind.SelectedIndex = Array.IndexOf(ShapeTools, tool);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The zoom the window is showing, for the Pan and Zoom rows.</summary>
    public void ShowZoom(double percent) => _zoom.Text = Localize.Format($"zoom {percent:0}%");

    /// <summary>Whether a row is on show, which is what the self check reads to see the gating works.</summary>
    internal bool Shows(string name) => _named.TryGetValue(name, out var cells) && cells[0].IsVisible;

    /// <summary>
    /// The Anti-alias tick as a press on it would leave it, for the check: a tick's own write goes through the
    /// same Set every other control's does, so this drives the whole path rather than reaching past it.
    /// </summary>
    internal void PressAntialias(bool on) => _antialias.IsChecked = on;

    /// <summary>The names of the rows on show, in the order they were built.</summary>
    internal IEnumerable<string> Showing => _named.Where(entry => entry.Value[0].IsVisible).Select(entry => entry.Key);

    /// <summary>Every row's value read back off its own control, which is what the bar is showing.</summary>
    private void Refresh()
    {
        _size.Content = Localize.Format($"Size {_options.Brush.Diameter:0}");
        _hardness.Content = Localize.Format($"Hardness {_options.Brush.Hardness * 100:0}%");
        _opacity.Content = Localize.Format($"Opacity {_options.Brush.Opacity * 100:0}%");
        _blurRadius.Content = Localize.Format($"Radius {_options.Brush.BlurRadius:0.#}");
        _tolerance.Content = Localize.Format($"Tolerance {_options.Wand.Tolerance}");
        _sampleSize.Content = Localize.Format($"Sample {_options.Wand.Radius}");
        _corner.Content = Localize.Format($"Radius {_options.ShapeCornerRadius:0}");
        _lineWidth.Content = Localize.Format($"Width {_options.ShapeLineWidth:0}");
        _fill.Show(_options.Brush.Red, _options.Brush.Green, _options.Brush.Blue);
        _gradientFill.Show(_options.GradientBackground.Red, _options.GradientBackground.Green,
            _options.GradientBackground.Blue);
        _brushMode.SelectedIndex = _options.Erase ? 1 : 0;
        _maskPaint.SelectedIndex = _options.PaintOnMask ? 1 : 0;
        _healMode.SelectedIndex = (int)_options.Brush.Healing;
        _aligned.IsChecked = _options.Brush.CloneAligned;
        _cloneAll.SelectedIndex = _options.Brush.CloneAllLayers ? 1 : 0;
        _contiguous.IsChecked = _options.Wand.Contiguous;
        _antialias.IsChecked = _options.SelectionAntialiased;
        _sampleRing.IsChecked = _options.ShowsSampleRing;
        _wandAll.SelectedIndex = _options.WandAllLayers ? 1 : 0;
        _gradientKind.SelectedIndex = (int)_options.Gradient;
        _gradientTo.SelectedIndex = _options.GradientToBackground ? 1 : 0;
        _gradientReversed.IsChecked = _options.GradientReversed;
    }

    /// <summary>Whether the marquee is drawing an ellipse, which is the tool in hand rather than a setting.</summary>
    private bool _marqueeEllipse;

    /// <summary>The bar told which shape the marquee is drawing, since that is the tool and not a setting.</summary>
    public void ShowMarquee(bool ellipse)
    {
        _loading = true;
        try
        {
            _marqueeEllipse = ellipse;
            _marqueeShape.SelectedIndex = ellipse ? 1 : 0;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The marquee's shape was picked from the bar, which is asking for the tool that draws it.</summary>
    public event Action<bool>? MarqueeShapeChosen;

    /// <summary>The lasso's kind was picked from the bar, which is the same as picking the tool.</summary>
    public event Action<bool>? LassoKindChosen;


    private readonly Button _size = new();
    private readonly Button _hardness = new();
    private readonly Button _opacity = new();
    private readonly Button _blurRadius = new();
    private readonly Button _tolerance = new();
    private readonly Button _sampleSize = new();
    private readonly Button _corner = new();
    private readonly Button _lineWidth = new();
    private readonly Swatch _fill = new();
    private readonly Swatch _gradientFill = new();
    private readonly ComboBox _brushMode = new();
    private readonly ComboBox _maskPaint = new();
    private readonly ComboBox _healMode = new();
    private readonly CheckBox _aligned = new() { Content = Localize.Text("Aligned") };
    private readonly ComboBox _cloneAll = new();
    private readonly ComboBox _marqueeShape = new();
    private readonly ComboBox _lassoKind = new();
    private readonly CheckBox _contiguous = new() { Content = Localize.Text("Contiguous") };
    private readonly CheckBox _antialias = new() { Content = Localize.Text("Anti-alias") };
    private readonly CheckBox _sampleRing = new() { Content = Localize.Text("Sample Ring") };
    private readonly ComboBox _wandAll = new();
    private readonly ComboBox _shapeKind = new();
    private readonly ComboBox _gradientKind = new();
    private readonly ComboBox _gradientTo = new();
    private readonly CheckBox _gradientReversed = new() { Content = Localize.Text("Reverse") };
    private readonly ComboBox _cropRatio = new() { Width = 150 };
    private readonly Button _cropApply = new() { Content = Localize.Text("Apply Crop") };
    private readonly Button _cropCancel = new() { Content = Localize.Text("Cancel") };
    private readonly Button _flipH = new() { Content = Localize.Text("Flip H") };
    private readonly Button _flipV = new() { Content = Localize.Text("Flip V") };
    private readonly Button _editText = new() { Content = Localize.Text("Edit Text…") };
    private readonly Button _historySource = new() { Content = Localize.Text("Set Source…") };
    private readonly Button _pathHint = new() { Content = Localize.Text("Drag the mask's nodes") };

    /// <summary>What each tool's strip is called, which is the Mac's own title.</summary>
    private static readonly Dictionary<Tool, string> Names = new()
    {
        [Tool.Pan] = "Pan",
        [Tool.Move] = "Transform",
        [Tool.Marquee] = "Marquee",
        [Tool.Ellipse] = "Elliptical marquee",
        [Tool.Lasso] = "Lasso",
        [Tool.Polygon] = "Polygonal lasso",
        [Tool.Object] = "Object Selection (Tab: Wand)",
        [Tool.Wand] = "Magic wand",
        [Tool.Brush] = "Brush",
        [Tool.Clone] = "Clone stamp",
        [Tool.Blur] = "Blur brush",
        [Tool.Liquify] = "Liquify brush",
        [Tool.Smudge] = "Smudge brush",
        [Tool.Heal] = "Spot healing",
        [Tool.Eyedropper] = "Eyedropper",
        [Tool.Type] = "Type",
        [Tool.Crop] = "Crop",
        [Tool.Shape] = "Shape",
        [Tool.Gradient] = "Gradient",
        [Tool.HistoryBrush] = "History brush",
        [Tool.Path] = "Path",
    };

    private static void StyleOptionButton(Button button)
    {
        button.Height = 24;
        button.Padding = new Thickness(7, 2);
        button.FontSize = 11;
        button.CornerRadius = new CornerRadius(7);
        button.Background = Skin.SurfaceControlBrush;
        button.BorderBrush = Skin.BorderControlBrush;
        button.BorderThickness = new Thickness(1);
        button.Foreground = Skin.LabelBrush;
        button.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private static void StyleOptionCombo(ComboBox combo)
    {
        combo.Height = 24;
        combo.FontSize = 11;
        combo.CornerRadius = new CornerRadius(7);
        combo.Background = Skin.SurfaceControlBrush;
        combo.BorderBrush = Skin.BorderControlBrush;
        combo.BorderThickness = new Thickness(1);
        combo.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private static void StyleOptionCheck(CheckBox check)
    {
        check.FontSize = 11;
        check.Foreground = Skin.LabelBrush;
        check.VerticalAlignment = VerticalAlignment.Center;
        check.Margin = new Thickness(0, 0, 4, 0);
    }

    private void Build()
    {
        // The brush's own amounts, each a button showing its value that opens the window's own prompt.
        foreach (var (setting, button) in new (BrushSetting, Button)[]
                 {
                     (BrushSetting.Size, _size), (BrushSetting.Hardness, _hardness), (BrushSetting.Opacity, _opacity),
                     (BrushSetting.Radius, _blurRadius),
                 })
        {
            var which = setting;
            StyleOptionButton(button);
            button.Click += (_, _) => BrushSettingAsked?.Invoke(which);
        }
        foreach (var (setting, button) in new (WandSetting, Button)[]
                 {
                     (WandSetting.Tolerance, _tolerance), (WandSetting.SampleSize, _sampleSize),
                 })
        {
            var which = setting;
            StyleOptionButton(button);
            button.Click += (_, _) => WandSettingAsked?.Invoke(which);
        }
        StyleOptionButton(_corner);
        StyleOptionButton(_lineWidth);
        _corner.Click += (_, _) => ShapeSettingAsked?.Invoke(ShapeSetting.CornerRadius);
        _lineWidth.Click += (_, _) => ShapeSettingAsked?.Invoke(ShapeSetting.LineWidth);
        _fill.Click += (_, _) => ColourAsked?.Invoke(true);
        _gradientFill.Click += (_, _) => ColourAsked?.Invoke(false);

        StyleOptionCombo(_brushMode);
        _brushMode.ItemsSource = new[] { "Paint", "Erase" };
        _brushMode.SelectedIndex = 0;
        _brushMode.SelectionChanged += (_, _) => Set(ref _options.Erase, _brushMode.SelectedIndex == 1);

        StyleOptionCombo(_maskPaint);
        _maskPaint.ItemsSource = new[] { "Paint Black · Hide", "Paint White · Reveal" };
        _maskPaint.SelectedIndex = 0;
        _maskPaint.SelectionChanged += (_, _) => Set(ref _options.PaintOnMask, _maskPaint.SelectedIndex == 1);

        StyleOptionCombo(_healMode);
        _healMode.ItemsSource = new[] { "Content-Aware", "Create Texture", "Proximity Match" };
        _healMode.SelectedIndex = 0;
        _healMode.SelectionChanged += (_, _) =>
        {
            var healed = _options.Brush with { Healing = (HealingMode)Math.Max(0, _healMode.SelectedIndex) };
            Set(ref _options.Brush, healed);
        };

        StyleOptionCheck(_aligned);
        _aligned.IsCheckedChanged += (_, _) =>
        {
            var brush = _options.Brush with { CloneAligned = _aligned.IsChecked == true };
            Set(ref _options.Brush, brush);
        };
        StyleOptionCheck(_antialias);
        _antialias.IsChecked = _options.SelectionAntialiased;
        _antialias.IsCheckedChanged += (_, _) =>
            Set(ref _options.SelectionAntialiased, _antialias.IsChecked == true);

        StyleOptionCheck(_sampleRing);
        _sampleRing.IsChecked = _options.ShowsSampleRing;
        _sampleRing.IsCheckedChanged += (_, _) =>
            Set(ref _options.ShowsSampleRing, _sampleRing.IsChecked == true);

        StyleOptionCombo(_cloneAll);
        _cloneAll.ItemsSource = new[] { "Sample: This Layer", "Sample: All Layers" };
        _cloneAll.SelectedIndex = 0;
        _cloneAll.SelectionChanged += (_, _) =>
        {
            var brush = _options.Brush with { CloneAllLayers = _cloneAll.SelectedIndex == 1 };
            Set(ref _options.Brush, brush);
        };

        StyleOptionCombo(_marqueeShape);
        _marqueeShape.ItemsSource = new[] { "Rectangle", "Ellipse" };
        _marqueeShape.SelectedIndex = 0;
        _marqueeShape.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _marqueeEllipse = _marqueeShape.SelectedIndex == 1;
            MarqueeShapeChosen?.Invoke(_marqueeEllipse);
        };

        StyleOptionCombo(_lassoKind);
        _lassoKind.ItemsSource = new[] { "Freehand", "Polygonal" };
        _lassoKind.SelectedIndex = 0;
        _lassoKind.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            LassoKindChosen?.Invoke(_lassoKind.SelectedIndex == 1);
        };

        StyleOptionCheck(_contiguous);
        _contiguous.IsCheckedChanged += (_, _) =>
        {
            var wand = _options.Wand with { Contiguous = _contiguous.IsChecked == true };
            Set(ref _options.Wand, wand);
        };

        StyleOptionCombo(_wandAll);
        _wandAll.ItemsSource = new[] { "Sample: This Layer", "Sample: All Layers" };
        _wandAll.SelectedIndex = 0;
        _wandAll.SelectionChanged += (_, _) => Set(ref _options.WandAllLayers, _wandAll.SelectedIndex == 1);

        StyleOptionCombo(_shapeKind);
        _shapeKind.ItemsSource = ShapeTools.Select(t => Localize.Text(ToolCatalog.Title(t))).ToArray();
        _shapeKind.SelectedIndex = 0;
        _shapeKind.SelectionChanged += (_, _) =>
        {
            if (!_loading && _shapeKind.SelectedIndex >= 0) ShapeToolChosen?.Invoke(ShapeTools[_shapeKind.SelectedIndex]);
        };

        StyleOptionCombo(_gradientKind);
        _gradientKind.ItemsSource = new[] { "Linear", "Radial", "Angle", "Reflected", "Diamond" };
        _gradientKind.SelectedIndex = 0;
        _gradientKind.SelectionChanged += (_, _) => Set(ref _options.Gradient, (GradientShape)Math.Max(0, _gradientKind.SelectedIndex));

        StyleOptionCombo(_gradientTo);
        _gradientTo.ItemsSource = new[] { "To nothing", "To the background color" };
        _gradientTo.SelectedIndex = 0;
        _gradientTo.SelectionChanged += (_, _) => Set(ref _options.GradientToBackground, _gradientTo.SelectedIndex == 1);

        StyleOptionCheck(_gradientReversed);
        _gradientReversed.IsCheckedChanged += (_, _) => Set(ref _options.GradientReversed, _gradientReversed.IsChecked == true);

        StyleOptionCombo(_cropRatio);
        _cropRatio.SelectionChanged += (_, _) => CropRatioChosen?.Invoke(_cropRatio.SelectedIndex);

        StyleOptionButton(_cropApply);
        _cropApply.Background = Skin.AccentBrush;
        _cropApply.Foreground = Brushes.White;
        _cropApply.Click += (_, _) => CropApplied?.Invoke();

        StyleOptionButton(_cropCancel);
        _cropCancel.Click += (_, _) => CropCancelled?.Invoke();

        StyleOptionButton(_flipH);
        _flipH.Click += (_, _) => FlipAsked?.Invoke(true);

        StyleOptionButton(_flipV);
        _flipV.Click += (_, _) => FlipAsked?.Invoke(false);

        StyleOptionButton(_editText);
        _editText.Click += (_, _) => TextAsked?.Invoke();

        StyleOptionButton(_historySource);
        _historySource.Click += (_, _) => HistorySourceAsked?.Invoke();

        StyleOptionButton(_pathHint);
        _pathHint.Click += (_, _) => PathAsked?.Invoke();

        Cell("brush", _size);
        Cell("brush", _hardness);
        Cell("opacity", _opacity);
        Cell("brush", _fill);
        Cell("mode", _brushMode);
        Cell("mask", _maskPaint);
        Cell("heal", _healMode);
        // The Blur brush's own Radius, which the Mac's brush controls show for that tool alone.
        Cell("blur", _blurRadius);
        Cell("clone", _aligned);
        Cell("clone", _cloneAll);
        Cell("marqueeShape", _marqueeShape);
        Cell("lasso", _lassoKind);
        Cell("lasso", _antialias);
        // The eyedropper's own row, which the Mac keeps in the picker's controls: the ring is the only thing that
        // tool can be told.
        Cell("eye", _sampleRing);
        Cell("wand", _tolerance);
        Cell("wand", _sampleSize);
        Cell("wand", _contiguous);
        Cell("selectionSample", _wandAll);
        Cell("gradient", _gradientKind);
        Cell("gradient", _gradientTo);
        Cell("gradient", _gradientFill);
        Cell("gradient", _gradientReversed);
        Cell("shape", _shapeKind);
        var shapeOptions = new Button { Content = Localize.Text("Shape Options…") };
        shapeOptions.Click += (_, _) => ShapeOptionsAsked?.Invoke(); Cell("shape", shapeOptions);
        var resetView = new Button { Content = Localize.Text("Reset View Rotation") };
        resetView.Click += (_, _) => ResetViewAsked?.Invoke(); Cell("viewRotation", resetView);
        Cell("corner", _corner);
        Cell("linewidth", _lineWidth);
        Cell("crop", _cropRatio);
        Cell("crop", _cropApply);
        Cell("crop", _cropCancel);
        Cell("transform", _flipH);
        Cell("transform", _flipV);
        Cell("type", _editText);
        Cell("history", _historySource);
        Cell("path", _pathHint);
        Cell("zoom", _zoom);
    }

    /// <summary>The History Brush's source was asked for, so the window picks the state to paint back.</summary>
    public event Action? HistorySourceAsked;

    /// <summary>The Path tool asked for a vector mask to edit on the current layer.</summary>
    public event Action? PathAsked;
    public event Action<Tool>? ShapeToolChosen;
    public event Action? ShapeOptionsAsked, ResetViewAsked;
    private static readonly Tool[] ShapeTools = [Tool.Shape, Tool.ShapeEllipse, Tool.Line, Tool.Triangle, Tool.PolygonShape, Tool.Star, Tool.CustomShape];

    /// <summary>Shows what the History Brush is painting back from.</summary>
    public void ShowHistorySource(string label) => _historySource.Content = Localize.Format($"Source: {label}");


    /// <summary>The crop ratios the window offers, so the bar's own list is the same list.</summary>
    public void ShowCropRatios(IReadOnlyList<string> ratios)
    {
        _cropRatio.ItemsSource = ratios;
        _cropRatio.SelectedIndex = 0;
    }

    /// <summary>Which ratio the crop frame is on, so the bar follows the menu and the frame follows the bar.</summary>
    public void ShowCropRatio(int index) => _cropRatio.SelectedIndex = index;

    /// <summary>One option changed by the bar: the window is told, and the values are read back.</summary>
    private void Set<T>(ref T field, T value)
    {
        // While the bar is filling itself in, a widget written by Show reads back as a change. It is not one:
        // the window is the one that asked for the filling, and answering it would be a loop.
        if (_loading) return;
        field = value;
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Whether the bar is filling itself in, which is when its own writes are not changes.</summary>
    private bool _loading;

    /// <summary>One option's place in the strip, under its tool's name, kept so Show can hide it.</summary>
    private void Cell(string name, Control control)
    {
        _cells.Children.Add(control);
        if (!_named.TryGetValue(name, out var cells))
        {
            cells = [];
            _named[name] = cells;
        }
        cells.Add(control);
    }

    /// <summary>Whether a row and every control in it are on show.</summary>
    private void On(string name, bool shown)
    {
        if (!_named.TryGetValue(name, out var cells)) return;
        foreach (var cell in cells) cell.IsVisible = shown;
    }

    /// <summary>A clickable colour of the bar's own, which the window finds out about rather than owns.</summary>
    private sealed class Swatch : Button
    {
        public Swatch()
        {
            Width = 44;
            Height = 22;
            Padding = new Thickness(0);
            CornerRadius = new CornerRadius(7);
            BorderThickness = new Thickness(1);
            BorderBrush = Skin.BorderControlBrush;
            VerticalAlignment = VerticalAlignment.Center;
        }

        public void Show(double red, double green, double blue) => Background = new SolidColorBrush(
            Color.FromRgb((byte)Math.Clamp(Math.Round(red * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(green * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(blue * 255), 0, 255)));
    }
}

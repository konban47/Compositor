using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;
namespace Compositor.Desktop;

internal sealed class BrushSettingsEditor : Grid, IDisposable
{
    internal static readonly string[] Sections = ["Brush Tip Shape", "Shape Dynamics", "Scattering", "Texture", "Dual Brush", "Color Dynamics", "Transfer", "Brush Pose", "Noise", "Wet Edges", "Build-up", "Smoothing", "Protect Texture"];
    internal static readonly string[] EnabledFields = ["", "ShapeEnabled", "ScatterEnabled", "TextureEnabled", "DualEnabled", "ColorEnabled", "TransferEnabled", "PoseEnabled", "Noise", "WetEdges", "BuildUp", "SmoothingEnabled", "ProtectTexture"];
    internal static readonly string[][] Fields = [
        ["Tip", "FlipX", "FlipY", "SpacingEnabled"],
        ["SizeJitter", "SizeControl", "MinimumDiameter", "TiltScale", "AngleJitter", "AngleControl", "RoundnessJitter", "RoundnessControl", "MinimumRoundness", "FlipXJitter", "FlipYJitter", "Projection", "FadeSteps"],
        ["Scatter", "ScatterBothAxes", "ScatterControl", "Count", "CountJitter", "CountControl", "FadeSteps"],
        ["Texture", "TextureImage", "TextureInvert", "TextureScale", "TextureBrightness", "TextureContrast", "TextureEachTip", "TextureMode", "TextureDepth", "TextureMinimumDepth", "TextureJitter", "TextureControl", "FadeSteps"],
        ["DualMode", "DualFlip", "DualTip", "DualSize", "DualSpacing", "DualScatter", "DualBothAxes", "DualCount"],
        ["ColorEachTip", "ForegroundBackground", "ColorControl", "HueJitter", "SaturationJitter", "BrightnessJitter", "Purity", "FadeSteps"],
        ["OpacityJitter", "OpacityControl", "MinimumOpacity", "FlowJitter", "FlowControl", "MinimumFlow", "WetJitter", "WetControl", "MinimumWet", "MixJitter", "MixControl", "MinimumMix", "FadeSteps"],
        ["TiltX", "OverrideTiltX", "TiltY", "OverrideTiltY", "Rotation", "OverrideRotation", "Pressure", "OverridePressure"], [], [], [], ["PulledString", "CatchUp", "CatchUpOnRelease", "AdjustForZoom"], [] ];
    private readonly Func<BrushSettings> _get;
    private readonly Action<BrushSettings> _set;
    private readonly Func<bool> _isMixer;
    private readonly Action _picker, _save, _texture;
    private readonly StackPanel _categories = new() { Spacing = 0 };
    private readonly StackPanel _fields = new() { Spacing = 12, Margin = new Thickness(12, 8, 8, 8) };
    private readonly Grid _layout = new() { ColumnDefinitions = new ColumnDefinitions("144,*") };
    private readonly Image _preview = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly List<Action> _refreshControls = [], _refreshCategories = [];
    private readonly ScrollViewer _fieldScroll;
    private WriteableBitmap? _previewBitmap;
    private bool _busy, _dirty, _disposed, _updating;
    private int _section;
    private PaintBrushOptions Options => _get().Painting ?? new();
    internal static string SplitName(string name) => Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    internal BrushSettingsEditor(Func<BrushSettings> get, Action<BrushSettings> set, Func<bool> isMixer, Action picker, Action save, Action texture)
    {
        _get = get; _set = set; _isMixer = isMixer; _picker = picker; _save = save; _texture = texture;
        RowDefinitions = new RowDefinitions("*,145,30"); ClipToBounds = true;
        var sidebar = new StackPanel { Margin = new Thickness(4, 8, 0, 4) };
        var brushes = new Button { Content = Localize.Text("Brushes"), Tag = "brush-presets", HorizontalAlignment = HorizontalAlignment.Center, MinHeight = 28, Margin = new Thickness(0, 0, 0, 14) };
        brushes.Click += (_, _) => _picker(); sidebar.Children.Add(brushes); sidebar.Children.Add(_categories);
        _layout.Children.Add(new ScrollViewer { Content = sidebar, AllowAutoHide = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _fieldScroll = new ScrollViewer { Content = _fields, AllowAutoHide = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Tag = "brush-parameter-scroll" };
        Grid.SetColumn(_fieldScroll, 1); _layout.Children.Add(_fieldScroll); Children.Add(_layout);
        var preview = new Border { Background = Skin.ScrollThumbBrush, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1), Margin = new Thickness(4, 4, 4, 2), Padding = new Thickness(16, 12), Child = _preview, Tag = "brush-stroke-preview" };
        Grid.SetRow(preview, 1); Children.Add(preview);
        var footer = new Border { BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 1), Tag = "brush-settings-footer" };
        var plus = new Button { Content = new BrushSettingsGlyph(false, true), Tag = "save-brush-preset", Width = 28, Height = 26, MinHeight = 0, MinWidth = 0, Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(plus, Localize.Text("New Brush")); Avalonia.Automation.AutomationProperties.SetName(plus, Localize.Text("New Brush")); plus.Click += (_, _) => _save(); footer.Child = plus;
        Grid.SetRow(footer, 2); Children.Add(footer);
        SizeChanged += (_, _) => { _layout.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(Bounds.Width * .31, 148, 172)); RowDefinitions[1].Height = new GridLength(Math.Clamp(Bounds.Height * .19, 80, 150)); };
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RenderPreview(); };
        SelectSection(0);
    }
    private void Set(string property, object value)
    {
        if (_updating) return;
        var next = Options with { }; typeof(PaintBrushOptions).GetProperty(property)!.SetValue(next, value); _set(_get() with { Painting = next }); Refresh();
    }
    internal void Refresh()
    {
        if (_updating || _disposed) return; _updating = true;
        try { foreach (var update in _refreshCategories) update(); foreach (var update in _refreshControls) update(); }
        finally { _updating = false; }
        UpdatePreview();
    }
    internal void SelectSection(int index)
    {
        _section = Math.Clamp(index, 0, Sections.Length - 1); _categories.Children.Clear(); _refreshCategories.Clear();
        for (var i = 0; i < Sections.Length; i++)
        {
            var n = i; var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,23"), MinHeight = 30 };
            var face = new Border { Child = row, BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1, 0, 1, 1), Background = i == index ? Skin.SurfaceControlBrush : Brushes.Transparent };
            if (i > 0)
            {
                var enabled = new CheckBox { MinHeight = 24, MinWidth = 0, Width = 22, Padding = new Thickness(0), Tag = "brush-enable-" + i, VerticalAlignment = VerticalAlignment.Center };
                Avalonia.Automation.AutomationProperties.SetName(enabled, Localize.Text(Sections[i]));
                enabled.IsCheckedChanged += (_, _) => { if (_updating) return; Set(EnabledFields[n], enabled.IsChecked == true); if (enabled.IsChecked == true) SelectSection(n); };
                _refreshCategories.Add(() => enabled.IsChecked = (bool)typeof(PaintBrushOptions).GetProperty(EnabledFields[n])!.GetValue(Options)!); row.Children.Add(enabled);
            }
            var glyph = new BrushSettingsGlyph(false);
            var locked = new ToggleButton { Content = glyph, Width = 22, MinWidth = 0, Height = 26, MinHeight = 0, Padding = new Thickness(3), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Tag = "brush-lock-" + i };
            ToolTip.SetTip(locked, Localize.Text("Keep This Section When Changing Presets")); Avalonia.Automation.AutomationProperties.SetName(locked, Localize.Text("Keep This Section When Changing Presets") + ": " + Localize.Text(Sections[i]));
            locked.IsCheckedChanged += (_, _) => { if (!_updating) Set("LockedSections", locked.IsChecked == true ? Options.LockedSections | (1UL << n) : Options.LockedSections & ~(1UL << n)); };
            _refreshCategories.Add(() => { locked.IsChecked = (Options.LockedSections & (1UL << n)) != 0; glyph.Locked = locked.IsChecked == true; glyph.InvalidateVisual(); }); Grid.SetColumn(locked, 2); row.Children.Add(locked);
            var button = new Button { Content = new TextBlock { Text = Localize.Text(Sections[i]), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, MinWidth = 0, MinHeight = 28, Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Tag = "brush-section-" + i };
            ToolTip.SetTip(button, Localize.Text(Sections[i])); button.Click += (_, _) => { if (n > 0) Set(EnabledFields[n], true); SelectSection(n); }; Grid.SetColumn(button, 1); row.Children.Add(button); _categories.Children.Add(face);
        }
        _fields.Children.Clear(); _refreshControls.Clear(); _fieldScroll.Offset = default;
        if (index == 0)
        {
            _fields.Children.Add(TipGrid(Options.Tip, tip => { Set("Tip", tip); _set(_get() with { TipImage = null, Hardness = tip == 0 ? 0 : 1 }); Refresh(); }));
            BrushNumber("Size", b => b.Diameter, (b, n) => b with { Diameter = n }, 1, 5000, " px");
            AddField("FlipX"); AddField("FlipY");
            _fields.Children.Add(new BrushTipAngleControl(() => _get(), b => { _set(b); Refresh(); }));
            BrushNumber("Angle", b => b.Angle, (b, n) => b with { Angle = n }, -180, 180, "°");
            BrushNumber("Roundness", b => b.Roundness * 100, (b, n) => b with { Roundness = n / 100 }, 1, 100, "%");
            BrushNumber("Hardness", b => b.Hardness * 100, (b, n) => b with { Hardness = n / 100 }, 0, 100, "%");
            AddField("SpacingEnabled");
            BrushNumber("Spacing", b => b.SpacingFraction > 0 ? b.SpacingFraction * 100 : 25, (b, n) => b with { SpacingFraction = n / 100 }, 1, 1000, "%", () => Options.SpacingEnabled);
        }
        else if (index == 6)
        {
            foreach (var (jitter, control, minimum) in new[] { ("OpacityJitter", "OpacityControl", "MinimumOpacity"), ("FlowJitter", "FlowControl", "MinimumFlow"), ("WetJitter", "WetControl", "MinimumWet"), ("MixJitter", "MixControl", "MinimumMix") })
            {
                var group = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6), Tag = "brush-transfer-" + jitter };
                var mixerOnly = jitter is "WetJitter" or "MixJitter";
                _refreshControls.Add(() => { group.IsEnabled = Options.TransferEnabled && (!mixerOnly || _isMixer()); group.Opacity = group.IsEnabled ? 1 : .4; });
                AddNumber(jitter, group); AddControl(control, group); AddNumber(minimum, group, compact: true, enabled: () => (BrushControl)typeof(PaintBrushOptions).GetProperty(control)!.GetValue(Options)! != BrushControl.Off); _fields.Children.Add(group);
            }
        }
        else foreach (var field in Fields[index]) if (field != "FadeSteps") AddField(field);
        if (index == 3) { var load = new Button { Content = Localize.Text("Load Texture…"), Tag = "brush-load-texture" }; load.Click += (_, _) => _texture(); _fields.Children.Add(load); }
        if (index is 8 or 9 or 10 or 12) _fields.Children.Add(new TextBlock { Text = Localize.Text(new[] { "", "", "", "", "", "", "", "", "Noise varies coverage within every tip.", "Wet edges reduce the center deposit and emphasize the rim.", "Build-up continues depositing while the pointer is held still.", "", "Preserve the current texture when switching brush presets." }[index]), TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush });
        _refreshControls.Add(() => _fields.IsEnabled = _section == 0 || (bool)typeof(PaintBrushOptions).GetProperty(EnabledFields[_section])!.GetValue(Options)!);
        Refresh();
    }
    private TextBlock Label(string title) => new() { Text = Localize.Text(title), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = Skin.LabelBrush };
    private void Number(StackPanel host, string title, Func<double> get, Action<double> set, double min, double max, string suffix, string tag, bool compact = false, Func<bool>? enabled = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "42,*,64" : "*,70"), RowDefinitions = new RowDefinitions(compact ? "Auto" : "Auto,32"), Tag = tag };
        row.Children.Add(Label(title));
        var number = new NumericUpDown { Value = (decimal)get(), Minimum = (decimal)min, Maximum = (decimal)max, Increment = 1, ShowButtonSpinner = false, FormatString = "0.##'" + suffix + "'", MinWidth = 0, MinHeight = 0, Height = 28, Padding = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = tag + "-value" };
        Avalonia.Automation.AutomationProperties.SetName(number, Localize.Text(title)); Grid.SetColumn(number, compact ? 2 : 1); row.Children.Add(number);
        var slider = new Slider { Minimum = min, Maximum = max, Value = get(), MinWidth = 0, MinHeight = 0, Height = 32, Margin = new Thickness(compact ? 3 : 0, 0, compact ? 8 : 0, 0), Tag = tag + "-slider" };
        slider.Resources["SliderPreContentMargin"] = new GridLength(6); slider.Resources["SliderPostContentMargin"] = new GridLength(6);
        Avalonia.Automation.AutomationProperties.SetName(slider, Localize.Text(title)); Grid.SetColumn(slider, compact ? 1 : 0); if (!compact) { Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 2); } row.Children.Add(slider);
        if (compact) row.SizeChanged += (_, _) =>
        {
            var narrow = row.Bounds.Width < 220;
            row.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,64" : "42,*,64"); row.RowDefinitions = new RowDefinitions(narrow ? "Auto,32" : "Auto");
            Grid.SetColumn(number, narrow ? 1 : 2); Grid.SetColumn(slider, narrow ? 0 : 1); Grid.SetRow(slider, narrow ? 1 : 0); Grid.SetColumnSpan(slider, narrow ? 2 : 1);
        };
        number.ValueChanged += (_, _) => { if (!_updating && number.Value is { } value) { set((double)value); Refresh(); } };
        slider.ValueChanged += (_, _) => { if (!_updating) { set(Math.Round(slider.Value, 2)); Refresh(); } };
        _refreshControls.Add(() => { number.Value = (decimal)get(); slider.Value = get(); row.IsEnabled = enabled?.Invoke() ?? true; }); host.Children.Add(row);
    }
    private void BrushNumber(string title, Func<BrushSettings, double> get, Func<BrushSettings, double, BrushSettings> set, double min, double max, string suffix, Func<bool>? enabled = null)
        => Number(_fields, title, () => get(_get()), n => _set(set(_get(), n)), min, max, suffix, "brush-field-" + title, enabled: enabled);
    private void AddControl(string name, StackPanel host)
    {
        var property = typeof(PaintBrushOptions).GetProperty(name)!;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,58"), Tag = "brush-field-" + name }; row.Children.Add(Label("Control:"));
        var combo = new ComboBox { ItemsSource = Enum.GetNames<BrushControl>().Select(n => Localize.Text(SplitName(n))).ToArray(), MinWidth = 0, MinHeight = 0, Height = 28, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 6, 0), Tag = "brush-field-" + name + "-choice" };
        Avalonia.Automation.AutomationProperties.SetName(combo, Localize.Text(SplitName(name))); Grid.SetColumn(combo, 1); row.Children.Add(combo);
        var fade = new NumericUpDown { Minimum = 1, Maximum = 10000, ShowButtonSpinner = false, FormatString = "0", MinWidth = 0, MinHeight = 0, Height = 28, Padding = new Thickness(6, 0), Tag = "brush-fade-" + name };
        ToolTip.SetTip(fade, Localize.Text("Fade Steps")); Avalonia.Automation.AutomationProperties.SetName(fade, Localize.Text("Fade Steps")); Grid.SetColumn(fade, 2); row.Children.Add(fade);
        combo.SelectionChanged += (_, _) => { if (!_updating && combo.SelectedIndex >= 0) Set(name, (BrushControl)combo.SelectedIndex); };
        fade.ValueChanged += (_, _) => { if (!_updating && fade.Value is { } value) Set("FadeSteps", (int)value); };
        void LayoutFade()
        {
            var wide = row.Bounds.Width >= 240; fade.IsVisible = wide || fade.IsEnabled;
            row.ColumnDefinitions[2].Width = new GridLength(wide ? 58 : 0); row.RowDefinitions = new RowDefinitions(!wide && fade.IsEnabled ? "Auto,28" : "Auto");
            Grid.SetRow(fade, wide ? 0 : 1); Grid.SetColumn(fade, wide ? 2 : 1); fade.Width = 58; fade.HorizontalAlignment = HorizontalAlignment.Right;
        }
        row.SizeChanged += (_, _) => LayoutFade();
        _refreshControls.Add(() => { var mode = (BrushControl)property.GetValue(Options)!; combo.SelectedIndex = (int)mode; fade.IsEnabled = mode == BrushControl.Fade; fade.Value = mode == BrushControl.Fade ? Options.FadeSteps : null; LayoutFade(); }); host.Children.Add(row);
    }
    private void AddNumber(string name, StackPanel host, bool compact = false, Func<bool>? enabled = null)
    {
        var property = typeof(PaintBrushOptions).GetProperty(name)!; var min = 0d; var max = 100d; var scale = property.PropertyType == typeof(int) ? 1 : 100d; var suffix = "%";
        if (name is "Count" or "DualCount") { min = 1; max = 16; suffix = ""; }
        if (name is "DualSize") { min = 1; max = 5000; scale = 1; suffix = " px"; }
        if (name is "TiltX" or "TiltY") { min = -90; max = 90; scale = 1; suffix = "°"; }
        if (name == "Rotation") { max = 360; scale = 1; suffix = "°"; }
        if (name is "Scatter" or "DualScatter" or "DualSpacing" or "TextureScale") max = 1000;
        if (name is "DualSpacing" or "TextureScale") min = 1;
        if (name == "TiltScale") { min = 1; max = 200; }
        if (name is "TextureBrightness" or "TextureContrast" or "Purity") min = -100;
        Number(host, compact ? "Minimum" : SplitName(name), () => Convert.ToDouble(property.GetValue(Options)) * scale, n => Set(name, property.PropertyType == typeof(int) ? (object)(int)n : n / scale), min, max, suffix, "brush-field-" + name, compact, enabled);
    }
    private void AddField(string name)
    {
        var property = typeof(PaintBrushOptions).GetProperty(name)!;
        if (name == "TextureImage") return;
        if (name == "DualTip") { _fields.Children.Add(TipGrid(Options.DualTip, n => Set(name, n))); return; }
        if (property.PropertyType == typeof(bool))
        {
            var check = new CheckBox { Content = Localize.Text(name == "SpacingEnabled" ? "Spacing" : SplitName(name)), MinHeight = 26, Tag = "brush-field-" + name };
            check.IsCheckedChanged += (_, _) => { if (!_updating) Set(name, check.IsChecked == true); }; _refreshControls.Add(() => check.IsChecked = (bool)property.GetValue(Options)!); _fields.Children.Add(check);
        }
        else if (property.PropertyType == typeof(BrushControl)) AddControl(name, _fields);
        else if (property.PropertyType.IsEnum || name is "Texture" or "TextureMode")
        {
            var values = name == "Texture" ? new[] { "Paper", "Weave", "Clouds", "Checkerboard", "Custom Image" } : name == "TextureMode" ? ["Height", "Multiply", "Darken", "Lighten", "Difference", "Color Burn"] : Enum.GetNames(property.PropertyType).Select(SplitName).ToArray();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*"), Tag = "brush-field-" + name }; row.Children.Add(Label(name.EndsWith("Mode", StringComparison.Ordinal) ? "Mode" : SplitName(name)));
            var combo = new ComboBox { ItemsSource = values.Select(Localize.Text).ToArray(), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (_updating || combo.SelectedIndex < 0) return; var n = combo.SelectedIndex; if (name == "Texture" && n == 4) _texture(); else { Set(name, property.PropertyType.IsEnum ? Enum.ToObject(property.PropertyType, n) : n); if (name == "Texture") Set("TextureImage", null!); } };
            _refreshControls.Add(() => combo.SelectedIndex = name == "Texture" && Options.TextureImage is not null ? 4 : Convert.ToInt32(property.GetValue(Options))); Grid.SetColumn(combo, 1); row.Children.Add(combo); _fields.Children.Add(row);
        }
        else AddNumber(name, _fields);
    }


    internal static WrapPanel TipGrid(int selected, Action<int> set)
    {
        var grid = new WrapPanel();
        for (var i = 0; i < BrushDabs.TipNames.Length; i++) { var n = i; var button = new Button { Width = 36, Height = 36, MinWidth = 0, Padding = new Thickness(2), Margin = new Thickness(1), Background = i == selected ? Skin.AccentBrush : Skin.SurfaceControlBrush, Content = new ProceduralTipView(i), Tag = "brush-tip-" + i }; ToolTip.SetTip(button, Localize.Text(BrushDabs.TipNames[i])); button.Click += (_, _) => { set(n); foreach (var sibling in grid.Children.OfType<Button>()) sibling.Background = ReferenceEquals(sibling, button) ? Skin.AccentBrush : Skin.SurfaceControlBrush; }; grid.Children.Add(button); } return grid;
    }
    internal void UpdatePreview() { if (_disposed) return; _dirty = true; _previewTimer.Stop(); _previewTimer.Start(); }
    private async Task RenderPreview()
    {
        if (_busy || !_dirty || _disposed) return; _busy = true; _dirty = false;
        var settings = _get();
        try
        {
            using var result = await Task.Run(() =>
            {
                using var doc = new CanvasDocument(Guid.NewGuid(), 360, 110); var bitmap = Bitmaps.Allocate(Bitmaps.ColorInfo(360, 110)); bitmap.Erase(SKColors.Transparent);
                using var original = ImportedImage.Create(bitmap, "Preview"); var layer = new ImageLayer(Guid.NewGuid(), original, new Compositor.Core.Model.LayerTransform(0, 0, 360, 110), "Preview"); doc.Layers.Add(layer);
                var points = Enumerable.Range(0, 41).Select(i => new SKPoint(20 + i * 8, (float)(55 - 20 * Math.Sin(i * Math.PI / 20)))).ToArray();
                var samples = Enumerable.Range(0, 41).Select(i => new PenSample((float)(.25 + .75 * Math.Sin(Math.PI * (i + 1) / 42)))).ToArray();
                var p = (settings.Painting ?? new()) with { Symmetry = BrushSymmetry.Off, Reservoir = null, Samples = samples };
                BrushEdits.Paint(doc, layer.ID, points, settings with { Diameter = Math.Clamp(settings.Diameter, 1, 36), Red = 1, Green = 1, Blue = 1, Seed = 91, Erasing = false, Painting = p, Mode = BrushMode.Paint });
                return Bitmaps.Copy(layer.Asset!.Image);
            });
            if (!_disposed) { var old = _previewBitmap; _previewBitmap = CanvasView.ToImage(result); _preview.Source = _previewBitmap; old?.Dispose(); }
        }
        catch (Exception error) when (error is InvalidOperationException or OutOfMemoryException or InvalidDataException) { ToolTip.SetTip(_preview, error.Message); }
        finally { _busy = false; if (_dirty && !_disposed) _previewTimer.Start(); }
    }
    public void Dispose() { _disposed = true; _previewTimer.Stop(); _preview.Source = null; _previewBitmap?.Dispose(); }
}
internal sealed class ProceduralTipView(int tip) : Control
{
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height); const int count = 14;
        for (var y = 0; y < count; y++) for (var x = 0; x < count; x++) { var coverage = BrushDabs.Tip(tip, (x + .5) / count * 2 - 1, (y + .5) / count * 2 - 1, tip == 0 ? 0 : 1); if (coverage > 0) context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(coverage * 255, 0, 255), 255, 255, 255)), null, new Rect(x * size / count, y * size / count, size / count + .1, size / count + .1)); }
    }
}
internal sealed class BrushTipAngleControl(Func<BrushSettings> get, Action<BrushSettings> set) : Control
{
    private bool _dragging;
    public BrushTipAngleControl() : this(() => new(), _ => { }) { }
    protected override Size MeasureOverride(Size availableSize) => new(100, 100);
    public override void Render(DrawingContext context)
    {
        var b = get(); var center = new Point(Bounds.Width / 2, 48); var pen = new Pen(Skin.LabelBrush, 1); var radius = 36d;
        context.DrawLine(new Pen(Skin.SecondaryBrush, 1), center + new Vector(-42, 0), center + new Vector(42, 0)); context.DrawLine(new Pen(Skin.SecondaryBrush, 1), center + new Vector(0, -42), center + new Vector(0, 42));
        using (context.PushTransform(Matrix.CreateRotation(b.Angle * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y))) context.DrawEllipse(null, pen, default, radius, radius * b.Roundness);
        var angle = b.Angle * Math.PI / 180; var handle = center + new Vector(Math.Cos(angle) * radius, Math.Sin(angle) * radius); context.DrawEllipse(Skin.LabelBrush, null, handle, 4, 4);
    }
    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e) { _dragging = true; e.Pointer.Capture(this); Update(e.GetPosition(this), e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)); e.Handled = true; }
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e) { if (_dragging) Update(e.GetPosition(this), e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)); }
    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e) { _dragging = false; e.Pointer.Capture(null); }
    private void Update(Point p, bool roundness) { var dx = p.X - Bounds.Width / 2; var dy = p.Y - 48; var b = get(); set(roundness ? b with { Roundness = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / 36, .01, 1) } : b with { Angle = Math.Atan2(dy, dx) * 180 / Math.PI }); InvalidateVisual(); }
}

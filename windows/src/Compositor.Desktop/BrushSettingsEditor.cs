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

internal sealed class BrushSettingsEditor : StackPanel, IDisposable
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
    private readonly StackPanel _categories = new() { Spacing = 2 };
    private readonly ToolSettingsPopup _fields = new() { Margin = new Thickness(6), MinWidth = 0 };
    private readonly Grid _layout = new() { ColumnDefinitions = new ColumnDefinitions("135,*") };
    private readonly ComboBox _compact = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Image _preview = new() { Height = 115, Stretch = Stretch.Uniform };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private WriteableBitmap? _previewBitmap;
    private bool _busy, _dirty, _disposed;
    private int _section;
    private PaintBrushOptions Options => _get().Painting ?? new();
    internal static string SplitName(string name) => Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    internal BrushSettingsEditor(Func<BrushSettings> get, Action<BrushSettings> set, Func<bool> isMixer, Action picker, Action save, Action texture)
    {
        _get = get; _set = set; _isMixer = isMixer; _picker = picker; _save = save; _texture = texture; Spacing = 8;
        var header = new DockPanel(); var plus = new Button { Content = "+", Tag = "save-brush-preset", Width = 28 }; plus.Click += (_, _) => _save(); DockPanel.SetDock(plus, Dock.Right); header.Children.Add(plus);
        var brushes = new Button { Content = Localize.Text("Brushes"), Tag = "brush-presets" }; brushes.Click += (_, _) => _picker(); header.Children.Add(brushes); Children.Add(header);
        _compact.ItemsSource = Sections.Select(Localize.Text).ToArray(); _compact.SelectedIndex = 0; _compact.SelectionChanged += (_, _) => { if (_compact.SelectedIndex >= 0) SelectSection(_compact.SelectedIndex); }; Children.Add(_compact);
        _layout.Children.Add(_categories); Grid.SetColumn(_fields, 1); _layout.Children.Add(_fields); Children.Add(_layout);
        Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#777777")), BorderBrush = Skin.BorderSubtleBrush, BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = _preview });
        SizeChanged += (_, _) => { var wide = Bounds.Width >= 390; _categories.IsVisible = wide; _compact.IsVisible = !wide; _layout.ColumnDefinitions[0].Width = new GridLength(wide ? 135 : 0); };
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RenderPreview(); };
        SelectSection(0);
    }
    private void Set(string property, object value)
    {
        var next = Options with { }; typeof(PaintBrushOptions).GetProperty(property)!.SetValue(next, value); _set(_get() with { Painting = next }); UpdatePreview();
    }
    internal void SelectSection(int index)
    {
        if (_compact.SelectedIndex != index) { _compact.SelectedIndex = index; return; }
        _section = index; _categories.Children.Clear();
        for (var i = 0; i < Sections.Length; i++)
        {
            var n = i; var row = new DockPanel();
            if (i > 0) { var enabled = new CheckBox { IsChecked = (bool)typeof(PaintBrushOptions).GetProperty(EnabledFields[i])!.GetValue(Options)!, MinHeight = 22, MinWidth = 22, Width = 24, Tag = "brush-enable-" + i }; enabled.IsCheckedChanged += (_, _) => { Set(EnabledFields[n], enabled.IsChecked == true); }; DockPanel.SetDock(enabled, Dock.Left); row.Children.Add(enabled); }
            var locked = new ToggleButton { Content = "♙", Width = 22, MinWidth = 0, Height = 24, Padding = new Thickness(0), IsChecked = (Options.LockedSections & (1UL << i)) != 0, Tag = "brush-lock-" + i }; ToolTip.SetTip(locked, Localize.Text("Keep This Section When Changing Presets"));
            locked.IsCheckedChanged += (_, _) => Set("LockedSections", locked.IsChecked == true ? Options.LockedSections | (1UL << n) : Options.LockedSections & ~(1UL << n)); DockPanel.SetDock(locked, Dock.Right); row.Children.Add(locked);
            var button = new Button { Content = Localize.Text(Sections[i]), MinWidth = 0, Padding = new Thickness(2), Height = 26, HorizontalAlignment = HorizontalAlignment.Stretch, Background = i == index ? Skin.SurfaceDarkBrush : Brushes.Transparent, Tag = "brush-section-" + i }; button.Click += (_, _) => { _compact.SelectedIndex = n; SelectSection(n); }; row.Children.Add(button); _categories.Children.Add(row);
        }
        _fields.Children.Clear(); _fields.Changed = UpdatePreview;
        if (index > 0) _fields.Check("Enable " + Sections[index], (bool)typeof(PaintBrushOptions).GetProperty(EnabledFields[index])!.GetValue(Options)!, v => Set(EnabledFields[index], v));
        if (index == 0)
        {
            _fields.Children.Add(TipGrid(Options.Tip, tip => { Set("Tip", tip); _set(_get() with { TipImage = null, Hardness = tip == 0 ? 0 : 1 }); }));
            BrushNumber("Size", b => b.Diameter, (b, n) => b with { Diameter = n }, 1, 5000);
            _fields.Check("Flip X", Options.FlipX, v => Set("FlipX", v)); _fields.Check("Flip Y", Options.FlipY, v => Set("FlipY", v));
            _fields.Children.Add(new BrushTipAngleControl(() => _get(), b => { _set(b); UpdatePreview(); }));
            BrushNumber("Angle", b => b.Angle, (b, n) => b with { Angle = n }, -180, 180);
            BrushNumber("Roundness %", b => b.Roundness * 100, (b, n) => b with { Roundness = n / 100 }, 1, 100);
            BrushNumber("Hardness %", b => b.Hardness * 100, (b, n) => b with { Hardness = n / 100 }, 0, 100);
            _fields.Check("Spacing", Options.SpacingEnabled, v => Set("SpacingEnabled", v));
            BrushNumber("Spacing %", b => b.SpacingFraction > 0 ? b.SpacingFraction * 100 : 25, (b, n) => b with { SpacingFraction = n / 100 }, 1, 1000);
        }
        else foreach (var field in Fields[index]) AddField(field);
        if (index == 3) { var load = new Button { Content = Localize.Text("Load Texture…"), Tag = "brush-load-texture" }; load.Click += (_, _) => _texture(); _fields.Children.Add(load); }
        if (index is 8 or 9 or 10 or 12) _fields.Heading(new[] { "", "", "", "", "", "", "", "", "Noise varies coverage within every tip.", "Wet edges reduce the center deposit and emphasize the rim.", "Build-up continues depositing while the pointer is held still.", "", "Preserve the current texture when switching brush presets." }[index]);
        UpdatePreview();
    }
    private void BrushNumber(string title, Func<BrushSettings, double> get, Func<BrushSettings, double, BrushSettings> set, double min, double max) => _fields.Number(title, get(_get()), min, max, n => _set(set(_get(), n)), true);
    private void AddField(string name)
    {
        var property = typeof(PaintBrushOptions).GetProperty(name)!; var value = property.GetValue(Options)!;
        if (name is "TextureImage") return;
        if (name == "DualTip") { _fields.Children.Add(TipGrid((int)value, n => Set(name, n))); return; }
        if (name == "Texture") { _fields.Choice("Texture", ["Paper", "Weave", "Clouds", "Checkerboard", "Custom Image"], Options.TextureImage is null ? (int)value : 4, n => { if (n == 4) _texture(); else { Set(name, n); Set("TextureImage", null!); } }); return; }
        if (name == "TextureMode") { _fields.Choice("Mode", ["Height", "Multiply", "Darken", "Lighten", "Difference", "Color Burn"], (int)value, n => Set(name, n)); return; }
        var title = SplitName(name);
        var before = _fields.Children.Count;
        if (property.PropertyType == typeof(bool)) _fields.Check(title, (bool)value, v => Set(name, v));
        else if (property.PropertyType.IsEnum)
        { var values = Enum.GetNames(property.PropertyType); _fields.Choice(title, values.Select(SplitName).ToArray(), Convert.ToInt32(value), n => Set(name, Enum.ToObject(property.PropertyType, n))); }
        else
        {
            var min = 0d; var max = 100d; var scale = property.PropertyType == typeof(int) ? 1 : 100d;
            if (name is "Count" or "DualCount") { min = 1; max = 16; }
            if (name is "FadeSteps") { min = 1; max = 10000; }
            if (name is "DualSize") { min = 1; max = 5000; scale = 1; }
            if (name is "TiltX" or "TiltY") { min = -90; max = 90; scale = 1; }
            if (name == "Rotation") { max = 360; scale = 1; }
            if (name is "Scatter" or "DualScatter" or "DualSpacing" or "TextureScale") max = 1000;
            if (name is "DualSpacing" or "TextureScale") min = 1;
            if (name == "TiltScale") { min = 1; max = 200; }
            if (name is "TextureBrightness" or "TextureContrast" or "Purity") min = -100;
            _fields.Number(title, Convert.ToDouble(value) * scale, min, max, n => Set(name, property.PropertyType == typeof(int) ? (object)(int)n : n / scale), true);
        }
        var mixerOnly = name.StartsWith("Wet", StringComparison.Ordinal) || name.StartsWith("Mix", StringComparison.Ordinal) || name is "MinimumWet" or "MinimumMix";
        foreach (var control in _fields.Children.Skip(before)) { control.Tag = "brush-field-" + name; if (_section == 6 && mixerOnly) control.IsEnabled = _isMixer(); }
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

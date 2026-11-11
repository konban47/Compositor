using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// The parts Photoshop's Image ▸ Adjustments dialogs are built from, in this app's own controls: a caption over a value
/// box and a slider with a round thumb on a track that can show the colors it moves toward, a preset row with its ≡ or
/// ⚙ menu, a row of color dots, a histogram with draggable triangles, and the eyedroppers.
/// </summary>
internal static class AdjustmentKit
{
    internal const double BoxWidth = 72, ColumnWidth = 142;

    /// <summary>A word as these dialogs use it where the shared translation means something else ("Fit" is the view's Fit on Screen
    /// elsewhere, but Variables' 限制 here).</summary>
    internal static string Word(string english, string chinese) => Localize.IsChinese ? chinese : Localize.Text(english);

    /// <summary>A caption with its Alt key in brackets, as both languages' screenshots write it: "Exposure(E)".</summary>
    internal static TextBlock Caption(string text, char? key = null, bool colon = false) => new()
    {
        Text = Localize.Text(text) + (key is { } k ? $"({k})" : "") + (colon ? ":" : ""),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Gives a caption's Alt key to the control it names.</summary>
    internal static void Key(Window window, char? key, Control target) { if (key is { } k) LayerDialogControls.Key(window, k, target); }

    /// <summary>A track from one color to the next across its length.</summary>
    internal static IBrush Track(params Color[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : i / (double)(colors.Length - 1)));
        return brush;
    }
    internal static IBrush Spectrum() => Track(Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red);

    /// <summary>The right-hand column: OK, Cancel, whatever the dialog adds, then Preview. Photoshop's newer dialogs caption
    /// Preview "Preview (Alt+P)", its older ones "Preview(P)"; both answer Alt+P.</summary>
    internal static StackPanel Actions(Window window, Action ok, Action cancel, CheckBox? preview, params Control[] extras)
    {
        var okButton = FormDialog.ActionButton("OK", ok); okButton.IsDefault = true; okButton.Tag = "OK";
        var cancelButton = FormDialog.ActionButton("Cancel", cancel); cancelButton.IsCancel = true; cancelButton.Tag = "Cancel";
        var column = new StackPanel { Spacing = 10, Width = 138, Margin = new Thickness(18, 0, 0, 0) };
        foreach (var control in new Control[] { okButton, cancelButton }.Concat(extras))
        {
            if (control is Button button) { button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Margin = default; button.MinHeight = 32; }
            column.Children.Add(control);
        }
        if (preview is not null) { preview.Margin = new Thickness(0, 4, 0, 0); column.Children.Add(preview); LayerDialogControls.Key(window, 'P', preview); }
        return column;
    }

    /// <summary>Preview, ticked: "Preview (Alt+P)" in the newer dialogs, "Preview(P)" in the older.</summary>
    internal static CheckBox Preview(bool newer = true) => new()
    {
        IsChecked = true, Tag = "Preview",
        Content = new TextBlock { Text = Localize.Text("Preview") + (newer ? " (Alt+P)" : "(P)") },
    };

    /// <summary>A button of the right column with its Alt key: "Auto(A)", "Options(T)…".</summary>
    internal static Button Action(Window window, string text, char? key, Action click)
    {
        // The whole caption is translated, then the key goes in before a trailing ellipsis: "载入统计数据(O)…".
        var caption = Localize.Text(text); var ellipsis = caption.EndsWith('…');
        var button = FormDialog.ActionButton("", click);
        button.Content = caption.TrimEnd('…') + (key is { } k ? $"({k})" : "") + (ellipsis ? "…" : ""); button.Tag = text;
        ToolTip.SetTip(button, null);
        if (key is { } letter) LayerDialogControls.Key(window, letter, button);
        return button;
    }

    /// <summary>A two-column body: the controls on the left, the action column on the right.</summary>
    internal static Grid Body(Control left, Control right, double margin = 20)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(margin, 16, margin, 18) };
        grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right);
        return grid;
    }

    /// <summary>A frame with its caption set into the top edge, as Photoshop's group boxes are drawn.</summary>
    internal static Control Group(string title, Control content, Control? heading = null) => DialogKit.Group(title, content, heading);
}

/// <summary>
/// Photoshop's slider: a thin track, plain or showing the colors it moves toward, with a round thumb (or a triangle, as the
/// older dialogs draw it). Dragging or clicking the track sets the value, as do the arrow keys.
/// </summary>
internal sealed class ToneSlider : Control
{
    private double _value;
    internal double Minimum { get; init; }
    internal double Maximum { get; init; } = 100;
    internal IBrush? TrackBrush { get; set; }
    internal bool Triangle { get; init; }
    internal event Action? ValueChanged;
    internal double Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (next == _value) return;
            _value = next; InvalidateVisual(); ValueChanged?.Invoke();
        }
    }
    public ToneSlider() { Height = 20; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand); GotFocus += (_, _) => InvalidateVisual(); LostFocus += (_, _) => InvalidateVisual(); }
    private double Inset => 9;
    private double X(double value) => Inset + (Bounds.Width - 2 * Inset) * (Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0);
    public override void Render(DrawingContext context)
    {
        var y = Triangle ? 4 : Bounds.Height / 2; var thumb = X(_value);
        var plain = new Pen(new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), 2);
        if (TrackBrush is { } brush) context.FillRectangle(brush, new Rect(Inset, y - 1, Math.Max(0, Bounds.Width - 2 * Inset), 2));
        else
        {
            // Photoshop leaves a gap in the plain track either side of the thumb.
            context.DrawLine(plain, new Point(Inset, y), new Point(Math.Max(Inset, thumb - 10), y));
            context.DrawLine(plain, new Point(Math.Min(Bounds.Width - Inset, thumb + 10), y), new Point(Bounds.Width - Inset, y));
        }
        var edge = new Pen(Brushes.White, 1.4);
        if (Triangle)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(new Point(thumb, y + 3), true); g.LineTo(new Point(thumb + 6, y + 11)); g.LineTo(new Point(thumb + 6, y + 15)); g.LineTo(new Point(thumb - 6, y + 15)); g.LineTo(new Point(thumb - 6, y + 11)); g.EndFigure(true); }
            context.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1), geometry);
        }
        else context.DrawEllipse(Skin.ChromeBrush, IsFocused ? new Pen(Skin.AccentBrush, 2) : edge, new Point(thumb, y), 8, 8);
    }
    private void Take(Point at) => Value = Minimum + (Maximum - Minimum) * Math.Clamp((at.X - Inset) / Math.Max(1, Bounds.Width - 2 * Inset), 0, 1);
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); Focus(); e.Pointer.Capture(this); Take(e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (ReferenceEquals(e.Pointer.Captured, this)) Take(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); e.Pointer.Capture(null); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = (Maximum - Minimum) / 100 * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1);
        if (e.Key == Key.Left) { Value -= step; e.Handled = true; } else if (e.Key == Key.Right) { Value += step; e.Handled = true; }
        base.OnKeyDown(e);
    }
}

/// <summary>
/// A number field as Photoshop's adjustment dialogs have them: the unit inside the box ("40%", "42°", "+1"), read back
/// when Enter is pressed or the field is left, kept within its range; Up and Down step it.
/// </summary>
internal sealed class ValueBox : TextBox
{
    private double _value;
    private readonly string _format, _suffix;
    private readonly bool _signed;
    internal double Minimum { get; }
    internal double Maximum { get; }
    internal event Action? ValueChanged;
    protected override Type StyleKeyOverride => typeof(TextBox);
    internal ValueBox(double min, double max, double value, string format = "0", string suffix = "", bool signed = false, string? tag = null)
    {
        Minimum = min; Maximum = max; _format = format; _suffix = suffix; _signed = signed; Tag = tag;
        Width = AdjustmentKit.BoxWidth; Height = 30; MinHeight = 0; Padding = new Thickness(8, 4); VerticalContentAlignment = VerticalAlignment.Center;
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        _value = Math.Clamp(value, min, max); Show();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Read(); SelectAll(); }
            else if (e.Key is Key.Up or Key.Down) { Value += (e.Key == Key.Up ? 1 : -1) * Step * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1); SelectAll(); e.Handled = true; }
        };
        LostFocus += (_, _) => Read();
    }
    /// <summary>Reads what has been typed and not yet entered.</summary>
    internal void Commit() => Read();
    private double Step => _format.Contains('.') ? Math.Pow(10, -(_format.Length - _format.IndexOf('.') - 1)) : 1;
    internal double Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(Math.Round(value / Step) * Step, Minimum, Maximum);
            if (next == _value) { Show(); return; }
            _value = next; Show(); ValueChanged?.Invoke();
        }
    }
    private void Show() => Text = (_signed && _value > 0 ? "+" : "") + _value.ToString(_format, CultureInfo.CurrentCulture) + _suffix;
    private void Read()
    {
        var digits = new string((Text ?? "").Where(c => char.IsDigit(c) || c is '-' or '.' or ',' or '+').ToArray());
        if (double.TryParse(digits, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) && double.IsFinite(parsed)) Value = parsed; else Show();
    }
}

/// <summary>
/// One of Photoshop's slider rows: the caption on the left and the value box on the right over a slider that spans both,
/// or, with <paramref name="inline"/>, caption, slider and box on one line as Shadows/Highlights has them.
/// </summary>
internal sealed class SliderRow : Grid
{
    internal ToneSlider Slider { get; }
    internal ValueBox Box { get; }
    internal TextBlock Label { get; }
    internal event Action? Changed;
    internal double Value { get => Box.Value; set { Box.Value = value; } }
    internal SliderRow(Window window, string label, char? key, double min, double max, double value, string format = "0", string suffix = "",
        IBrush? track = null, bool signed = false, bool inline = false, bool triangle = false, string? tag = null)
    {
        Tag = tag ?? label;
        Label = AdjustmentKit.Caption(label, key);
        Box = new ValueBox(min, max, value, format, suffix, signed, (tag ?? label) + " value");
        Slider = new ToneSlider { Minimum = min, Maximum = max, TrackBrush = track, Triangle = triangle, Tag = (tag ?? label) + " slider" };
        Slider.Value = value;
        var syncing = false;
        Slider.ValueChanged += () => { if (syncing) return; syncing = true; Box.Value = Slider.Value; syncing = false; Changed?.Invoke(); };
        Box.ValueChanged += () => { if (syncing) return; syncing = true; Slider.Value = Box.Value; syncing = false; Changed?.Invoke(); };
        if (inline)
        {
            ColumnDefinitions = new("Auto,*,Auto"); ColumnSpacing = 10; Label.MinWidth = 70;
            Children.Add(Label); SetColumn(Slider, 1); Slider.VerticalAlignment = VerticalAlignment.Center; Children.Add(Slider); SetColumn(Box, 2); Children.Add(Box);
        }
        else
        {
            ColumnDefinitions = new("*,Auto"); RowDefinitions = new("Auto,Auto"); RowSpacing = 4; Margin = new Thickness(0, 0, 0, 6);
            Children.Add(Label); SetColumn(Box, 1); Children.Add(Box);
            SetRow(Slider, 1); SetColumnSpan(Slider, 2); Children.Add(Slider);
        }
        AdjustmentKit.Key(window, key, Box);
    }
}

/// <summary>
/// The preset row: a list of the built-in presets between Default and Custom, with the ones saved to this computer, and
/// the ≡ (or ⚙ in the older dialogs) menu of Save Preset…, Load Preset… and Delete Current Preset. Presets are amounts by
/// name, kept as JSON in the settings folder under Adjustment Presets; Photoshop's own preset files are not read.
/// </summary>
internal sealed class PresetBar : Grid
{
    private readonly string _kind, _custom;
    private readonly IReadOnlyList<(string Name, Dictionary<string, double> Values)> _builtIn;
    private readonly Func<Dictionary<string, double>> _capture;
    private readonly Action<Dictionary<string, double>> _restore;
    private readonly List<(string Name, string? Path, Dictionary<string, double>? Values)> _rows = [];
    private bool _applying;
    internal ComboBox Choice { get; } = new() { Tag = "preset", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
    internal Button MenuButton { get; }
    internal MenuItem DeleteItem { get; }
    internal static string Folder(string kind) => Path.Combine(AppPaths.SettingsDirectory, "Adjustment Presets", kind);

    internal PresetBar(Window window, string kind, char key, IReadOnlyList<(string Name, Dictionary<string, double> Values)> builtIn,
        Func<Dictionary<string, double>> capture, Action<Dictionary<string, double>> restore, bool gear = false, bool newer = true, string? tooltip = null)
    {
        _kind = kind; _builtIn = builtIn; _capture = capture; _restore = restore; _custom = newer ? "Custom Preset" : "Custom";
        ColumnDefinitions = new("Auto,*,Auto"); ColumnSpacing = 8;
        var caption = AdjustmentKit.Caption("Preset", key, colon: !newer); Children.Add(caption);
        SetColumn(Choice, 1); Children.Add(Choice);
        var save = new MenuItem { Header = Localize.Text("Save Preset…"), Tag = "Save Preset…" };
        var load = new MenuItem { Header = Localize.Text("Load Preset…"), Tag = "Load Preset…" };
        DeleteItem = new MenuItem { Header = Localize.Text("Delete Current Preset"), Tag = "Delete Current Preset" };
        var menu = new ContextMenu { Items = { save, load, new Separator(), DeleteItem } };
        MenuButton = new Button
        {
            Content = gear ? "⚙▾" : "☰", Width = 34, Height = 30, Padding = default, Tag = "preset menu",
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = default,
        };
        if (tooltip is not null) ToolTip.SetTip(MenuButton, Localize.Text(tooltip));
        MenuButton.Click += (_, _) => { DeleteItem.IsEnabled = Current?.Path is not null; menu.Open(MenuButton); };
        SetColumn(MenuButton, 2); Children.Add(MenuButton);
        save.Click += async (_, _) => await Save(window);
        load.Click += async (_, _) => await Load(window);
        DeleteItem.Click += (_, _) => Delete();
        Choice.SelectionChanged += (_, _) =>
        {
            if (_applying || Choice.SelectedIndex < 0 || Choice.SelectedIndex >= _rows.Count) return;
            if (_rows[Choice.SelectedIndex] is { Values: { } values }) { _applying = true; _restore(new(values)); _applying = false; }
            else if (_rows[Choice.SelectedIndex].Path is { } path && Read(path) is { } read) { _applying = true; _restore(read); _applying = false; }
        };
        AdjustmentKit.Key(window, key, Choice);
        Fill(0);
    }

    private (string Name, string? Path, Dictionary<string, double>? Values)? Current => Choice.SelectedIndex >= 0 && Choice.SelectedIndex < _rows.Count ? _rows[Choice.SelectedIndex] : null;

    /// <summary>The settings moved away from any preset: the list reads Custom, as Photoshop's does.</summary>
    internal void MarkCustom()
    {
        if (_applying) return;
        _applying = true; Choice.SelectedIndex = _rows.FindIndex(r => r.Name == _custom && r.Path is null && r.Values is null); _applying = false;
    }

    /// <summary>Rebuilds the list and selects the given row (0 is Default).</summary>
    private void Fill(int select)
    {
        _rows.Clear(); var items = new List<object>();
        void Row(string name, string? path, Dictionary<string, double>? values, bool translate = true)
        { _rows.Add((name, path, values)); items.Add(new ComboBoxItem { Content = translate ? Localize.Text(name) : name, Tag = name }); }
        void Rule() { _rows.Add(("", null, null)); items.Add(new ComboBoxItem { Content = new Separator { Margin = new Thickness(0, 2) }, IsEnabled = false, Height = 9, Padding = default }); }
        Row(_builtIn.Count > 0 ? _builtIn[0].Name : "Default", null, _builtIn.Count > 0 ? _builtIn[0].Values : new());
        Rule();
        foreach (var preset in _builtIn.Skip(1)) Row(preset.Name, null, preset.Values);
        foreach (var path in UserPresets()) Row(Path.GetFileNameWithoutExtension(path), path, null, translate: false);
        Rule();
        Row(_custom, null, null);
        _applying = true; Choice.ItemsSource = items; Choice.SelectedIndex = Math.Clamp(select, 0, _rows.Count - 1); _applying = false;
    }

    private IEnumerable<string> UserPresets()
    {
        try { return Directory.Exists(Folder(_kind)) ? Directory.GetFiles(Folder(_kind), "*.json").OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).Take(200) : []; }
        catch (IOException) { return []; } catch (UnauthorizedAccessException) { return []; }
    }

    internal static Dictionary<string, double>? Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 256 * 1024) return null;
            var values = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path));
            return values is { Count: <= 512 } && values.All(v => v.Key.Length <= 64 && double.IsFinite(v.Value)) ? values : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private async Task Save(Window window)
    {
        Directory.CreateDirectory(Folder(_kind));
        var folder = await window.StorageProvider.TryGetFolderFromPathAsync(Folder(_kind));
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localize.Text("Save Preset…"), SuggestedFileName = Localize.Text("Preset") + ".json", DefaultExtension = ".json", SuggestedStartLocation = folder,
            FileTypeChoices = [new FilePickerFileType(Localize.Text("Adjustment preset")) { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { await AtomicFile.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(_capture(), new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        Fill(0); Select(path);
    }

    private async Task Load(Window window)
    {
        var picked = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = Localize.Text("Load Preset…"), FileTypeFilter = [new FilePickerFileType(Localize.Text("Adjustment preset")) { Patterns = ["*.json"] }] });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path || Read(path) is not { } values) return;
        // A preset loaded from elsewhere is kept with the others, as Photoshop lists a loaded preset.
        var kept = path;
        try
        {
            Directory.CreateDirectory(Folder(_kind));
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder(_kind)), StringComparison.OrdinalIgnoreCase))
            { kept = Path.Combine(Folder(_kind), Path.GetFileName(path)); File.Copy(path, kept, true); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { kept = path; }
        Fill(0); Select(kept); _applying = true; _restore(values); _applying = false;
    }

    private void Select(string path)
    {
        var index = _rows.FindIndex(r => r.Path is { } p && string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (index >= 0) { _applying = true; Choice.SelectedIndex = index; _applying = false; }
    }

    private void Delete()
    {
        if (Current?.Path is not { } path) return;
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        Fill(_rows.FindIndex(r => r.Name == _custom && r.Path is null && r.Values is null));
    }
}

/// <summary>
/// A row of round color buttons, the chosen one ringed in white: Hue/Saturation's ranges (the first a color wheel for
/// Master), Selective Color's nine colors, Channel Mixer's output channels.
/// </summary>
internal sealed class ColorDots : StackPanel
{
    private readonly List<Dot> _dots = [];
    private int _selected;
    internal event Action<int>? Selected;
    internal int Index { get => _selected; set { _selected = Math.Clamp(value, 0, _dots.Count - 1); foreach (var (dot, i) in _dots.Select((d, i) => (d, i))) dot.Chosen = i == _selected; } }
    internal ColorDots(IEnumerable<(Color? Color, string Name)> dots, double size = 34)
    {
        Orientation = Orientation.Horizontal; Spacing = 10;
        foreach (var (color, name) in dots)
        {
            var dot = new Dot(color, size) { Tag = name }; ToolTip.SetTip(dot, Localize.Text(name));
            var index = _dots.Count; dot.PointerPressed += (_, e) => { Index = index; Selected?.Invoke(index); e.Handled = true; };
            _dots.Add(dot); Children.Add(dot);
        }
        Index = 0;
    }
    internal Control DotAt(int index) => _dots[index];
    private sealed class Dot(Color? color, double size) : Control
    {
        private bool _chosen;
        internal bool Chosen { get => _chosen; set { _chosen = value; InvalidateVisual(); } }
        protected override Size MeasureOverride(Size availableSize) => new(size, size);
        public override void Render(DrawingContext context)
        {
            var center = new Point(size / 2, size / 2); var radius = size / 2 - 3;
            if (color is { } c) context.DrawEllipse(new SolidColorBrush(c), new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), center, radius, radius);
            else
            {
                // The color wheel that stands for every color at once.
                for (var i = 0; i < 12; i++)
                {
                    var hue = i * 30.0; var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(center, true);
                        g.LineTo(center + new Point(Math.Cos(hue * Math.PI / 180) * radius, Math.Sin(hue * Math.PI / 180) * radius));
                        g.ArcTo(center + new Point(Math.Cos((hue + 30) * Math.PI / 180) * radius, Math.Sin((hue + 30) * Math.PI / 180) * radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                        g.EndFigure(true);
                    }
                    var rgb = SKColor.FromHsv((float)(hue + 15) % 360, 100, 100);
                    context.DrawGeometry(new SolidColorBrush(Color.FromRgb(rgb.Red, rgb.Green, rgb.Blue)), null, geometry);
                }
            }
            if (_chosen) context.DrawEllipse(null, new Pen(Brushes.White, 2.5), center, size / 2 - 1.5, size / 2 - 1.5);
        }
    }
}

/// <summary>
/// A histogram with Photoshop's triangles under it: Levels' black, gray and white input points, or Threshold's one. The
/// triangles drag; the gray one keeps its place between the other two as they move, as it does in Photoshop.
/// </summary>
internal sealed class HistogramView : Control
{
    private double[] _bins = new double[256];
    private int _held = -1;
    internal double[] Points { get; } // input levels 0–255; Levels' gray is a gamma, shown between black and white
    internal bool Gamma { get; init; }
    internal event Action? Moved;
    internal HistogramView(params double[] points) { Points = points; Height = 170; Cursor = new Cursor(StandardCursorType.Arrow); }
    internal double[] Bins { get => _bins; set { _bins = value.Length == 256 ? value : new double[256]; InvalidateVisual(); } }
    private const double Foot = 18;
    private double X(double level) => 2 + (Bounds.Width - 4) * level / 255;
    /// <summary>Levels' gamma as the middle triangle's place: 1 is halfway between black and white.</summary>
    internal static double GammaPosition(double black, double white, double gamma) => black + (white - black) * Math.Pow(.5, gamma);
    internal static double GammaFrom(double black, double white, double position) => Math.Clamp(Math.Log(Math.Clamp((position - black) / Math.Max(1e-6, white - black), .001, .999)) / Math.Log(.5), .1, 9.99);
    public override void Render(DrawingContext context)
    {
        var plot = new Rect(0, 0, Bounds.Width, Math.Max(0, Bounds.Height - Foot));
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), plot);
        context.DrawRectangle(null, new Pen(Skin.BorderControlBrush, 1), plot);
        var peak = Math.Max(1e-9, _bins.OrderByDescending(v => v).Skip(2).FirstOrDefault()); var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)), Math.Max(1, (Bounds.Width - 4) / 256));
        for (var i = 0; i < 256; i++)
        {
            var h = Math.Min(1, _bins[i] / peak) * (plot.Height - 4); if (h <= 0) continue;
            context.DrawLine(pen, new Point(X(i), plot.Bottom - 2), new Point(X(i), plot.Bottom - 2 - h));
        }
        for (var i = 0; i < Points.Length; i++)
        {
            var at = X(Points[i]); var fill = Points.Length == 1 ? Brushes.White : i == 0 ? Brushes.Black : i == Points.Length - 1 ? Brushes.White : Brushes.Gray;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(new Point(at, plot.Bottom + 3), true); g.LineTo(new Point(at + 7, plot.Bottom + 11)); g.LineTo(new Point(at + 7, plot.Bottom + 16)); g.LineTo(new Point(at - 7, plot.Bottom + 16)); g.LineTo(new Point(at - 7, plot.Bottom + 11)); g.EndFigure(true); }
            context.DrawGeometry(fill, new Pen(Brushes.LightGray, 1), geometry);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); var at = e.GetPosition(this);
        _held = Enumerable.Range(0, Points.Length).OrderBy(i => Math.Abs(X(Points[i]) - at.X)).FirstOrDefault(i => Math.Abs(X(Points[i]) - at.X) < 12, -1);
        if (_held < 0 && Points.Length == 1) _held = 0;
        if (_held >= 0) { e.Pointer.Capture(this); Drag(at); }
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_held >= 0 && ReferenceEquals(e.Pointer.Captured, this)) Drag(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); _held = -1; e.Pointer.Capture(null); }
    private void Drag(Point at)
    {
        var level = Math.Clamp((at.X - 2) / Math.Max(1, Bounds.Width - 4) * 255, 0, 255);
        if (Points.Length == 3)
        {
            if (_held == 0) level = Math.Min(level, Points[2] - 2); else if (_held == 2) level = Math.Max(level, Points[0] + 2); else level = Math.Clamp(level, Points[0] + 1, Points[2] - 1);
            // The gray point rides between the others, keeping its proportion, as Photoshop's does.
            if (_held != 1) { var share = (Points[1] - Points[0]) / Math.Max(1e-6, Points[2] - Points[0]); Points[_held] = Math.Round(level); Points[1] = Points[0] + share * (Points[2] - Points[0]); }
            else Points[1] = level;
        }
        else if (Points.Length == 2) Points[_held] = Math.Round(_held == 0 ? Math.Min(level, Points[1] - 1) : Math.Max(level, Points[0] + 1));
        else Points[_held] = Math.Round(level);
        InvalidateVisual(); Moved?.Invoke();
    }
}

/// <summary>Levels' Output Levels: a black-to-white ramp with its two triangles.</summary>
internal sealed class OutputRamp : Control
{
    private int _held = -1;
    internal double[] Points { get; } = [0, 255];
    internal event Action? Moved;
    public OutputRamp() { Height = 40; }
    private double X(double level) => 2 + (Bounds.Width - 4) * level / 255;
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(AdjustmentKit.Track(Colors.Black, Colors.White), new Rect(0, 0, Bounds.Width, 20));
        for (var i = 0; i < 2; i++)
        {
            var at = X(Points[i]); var geometry = new StreamGeometry();
            using (var g = geometry.Open()) { g.BeginFigure(new Point(at, 23), true); g.LineTo(new Point(at + 7, 31)); g.LineTo(new Point(at + 7, 36)); g.LineTo(new Point(at - 7, 36)); g.LineTo(new Point(at - 7, 31)); g.EndFigure(true); }
            context.DrawGeometry(i == 0 ? Brushes.Black : Brushes.White, new Pen(Brushes.LightGray, 1), geometry);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); var x = e.GetPosition(this).X; _held = Math.Abs(X(Points[0]) - x) <= Math.Abs(X(Points[1]) - x) ? 0 : 1; e.Pointer.Capture(this); Drag(x); }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_held >= 0 && ReferenceEquals(e.Pointer.Captured, this)) Drag(e.GetPosition(this).X); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); _held = -1; e.Pointer.Capture(null); }
    private void Drag(double x) { Points[_held] = Math.Round(Math.Clamp((x - 2) / Math.Max(1, Bounds.Width - 4) * 255, 0, 255)); InvalidateVisual(); Moved?.Invoke(); }
}

/// <summary>
/// Photoshop's three eyedroppers — set black point, gray point, white point — or Hue/Saturation's sample, add and
/// subtract. The adjustment dialogs are modal, so a pick is taken from a small copy of the layer that the button opens,
/// rather than from the canvas behind the dialog.
/// </summary>
internal sealed class Eyedroppers : StackPanel
{
    internal event Action<int, SKColor>? Picked;
    internal Eyedroppers(Func<SKBitmap?> picture, string[] names, Color[] inks, string[]? marks = null)
    {
        Orientation = Orientation.Horizontal; Spacing = 10;
        for (var i = 0; i < names.Length; i++)
        {
            var which = i; var glyph = new EyedropperGlyph(inks[i], marks?[i]);
            var button = new Button { Content = glyph, Width = 34, Height = 32, Padding = new Thickness(4), Background = Brushes.Transparent, BorderThickness = default, Tag = names[i] };
            ToolTip.SetTip(button, Localize.Text(names[i]));
            var image = new Image { Stretch = Stretch.Uniform, MaxWidth = 300, MaxHeight = 300, Cursor = new Cursor(StandardCursorType.Cross) };
            var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = Localize.Text("Click the picture to sample a color."), Foreground = Skin.SecondaryBrush }, image } }, Placement = PlacementMode.Bottom };
            SKBitmap? shown = null;
            button.Click += (_, _) =>
            {
                shown?.Dispose(); shown = picture(); if (shown is null) return;
                image.Source = Thumbnail(shown); flyout.ShowAt(button);
            };
            image.PointerPressed += (_, e) =>
            {
                if (shown is null || image.Bounds.Width <= 0) return; var at = e.GetPosition(image);
                var x = Math.Clamp((int)(at.X / image.Bounds.Width * shown.Width), 0, shown.Width - 1); var y = Math.Clamp((int)(at.Y / image.Bounds.Height * shown.Height), 0, shown.Height - 1);
                var color = shown.GetPixel(x, y); flyout.Hide(); Picked?.Invoke(which, color);
            };
            Children.Add(button);
        }
    }

    /// <summary>A bitmap Avalonia can draw, copied from a Skia one.</summary>
    internal static Avalonia.Media.Imaging.Bitmap Thumbnail(SKBitmap source)
    {
        using var image = SKImage.FromBitmap(source); using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream(data.ToArray()); return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}

/// <summary>An eyedropper drawn with its tip filled in black, gray or white, or marked + / −, as Photoshop draws them.</summary>
internal sealed class EyedropperGlyph(Color ink, string? mark = null) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var body = new StreamGeometry();
        using (var g = body.Open()) { g.BeginFigure(new Point(5, 18), true); g.LineTo(new Point(4, 17)); g.LineTo(new Point(12, 9)); g.LineTo(new Point(14, 11)); g.LineTo(new Point(6, 19)); g.EndFigure(true); }
        context.DrawGeometry(new SolidColorBrush(ink), pen, body);
        context.DrawLine(new Pen(Skin.LabelBrush, 3) { LineCap = PenLineCap.Round }, new Point(13, 8), new Point(17, 4));
        context.DrawLine(pen, new Point(10, 7), new Point(16, 13));
        if (mark is { } m)
        {
            context.DrawLine(pen, new Point(15, 18), new Point(21, 18));
            if (m == "+") context.DrawLine(pen, new Point(18, 15), new Point(18, 21));
        }
    }
}

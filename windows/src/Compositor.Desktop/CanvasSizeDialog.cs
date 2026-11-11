using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

/// <summary>
/// Image ▸ Canvas Size, laid out as Photoshop's: Current Size and New Size with their memory, Width and Height in pixels,
/// inches, centimeters, millimeters, points, picas, percent (and columns across), Relative, the nine-square Anchor and
/// the Canvas extension color with its swatch. The color fills what the canvas gains when the document has a Background
/// layer; otherwise the new canvas is transparent, as in Photoshop.
/// </summary>
internal sealed class CanvasSizeDialog : DialogWindow
{
    private static readonly string[] Units = ["Pixels", "Inches", "Centimeters", "Millimeters", "Points", "Picas", "Percent", "Columns"];
    private static readonly double[] PerInch = [0, 1, 2.54, 25.4, 72, 6, 0, 0];
    private static int _unit;
    private readonly int _startWidth, _startHeight, _bytesPerPixel;
    private readonly double _resolution;
    private double _width, _height;
    private int _anchor;
    private bool _syncing;
    private uint _color;
    private (int Width, int Height, int Anchor, CanvasFill? Fill)? _result;
    internal ValueBox WidthBox { get; } = new(-1e9, 1e9, 0, "0.##", tag: "Width") { Width = 120 };
    internal ValueBox HeightBox { get; } = new(-1e9, 1e9, 0, "0.##", tag: "Height") { Width = 120 };
    internal CheckBox Relative { get; } = new() { Tag = "Relative" };
    internal ComboBox Extension { get; } = new() { Width = 236, Tag = "Canvas extension color" };
    private readonly ComboBox _widthUnit = new() { Width = 236, Tag = "Width unit" }, _heightUnit = new() { Width = 236, Tag = "Height unit" };
    private readonly TextBlock _newSize = new() { FontWeight = FontWeight.Bold };
    private readonly Button[] _anchors = new Button[9];

    private CanvasSizeDialog(int width, int height, int anchor, double resolution, ImageSettings image, bool background, uint foreground, uint backgroundColor)
    {
        Title = Localize.Text("Canvas Size"); SizeToContent = SizeToContent.WidthAndHeight; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _startWidth = width; _startHeight = height; _width = width; _height = height; _anchor = anchor; _resolution = resolution;
        _bytesPerPixel = (image.Mode switch { ImageMode.Grayscale or ImageMode.Bitmap or ImageMode.Indexed or ImageMode.Duotone => 1, ImageMode.CMYK => 4, _ => 3 }) * Math.Max(1, image.Depth / 8);
        _color = backgroundColor;
        Control Heading(TextBlock text) => new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12, Children = { text, new Border { Height = 1, Background = Skin.BorderControlBrush, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 } } };
        var current = new TextBlock { FontWeight = FontWeight.Bold, Text = Localize.Text("Current Size") + "：" + Megabytes((double)width * height * _bytesPerPixel) };
        Grid Pair(string label, string value) => new() { ColumnDefinitions = new("56,*"), Children = { new TextBlock { Text = Localize.Text(label) }, new TextBlock { Text = value, [Grid.ColumnProperty] = 1 } } };

        _widthUnit.ItemsSource = Units.Select(UnitName).ToArray(); _heightUnit.ItemsSource = Units.Take(7).Select(UnitName).ToArray();
        _widthUnit.SelectedIndex = _unit; _heightUnit.SelectedIndex = Math.Min(_unit, 6);
        _widthUnit.SelectionChanged += (_, _) => { if (_syncing) return; _unit = Math.Max(0, _widthUnit.SelectedIndex); if (_unit < 7) { _syncing = true; _heightUnit.SelectedIndex = _unit; _syncing = false; } Refresh(); };
        _heightUnit.SelectionChanged += (_, _) => { if (_syncing) return; _unit = Math.Max(0, _heightUnit.SelectedIndex); _syncing = true; _widthUnit.SelectedIndex = _unit; _syncing = false; Refresh(); };
        WidthBox.ValueChanged += () => { if (!_syncing) Typed(true, WidthBox.Value); };
        HeightBox.ValueChanged += () => { if (!_syncing) Typed(false, HeightBox.Value); };
        Relative.Content = new TextBlock { Text = AdjustmentKit.Word("Relative", "相对于当前尺寸") + "(R)" };
        Relative.IsCheckedChanged += (_, _) => Refresh();
        LayerDialogControls.Key(this, 'R', Relative); LayerDialogControls.Key(this, 'W', WidthBox); LayerDialogControls.Key(this, 'H', HeightBox);

        var fields = new Grid { ColumnDefinitions = new("56,Auto,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 14, RowSpacing = 10 };
        void Put(Grid grid, Control c, int row, int column) { Grid.SetRow(c, row); Grid.SetColumn(c, column); c.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(c); }
        Put(fields, new TextBlock { Text = Localize.Text("Width") }, 0, 0); Put(fields, WidthBox, 0, 1); Put(fields, _widthUnit, 0, 2);
        Put(fields, new TextBlock { Text = Localize.Text("Height") }, 1, 0); Put(fields, HeightBox, 1, 1); Put(fields, _heightUnit, 1, 2);

        // The anchor: nine squares, the chosen one marked, the arrows showing which way the canvas grows.
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 104, Height = 104, Tag = "anchor" };
        for (var i = 0; i < 9; i++)
        {
            var index = i; var button = new Button { Padding = default, Margin = default, BorderBrush = Skin.SecondaryBrush, BorderThickness = new Thickness(.5), Background = Brushes.Transparent, CornerRadius = default, Tag = "anchor " + i, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => { _anchor = index; Arrows(); };
            _anchors[i] = button; grid.Children.Add(button);
        }
        Arrows();
        var anchorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, Children = { new TextBlock { Text = AdjustmentKit.Word("Anchor", "锚点"), VerticalAlignment = VerticalAlignment.Top }, grid } };

        // Canvas extension color: Foreground, Background, White, Black, Gray, Other….
        var colors = new (string Name, uint? Color)[] { ("Foreground", foreground), ("Background", backgroundColor), ("", null), ("White", 0xffffffff), ("Black", 0xff000000), ("Gray", 0xff808080), ("", null), ("Other…", null) };
        Extension.ItemsSource = colors.Select(c => (object)(c.Name.Length == 0 ? new ComboBoxItem { Content = new Separator(), IsEnabled = false, Height = 9, Padding = default } : new ComboBoxItem { Content = c.Name switch { "Foreground" => AdjustmentKit.Word("Foreground", "前景"), "Background" => AdjustmentKit.Word("Background", "背景"), _ => Localize.Text(c.Name) }, Tag = c.Name })).ToArray();
        Extension.SelectedIndex = 1;
        var swatch = new Button { Width = 70, Height = 34, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Tag = "extension swatch" };
        void Paint() => swatch.Background = new SolidColorBrush(Color.FromUInt32(_color));
        async Task Other()
        {
            var c = Color.FromUInt32(_color);
            await ColorPickerDialog.Pick(this, "Color Picker (Canvas Extension Color)", (c.R / 255.0, c.G / 255.0, c.B / 255.0), moved =>
            { _color = 0xff000000u | (uint)Math.Round(moved.Red * 255) << 16 | (uint)Math.Round(moved.Green * 255) << 8 | (uint)Math.Round(moved.Blue * 255); Paint(); });
        }
        Extension.SelectionChanged += async (_, _) =>
        {
            if (Extension.SelectedIndex < 0) return; var chosen = colors[Extension.SelectedIndex];
            if (chosen.Color is { } value) { _color = value; Paint(); } else if (chosen.Name == "Other…") await Other();
        };
        swatch.Click += async (_, _) => { Extension.SelectedIndex = 7; await Other(); };
        Paint();
        var extension = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { new TextBlock { Text = Localize.Text("Canvas extension color"), VerticalAlignment = VerticalAlignment.Center }, Extension, swatch } };
        if (!background)
        {
            extension.IsEnabled = false; extension.Opacity = .5;
            ToolTip.SetTip(extension, Localize.Text("The document has no Background layer, so the added canvas is transparent."));
        }

        var left = new StackPanel
        {
            Width = 470, Spacing = 8,
            Children =
            {
                Heading(current), Pair("Width", $"{width} {Localize.Text("Pixels")}"), Pair("Height", $"{height} {Localize.Text("Pixels")}"),
                new Border { Height = 8 }, Heading(_newSize), fields, new Border { Height = 4 },
                new StackPanel { Margin = new Thickness(70, 4, 0, 6), Children = { Relative } }, anchorRow, new Border { Height = 6 }, extension,
            },
        };
        var actions = AdjustmentKit.Actions(this, () =>
        {
            WidthBox.Commit(); HeightBox.Commit();
            var w = (int)Math.Round(_width); var h = (int)Math.Round(_height);
            if (w < 1 || h < 1 || w > DocumentLimits.MaxSide || h > DocumentLimits.MaxSide) return;
            var fill = background ? new CanvasFill((_color >> 16 & 255) / 255.0, (_color >> 8 & 255) / 255.0, (_color & 255) / 255.0) : (CanvasFill?)null;
            _result = (w, h, _anchor, fill); Close();
        }, Close, null);
        Content = AdjustmentKit.Body(left, actions);
        Refresh();
    }

    private double Inches(double value) => _unit == 7
        ? value * ColumnInches() + Math.Max(0, value - 1) * GutterInches() : value / PerInch[_unit];
    private static double ColumnInches() => EditorPreferences.Current.ColumnWidth / new[] { 1, 2.54, 25.4, 72, 6 }[Math.Clamp(EditorPreferences.Current.ColumnUnit, 0, 4)];
    private static double GutterInches() => EditorPreferences.Current.ColumnGutter / new[] { 1, 2.54, 25.4, 72, 6 }[Math.Clamp(EditorPreferences.Current.GutterUnit, 0, 4)];

    /// <summary>Pixels shown in the unit, measured from nothing or, with Relative, from the current size.</summary>
    private double Shown(double pixels, int start)
    {
        var amount = Relative.IsChecked == true ? pixels - start : pixels;
        return _unit switch
        {
            0 => amount, 6 => Relative.IsChecked == true ? amount / start * 100 : pixels / start * 100,
            7 => (amount / _resolution + GutterInches()) / Math.Max(1e-6, ColumnInches() + GutterInches()), _ => amount / _resolution * PerInch[_unit],
        };
    }

    private void Typed(bool width, double value)
    {
        var start = width ? _startWidth : _startHeight;
        var pixels = _unit switch { 0 => value, 6 => start * value / 100, _ => Inches(value) * _resolution };
        if (Relative.IsChecked == true && _unit != 6) pixels += start; else if (Relative.IsChecked == true) pixels = start + start * value / 100;
        if (width) _width = Math.Max(1, pixels); else _height = Math.Max(1, pixels);
        Refresh();
    }

    private void Refresh()
    {
        _syncing = true;
        WidthBox.Value = Math.Round(Shown(_width, _startWidth), 2); HeightBox.Value = Math.Round(Shown(_height, _startHeight), 2);
        _newSize.Text = Localize.Text("New Size") + "：" + Megabytes(Math.Round(_width) * Math.Round(_height) * _bytesPerPixel);
        _syncing = false;
    }

    private static string UnitName(string unit) => unit == "Columns" ? AdjustmentKit.Word("Columns", "列") : Localize.Text(unit);
    private static string Megabytes(double bytes) => bytes >= 1024 * 1024 ? (bytes / 1024 / 1024).ToString("0.0M", CultureInfo.InvariantCulture) : (bytes / 1024).ToString("0.0K", CultureInfo.InvariantCulture);

    /// <summary>The dot in the anchor's square, and arrows in the neighbors pointing the way the canvas grows.</summary>
    private void Arrows()
    {
        var ax = _anchor % 3; var ay = _anchor / 3;
        for (var i = 0; i < 9; i++)
        {
            var dx = i % 3 - ax; var dy = i / 3 - ay;
            _anchors[i].Content = i == _anchor ? new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brushes.White }
                : Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 ? new TextBlock { Text = (dx, dy) switch { (0, -1) => "↑", (0, 1) => "↓", (-1, 0) => "←", (1, 0) => "→", (-1, -1) => "↖", (1, -1) => "↗", (-1, 1) => "↙", _ => "↘" }, Foreground = Skin.SecondaryBrush }
                : null;
        }
    }

    public static async Task<(int Width, int Height, int Anchor, CanvasFill? Fill)?> Ask(Window owner, int width, int height, int anchor, double resolution = 72,
        ImageSettings? image = null, bool background = false, uint foreground = 0xff000000, uint backgroundColor = 0xffffffff)
    {
        var dialog = new CanvasSizeDialog(width, height, anchor, resolution, image ?? new ImageSettings(), background, foreground, backgroundColor);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}

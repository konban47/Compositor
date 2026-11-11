using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;

namespace Compositor.Desktop;

internal sealed record ColorRow(string Name, string Swatch);

/// <summary>The small pieces Photoshop's Layer menu dialogs share, drawn in Compositor's own controls.</summary>
internal static class LayerDialogControls
{
    /// <summary>Photoshop's layer colors and the swatch each shows, None first.</summary>
    internal static readonly (LayerLabel Label, string Name, string Swatch)[] Colors =
    [
        (LayerLabel.None, "None", ""), (LayerLabel.Red, "Red", "#FF6E6E"), (LayerLabel.Orange, "Orange", "#FFA24D"), (LayerLabel.Yellow, "Yellow", "#E3D85B"),
        (LayerLabel.Green, "Green", "#8FD97A"), (LayerLabel.Teal, "Seafoam", "#1D9A9A"), (LayerLabel.Blue, "Blue", "#8DBDF5"), (LayerLabel.Indigo, "Indigo", "#6A6AE6"),
        (LayerLabel.Magenta, "Magenta", "#D93A8E"), (LayerLabel.Purple, "Fuchsia", "#C24AD3"), (LayerLabel.Violet, "Violet", "#A28BDB"), (LayerLabel.Gray, "Gray", "#A9A9A9"),
    ];

    /// <summary>The layer color list: a crossed box for None, a rule, then the eleven colors with their swatches.</summary>
    internal static ComboBox ColorChoice(LayerLabel current, out Func<LayerLabel> read)
    {
        var box = new ComboBox { Width = 240, Tag = "layer-color", MinHeight = DialogKit.FieldHeight };
        // Rows are data shown by one template, so the closed box draws the chosen swatch too.
        var template = new FuncDataTemplate<ColorRow>((row, _) =>
        {
            if (row is null) return new TextBlock();
            Control mark = row.Swatch.Length == 0
                ? new Border { Width = 16, Height = 16, BorderBrush = Skin.LabelBrush, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(2), Child = new TextBlock { Text = "✕", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                : new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.Parse(row.Swatch)) };
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { mark, new TextBlock { Text = Localize.Text(row.Name), VerticalAlignment = VerticalAlignment.Center } } };
        });
        box.ItemTemplate = template;
        var rows = new List<LayerLabel?>();
        foreach (var (label, name, swatch) in Colors)
        {
            box.Items.Add(new ComboBoxItem { Content = new ColorRow(name, swatch), ContentTemplate = template });
            rows.Add(label);
            if (label == LayerLabel.None)
            {
                box.Items.Add(new ComboBoxItem { Content = new Border { Height = 1, Margin = new Thickness(4, 3), Background = Skin.MenuRule, HorizontalAlignment = HorizontalAlignment.Stretch }, IsEnabled = false, Focusable = false });
                rows.Add(null);
            }
        }
        box.SelectedIndex = Math.Max(0, rows.IndexOf(current));
        read = () => box.SelectedIndex >= 0 && rows[box.SelectedIndex] is { } chosen ? chosen : LayerLabel.None;
        return box;
    }

    /// <summary>
    /// The blend modes in Photoshop's groups, rules between them. A group's list starts with Pass Through, which is what a
    /// group with Normal does in Compositor: its layers blend straight into what is under the group.
    /// </summary>
    internal static ComboBox ModeChoice(LayerBlendMode current, bool group, out Func<LayerBlendMode> read, double width = 240)
    {
        var box = new ComboBox { Width = width, Tag = "layer-mode", MinHeight = DialogKit.FieldHeight };
        IReadOnlyList<LayerBlendMode[]> groups = group ? [[LayerBlendMode.Normal], .. LayerEdits.BlendGroups] : LayerEdits.BlendGroups;
        var rows = GroupedChoice.Fill(box, groups, m => BlendModes(m));
        if (group && box.Items[0] is ComboBoxItem first) first.Content = Localize.Text("Pass Through");
        box.SelectedIndex = Math.Max(0, rows.IndexOf(current));
        read = () => box.SelectedIndex >= 0 && rows[box.SelectedIndex] is { } chosen ? chosen : LayerBlendMode.Normal;
        return box;
    }
    internal static string BlendModes(LayerBlendMode mode) => Compositor.Core.Pixels.BlendModes.Name(Compositor.Core.Pixels.BlendModes.From(mode));

    /// <summary>A percentage field with Photoshop's ⌄ beside it that drops a slider down, as the Opacity fields have.</summary>
    internal static Control PercentField(double percent, string tag, out NumericUpDown field, double min = 0, double max = 100, string unit = "%")
    {
        var input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Clamp(percent, min, max), Increment = 1, FormatString = "0'" + unit + "'", ShowButtonSpinner = false, Width = 76, MinHeight = DialogKit.FieldHeight, Tag = tag };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(percent, min, max), Width = 170 };
        var popup = new Popup { PlacementTarget = input, Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true, Child = new Border { Background = Skin.ChromeBrush, BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Padding = new Thickness(10, 2), Child = slider } };
        var syncing = false;
        slider.ValueChanged += (_, _) => { if (syncing) return; syncing = true; input.Value = (decimal)Math.Round(slider.Value); syncing = false; };
        input.ValueChanged += (_, _) => { if (syncing || input.Value is not { } v) return; syncing = true; slider.Value = (double)v; syncing = false; };
        var arrow = new Button { Content = "⌄", Width = 24, MinHeight = DialogKit.FieldHeight, Padding = default, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Tag = tag + " Slider" };
        arrow.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        field = input;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { input, arrow, popup } };
    }

    /// <summary>A caption as Photoshop's layer dialogs write it, its Alt key in brackets in either language: "Name(N)".</summary>
    internal static TextBlock Caption(string text, char key, bool colon = false) => new()
    { Text = Localize.Text(text) + $"({key})" + (colon ? ":" : ""), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>Alt and the letter focuses the control a caption names, as Photoshop's dialogs do.</summary>
    internal static void Key(Window window, char key, Control target)
    {
        window.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Alt || e.Key != Avalonia.Input.Key.A + (char.ToUpperInvariant(key) - 'A') || !target.IsEffectivelyEnabled) return;
            if (target is ToggleButton toggle) toggle.IsChecked = toggle.IsChecked != true; else if (target is ComboBox combo) combo.IsDropDownOpen = true; else target.Focus();
            e.Handled = true;
        };
    }
}

/// <summary>
/// Photoshop's angle control: a circle with a line from its middle to the angle. With <see cref="Altitude"/> shown, as
/// Bevel and Emboss and Global Light have it, a dot marks angle and altitude together, nearer the rim the lower the light.
/// Dragging sets them; angles are degrees counterclockwise from the right.
/// </summary>
internal sealed class AngleDial : Control
{
    private double _angle, _altitude = 30;
    internal bool ShowsAltitude { get; init; }
    internal event Action? Changed;
    internal double Angle { get => _angle; set { _angle = Normalize(value); InvalidateVisual(); } }
    internal double Altitude { get => _altitude; set { _altitude = Math.Clamp(value, 0, 90); InvalidateVisual(); } }
    internal AngleDial(double size = 52) { Width = Height = size; Cursor = new Cursor(StandardCursorType.Cross); Tag = "angle-dial"; }
    private static double Normalize(double degrees) { degrees %= 360; if (degrees > 180) degrees -= 360; if (degrees <= -180) degrees += 360; return degrees; }
    public override void Render(DrawingContext context)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2); var r = Math.Min(c.X, c.Y) - 1.5;
        context.DrawEllipse(Skin.SurfaceDarkBrush, new Pen(Skin.LabelBrush, 1.2), c, r, r);
        var rad = _angle * Math.PI / 180;
        if (ShowsAltitude)
        {
            var reach = r * (1 - _altitude / 90); var dot = new Point(c.X + Math.Cos(rad) * reach, c.Y - Math.Sin(rad) * reach);
            context.DrawEllipse(null, new Pen(Skin.LabelBrush, 1), dot, 2.5, 2.5);
            context.DrawEllipse(Skin.LabelBrush, null, c, 1.2, 1.2);
        }
        else
        {
            context.DrawLine(new Pen(Skin.LabelBrush, 1.2), c, new Point(c.X + Math.Cos(rad) * r, c.Y - Math.Sin(rad) * r));
            context.DrawEllipse(Skin.LabelBrush, null, c, 1.6, 1.6);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { e.Pointer.Capture(this); Take(e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { if (e.Pointer.Captured == this) Take(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) => e.Pointer.Capture(null);
    internal void Take(Point at)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2); var r = Math.Max(1, Math.Min(c.X, c.Y) - 1.5);
        var dx = at.X - c.X; var dy = c.Y - at.Y;
        _angle = Math.Round(Math.Atan2(dy, dx) * 180 / Math.PI);
        if (ShowsAltitude) _altitude = Math.Round(Math.Clamp(90 * (1 - Math.Sqrt(dx * dx + dy * dy) / r), 0, 90));
        InvalidateVisual(); Changed?.Invoke();
    }
}

/// <summary>Layer ▸ Combine Shapes: two overlapping squares, filled where the operation keeps area.</summary>
internal sealed class ShapeOperationGlyph(SkiaSharp.SKPathOp operation) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);
    public override void Render(DrawingContext context)
    {
        var back = new RectangleGeometry(new Rect(1.5, 1.5, 9, 9)); var front = new RectangleGeometry(new Rect(5.5, 5.5, 9, 9));
        var mode = operation switch { SkiaSharp.SKPathOp.Union => GeometryCombineMode.Union, SkiaSharp.SKPathOp.Difference => GeometryCombineMode.Exclude,
            SkiaSharp.SKPathOp.Intersect => GeometryCombineMode.Intersect, _ => GeometryCombineMode.Xor };
        context.DrawGeometry(Skin.LabelBrush, null, new CombinedGeometry(mode, back, front));
        var pen = new Pen(Skin.LabelBrush, 1);
        context.DrawGeometry(null, pen, back); context.DrawGeometry(null, pen, front);
    }
}

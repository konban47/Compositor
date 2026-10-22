using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;
internal sealed partial class ToolOptionsBar
{
    internal event Action<string>? UtilityAsked;
    private readonly Dictionary<string, Button> _utilityButtons = [];
    private readonly Dictionary<string, TextBlock> _measureFields = [];
    private readonly TextBlock _countTotal = new() { VerticalAlignment = VerticalAlignment.Center, Width = 55 };
    private ComboBox? _countGroups;
    private TextBox? _noteAuthor;
    private Button UtilityButton(string group, string action, string? icon = null)
    {
        var button = icon is null ? new Button { Content = Localize.Text(action), Height = 26, Padding = new Thickness(8, 2) } : IconButton(icon, action, null);
        button.Tag = action; button.Click += (_, _) => UtilityAsked?.Invoke(action); _utilityButtons[action] = button; Cell(group, button); return button;
    }
    private void BuildUtilityOptions()
    {
        var p = Professional;
        for (var shape = 0; shape < 5; shape++)
        {
            var n = shape; var title = new[] { "Rectangle Frame", "Ellipse Frame", "Triangle Frame", "Polygon Frame", "Custom Frame" }[shape];
            var button = UtilityButton("utilityFrame", title); button.Width = 30; button.Padding = new Thickness(2);
            _optionRefreshers.Add(() => button.Background = p.FrameShape == n ? Skin.SurfaceDarkBrush : Brushes.Transparent);
            button.Content = new FrameGlyph(shape) { Width = 24, Height = 24 }; ToolTip.SetTip(button, Localize.Text(title));
        }
        OptionLabel("utilityFrame", "Stroke:"); UtilityButton("utilityFrame", "Frame Stroke");
        OptionNumber("utilityFrame", "", () => p.FrameStrokeWidth, v => p.FrameStrokeWidth = v, 0, 1000, 70);
        OptionChoice("utilityFrame", "", ["Inside", "Center", "Outside"], () => p.FrameAlignment, v => p.FrameAlignment = v, 105);
        OptionNumber("utilityFrame", "Corner:", () => p.FrameCorner, v => p.FrameCorner = v, 0, 30000);
        UtilityButton("utilityFrame", "Place Image in Frame…", "image");
        var sizes = ColorSampling.Sizes.Select(n => n == 1 ? "Point Sample" : $"{n} × {n} Average").ToArray();
        OptionChoice("utilityEye", "Sample Size:", sizes, () => Array.IndexOf(ColorSampling.Sizes, p.EyeSize), v => p.EyeSize = ColorSampling.Sizes[v], 135);
        OptionChoice("utilityEye", "Sample:", ["Current Layer", "Current & Below", "All Layers", "All Layers No Adjustments", "Current & Below No Adjustments"], () => p.EyeScope, v => p.EyeScope = v, 215);
        Cell("utilityEye", _sampleRing);
        OptionChoice("utilitySampler", "Sample Size:", sizes, () => Array.IndexOf(ColorSampling.Sizes, p.SamplerSize), v => p.SamplerSize = ColorSampling.Sizes[v], 135);
        UtilityButton("utilitySampler", "Clear Samplers");
        foreach (var name in new[] { "X", "Y", "W", "H", "A", "L1", "L2" })
        {
            var value = new TextBlock { Text = name + ": 0.00", MinWidth = 72, VerticalAlignment = VerticalAlignment.Center, Tag = "measure-" + name };
            _measureFields[name] = value; Cell("utilityRuler", value);
        }
        OptionCheck("utilityRuler", "Use Measurement Scale", () => p.MeasureUseScale, v => p.MeasureUseScale = v);
        UtilityButton("utilityRuler", "Straighten Layer"); UtilityButton("utilityRuler", "Clear Ruler");
        OptionLabel("utilityNote", "Author:");
        _noteAuthor = new TextBox { Width = 230, Height = 26, MaxLength = 256, Tag = "note-author" };
        _noteAuthor.TextChanged += (_, _) => { if (!_loading) { p.NoteAuthor = _noteAuthor.Text ?? ""; Changed?.Invoke(); } }; Cell("utilityNote", _noteAuthor);
        OptionLabel("utilityNote", "Color:"); UtilityButton("utilityNote", "Note Color"); UtilityButton("utilityNote", "Clear Notes"); UtilityButton("utilityNote", "Notes Panel", "Note");
        OptionLabel("utilityCount", "Count:"); Cell("utilityCount", _countTotal);
        _countGroups = new ComboBox { Width = 160, Height = 26, Tag = "count-groups" }; StyleOptionCombo(_countGroups);
        _countGroups.SelectionChanged += (_, _) => { if (!_loading && _countGroups.SelectedItem is string name) { p.CountGroup = name; UtilityAsked?.Invoke("Select Count Group"); } }; Cell("utilityCount", _countGroups);
        UtilityButton("utilityCount", "Toggle Count Group", "eye"); UtilityButton("utilityCount", "New Count Group", "folder"); UtilityButton("utilityCount", "Delete Count Group", "trash");
        UtilityButton("utilityCount", "Rename Count Group", "rename"); UtilityButton("utilityCount", "Clear Count Group"); UtilityButton("utilityCount", "Count Color");
        OptionNumber("utilityCount", "Marker Size:", () => p.CountMarkerSize, v => p.CountMarkerSize = v, 1, 10, 48);
        OptionNumber("utilityCount", "Label Size:", () => p.CountLabelSize, v => p.CountLabelSize = v, 8, 72, 48);
        _optionRefreshers.Add(() => {
            if (_noteAuthor is not null) _noteAuthor.Text = p.NoteAuthor;
            foreach (var (key, color) in new[] { ("Frame Stroke", p.FrameStroke), ("Note Color", (uint?)p.NoteColor), ("Count Color", (uint?)p.CountColor) })
            {
                var b = _utilityButtons[key]; b.Content = color is { } c ? new Border { Width = 34, Height = 16, Background = new SolidColorBrush(Color.FromUInt32(c)), BorderBrush = Skin.LabelBrush, BorderThickness = new Thickness(1) } : new TextBlock { Text = "∕", Foreground = Brushes.IndianRed, Width = 34, TextAlignment = Avalonia.Media.TextAlignment.Center };
                ToolTip.SetTip(b, Localize.Text(key));
            }
        });
    }
    private void ShowUtilityOptions(Tool tool)
    {
        On("frame", false); On("eye", false); On("ruler", false); On("count", false);
        On("utilityFrame", tool == Tool.Frame); On("utilityEye", tool == Tool.Eyedropper); On("utilitySampler", tool == Tool.ColorSampler);
        On("utilityRuler", tool == Tool.Ruler); On("utilityNote", tool == Tool.Note); On("utilityCount", tool == Tool.Count);
        if (tool is Tool.Frame or Tool.Ruler or Tool.Note or Tool.Count or Tool.ColorSampler) On("marks", false);
        if (tool == Tool.ColorSampler) On("proSample", false);
        if (tool is Tool.Ruler or Tool.Note or Tool.Count) On("proReadout", false);
    }
    internal void UtilityDocument(CanvasDocument? doc, DocumentMark? selected, Guid? layer)
    {
        var loading = _loading; _loading = true;
        try
        {
            var groups = (doc?.CountGroups.Select(g => g.Name) ?? []).Concat(doc?.Marks.Where(m => m.Kind == MarkKind.Count).Select(m => m.Group) ?? []).Append(Professional.CountGroup).Distinct().ToArray();
            if (_countGroups is not null) { if (!groups.SequenceEqual((_countGroups.ItemsSource as string[]) ?? [])) _countGroups.ItemsSource = groups; _countGroups.SelectedItem = Professional.CountGroup; }
            _countTotal.Text = (doc?.Marks.Count(m => m.Kind == MarkKind.Count && m.Group == Professional.CountGroup) ?? 0).ToString();
            var measure = selected?.Kind == MarkKind.Measure ? selected : doc?.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure);
            var ratio = Professional.MeasureUseScale && doc is not null ? doc.MeasurementScale.Units / doc.MeasurementScale.Pixels : 1;
            var x = measure?.X ?? 0; var y = measure?.Y ?? 0; var w = measure?.Width ?? 0; var h = measure?.Height ?? 0;
            var a = Math.Atan2(h, w) * 180 / Math.PI;
            var l2 = measure?.SecondX is { } sx && measure.SecondY is { } sy ? double.Hypot(sx - x, sy - y) : 0;
            if (l2 > 0) a = Math.Abs(Math.IEEERemainder(Math.Atan2(measure!.SecondY!.Value - y, measure.SecondX!.Value - x) * 180 / Math.PI - a, 360));
            foreach (var (name, number) in new[] { ("X", x * ratio), ("Y", y * ratio), ("W", w * ratio), ("H", h * ratio), ("A", a), ("L1", double.Hypot(w, h) * ratio), ("L2", l2 * ratio) }) _measureFields[name].Text = $"{name}: {number:0.00}" + (name == "A" ? "°" : "");
            _utilityButtons["Straighten Layer"].IsEnabled = measure is not null && layer is { } id && doc is not null && LayerProtection.CanMove(doc, id);
            _utilityButtons["Clear Ruler"].IsEnabled = measure is not null;
            _utilityButtons["Clear Samplers"].IsEnabled = doc?.Marks.Any(m => m.Kind == MarkKind.Sampler) == true;
            _utilityButtons["Clear Notes"].IsEnabled = doc?.Marks.Any(m => m.Kind == MarkKind.Note) == true;
            _utilityButtons["Notes Panel"].IsEnabled = doc?.Marks.Any(m => m.Kind == MarkKind.Note) == true;
            _utilityButtons["Place Image in Frame…"].IsEnabled = doc?.Layers.Any(l => l.ID == layer && l.Container == LayerContainer.Frame) == true;
        }
        finally { _loading = loading; }
    }
    private sealed class FrameGlyph(int kind) : Control
    {
        public override void Render(DrawingContext context)
        {
            using var path = FrameEdits.Path(new FrameStyle { Shape = kind, CustomPath = ProfessionalDefaults.Star }, 18, 18);
            using var pushed = context.PushTransform(Matrix.CreateTranslation(3, 3));
            context.DrawGeometry(null, new Pen(Skin.LabelBrush, 1.4), StreamGeometry.Parse(path.ToSvgPathData()));
            context.DrawLine(new Pen(Skin.SecondaryBrush, 1), new Point(3, 3), new Point(15, 15)); context.DrawLine(new Pen(Skin.SecondaryBrush, 1), new Point(3, 15), new Point(15, 3));
        }
    }
}
internal static class ProfessionalDefaults { internal const string Star = "M.5 0 L.62 .35 L1 .38 L.72 .62 L.8 1 L.5 .78 L.2 1 L.28 .62 L0 .38 L.38 .35 Z"; }

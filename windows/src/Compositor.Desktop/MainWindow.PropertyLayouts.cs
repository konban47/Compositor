using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private int _propertyRulerUnit;
    private bool _documentProportions;
    private static Control PropertyIconField(string icon, string tooltip, Control field)
    {
        var label = new InspectorGlyph(icon) { Width = 24, Height = 24 }; ToolTip.SetTip(label, Localize.Text(tooltip));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*") }; row.Children.Add(label); Grid.SetColumn(field, 1); row.Children.Add(field); return row;
    }
    private void BuildDocumentProperties(CanvasDocument document)
    {
        PropertyHeading("Document", "document");
        void Size(int w, int h) => Edit("Canvas Size", () => CanvasEdits.Resize(document, w, h));
        var dimensions = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), MinWidth = 256 };
        var linked = PropertyToggle("link", "Constrain Proportions", _documentProportions, on => _documentProportions = on);
        dimensions.Children.Add(linked); Grid.SetRowSpan(linked, 2);
        void Put(Control field, int row, int col) { field.Margin = new Thickness(3, 4); Grid.SetRow(field, row); Grid.SetColumn(field, col); dimensions.Children.Add(field); }
        Put(InlineProperty("W", PropertyNumber("canvas-width", document.Width, 1, DocumentLimits.MaxSide, value => Size((int)value, _documentProportions ? Math.Max(1, (int)Math.Round(document.Height * value / document.Width)) : document.Height))), 0, 1);
        Put(InlineProperty("H", PropertyNumber("canvas-height", document.Height, 1, DocumentLimits.MaxSide, value => Size(_documentProportions ? Math.Max(1, (int)Math.Round(document.Width * value / document.Height)) : document.Width, (int)value))), 1, 1);
        var x = PropertyNumber("canvas-x", 0, 0, 0, _ => { }); x.IsEnabled = false;
        var y = PropertyNumber("canvas-y", 0, 0, 0, _ => { }); y.IsEnabled = false;
        Put(InlineProperty("X", x), 0, 2); Put(InlineProperty("Y", y), 1, 2);
        var orientation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(40, 2, 0, 4) };
        orientation.Children.Add(PropertyToggle("portrait", "Portrait", document.Height >= document.Width, _ => Size(Math.Min(document.Width, document.Height), Math.Max(document.Width, document.Height))));
        orientation.Children.Add(PropertyToggle("landscape", "Landscape", document.Width > document.Height, _ => Size(Math.Max(document.Width, document.Height), Math.Min(document.Width, document.Height))));
        var modes = Enum.GetValues<ImageMode>();
        var mode = PropertyChoice("Mode", modes.Select(ModeTitle).ToArray(), Array.IndexOf(modes, document.Image.Mode), i => _ = ConvertImageMode(modes[i]));
        var depth = PropertyChoice("Bit Depth", ["8 Bits/Channel", "16 Bits/Channel", "32 Bits/Channel"], Array.IndexOf(new[] { 8, 16, 32 }, document.Image.Depth), i => _ = ConvertImageDepth(new[] { 8, 16, 32 }[i]));
        var fill = PropertyChoice("Canvas Fill", ["Transparent", "White", "Black", "Foreground Color"], document.Image.Background is null ? 0 : document.Image.Background == 0xFFFFFFFF ? 1 : document.Image.Background == 0xFF000000 ? 2 : 3,
            i => Edit("Canvas Fill", () => { document.Image = document.Image with { Background = i switch { 0 => null, 1 => 0xFFFFFFFFu, 2 => 0xFF000000u, _ => (uint)BrushColour() } }; return true; }));
        Section("Canvas", dimensions, orientation,
            PropertyPair(PropertyLabel("Resolution (ppi)"), PropertyNumber("canvas-resolution", document.Resolution, 1, 9600, v => Edit("Resolution", () => { document.Resolution = v; return true; }))),
            PropertyPair(PropertyLabel("Mode"), mode), PropertyPair(PropertyLabel("Bit Depth"), depth), PropertyPair(PropertyLabel("Fill"), fill));
        var grids = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        grids.Children.Add(PropertyToggle("Ruler", "Rulers", _rulersVisible, _ => ShowRulers()));
        grids.Children.Add(PropertyToggle("grid", "Grid", _gridVisible, _ => ShowGrid()));
        grids.Children.Add(PropertyToggle("transparency", "Transparency Grid", _canvas.ShowsTransparency, on => { _canvas.ShowsTransparency = on; _canvas.InvalidateVisual(); }));
        var units = PropertyChoice("Ruler Units", ["Pixels", "Inches", "Centimeters", "Points"], _propertyRulerUnit, i => { _propertyRulerUnit = i; UpdateRulers(); });
        Section("Rulers and Grids", PropertyPair(grids, units), MoreProperties("Grids", PropertyAction("Grid Settings…", () => _ = GridSettings())));
        var guides = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        guides.Children.Add(PropertyToggle("guides", "Show Guides", _guidesVisible, _ => ShowGuides()));
        guides.Children.Add(PropertyToggle("guides-lock", "Lock Guides", _guidesLocked, _ => LockGuides()));
        guides.Children.Add(PropertyToggle("snap", "Snap", _snappingOn, _ => ShowSnapping()));
        var line = PropertyChoice("Guide Style", ["Solid", "Dashed", "Dotted"], _canvas.GuideLineStyle, i => { _canvas.GuideLineStyle = i; _canvas.InvalidateVisual(); });
        Section("Guides", PropertyPair(guides, line), MoreProperties("Guides", PropertyPair(PropertyAction("New Guide…", () => _ = NewGuide()), PropertyAction("Clear Guides", ClearGuides))));
        Section("Quick Actions", PropertyPair(PropertyAction("Image Size…", () => _ = ImageSize()), PropertyAction("Crop", () => SetTool(Tool.Crop))),
            PropertyPair(PropertyAction("Trim…", () => _ = Trim()), PropertyAction("Rotate…", () => _ = RotateImageArbitrary())));
    }

    private void BuildTextProperties(Guid id, LayerTextStyle text)
    {
        var layout = text.Layout ?? new();
        void Change(Action<LayerTextStyle> modify)
        {
            if (_document is not { } document || PropertyLayer?.ID != id) return;
            var style = JsonSerializer.Deserialize<LayerTextStyle>(JsonSerializer.Serialize(text, ManifestJson.Options), ManifestJson.Options)!;
            modify(style); Edit("Character Properties", () => TextEdits.SetStyle(document, id, style)); ShowLayers(document);
        }
        void Layout(TextLayoutOptions next) => Change(style => style.Layout = next == new TextLayoutOptions() ? null : next);
        ToggleButton Flag(string name, string icon, bool? on, Action<LayerTextStyle, bool?> set) => PropertyToggle(icon, name, on == true, value => Change(style => set(style, value ? true : null)));
        ToggleButton Feature(string name, string icon, string tag) => PropertyToggle(icon, name, text.Features?.Contains(tag) == true, on => Change(style => { var tags = (style.Features ?? []).ToHashSet(); if (on) tags.Add(tag); else tags.Remove(tag); style.Features = tags.Count == 0 ? null : tags.ToList(); }));
        Control Number(string icon, string label, string tag, double value, double min, double max, Action<double> change) => PropertyIconField(icon, label, PropertyNumber(tag, value, min, max, change));
        var font = new TextBox { Text = text.FontName, Tag = "property-font", MinHeight = 28, Padding = new Thickness(5, 2), FontSize = 12, CornerRadius = new CornerRadius(1), Background = PropertyFieldSurface, BorderBrush = PropertyLine };
        ToolTip.SetTip(font, Localize.Text("Font Family"));
        font.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(font.Text) && font.Text != text.FontName) Change(style => { style.FontName = font.Text.Trim(); style.FontRuns = null; }); };
        var face = PropertyChoice("Font Style", ["Regular", "Bold", "Italic", "Bold Italic"], (text.Bold == true ? 1 : 0) + (text.Italic == true ? 2 : 0), i => Change(style => { style.Bold = (i & 1) != 0 ? true : null; style.Italic = (i & 2) != 0 ? true : null; }));
        var color = new TextBox { Text = $"#{(int)(text.Red * 255):X2}{(int)(text.Green * 255):X2}{(int)(text.Blue * 255):X2}", Tag = "property-text-color", Height = 28, Padding = new Thickness(5, 2), Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb((byte)(text.Red * 255), (byte)(text.Green * 255), (byte)(text.Blue * 255))),
            Foreground = text.Red + text.Green + text.Blue > 1.5 ? Brushes.Black : Brushes.White, FontSize = 11, CornerRadius = new CornerRadius(0) };
        ToolTip.SetTip(color, Localize.Text("Text Color (#RRGGBB)"));
        color.LostFocus += (_, _) => { if (SKColor.TryParse(color.Text, out var parsed) && color.Text != $"#{(int)(text.Red * 255):X2}{(int)(text.Green * 255):X2}{(int)(text.Blue * 255):X2}") Change(style => { style.Red = parsed.Red / 255.0; style.Green = parsed.Green / 255.0; style.Blue = parsed.Blue / 255.0; style.ColorRuns = null; }); };
        var characterMore = new StackPanel { Spacing = 10 };
        characterMore.Children.Add(PropertyPair(Number("v-scale", "Vertical Scale (%)", "text-vscale", layout.VerticalScale, 1, 1000, v => Layout(layout with { VerticalScale = v })), Number("h-scale", "Horizontal Scale (%)", "text-hscale", layout.HorizontalScale, 1, 1000, v => Layout(layout with { HorizontalScale = v }))));
        characterMore.Children.Add(PropertyPair(Number("baseline", "Baseline Shift (px)", "text-baseline", layout.BaselineShift, -10000, 10000, v => Layout(layout with { BaselineShift = v })), PropertyPair(PropertyLabel("Color"), color)));
        var languages = new[] { "en", "zh-Hans", "zh-Hant", "ja", "ko", "ar", "he", "hi", "fa" };
        characterMore.Children.Add(PropertyPair(PropertyChoice("Language", ["English", "Simplified Chinese", "Traditional Chinese", "Japanese", "Korean", "Arabic", "Hebrew", "Hindi", "Persian"], Math.Max(0, Array.IndexOf(languages, text.Language ?? "en")), i => Change(style => style.Language = languages[i])),
            PropertyChoice("Anti-aliasing", ["None", "Sharp", "Crisp", "Strong", "Smooth"], (int)layout.Smoothing, i => Layout(layout with { Smoothing = (TextSmoothing)i }))));
        var bold = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bold.Children.Add(Flag("Bold", "bold", text.Bold, (s, v) => s.Bold = v)); bold.Children.Add(Flag("Italic", "italic", text.Italic, (s, v) => s.Italic = v)); characterMore.Children.Add(bold);
        Section("Character", font, face,
            PropertyPair(Number("type-size", "Size (pt)", "text-size", text.FontSize * 72 / _document!.Resolution, 72 / _document.Resolution, 2000 * 72 / _document.Resolution, v => Change(style => style.FontSize = v * _document.Resolution / 72)), Number("leading", "Leading (px, 0 = Auto)", "text-leading", text.Leading, 0, 5000, v => Change(style => style.Leading = v))),
            PropertyPair(PropertyIconField("kerning", "Kerning", PropertyChoice("Kerning", ["Metrics", "None"], text.Kerning == false ? 1 : 0, i => Change(style => style.Kerning = i == 0))), Number("tracking", "Tracking (px)", "text-tracking", text.Tracking, -100, 1000, v => Change(style => style.Tracking = v))), MoreProperties("Character", characterMore, true));
        var typeOptions = new WrapPanel { Orientation = Orientation.Horizontal };
        typeOptions.Children.Add(Flag("All Caps", "all-caps", text.AllCaps, (s, v) => { s.AllCaps = v; if (v == true) s.SmallCaps = null; }));
        typeOptions.Children.Add(Flag("Small Caps", "small-caps", text.SmallCaps, (s, v) => { s.SmallCaps = v; if (v == true) s.AllCaps = null; }));
        typeOptions.Children.Add(Flag("Superscript", "superscript", text.Superscript, (s, v) => { s.Superscript = v; if (v == true) s.Subscript = null; }));
        typeOptions.Children.Add(Flag("Subscript", "subscript", text.Subscript, (s, v) => { s.Subscript = v; if (v == true) s.Superscript = null; }));
        typeOptions.Children.Add(Flag("Underline", "underline", text.Underline, (s, v) => s.Underline = v));
        typeOptions.Children.Add(Flag("Strikethrough", "strike", text.Strikethrough, (s, v) => s.Strikethrough = v));
        typeOptions.Children.Add(Feature("Ordinals", "ordinals", "ordn")); typeOptions.Children.Add(Feature("Fractions", "fractions", "frac"));
        var openType = new WrapPanel();
        openType.Children.Add(Flag("Ligatures", "ligatures", text.Ligatures, (s, v) => s.Ligatures = v));
        foreach (var item in new[] { ("Contextual Alternates", "alternates", "calt"), ("Discretionary Ligatures", "discretionary", "dlig"), ("Swashes", "swash", "swsh"), ("Stylistic Alternates", "stylistic", "salt"), ("Titling Alternates", "titling", "titl") }) openType.Children.Add(Feature(item.Item1, item.Item2, item.Item3));
        void Exclusive(string[] tags, string? enabled) => Change(style => { var features = (style.Features ?? []).Where(t => !tags.Contains(t)).ToList(); if (enabled is not null) features.Add(enabled); style.Features = features.Count == 0 ? null : features; });
        var numeralTags = new[] { "lnum", "onum", "tnum", "pnum" }; var posTags = new[] { "numr", "dnom" };
        Section("Type Options", typeOptions, openType, MoreProperties("Type Options", new StackPanel { Spacing = 10, Children = {
            PropertyPair(PropertyLabel("Numerals"), PropertyChoice("Numerals", ["Default", "Lining", "Oldstyle", "Tabular", "Proportional"], Math.Max(0, Array.FindIndex(numeralTags, t => text.Features?.Contains(t) == true) + 1), i => Exclusive(numeralTags, i == 0 ? null : numeralTags[i - 1]))),
            PropertyPair(PropertyLabel("Position"), PropertyChoice("Position", ["Default", "Numerator", "Denominator"], Math.Max(0, Array.FindIndex(posTags, t => text.Features?.Contains(t) == true) + 1), i => Exclusive(posTags, i == 0 ? null : posTags[i - 1]))) } }));
        var align = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        void EnsureBox(LayerTextStyle style) => style.BoxSize ??= new JsonSize { Width = PropertyLayer!.Asset!.Width, Height = PropertyLayer.Asset.Height };
        foreach (var (name, index) in new[] { ("Left", 0), ("Center", 1), ("Right", 2) })
            align.Children.Add(PropertyToggle("paragraph-" + index, "Align " + name, layout.Justification == TextJustification.None && (int)text.Alignment == index, _ => Change(style => { style.Alignment = (Compositor.Core.Format.TextAlignment)index; style.Layout = layout with { Justification = TextJustification.None }; EnsureBox(style); })));
        for (var i = 1; i <= 4; i++) { var value = i; align.Children.Add(PropertyToggle("justify-" + i, new[] { "", "Justify Last Left", "Justify Last Center", "Justify Last Right", "Justify All" }[i], (int)layout.Justification == i, _ => Change(style => { style.Layout = layout with { Justification = (TextJustification)value }; EnsureBox(style); }))); }
        var paragraphMore = new StackPanel { Spacing = 10, Children = {
            PropertyPair(Number("indent-left", "Left Indent (px)", "text-indent-left", layout.LeftIndent, 0, 10000, v => Layout(layout with { LeftIndent = v })), Number("indent-right", "Right Indent (px)", "text-indent-right", layout.RightIndent, 0, 10000, v => Layout(layout with { RightIndent = v }))),
            PropertyPair(Number("indent-first", "First Line Indent (px)", "text-indent-first", layout.FirstLineIndent, -10000, 10000, v => Layout(layout with { FirstLineIndent = v })), new Border()),
            PropertyPair(Number("space-before", "Space Before (px)", "text-space-before", layout.SpaceBefore, 0, 10000, v => Layout(layout with { SpaceBefore = v })), Number("space-after", "Space After (px)", "text-space-after", layout.SpaceAfter, 0, 10000, v => Layout(layout with { SpaceAfter = v }))),
            PropertyAction("Edit Text…", () => _ = EditText()) } };
        Section("Paragraph", align, MoreProperties("Paragraph", paragraphMore, true));
        var dynamic = new CheckBox { Content = new TextBlock { Text = Localize.Text("Dynamic text (fills {width}, {date} and other tokens)"), TextWrapping = TextWrapping.Wrap, FontSize = 12 }, IsChecked = text.Dynamic == true };
        dynamic.IsCheckedChanged += (_, _) => Change(style => style.Dynamic = dynamic.IsChecked == true ? true : null);
        Section("Dynamic Text", dynamic);
        var lists = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        lists.Children.Add(InspectorButton("bullets", "Bulleted List", () => Change(style => { style.Content = string.Join('\n', style.Content.Split('\n').Select(line => line.StartsWith("• ") ? line[2..] : "• " + line)); style.ColorRuns = null; style.FontRuns = null; })));
        lists.Children.Add(InspectorButton("numbered", "Numbered List", () => Change(style => { style.Content = string.Join('\n', style.Content.Split('\n').Select((line, index) => $"{index + 1}. {System.Text.RegularExpressions.Regex.Replace(line, @"^\d+\. ", "")}")); style.ColorRuns = null; style.FontRuns = null; })));
        Section("Bullets and Numbering", lists);
        var cjkMore = new StackPanel { Spacing = 10, Children = {
            PropertyPair(PropertyLabel("Kinsoku"), PropertyChoice("Kinsoku", ["None", "Standard"], layout.Kinsoku ? 1 : 0, i => Layout(layout with { Kinsoku = i == 1 }))),
            Feature("Proportional CJK Metrics", "proportional", "palt"), Feature("Vertical Alternates", "vertical-alternates", "vert") } };
        Section("East Asian Features", Number("tsume", "Tsume (%)", "text-tsume", layout.Tsume, 0, 100, v => Layout(layout with { Tsume = v })), MoreProperties("East Asian", cjkMore));
        var directions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        directions.Children.Add(PropertyToggle("ltr", "Left to right", text.Direction == Compositor.Core.Format.TextDirection.LeftToRight, _ => Change(style => style.Direction = Compositor.Core.Format.TextDirection.LeftToRight)));
        directions.Children.Add(PropertyToggle("rtl", "Right to left", text.Direction == Compositor.Core.Format.TextDirection.RightToLeft, _ => Change(style => style.Direction = Compositor.Core.Format.TextDirection.RightToLeft)));
        Section("Middle Eastern Features", directions, MoreProperties("Middle Eastern", new StackPanel { Spacing = 10, Children = {
            PropertyChoice("Reading Direction", ["Auto direction", "Left to right", "Right to left"], (int)text.Direction, i => Change(style => style.Direction = (Compositor.Core.Format.TextDirection)i)),
            Feature("Justification Alternates", "justify-alternates", "jalt"), Flag("Complex Shaping", "shaping", text.ComplexShaping, (s, v) => s.ComplexShaping = v) } }));
    }
}

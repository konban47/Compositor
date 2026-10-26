using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;
using TypeAlignment = Compositor.Core.Format.TextAlignment;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private string? _typeBarKey;
    private static readonly double[] TypeSizes = [6, 8, 9, 10, 11, 12, 14, 16, 18, 24, 30, 36, 48, 60, 72, 150, 300];
    private double TypeResolution => _document?.Resolution ?? 72;
    private void RefreshTypography()
    {
        var text = CurrentType ?? TypeDefaults;
        var key = _tool + "/" + (_text is not null) + "/" + TypeResolution + "/" + TypographyKey(text);
        if (_optionsBar.GetVisualDescendants().Any(c => c is Avalonia.Controls.Primitives.TemplatedControl { IsFocused: true } && c is not Button)) return;
        if (_typeBarKey == key) return; _typeBarKey = key;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Tag = "type-options" };
        row.Children.Add(TypeButton("T↕", "Change Text Orientation", ToggleTypeOrientation));
        var font = TypeFont(text); font.Width = 190; row.Children.Add(font);
        var face = TypeFace(text); face.Width = 170; row.Children.Add(face);
        var size = TypeNumber("type-option-size", "Size (pt)", text.FontSize * 72 / TypeResolution, 72 / TypeResolution, 2000 * 72 / TypeResolution,
            v => ChangeType(s => s.FontSize = v * TypeResolution / 72), TypeSizes, " pt"); size.Width = 100; row.Children.Add(size);
        row.Children.Add(TypeAlignments(text, false));
        row.Children.Add(TypeColor(text, _tool is Tool.TypeMask or Tool.VerticalTypeMask));
        var shape = TypeButton("Tϟ", "Dynamic Text", () => { }); var menu = new MenuFlyout();
        foreach (var name in new[] { "Block", "Circle", "Arch", "Bowtie" }) menu.Items.Add(TypeItem(name, () => ChangeType(s => { s.Geometry = new(Enum.Parse<TextGeometryShape>(name)); s.BoxSize = null; }, true)));
        shape.Flyout = menu; row.Children.Add(shape);
        row.Children.Add(TypeButton("T⌒", "Warp Text…", () => _ = WarpType()));
        row.Children.Add(TypeButton("▤", "Character and Paragraph Panels", () => { OpenTypePanel("Paragraph"); OpenTypePanel("Character"); }));
        if (_text is not null) { row.Children.Add(TypeButton("⊘", "Cancel Text", CancelText)); row.Children.Add(TypeButton("✓", "Commit Text", CommitText)); }
        _optionsBar.SetTypeControls(row);
    }

    private static string TypographyKey(LayerTextStyle style)
    {
        var flat = TextRanges.Flat(style); flat.Content = ""; flat.StyleRuns = null; flat.ColorRuns = null; flat.FontRuns = null;
        return System.Text.Json.JsonSerializer.Serialize(flat);
    }
    private Button TypeButton(string glyph, string title, Action action)
    {
        var button = new Button { Content = glyph, Tag = title, Height = 27, MinHeight = 0, MinWidth = 28, Padding = new Thickness(5, 1), FontSize = 15 };
        ToolTip.SetTip(button, Localize.Text(title)); button.Click += (_, _) => action(); return button;
    }
    private MenuItem TypeItem(string title, Action action, bool? check = null, bool enabled = true)
    {
        var item = new MenuItem { Header = Localize.Text(title), Tag = title, IsEnabled = enabled, ToggleType = check is null ? MenuItemToggleType.None : MenuItemToggleType.CheckBox, IsChecked = check == true };
        item.Click += (_, _) => action(); return item;
    }
    private Control TypeFont(LayerTextStyle text)
    {
        var box = new TextBox { Text = FontCatalog.Family(text.FontName), Tag = "type-font-family", MinHeight = 26, Padding = new Thickness(4, 2), FontSize = 12 };
        var chosen = FontCatalog.Family(text.FontName);
        void Choose(string name) { chosen = FontCatalog.Family(name); box.Text = chosen; ChangeType(s => { s.FontName = name; s.FontRuns = null; }); }
        box.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(box.Text) && box.Text != chosen) Choose(box.Text.Trim()); };
        var open = TypeButton("⌄", "Choose Font…", () => FontBrowser.Show(box, text.FontName, _text?.SelectedText ?? "Sample 字体", Choose)); open.Tag = "type-font-browser";
        var grid = new Grid { ColumnDefinitions = new("*,27") }; grid.Children.Add(box); Grid.SetColumn(open, 1); grid.Children.Add(open); return grid;
    }
    private ComboBox TypeFace(LayerTextStyle text)
    {
        var faces = FontCatalog.Styles(text.FontName); var index = faces.ToList().FindIndex(f => f.Name == text.FontName);
        var choice = new ComboBox { ItemsSource = faces.Select(f => f.Style).ToArray(), SelectedIndex = Math.Max(0, index), Tag = "type-font-style", Height = 26, MinHeight = 0, Padding = new Thickness(4, 1), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(choice, Localize.Text("Font Style"));
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex < 0) return; var face = faces[choice.SelectedIndex]; ChangeType(s => { s.FontName = face.Name; s.FontRuns = null; }); };
        return choice;
    }
    private Control TypeNumber(string tag, string label, double value, double min, double max, Action<double> change, double[]? presets = null, string suffix = "", bool auto = false)
    {
        var number = new NumericUpDown { Tag = tag, Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Clamp(value, min, max), Increment = 1,
            ShowButtonSpinner = false, FormatString = "0.##", MinHeight = 25, Height = 25, Padding = new Thickness(3, 0), FontSize = 12 };
        ToolTip.SetTip(number, Localize.Text(label)); number.ValueChanged += (_, _) => { if (number.Value is { } n && Math.Abs((double)n - value) > .00001) { value = (double)n; change(value); } };
        var grid = new Grid { ColumnDefinitions = new("*,22") }; grid.Children.Add(number);
        var drop = TypeButton("⌄", label, () => { }); drop.Width = 22; drop.MinWidth = 0; drop.Height = 25;
        var fly = new MenuFlyout(); if (auto) { fly.Items.Add(TypeItem("Auto", () => change(0), value == 0)); fly.Items.Add(new Separator()); }
        foreach (var p in presets ?? [min, max]) if (p >= min && p <= max) fly.Items.Add(TypeItem(p.ToString("0.##") + suffix, () => change(p), Math.Abs(value - p) < .001));
        drop.Flyout = fly; Grid.SetColumn(drop, 1); grid.Children.Add(drop); return grid;
    }
    private Control TypeColor(LayerTextStyle text, bool mask = false)
    {
        var color = new SKColor((byte)(text.Red * 255), (byte)(text.Green * 255), (byte)(text.Blue * 255));
        var button = TypeButton(mask ? "?" : "", "Text Color", () => _ = PickTypeColor(color));
        button.Width = 44; button.Background = mask ? Skin.SurfaceDarkBrush : new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        button.IsEnabled = !mask; return button;
    }
    private async Task PickTypeColor(SKColor color)
    {
        var form = new FormDialog("Text Color", 390, 180); var hex = form.Text("Color (#RRGGBB)", $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");
        form.Validate = () => SKColor.TryParse(hex.Text, out _) ? null : "Enter a valid color.";
        var doc = _document; var session = _text;
        if (await form.ShowDialog<bool>(this) && doc == _document && session == _text && SKColor.TryParse(hex.Text, out var next)) ChangeType(s => { s.Red = next.Red / 255d; s.Green = next.Green / 255d; s.Blue = next.Blue / 255d; s.ColorRuns = null; });
    }
    private void ToggleTypeOrientation()
    {
        var vertical = _text is not null ? _text.Style.Vertical != true : CurrentType is { } style ? style.Vertical != true : _tool is not (Tool.VerticalType or Tool.VerticalTypeMask);
        ChangeType(s => s.Vertical = vertical ? true : null, true);
        if (ToolCatalog.IsType(_tool)) SetTool(_tool is Tool.TypeMask or Tool.VerticalTypeMask ? vertical ? Tool.VerticalTypeMask : Tool.TypeMask : vertical ? Tool.VerticalType : Tool.Type);
    }
    private Control TypeAlignments(LayerTextStyle text, bool justify)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var layout = text.Layout ?? new();
        for (var i = 0; i < (justify ? 7 : 3); i++)
        {
            var index = i; var title = new[] { "Align Left", "Align Center", "Align Right", "Justify Last Left", "Justify Last Center", "Justify Last Right", "Justify All" }[i];
            row.Children.Add(PropertyToggle(i < 3 ? "paragraph-" + i : "justify-" + (i - 2), title,
                i < 3 ? layout.Justification == TextJustification.None && (int)text.Alignment == i : (int)layout.Justification == i - 2,
                _ => ChangeType(s => { s.Alignment = (TypeAlignment)Math.Min(index, 2); s.Layout = (s.Layout ?? new()) with { Justification = index < 3 ? TextJustification.None : (TextJustification)(index - 2) }; }, true)));
        }
        return row;
    }

    private void BuildCompactTypePanel(StackPanel body, LayerTextStyle text, bool paragraph)
    {
        body.Spacing = 8; var layout = text.Layout ?? new();
        var header = new DockPanel(); var menu = TypeButton("≡", paragraph ? "Paragraph Menu" : "Character Menu", () => { });
        menu.Flyout = BuildTypographyMenu(text, paragraph); menu.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(menu, Dock.Right); header.Children.Add(menu); body.Children.Add(header);
        void Set(Func<TextLayoutOptions, TextLayoutOptions> update, bool whole = false) => ChangeType(s => s.Layout = update(s.Layout ?? new()), whole);
        Control N(string icon, string label, string tag, double value, double min, double max, Action<double> change, double[]? presets = null, string suffix = "", bool auto = false) => PropertyIconField(icon, label, TypeNumber(tag, label, value, min, max, change, presets, suffix, auto));
        Control Points(string icon, string label, string tag, double value, double min, double max, Action<double> change, bool auto = false) => N(icon, label, tag, value * 72 / TypeResolution, min * 72 / TypeResolution, max * 72 / TypeResolution, v => change(v * TypeResolution / 72), TypeSizes, " pt", auto);
        if (paragraph)
        {
            body.Children.Add(TypeAlignments(text, true));
            body.Children.Add(PropertyPair(Points("indent-left", "Left Indent (pt)", "type-indent-left", layout.LeftIndent, 0, 10000, v => Set(o => o with { LeftIndent = v }, true)), Points("indent-right", "Right Indent (pt)", "type-indent-right", layout.RightIndent, 0, 10000, v => Set(o => o with { RightIndent = v }, true))));
            body.Children.Add(PropertyPair(Points("indent-first", "First Line Indent (pt)", "type-indent-first", layout.FirstLineIndent, -10000, 10000, v => Set(o => o with { FirstLineIndent = v }, true)), new Border()));
            body.Children.Add(PropertyPair(Points("space-before", "Space Before (pt)", "type-space-before", layout.SpaceBefore, 0, 10000, v => Set(o => o with { SpaceBefore = v }, true)), Points("space-after", "Space After (pt)", "type-space-after", layout.SpaceAfter, 0, 10000, v => Set(o => o with { SpaceAfter = v }, true))));
            body.Children.Add(PropertyPair(PropertyLabel("Kinsoku"), PropertyChoice("Kinsoku", ["None", "JIS Loose", "JIS Strict"], layout.Kinsoku ? Math.Max(1, layout.KinsokuMode) : 0, i => Set(o => o with { Kinsoku = i != 0, KinsokuMode = i }, true))));
            body.Children.Add(PropertyPair(PropertyLabel("Mojikumi"), PropertyChoice("Mojikumi", ["None", "Spacing Set 1", "Spacing Set 2", "Spacing Set 3", "Spacing Set 4"], layout.Mojikumi, i => Set(o => o with { Mojikumi = i }, true))));
            var hyphen = new CheckBox { Content = Localize.Text("Hyphenate"), IsChecked = layout.Hyphenate, Tag = "type-hyphenate" }; hyphen.IsCheckedChanged += (_, _) => Set(o => o with { Hyphenate = hyphen.IsChecked == true }, true); body.Children.Add(hyphen);
            body.Children.Add(PropertyLabel("Bullets and Numbering"));
            body.Children.Add(PropertyPair(TypeButton("• ≡", "Bulleted List", () => ToggleTypeList(false)), TypeButton("1 ≡", "Numbered List", () => ToggleTypeList(true))));
            return;
        }
        body.Children.Add(PropertyPair(TypeFont(text), TypeFace(text)));
        body.Children.Add(PropertyPair(Points("type-size", "Size (pt)", "type-panel-size", text.FontSize, 1, 2000, v => ChangeType(s => s.FontSize = v)), Points("leading", "Leading (pt, 0 = Auto)", "type-leading", text.Leading, 0, 5000, v => ChangeType(s => s.Leading = v), true)));
        body.Children.Add(PropertyPair(PropertyIconField("kerning", "Kerning", PropertyChoice("Kerning", ["Metrics", "None"], text.Kerning == false ? 1 : 0, i => ChangeType(s => s.Kerning = i == 0))),
            N("tracking", "Tracking (1/1000 em)", "type-tracking", text.Tracking * 1000 / text.FontSize, -100 * 1000 / text.FontSize, 1000 * 1000 / text.FontSize, v => ChangeType(s => s.Tracking = Math.Clamp(v * s.FontSize / 1000, -100, 1000)), [-100,-75,-50,-25,-10,-5,0,5,10,25,50,75,100,200])));
        body.Children.Add(PropertyPair(N("tsume", "Tsume (%)", "type-tsume", layout.Tsume, 0, 100, v => Set(o => o with { Tsume = v }), Enumerable.Range(0,11).Select(i => i * 10d).ToArray(), "%"), new Border()));
        body.Children.Add(PropertyPair(N("v-scale", "Vertical Scale (%)", "type-vscale", layout.VerticalScale, 1, 1000, v => Set(o => o with { VerticalScale = v }), [50,75,100,125,150,200], "%"), N("h-scale", "Horizontal Scale (%)", "type-hscale", layout.HorizontalScale, 1, 1000, v => Set(o => o with { HorizontalScale = v }), [50,75,100,125,150,200], "%")));
        body.Children.Add(PropertyPair(Points("baseline", "Baseline Shift (pt)", "type-baseline", layout.BaselineShift, -10000, 10000, v => Set(o => o with { BaselineShift = v })), PropertyPair(PropertyLabel("Color"), TypeColor(text, _typeMaskSession))));
        var styles = new WrapPanel();
        foreach (var (title, icon, property) in TypeFlags)
        {
            var field = typeof(LayerTextStyle).GetProperty(property)!; var on = (bool?)field.GetValue(text) == true;
            styles.Children.Add(PropertyToggle(icon, title, on, _ => ToggleTypeFlag(property)));
        }
        body.Children.Add(styles);
        var features = new WrapPanel(); foreach (var (title, icon, tag) in TypeFeatures.Take(8)) features.Children.Add(PropertyToggle(icon, title, FeatureOn(text, tag), _ => ToggleTypeFeature(tag)));
        body.Children.Add(features);
        var codes = new[] { "en-US","zh-Hans","zh-Hant","ja","ko","ar","he","hi","fa","fr","de","es" };
        body.Children.Add(PropertyPair(PropertyChoice("Language", ["English (USA)","Simplified Chinese","Traditional Chinese","Japanese","Korean","Arabic","Hebrew","Hindi","Persian","French","German","Spanish"], Math.Max(0,Array.IndexOf(codes,text.Language ?? "en-US")), i => ChangeType(s => s.Language = codes[i])), PropertyChoice("Anti-aliasing", ["None","Sharp","Crisp","Strong","Smooth","Windows LCD","Windows"], (int)layout.Smoothing, i => Set(o => o with { Smoothing = (TextSmoothing)i }))));
    }
    private static readonly (string Title, string Icon, string Property)[] TypeFlags = [("Faux Bold","bold","Bold"),("Faux Italic","italic","Italic"),("All Caps","all-caps","AllCaps"),("Small Caps","small-caps","SmallCaps"),("Superscript","superscript","Superscript"),("Subscript","subscript","Subscript"),("Underline","underline","Underline"),("Strikethrough","strike","Strikethrough")];
    private static readonly (string Title, string Icon, string Tag)[] TypeFeatures = [("Standard Ligatures","ligatures","liga"),("Contextual Alternates","alternates","calt"),("Discretionary Ligatures","discretionary","dlig"),("Swashes","swash","swsh"),("Stylistic Alternates","stylistic","salt"),("Titling Alternates","titling","titl"),("Ordinals","ordinals","ordn"),("Fractions","fractions","frac"),("Oldstyle Figures","oldstyle","onum"),("Ornaments","ornaments","ornm"),("Justification Alternates","justify-alternates","jalt"),("Japanese 78","japanese","jp78"),("Japanese Expert","japanese","expt"),("Traditional Forms","japanese","trad"),("Proportional CJK Metrics","proportional","palt"),("Kana Alternates","japanese","hkna"),("Roman Italics","italic","ital")];
    private static bool FeatureOn(LayerTextStyle text, string tag) => tag == "liga" ? text.Ligatures != false : text.Features?.Contains(tag) == true;
    private void ToggleTypeFeature(string tag) => ChangeType(s => { if (tag == "liga") { s.Ligatures = s.Ligatures == false; return; } var tags = (s.Features ?? []).ToHashSet(); if (!tags.Remove(tag)) tags.Add(tag); s.Features = tags.Count == 0 ? null : tags.ToList(); });
    private void ToggleTypeFlag(string property) => ChangeType(s =>
    {
        var field = typeof(LayerTextStyle).GetProperty(property)!; var on = (bool?)field.GetValue(s) != true; field.SetValue(s,on ? true : null);
        if (on) { if (property == "Superscript") s.Subscript = null; if (property == "Subscript") s.Superscript = null; if (property == "AllCaps") s.SmallCaps = null; if (property == "SmallCaps") s.AllCaps = null; }
    });
    private void ToggleTypeList(bool numbered) => ChangeType(s =>
    {
        // Apply insertions through UTF-16 ranges so list controls retain mixed fonts and colors.
        var lines = s.Content.Split('\n'); var remove = lines.All(l => numbered ? System.Text.RegularExpressions.Regex.IsMatch(l,@"^\d+\. ") : l.StartsWith("• "));
        var offset = s.Content.Length;
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            offset -= lines[i].Length; var count = numbered ? System.Text.RegularExpressions.Regex.Match(lines[i],@"^\d+\. ").Length : lines[i].StartsWith("• ") ? 2 : 0;
            var next = TextRanges.Splice(s,offset,count,remove ? "" : numbered ? $"{i+1}. " : "• ");
            s.Content = next.Content; s.StyleRuns = next.StyleRuns; s.ColorRuns = next.ColorRuns; s.FontRuns = next.FontRuns; offset--;
        }
    }, true);
}

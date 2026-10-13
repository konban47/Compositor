using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, StackPanel> _typePanels = [];
    private StackPanel? _propertyDestination;
    private HashSet<string>? _propertySectionFilter;
    private void OpenTypePanel(string name)
    {
        var tab = _dockPanels.GetValueOrDefault(name)?.Tab;
        if (tab is null)
        {
            var body = new StackPanel { Spacing = 6, Margin = new Thickness(8) }; _typePanels[name] = body;
            tab = new TabItem { Header = Localize.Text(name), Tag = "type-panel-" + name, Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            _inspectorTabs.Items.Add(tab); RegisterPanel(name, _inspectorTabs, tab);
        }
        ShowDockPanel(name); RefreshTypePanels();
    }
    private void RefreshTypePanels()
    {
        foreach (var (name, body) in _typePanels)
        {
            body.Children.Clear();
            if (name == "Glyphs") { BuildGlyphPanel(body); continue; }
            if (name.EndsWith("Styles")) { BuildTypeStyles(body, name == "Paragraph Styles"); continue; }
            if (PropertyLayer is not { LiveText: { } text } layer) { body.Children.Add(PropertyLabel("Select a type layer to edit its properties.")); continue; }
            _propertyDestination = body; _propertySectionFilter = name == "Character" ? ["Character", "Type Options", "East Asian Features", "Middle Eastern Features"] : ["Paragraph", "Bullets and Numbering"];
            try { BuildTextProperties(layer.ID, text); } finally { _propertyDestination = null; _propertySectionFilter = null; }
        }
    }
    private void BuildGlyphPanel(StackPanel body)
    {
        var family = CurrentType?.FontName ?? EditorPreferences.Current.DefaultFont;
        var font = new TextBox { Text = family, Tag = "glyph-font" };
        var pick = PropertyAction("Choose Font…", () => FontBrowser.Show(font, font.Text ?? family, "Aa 字体", selected => { font.Text = selected; }));
        body.Children.Add(PropertyPair(font, pick));
        var ranges = new[] { ("Basic Latin", 32, 0x7F), ("Latin Extended", 0x80, 0x2FF), ("Greek and Cyrillic", 0x370, 0x52F), ("Arabic and Hebrew", 0x590, 0x8FF), ("Symbols", 0x2000, 0x2BFF), ("CJK", 0x3000, 0x9FFF), ("Emoji", 0x1F300, 0x1FAFF), ("Full Font", 32, 0x10FFFF) };
        var range = new ComboBox { ItemsSource = ranges.Select(r => Localize.Text(r.Item1)).ToArray(), SelectedIndex = 0, Tag = "glyph-range" }; body.Children.Add(range);
        var list = new ListBox { Tag = "glyph-grid", Height = 250, ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()) };
        list.ItemTemplate = new FuncDataTemplate<string>((glyph, _) => new TextBlock { Text = glyph, FontFamily = new FontFamily(FontCatalog.Family(font.Text ?? family)), FontSize = 28, Width = 34, Height = 38, TextAlignment = Avalonia.Media.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var offset = 0; var generation = 0; int[] characters = [];
        var page = new TextBlock { Text = "1", VerticalAlignment = VerticalAlignment.Center };
        void Show() { list.ItemsSource = characters.Skip(offset).Take(128).Select(char.ConvertFromUtf32).ToArray(); page.Text = $"{offset / 128 + 1} / {Math.Max(1, (characters.Length + 127) / 128)}"; }
        async void Load()
        {
            var thisGeneration = ++generation; var selected = ranges[Math.Max(0, range.SelectedIndex)]; var name = font.Text ?? family; offset = 0;
            var result = await Task.Run(() => { var face = TextEdits.Typeface(name); using var glyphFont = new SKFont(face); return Enumerable.Range(selected.Item2, selected.Item3 - selected.Item2 + 1).Where(c => c is < 0xD800 or > 0xDFFF && glyphFont.GetGlyph(c) != 0).ToArray(); });
            if (thisGeneration == generation) { characters = result; Show(); }
        }
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is string glyph) InsertType(glyph); };
        body.Children.Add(list);
        body.Children.Add(PropertyPair(PropertyAction("Previous", () => { offset = Math.Max(0, offset - 128); Show(); }), PropertyAction("Next", () => { if (offset + 128 < characters.Length) offset += 128; Show(); })));
        body.Children.Add(page); body.Children.Add(PropertyAction("Insert Glyph", () => { if (list.SelectedItem is string glyph) InsertType(glyph); }));
        range.SelectionChanged += (_, _) => Load(); font.LostFocus += (_, _) => Load(); Load();
    }
    private void BuildTypeStyles(StackPanel body, bool paragraph)
    {
        var presets = paragraph ? EditorPreferences.Current.ParagraphStyles : EditorPreferences.Current.CharacterStyles;
        var list = new ListBox { ItemsSource = presets.Keys.ToArray(), MinHeight = 100, Tag = paragraph ? "paragraph-styles" : "character-styles" }; body.Children.Add(list);
        void Apply() { if (list.SelectedItem is string key && presets.TryGetValue(key, out var style)) ChangeType(target => CopyTypeStyle(style, target, paragraph), paragraph); }
        list.DoubleTapped += (_, _) => Apply();
        async Task New()
        {
            if (CurrentType is not { } text) return;
            var name = await TextPrompt.Ask(this, paragraph ? "New Paragraph Style" : "New Character Style", "Name", "Style");
            if (string.IsNullOrWhiteSpace(name)) return;
            presets[name.Trim()] = TextRanges.Flat(text); SavePreferenceChanges(); RefreshTypePanels();
        }
        body.Children.Add(PropertyPair(PropertyAction("New Style…", () => _ = New()), PropertyAction("Apply Style", Apply)));
        body.Children.Add(PropertyPair(PropertyAction("Update Style", () => { if (list.SelectedItem is string key && CurrentType is { } text) { presets[key] = TextRanges.Flat(text); SavePreferenceChanges(); } }),
            PropertyAction("Delete Style", () => { if (list.SelectedItem is string key) { presets.Remove(key); SavePreferenceChanges(); RefreshTypePanels(); } })));
        body.Children.Add(PropertyAction("Clear Overrides", () => { if (list.SelectedItem is string) Apply(); else LoadDefaultType(); }));
    }
    private static void CopyTypeStyle(LayerTextStyle source, LayerTextStyle target, bool paragraph)
    {
        var a = source.Layout ?? new(); var b = target.Layout ?? new();
        if (paragraph)
        {
            target.Alignment = source.Alignment; target.Leading = source.Leading;
            target.Layout = b with { LeftIndent = a.LeftIndent, RightIndent = a.RightIndent, FirstLineIndent = a.FirstLineIndent, SpaceBefore = a.SpaceBefore, SpaceAfter = a.SpaceAfter, Justification = a.Justification, Kinsoku = a.Kinsoku, BottomLeading = a.BottomLeading }; return;
        }
        foreach (var property in typeof(LayerTextStyle).GetProperties().Where(p => p.CanWrite && p.Name is not ("Content" or "StyleRuns" or "ColorRuns" or "FontRuns" or "BoxSize" or "Alignment" or "Layout" or "Vertical" or "Dynamic" or "Geometry"))) property.SetValue(target, property.GetValue(source));
        target.Layout = b with { HorizontalScale = a.HorizontalScale, VerticalScale = a.VerticalScale, BaselineShift = a.BaselineShift, Smoothing = a.Smoothing, Tsume = a.Tsume };
        target.FontRuns = null; target.ColorRuns = null;
    }
    private void SaveDefaultType()
    { if (CurrentType is { } text) { EditorPreferences.Current.DefaultTypeStyle = TextRanges.Flat(text); EditorPreferences.Current.DefaultFont = text.FontName; EditorPreferences.Current.DefaultFontSize = text.FontSize; SavePreferenceChanges(); } }
    private void LoadDefaultType() => ChangeType(s => CopyTypeStyle(EditorPreferences.Current.DefaultTypeStyle ?? new() { FontName = EditorPreferences.Current.DefaultFont, FontSize = EditorPreferences.Current.DefaultFontSize }, s, false));

    private async Task ManageMissingFonts()
    {
        if (_document is not { } doc) return;
        var missing = doc.Layers.Where(l => l.LiveText is not null).SelectMany(l => new[] { l.LiveText!.FontName }.Concat(l.LiveText.FontRuns?.Select(r => r.FontName) ?? []).Concat(l.LiveText.StyleRuns?.Select(r => r.Style.FontName) ?? []))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !FontCatalog.IsInstalled(n)).ToArray();
        var dialog = new FormDialog("Manage Missing Fonts", 680, 470); var replacements = new Dictionary<string, ComboBox>();
        dialog.Description(missing.Length == 0 ? "All requested fonts are installed." : "Choose replacements for missing fonts. Changes apply to every matching character in this document.");
        var choices = new[] { Localize.Text("Do Not Replace") }.Concat(FontCatalog.Families).ToArray();
        foreach (var name in missing) { var choice = new ComboBox { ItemsSource = choices, SelectedIndex = 0 }; dialog.Row(name, choice); replacements[name] = choice; }
        var all = dialog.Check("Replace All Missing Fonts with Default Font", false);
        if (!await dialog.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document)) return;
        CommitText();
        Edit("Replace Missing Fonts", () =>
        {
            string Replace(string font) => replacements.TryGetValue(font, out var choice) ? all.IsChecked == true ? EditorPreferences.Current.DefaultFont : choice.SelectedIndex > 0 ? choices[choice.SelectedIndex] : font : font;
            var changed = false;
            foreach (var layer in doc.Layers.Where(l => l.LiveText is not null))
            {
                var style = TextEdits.Copy(layer.LiveText!); var old = System.Text.Json.JsonSerializer.Serialize(style);
                style.FontName = Replace(style.FontName);
                style.FontRuns = style.FontRuns?.Select(r => new LayerTextFontRun { Location = r.Location, Length = r.Length, FontName = Replace(r.FontName) }).ToList();
                style.StyleRuns = style.StyleRuns?.Select(r => { var value = TextRanges.Flat(r.Style); value.FontName = Replace(value.FontName); return new LayerTextStyleRun { Location = r.Location, Length = r.Length, Style = value }; }).ToList();
                if (old != System.Text.Json.JsonSerializer.Serialize(style)) changed |= TextEdits.SetStyle(doc, layer.ID, style);
            }
            return changed;
        });
    }
}

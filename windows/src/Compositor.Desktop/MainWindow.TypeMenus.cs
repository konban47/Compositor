using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private LayerTextStyle? CurrentType => _text?.Style ?? PropertyLayer?.LiveText;
    private void ChangeType(Action<LayerTextStyle> change, bool paragraph = false)
    {
        if (_document is not { } doc) return;
        if (_text is { } session)
        {
            if (session.ApplyStyle(doc, change, paragraph)) { ShowText(); BuildProperties(); } return;
        }
        var layers = doc.Layers.Where(l => SelectedLayers.Contains(l.ID) && l.LiveText is not null).ToArray();
        Edit("Type Properties", () =>
        {
            var changed = false;
            foreach (var layer in layers)
            {
                var value = TextEdits.Copy(layer.LiveText!); change(value);
                if (!paragraph && value.StyleRuns is not null)
                    value.StyleRuns = value.StyleRuns.Select(r => { var style = TextRanges.Flat(r.Style); change(style); return new LayerTextStyleRun { Location = r.Location, Length = r.Length, Style = TextRanges.Flat(style) }; }).ToList();
                if (value.IsValid) changed |= TextEdits.SetStyle(doc, layer.ID, value);
            }
            return changed;
        });
    }
    private MenuItem BuildTypeMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("_Type"), Tag = "_Type" };
        void Populate()
        {
            menu.Items.Clear(); var text = CurrentType;
            MenuItem Item(string name, Action action, bool enabled = true, bool? check = null)
            {
                var row = new MenuItem { Header = Localize.Text(name), Tag = name, IsEnabled = enabled,
                    ToggleType = check is null ? MenuItemToggleType.None : MenuItemToggleType.CheckBox, IsChecked = check == true };
                row.Click += (_, _) => action(); return row;
            }
            MenuItem Sub(string name) { var row = new MenuItem { Header = Localize.Text(name), Tag = name }; menu.Items.Add(row); return row; }
            void Add(string name, Action action, bool requiresType = true) => menu.Items.Add(Item(name, action, !requiresType || text is not null));
            var panels = Sub("Panels");
            foreach (var name in new[] { "Character", "Paragraph", "Glyphs", "Character Styles", "Paragraph Styles" }) panels.Items.Add(Item(name + " Panel", () => OpenTypePanel(name)));
            menu.Items.Add(new Separator());
            var antialias = Sub("Anti-aliasing");
            foreach (var mode in Enum.GetValues<TextSmoothing>()) antialias.Items.Add(Item(mode.ToString(), () => ChangeType(s => s.Layout = (s.Layout ?? new()) with { Smoothing = mode }), text is not null, (text?.Layout?.Smoothing ?? TextSmoothing.Smooth) == mode));
            var orientation = Sub("Text Orientation");
            orientation.Items.Add(Item("Horizontal", () => ChangeType(s => s.Vertical = null, true), text is not null, text?.Vertical != true));
            orientation.Items.Add(Item("Vertical", () => ChangeType(s => s.Vertical = true, true), text is not null, text?.Vertical == true));
            var features = Sub("OpenType");
            foreach (var (name, tag) in new[] { ("Standard Ligatures", "liga"), ("Contextual Alternates", "calt"), ("Discretionary Ligatures", "dlig"), ("Swashes", "swsh"), ("Stylistic Alternates", "salt"), ("Titling Alternates", "titl"), ("Ordinals", "ordn"), ("Fractions", "frac") })
            {
                var on = tag == "liga" ? text?.Ligatures == true : text?.Features?.Contains(tag) == true;
                features.Items.Add(Item(name, () => ChangeType(s => { if (tag == "liga") s.Ligatures = !on; else { var tags = (s.Features ?? []).ToHashSet(); if (on) tags.Remove(tag); else tags.Add(tag); s.Features = tags.Count == 0 ? null : tags.ToList(); } }), text is not null, on));
            }
            menu.Items.Add(new Separator());
            Add("Create Work Path", CreateTypePath); Add("Convert to Shape", () => TypeLayerAction("Convert to Shape", TextEdits.ToVectorShape));
            Add("Rasterize Type Layer", () => TypeLayerAction("Rasterize Type Layer", (doc, id) => { doc.Layers.Single(l => l.ID == id).Text = null; return true; }));
            Add(text?.BoxSize is null ? "Convert to Paragraph Text" : "Convert to Point Text", () => ChangeType(s => s.BoxSize = s.BoxSize is null ? new(PropertyLayer?.Asset?.Width ?? 300, PropertyLayer?.Asset?.Height ?? 150) : null, true));
            var dynamic = Sub("Convert to Dynamic Text");
            foreach (var shape in new[] { "Block", "Circle", "Arch", "Bowtie" }) dynamic.Items.Add(Item(shape, () => SetTypeShape(shape), text is not null));
            Add("Warp Text…", () => _ = WarpType()); Add("Match Font…", () => _ = MatchFont(), false);
            menu.Items.Add(new Separator()); var preview = Sub("Font Preview Size");
            var sizes = new[] { "None", "Small", "Medium", "Large", "Extra Large", "Huge" };
            for (var i = 0; i < sizes.Length; i++) { var size = i; preview.Items.Add(Item(sizes[i], () => { EditorPreferences.Current.FontPreviewSize = size; SavePreferenceChanges(); }, true, EditorPreferences.Current.FontPreviewSize == i)); }
            var language = Sub("Language Options");
            foreach (var (name, code) in new[] { ("Default Features", "en"), ("East Asian Features", "zh-Hans"), ("Middle Eastern Features", "ar") })
                language.Items.Add(Item(name, () => ChangeType(s => { s.Language = code; s.ComplexShaping = true; }), text is not null, text?.Language == code));
            language.Items.Add(new Separator());
            language.Items.Add(Item("Standard Vertical Roman Alignment", () => ChangeType(s => s.Features = [.. (s.Features ?? []).Where(f => f != "vert"), "vert"]), text is not null));
            language.Items.Add(Item("Tate-chu-yoko", () => ChangeType(s => s.Layout = (s.Layout ?? new()) with { TateChuYoko = s.Layout?.TateChuYoko != true }), text?.Vertical == true));
            language.Items.Add(new Separator());
            language.Items.Add(Item("Top-to-Top Leading", () => ChangeType(s => s.Layout = (s.Layout ?? new()) with { BottomLeading = false }, true), text is not null, text?.Layout?.BottomLeading != true));
            language.Items.Add(Item("Bottom-to-Bottom Leading", () => ChangeType(s => s.Layout = (s.Layout ?? new()) with { BottomLeading = true }, true), text is not null, text?.Layout?.BottomLeading == true));
            language.Items.Add(Item("Arabic Digits (Left to Right) — 123%", () => { ReplaceTypeDigits('0'); ChangeType(s => s.Direction = Compositor.Core.Format.TextDirection.LeftToRight); }, text is not null));
            language.Items.Add(Item("Arabic Digits (Right to Left) — %123", () => { ReplaceTypeDigits('0'); ChangeType(s => s.Direction = Compositor.Core.Format.TextDirection.RightToLeft); }, text is not null));
            foreach (var (name, zero) in new[] { ("Hindi Digits", '٠'), ("Persian Digits", '۰') })
                language.Items.Add(Item(name, () => ReplaceTypeDigits(zero), text is not null));
            language.Items.Add(Item("Kashidas", () => InsertType("ـ"), text is not null));
            foreach (var direction in Enum.GetValues<Compositor.Core.Format.TextDirection>()) language.Items.Add(Item(direction switch { Compositor.Core.Format.TextDirection.LeftToRight => "Left to right", Compositor.Core.Format.TextDirection.RightToLeft => "Right to left", _ => "Auto direction" }, () => ChangeType(s => s.Direction = direction), text is not null, text?.Direction == direction));
            menu.Items.Add(new Separator()); Add("Update All Type Layers", UpdateAllType, false); Add("Manage Missing Fonts…", () => _ = ManageMissingFonts(), false);
            menu.Items.Add(new Separator()); Add("Paste Lorem Ipsum", () => InsertType("Lorem ipsum dolor sit amet, consectetur adipiscing elit."));
            menu.Items.Add(new Separator()); Add("Load Default Type Style", LoadDefaultType); Add("Save Default Type Style", SaveDefaultType);
        }
        Populate(); menu.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.Source, menu)) Populate(); }; return menu;
    }
    private void TypeLayerAction(string name, Func<CanvasDocument, Guid, bool> action)
    {
        CommitText(); if (_document is not { } doc || Selected is not { } id || PropertyLayer?.LiveText is null) return;
        Edit(name, () => action(doc, id)); BuildProperties();
    }
    private void InsertType(string content)
    {
        if (_document is not { } doc) return;
        if (_text is { } session) { if (session.Type(doc, content)) ShowText(); return; }
        if (PropertyLayer is not { LiveText: { } style } layer) return;
        Edit("Insert Text", () => TextEdits.SetStyle(doc, layer.ID, TextRanges.Splice(style, style.Content.Length, 0, content)));
    }
    private void ReplaceTypeDigits(char zero)
    {
        var content = _text?.SelectionLength > 0 ? _text.SelectedText : CurrentType?.Content;
        if (content is null) return;
        var converted = new string(content.Select(c => char.GetNumericValue(c) is >= 0 and <= 9 && char.IsDigit(c) ? (char)(zero + (int)char.GetNumericValue(c)) : c).ToArray());
        if (_text is { SelectionLength: > 0 }) InsertType(converted);
        else ChangeType(s => s.Content = converted, true);
    }
    private void CreateTypePath()
    {
        CommitText(); if (_document is not { } doc || PropertyLayer is not { LiveText: { } style, Asset: { } asset } layer) return;
        using var path = TextEdits.Outline(style); if (path is null) return;
        path.Transform(BrushEdits.PixelToDocument(layer.Transform, asset.Width, asset.Height));
        _pen = PathNodes.Parse(path.ToSvgPathData()); SetTool(Tool.Pen); _canvas.InvalidateOverlay();
    }
    private void UpdateAllType()
    {
        CommitText(); if (_document is not { } doc) return;
        Edit("Update All Type Layers", () => { var changed = false; foreach (var layer in doc.Layers.Where(l => l.LiveText is not null)) changed |= TextEdits.SetStyle(doc, layer.ID, TextEdits.Copy(layer.LiveText!)); return changed; });
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Compositor.Core.Document;
using Compositor.Core.Format;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private MenuFlyout BuildTypographyMenu(LayerTextStyle text, bool paragraph)
    {
        var menu = new MenuFlyout(); var layout = text.Layout ?? new();
        void Add(string title, Action action, bool? check = null) => menu.Items.Add(TypeItem(title, action, check));
        MenuItem Sub(string title) { var item = new MenuItem { Header = Localize.Text(title), Tag = title }; menu.Items.Add(item); return item; }
        void Layout(Func<TextLayoutOptions, TextLayoutOptions> update) => ChangeType(s => s.Layout = update(s.Layout ?? new()), paragraph);
        if (!paragraph)
        {
            Add("Change Text Orientation", ToggleTypeOrientation);
            Add("Standard Vertical Roman Alignment", () => ToggleTypeFeature("vert"), FeatureOn(text,"vert"));
            Add("Tate-chu-yoko", () => Layout(o => o with { TateChuYoko = !o.TateChuYoko }), layout.TateChuYoko);
            menu.Items.Add(new Separator()); var alignment = Sub("Character Alignment");
            var names = new[] { "Roman Baseline", "Em Box Top / Right", "Em Box Center", "Em Box Bottom / Left", "Ideographic Top / Right", "Ideographic Bottom / Left" };
            for (var i = 0; i < names.Length; i++) { var index = i; alignment.Items.Add(TypeItem(names[i], () => Layout(o => o with { CharacterAlignment = index }), layout.CharacterAlignment == i)); }
            var openType = Sub("OpenType"); foreach (var (title, _, tag) in TypeFeatures) openType.Items.Add(TypeItem(title, () => ToggleTypeFeature(tag), FeatureOn(text,tag)));
            foreach (var (title, _, property) in TypeFlags) Add(title, () => ToggleTypeFlag(property), (bool?)typeof(LayerTextStyle).GetProperty(property)!.GetValue(text) == true);
            menu.Items.Add(new Separator());
            Add("Fractional Widths", () => Layout(o => o with { FractionalWidths = !o.FractionalWidths }), layout.FractionalWidths);
            Add("System Layout", () => ChangeType(s => s.ComplexShaping = s.ComplexShaping == false), text.ComplexShaping != false);
            Add("No Break", () => Layout(o => o with { NoBreak = !o.NoBreak }), layout.NoBreak);
            var middle = Sub("Middle Eastern Features");
            foreach (var (name, zero) in new[] { ("Arabic Digits",'0'),("Hindi Digits",'٠'),("Persian Digits",'۰') }) middle.Items.Add(TypeItem(name, () => ReplaceTypeDigits(zero)));
            middle.Items.Add(TypeItem("Kashidas", () => ToggleTypeFeature("jalt"), FeatureOn(text,"jalt")));
            foreach (var direction in Enum.GetValues<TextDirection>()) middle.Items.Add(TypeItem(direction switch { TextDirection.LeftToRight => "Left to right", TextDirection.RightToLeft => "Right to left", _ => "Auto direction" }, () => ChangeType(s => s.Direction = direction), text.Direction == direction));
            menu.Items.Add(new Separator()); Add("Reset Character", () => ChangeType(s => CopyTypeStyle(new() { FontName = EditorPreferences.Current.DefaultFont, FontSize = 16 * TypeResolution / 72 }, s, false)));
        }
        else
        {
            Add("Hanging Punctuation", () => Layout(o => o with { HangingPunctuation = !o.HangingPunctuation }), layout.HangingPunctuation);
            Add("Top-to-Top Leading", () => Layout(o => o with { BottomLeading = false }), !layout.BottomLeading);
            Add("Bottom-to-Bottom Leading", () => Layout(o => o with { BottomLeading = true }), layout.BottomLeading);
            Add("Justification…", () => _ = TypeSpacingDialog(false)); Add("Hyphenation…", () => _ = TypeSpacingDialog(true));
            Add("Latin and East Asian Layout", () => ChangeType(s => s.ComplexShaping = false, true), text.ComplexShaping == false);
            Add("World-Ready Layout", () => ChangeType(s => s.ComplexShaping = true, true), text.ComplexShaping != false);
            menu.Items.Add(new Separator());
            foreach (var (name, code) in new[] { ("Default Features","en-US"),("East Asian Features","ja"),("Middle Eastern Features","ar") }) Add(name, () => ChangeType(s => s.Language = code,true), text.Language == code);
            menu.Items.Add(new Separator());
            Add("Single-line Composer", () => Layout(o => o with { MultiLineComposer = false }), !layout.MultiLineComposer);
            Add("Multi-line Composer", () => Layout(o => o with { MultiLineComposer = true }), layout.MultiLineComposer);
            Add("Reset Paragraph", () => ChangeType(s => { s.Alignment = TextAlignment.Left; s.Leading = 0; s.Layout = new TextLayoutOptions { HorizontalScale = s.Layout?.HorizontalScale ?? 100, VerticalScale = s.Layout?.VerticalScale ?? 100, BaselineShift = s.Layout?.BaselineShift ?? 0, Smoothing = s.Layout?.Smoothing ?? TextSmoothing.Smooth }; }, true));
        }
        menu.Items.Add(new Separator());
        var panel = paragraph ? "Paragraph" : "Character";
        Add("Close", () => { if (_dockPanels.TryGetValue(panel,out var entry)) HidePanel(entry); });
        Add("Close Tab Group", () => { if (_dockPanels.GetValueOrDefault(panel)?.Group is { } group) foreach (var member in Members(group).ToArray()) HidePanel(member); });
        return menu;
    }
    private async Task TypeSpacingDialog(bool hyphenation)
    {
        var layout = CurrentType?.Layout ?? TypeDefaults.Layout ?? new(); var doc = _document; var session = _text;
        var form = new FormDialog(hyphenation ? "Hyphenation" : "Justification", 420, 280);
        var first = form.Number(hyphenation ? "Words Longer Than" : "Word Spacing (%)", hyphenation ? layout.HyphenMinimum : layout.WordSpacing, hyphenation ? 2 : 0, hyphenation ? 100 : 1000);
        var second = hyphenation ? null : form.Number("Letter Spacing (%)", layout.LetterSpacing, -100,1000);
        var enable = hyphenation ? form.Check("Hyphenate",layout.Hyphenate) : null;
        if (!await form.ShowDialog<bool>(this) || doc != _document || session != _text) return;
        ChangeType(s => s.Layout = hyphenation ? (s.Layout ?? new()) with { HyphenMinimum = (int)first.Value!, Hyphenate = enable?.IsChecked == true }
            : (s.Layout ?? new()) with { WordSpacing = (double)first.Value!, LetterSpacing = (double)second!.Value! }, true);
    }
}

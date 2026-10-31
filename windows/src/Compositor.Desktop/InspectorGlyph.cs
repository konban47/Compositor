using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Small vector icons keep the inspector legible at every display scale.</summary>
internal sealed class InspectorGlyph(string kind) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(22, 22);
    public override void Render(DrawingContext context)
    {
        if (AlignmentGlyph.Draw(context, kind, Bounds.Size)) return;
        if (EditorIcon.Draw(context, kind, Bounds.Size)) return;
        var glyph = kind switch {
            "bold" => "B", "italic" => "I", "all-caps" => "TT", "small-caps" => "Tᴛ", "superscript" => "T¹", "subscript" => "T₁", "underline" => "T̲", "strike" => "T̶",
            "type-size" => "Tᴛ", "leading" => "A↕", "kerning" => "V/A", "tracking" => "VA", "h-scale" => "↔T", "v-scale" => "↕T", "baseline" => "A↟",
            "ordinals" => "1ˢᵗ", "fractions" => "½", "ligatures" => "fi", "alternates" => "ℒ", "discretionary" => "st", "swash" => "𝒜", "stylistic" => "a↔", "titling" => "T", "tsume" => "あ",
            "proportional" => "詰", "vertical-alternates" => "↕あ", "justify-alternates" => "ـبـ", "shaping" => "ش", "ltr" => "¶▶", "rtl" => "◀¶", _ => null };
        if (glyph is not null) {
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI", kind == "italic" ? FontStyle.Italic : FontStyle.Normal, FontWeight.SemiBold), glyph.Length > 2 ? 12 : 17, Skin.LabelBrush);
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2)); return;
        }
        var pen = new Pen(Skin.LabelBrush, IconSet.Stroke) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        void Line(double a, double b, double c, double d) => context.DrawLine(pen, new Point(a, b), new Point(c, d));
        if (kind.StartsWith("paragraph-") || kind.StartsWith("justify-") || kind.StartsWith("indent-") || kind.StartsWith("space-"))
        {
            for (var i = 0; i < 5; i++) { var length = kind.StartsWith("justify") && i < 4 || i % 2 == 0 ? 18 : 12; var x = kind == "paragraph-1" || kind == "justify-2" ? (22 - length) / 2 : kind == "paragraph-2" || kind == "justify-3" ? 20 - length : 2; Line(x, 3 + i * 4, x + length, 3 + i * 4); }
            if (kind.StartsWith("indent") || kind.StartsWith("space")) { Line(1, 10, 6, 10); Line(3, 7, 6, 10); Line(3, 13, 6, 10); }
            return;
        }
        IconSet.Draw(context, "link", Bounds.Size);
    }
}

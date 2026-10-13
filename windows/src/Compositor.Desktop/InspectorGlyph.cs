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
        var pen = new Pen(Skin.LabelBrush, 1.65) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        void Line(double a, double b, double c, double d) => context.DrawLine(pen, new Point(a, b), new Point(c, d));
        if (kind.StartsWith("paragraph-") || kind.StartsWith("justify-") || kind.StartsWith("indent-") || kind.StartsWith("space-"))
        {
            for (var i = 0; i < 5; i++) { var length = kind.StartsWith("justify") && i < 4 || i % 2 == 0 ? 18 : 12; var x = kind == "paragraph-1" || kind == "justify-2" ? (22 - length) / 2 : kind == "paragraph-2" || kind == "justify-3" ? 20 - length : 2; Line(x, 3 + i * 4, x + length, 3 + i * 4); }
            if (kind.StartsWith("indent") || kind.StartsWith("space")) { Line(1, 10, 6, 10); Line(3, 7, 6, 10); Line(3, 13, 6, 10); }
            return;
        }
        var data = kind switch
        {
            "plugin" => "M2,7 L7,7 L7,2 L11,2 L11,7 L15,7 L15,2 L19,2 L19,7 L21,7 L21,20 L2,20 Z",
            "image" => "M2,3 L20,3 L20,19 L2,19 Z M3,17 L9,10 L13,14 L16,10 L20,15 M8,7 L8,8",
            "document" => "M4,2 L13,2 L18,7 L18,20 L4,20 Z M13,2 L13,7 L18,7",
            "portrait" => "M5,2 L17,2 L17,20 L5,20 Z M8,15 L14,15 M11,9 L11,15 M13,6 A2,2 0 1 1 9,6 A2,2 0 1 1 13,6",
            "landscape" => "M2,5 L20,5 L20,18 L2,18 Z M8,16 L14,16 M11,12 L11,16 M13,9 A2,2 0 1 1 9,9 A2,2 0 1 1 13,9",
            "grid" => "M2,2 L20,2 L20,20 L2,20 Z M8,2 L8,20 M14,2 L14,20 M2,8 L20,8 M2,14 L20,14",
            "guides" => "M6,2 L6,20 M14,2 L14,20 M2,7 L20,7 M2,15 L20,15",
            "guides-lock" => "M6,2 L6,20 M2,7 L20,7 M10,13 L20,13 L20,21 L10,21 Z M13,13 L13,10 Q15,6 18,10 L18,13",
            "snap" => "M3,2 L3,19 L20,19 M8,5 L4,9 L8,13 M4,9 L14,9 M17,3 L13,11 L20,11 L16,18",
            "bullets" => "M2,4 L4,4 M2,11 L4,11 M2,18 L4,18 M8,4 L20,4 M8,11 L20,11 M8,18 L20,18",
            "numbered" => "M2,3 L4,2 L4,8 M2,11 Q7,8 4,14 L2,16 L6,16 M9,4 L20,4 M9,11 L20,11 M9,18 L20,18",
            "flip-x" => "M10,2 L10,20 M7,6 L2,11 L7,16 Z M13,6 L18,11 L13,16 Z",
            "flip-y" => "M2,11 L20,11 M6,8 L11,3 L16,8 Z M6,14 L11,19 L16,14 Z",
            "rotation" => "M3,17 L18,17 M3,17 L13,5 M9,17 A6,6 0 0 0 6,12",
            "reset" => "M5,6 A8,8 0 1 1 4,16 M5,1 L5,6 L10,6",
            "snapshot" => "M2,6 L7,6 L8,3 L14,3 L15,6 L20,6 L20,19 L2,19 Z M15,12 A4,4 0 1 1 7,12 A4,4 0 1 1 15,12",
            "new-document" => "M4,2 L13,2 L18,7 L18,20 L4,20 Z M13,2 L13,7 L18,7 M7,13 L15,13 M11,9 L11,17",
            "history" => "M5,3 L19,3 L19,20 L5,20 Z M8,7 L16,7 M8,11 L16,11 M8,15 L16,15",
            "up" => "M4,13 L11,6 L18,13 M11,6 L11,20",
            "down" => "M4,9 L11,16 L18,9 M11,2 L11,16",
            "delete" => "M8,2 L14,2 M3,5 L19,5 M5,7 L6,20 L16,20 L17,7 M9,9 L9,17 M13,9 L13,17",
            "more" => "M3,11 L5,11 M10,11 L12,11 M17,11 L19,11",
            "colors" => "M2,2 L14,2 L14,14 L2,14 Z M8,16 L8,20 L20,20 L20,8 L16,8",
            "screen" => "M2,3 L20,3 L20,16 L2,16 Z M8,20 L14,20 M11,16 L11,20",
            "generate" => "M2,4 L20,4 L20,19 L2,19 Z M3,17 L8,10 L12,15 L15,12 L19,17 M15,1 L15,7 M12,4 L18,4",
            _ => "M8,5 L5,5 C0,5 0,17 5,17 L8,17 M14,5 L17,5 C22,5 22,17 17,17 L14,17 M7,11 L15,11",
        };
        context.DrawGeometry(null, pen, StreamGeometry.Parse(data));
    }
}

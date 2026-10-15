using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly HashSet<Guid> _selectedSlices = [];
    private DocumentMark[]? _sliceMoveOriginals;
    private IReadOnlyList<DocumentMark> _slicePreview = [];
    private bool _sliceAlignCanvas;
    private IReadOnlyList<DocumentMark> _automaticSlices = [];
    private int _automaticSliceKey;
    private IReadOnlyList<DocumentMark> AutoSlices()
    {
        if (_document is not { } doc) return [];
        var hash = new HashCode(); hash.Add(doc.ID); hash.Add(doc.Width); hash.Add(doc.Height);
        foreach (var m in doc.Marks.Where(m => m.Kind == MarkKind.Slice)) hash.Add(m);
        var key = hash.ToHashCode(); if (key != _automaticSliceKey) { _automaticSlices = SliceEdits.Automatic(doc); _automaticSliceKey = key; }
        return _automaticSlices;
    }
    private IEnumerable<DocumentMark> DisplayMarks()
    {
        if (_document is not { } doc) return [];
        var marks = doc.Marks.AsEnumerable();
        if (_tool is Tool.Slice or Tool.SliceSelect && !_options.Crop.HideAutoSlices) marks = AutoSlices().Concat(marks);
        return marks.Concat(_slicePreview);
    }
    private bool PromoteSelectedSlice()
    {
        if (_document is not { } doc || AutoSlices().FirstOrDefault(m => m.ID == _selectedMark) is not { } automatic || doc.Marks.Count >= 10000) return false;
        var slice = automatic with { ID = Guid.NewGuid(), Color = 0xffffb74d, Name = "slice-" + (doc.Marks.Count(m => m.Kind == MarkKind.Slice) + 1) };
        Edit("Promote Slice", () => { doc.Marks.Add(slice); return true; }); _selectedMark = slice.ID; _selectedSlices.Clear(); _selectedSlices.Add(slice.ID); return true;
    }
    private async Task SliceAction(string action, Control anchor)
    {
        if (_document is not { } doc) return;
        if (action == "Hide Auto Slices") { _options.Crop.HideAutoSlices = !_options.Crop.HideAutoSlices; OptionsChanged(); return; }
        if (action == "Promote Slice") { PromoteSelectedSlice(); _canvas.InvalidateVisual(); return; }
        if (action == "Slice Options") { await EditSliceOptions(); return; }
        if (action == "Divide Slice…") { await DivideSlice(); return; }
        if (action is "Slices from Guides" or "Export Slices…") { await ProfessionalAction(action); return; }
        if (action == "Slice Alignment Options")
        {
            var popup = new ToolSettingsPopup();
            void Buttons(string heading, (string Label, string Icon)[] values)
            {
                popup.Heading(heading); var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var (label, icon) in values) { var b = ToolOptionsBar.IconButton("", label, () => _ = SliceAction(label, anchor)); b.Content = new InspectorGlyph(icon) { Width = 22, Height = 22 }; row.Children.Add(b); } popup.Children.Add(row);
            }
            Buttons("Align:", [("Align Slice Left", "Align Left"), ("Align Slice Center", "Align H Center"), ("Align Slice Right", "Align Right"), ("Align Slice Top", "Align Top"), ("Align Slice Middle", "Align V Center"), ("Align Slice Bottom", "Align Bottom")]);
            Buttons("Distribute:", [("Distribute Top", "Distribute Top"), ("Distribute V Center", "Distribute V Center"), ("Distribute Bottom", "Distribute Bottom"), ("Distribute Left", "Distribute Left"), ("Distribute H Center", "Distribute H Center"), ("Distribute Right", "Distribute Right")]);
            Buttons("Distribute Spacing:", [("Distribute Vertical Spacing", "Distribute Vertical Spacing"), ("Distribute Horizontal Spacing", "Distribute Horizontal Spacing")]);
            popup.Choice("Align To:", ["Selection", "Canvas"], _sliceAlignCanvas ? 1 : 0, i => _sliceAlignCanvas = i == 1); popup.Show(anchor); return;
        }
        if (_selectedMark is { } id && !_selectedSlices.Contains(id)) _selectedSlices.Add(id);
        Edit(action, () => SliceEdits.Arrange(doc, _selectedSlices, action, _sliceAlignCanvas));
    }
    private async Task EditSliceOptions()
    {
        if (_document is not { } doc) return;
        if ((doc.Marks.FirstOrDefault(m => m.ID == _selectedMark && m.Kind == MarkKind.Slice) ?? AutoSlices().FirstOrDefault(m => m.ID == _selectedMark)) is not { } slice) { Say("Select a slice first."); return; }
        var revision = _history.CurrentRevision; var form = new FormDialog("Slice Options", 650, 690);
        var type = form.Choice("Slice Type:", ["Image", "No Image"], slice.NoImage ? 1 : 0);
        var name = form.Text("Name:", slice.Name); var url = form.Text("URL:", slice.Url); var target = form.Text("Target:", slice.Target); var message = form.Text("Message Text:", slice.Text); var alt = form.Text("Alt Tag:", slice.AltText);
        var x = form.Number("X:", slice.X, -1000000, 1000000); var y = form.Number("Y:", slice.Y, -1000000, 1000000);
        var w = form.Number("W:", slice.Width, 1, 1000000); var h = form.Number("H:", slice.Height, 1, 1000000);
        var background = form.Choice("Slice Background Type:", ["None", "Custom"], slice.Background is null ? 0 : 1);
        var color = form.Text("Background Color (#RRGGBB):", $"{slice.Background.GetValueOrDefault(0xffffffff) & 0xffffff:X6}"); color.IsEnabled = background.SelectedIndex == 1;
        background.SelectionChanged += (_, _) => color.IsEnabled = background.SelectedIndex == 1;
        uint? backgroundColor = slice.Background;
        DocumentMark Updated() => slice with { NoImage = type.SelectedIndex == 1, Name = name.Text ?? "", Url = url.Text ?? "", Target = target.Text ?? "", Text = message.Text ?? "", AltText = alt.Text ?? "", X = (double)(x.Value ?? 0), Y = (double)(y.Value ?? 0), Width = (double)(w.Value ?? 1), Height = (double)(h.Value ?? 1), Background = backgroundColor };
        form.Validate = () => { backgroundColor = null; if (background.SelectedIndex == 1) { if (!uint.TryParse(color.Text?.Trim().TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var parsed) || parsed > 0xffffff) return "Enter a six-digit hexadecimal color."; backgroundColor = 0xff000000 | parsed; } return Updated().IsValid ? null : "Invalid slice properties."; };
        if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        var next = Updated(); Edit("Slice Options", () => { var i = doc.Marks.FindIndex(m => m.ID == slice.ID); if (i < 0) { doc.Marks.Add(next with { Color = 0xffffb74d }); return true; } if (doc.Marks[i] == next) return false; doc.Marks[i] = next; return true; });
    }
    private async Task DivideSlice()
    {
        if (_document is not { } doc) return;
        if ((doc.Marks.FirstOrDefault(m => m.ID == _selectedMark && m.Kind == MarkKind.Slice) ?? AutoSlices().FirstOrDefault(m => m.ID == _selectedMark)) is not { } slice) { Say("Select a slice first."); return; }
        var revision = _history.CurrentRevision; var form = new FormDialog("Divide Slice", 530, 570);
        var horizontal = form.Check("Divide Horizontally Into", false); var hMode = form.Choice("Horizontal Division:", ["Slices, Evenly Spaced", "Pixels per Slice"], 0);
        var rows = form.Number("Rows / Pixels:", 2, 1, 1000000); var vertical = form.Check("Divide Vertically Into", false); var vMode = form.Choice("Vertical Division:", ["Slices, Evenly Spaced", "Pixels per Slice"], 0); var columns = form.Number("Columns / Pixels:", 2, 1, 1000000); form.Preview();
        IReadOnlyList<DocumentMark> Result()
        {
            var px = vertical.IsChecked == true && vMode.SelectedIndex == 1 ? (double)(columns.Value ?? 1) : 0;
            var py = horizontal.IsChecked == true && hMode.SelectedIndex == 1 ? (double)(rows.Value ?? 1) : 0;
            var nx = vertical.IsChecked != true ? 1 : px > 0 ? (int)Math.Ceiling(slice.Width / px) : (int)(columns.Value ?? 1);
            var ny = horizontal.IsChecked != true ? 1 : py > 0 ? (int)Math.Ceiling(slice.Height / py) : (int)(rows.Value ?? 1);
            if ((long)nx * ny + doc.Marks.Count - 1 > 10000) return [];
            return SliceEdits.Divide(slice, nx, ny, px, py);
        }
        void Preview() { hMode.IsEnabled = rows.IsEnabled = horizontal.IsChecked == true; vMode.IsEnabled = columns.IsEnabled = vertical.IsChecked == true; _slicePreview = form.PreviewEnabled ? Result() : []; _canvas.InvalidateVisual(); }
        form.Changed = Preview; form.Validate = () => Result().Count > 0 ? null : "The document can contain at most 10,000 annotations."; Preview();
        var accepted = await form.ShowDialog<bool>(this); _slicePreview = []; _canvas.InvalidateVisual();
        if (!accepted || !ReferenceEquals(doc, _document) || _history.CurrentRevision != revision) return;
        var divided = Result(); if (divided.Count <= 1) return;
        Edit("Divide Slice", () => { var i = doc.Marks.FindIndex(m => m.ID == slice.ID); if (i < 0) i = doc.Marks.Count; else doc.Marks.RemoveAt(i); doc.Marks.InsertRange(i, divided.Select(m => m with { Color = 0xffffb74d })); return true; }); _selectedSlices.Clear(); _selectedMark = divided[0].ID; foreach (var mark in divided) _selectedSlices.Add(mark.ID);
    }
    private SKRect SliceRectangle()
    {
        var box = new SKRect(Math.Min(_proStart.X, _proEnd.X), Math.Min(_proStart.Y, _proEnd.Y), Math.Max(_proStart.X, _proEnd.X), Math.Max(_proStart.Y, _proEnd.Y)); var c = _options.Crop;
        if (c.SliceStyle == 0) return box;
        var w = c.SliceStyle == 2 ? (float)c.SliceWidth : box.Width; var h = c.SliceStyle == 2 ? (float)c.SliceHeight : (float)(w * c.SliceHeight / Math.Max(1, c.SliceWidth));
        return SKRect.Create(_proEnd.X < _proStart.X ? _proStart.X - w : _proStart.X, _proEnd.Y < _proStart.Y ? _proStart.Y - h : _proStart.Y, w, h);
    }
}

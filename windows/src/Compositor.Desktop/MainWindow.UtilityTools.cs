using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;
public sealed partial class MainWindow
{
    private sealed class NoteEditorWindow : DialogWindow { }
    private Window? _noteEditor;
    private Guid? _editingNote;
    private bool AnnotationVisible(DocumentMark mark) => mark.Visible && (mark.Kind != MarkKind.Count || _document?.CountGroups.FirstOrDefault(g => g.Name == mark.Group)?.Visible != false);
    private FrameStyle CurrentFrameStyle() => new() { Shape = Pro.EllipseFrame && Pro.FrameShape == 0 ? 1 : Pro.FrameShape,
        Stroke = Pro.FrameStroke, StrokeWidth = Pro.FrameStrokeWidth, StrokeAlignment = Pro.FrameAlignment,
        CornerRadius = Pro.FrameCorner, CustomPath = Pro.FrameCustomPath };
    private CountGroup CurrentCountStyle() => new(Pro.CountGroup, Pro.CountColor, true, Pro.CountMarkerSize, Pro.CountLabelSize);
    private void EnsureCountGroup()
    {
        if (_document is not { } doc || doc.CountGroups.Any(g => g.Name == Pro.CountGroup)) return;
        Edit("New Count Group", () => { if (doc.CountGroups.Count >= 256) return false; doc.CountGroups.Add(CurrentCountStyle()); return true; });
    }
    private void SyncUtilityOptions()
    {
        if (_document is not { } doc) { _optionsBar.UtilityDocument(null, null, null); return; }
        if (_tool == Tool.Frame && PropertyLayer?.FrameStyle is { } frame)
        { Pro.FrameShape = frame.Shape; Pro.EllipseFrame = frame.Shape == 1; Pro.FrameStroke = frame.Stroke; Pro.FrameStrokeWidth = frame.StrokeWidth; Pro.FrameAlignment = frame.StrokeAlignment; Pro.FrameCorner = frame.CornerRadius; Pro.FrameCustomPath = frame.CustomPath ?? ProfessionalDefaults.Star; }
        var mark = doc.Marks.FirstOrDefault(m => m.ID == _selectedMark);
        if (_tool == Tool.Note && mark?.Kind == MarkKind.Note) { Pro.NoteAuthor = mark.Author; Pro.NoteColor = mark.Color; }
        if (_tool == Tool.Count && doc.CountGroups.FirstOrDefault(g => g.Name == Pro.CountGroup) is { } group)
        { Pro.CountColor = group.Color; Pro.CountMarkerSize = group.MarkerSize; Pro.CountLabelSize = group.LabelSize; }
        _optionsBar.UtilityDocument(doc, mark, Selected);
    }
    private void ApplyUtilityOptions()
    {
        if (_document is not { } doc) return;
        if (_tool == Tool.Frame && PropertyLayer is { Container: LayerContainer.Frame } frame && frame.FrameStyle != CurrentFrameStyle())
            Edit("Frame Options", () => LayerProtection.CanMove(doc, frame.ID) && FrameEdits.Apply(frame, CurrentFrameStyle()));
        if (_tool == Tool.Note && doc.Marks.FindIndex(m => m.ID == _selectedMark && m.Kind == MarkKind.Note) is var note && note >= 0)
        {
            var old = doc.Marks[note]; var changed = old with { Author = Pro.NoteAuthor, Color = Pro.NoteColor };
            if (old != changed) Edit("Note Options", () => { doc.Marks[note] = changed; return true; });
        }
        if (_tool == Tool.Count)
        {
            var index = doc.CountGroups.FindIndex(g => g.Name == Pro.CountGroup);
            var changed = CurrentCountStyle() with { Visible = index < 0 || doc.CountGroups[index].Visible };
            if (index < 0 || doc.CountGroups[index] != changed)
                Edit("Count Options", () => { if (index < 0) { if (doc.CountGroups.Count >= 256) return false; doc.CountGroups.Add(changed); } else doc.CountGroups[index] = changed; return true; });
        }
        ShowAnnotationReadout(); _canvas.InvalidateOverlay();
    }
    private async Task UtilityAction(string action)
    {
        var doc = _document; if (doc is null) return;
        var shape = Array.IndexOf(new[] { "Rectangle Frame", "Ellipse Frame", "Triangle Frame", "Polygon Frame", "Custom Frame" }, action);
        if (shape >= 0)
        {
            if (shape == 4)
            {
                var text = await TextPrompt.Ask(this, "Custom Frame", "Normalized SVG path (0–1)", Pro.FrameCustomPath);
                if (text is null || !ReferenceEquals(doc, _document)) return;
                if (!new FrameStyle { Shape = 4, CustomPath = text }.IsValid) { Say("Invalid SVG path."); return; }
                Pro.FrameCustomPath = text;
            }
            Pro.FrameShape = shape; Pro.EllipseFrame = shape == 1; ApplyUtilityOptions(); RefreshOptionsBar(); return;
        }
        switch (action)
        {
            case "Place Image in Frame…": case "Straighten Layer": await ProfessionalAction(action); return;
            case "Clear Samplers": case "Clear Notes": case "Clear Ruler":
                var kind = action == "Clear Samplers" ? MarkKind.Sampler : action == "Clear Notes" ? MarkKind.Note : MarkKind.Measure;
                if (kind == MarkKind.Note) _noteEditor?.Close();
                Edit(action, () => doc.Marks.RemoveAll(m => m.Kind == kind) > 0); _selectedMark = null; break;
            case "Notes Panel":
                if ((doc.Marks.FirstOrDefault(m => m.ID == _selectedMark && m.Kind == MarkKind.Note) ?? doc.Marks.FirstOrDefault(m => m.Kind == MarkKind.Note)) is { } note) OpenNote(note);
                return;
            case "Select Count Group": SyncUtilityOptions(); break;
            case "New Count Group": case "Rename Count Group":
                var name = await TextPrompt.Ask(this, action, "Name", action == "New Count Group" ? (doc.CountGroups.Count + 1).ToString() : Pro.CountGroup);
                if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || !ReferenceEquals(doc, _document)) return;
                if (name != Pro.CountGroup && (doc.CountGroups.Any(g => g.Name == name) || doc.Marks.Any(m => m.Kind == MarkKind.Count && m.Group == name))) { Say("A count group with this name already exists."); return; }
                if (action == "New Count Group") { Pro.CountGroup = name; EnsureCountGroup(); }
                else
                {
                    var oldName = Pro.CountGroup;
                    Edit(action, () => {
                        var i = doc.CountGroups.FindIndex(g => g.Name == oldName);
                        if (i >= 0) doc.CountGroups[i] = doc.CountGroups[i] with { Name = name };
                        for (var j = 0; j < doc.Marks.Count; j++) if (doc.Marks[j].Kind == MarkKind.Count && doc.Marks[j].Group == oldName) doc.Marks[j] = doc.Marks[j] with { Group = name };
                        return name != oldName;
                    }); Pro.CountGroup = name;
                }
                break;
            case "Delete Count Group": case "Clear Count Group":
                var current = Pro.CountGroup;
                Edit(action, () => { var removed = doc.Marks.RemoveAll(m => m.Kind == MarkKind.Count && m.Group == current); if (action == "Delete Count Group") removed += doc.CountGroups.RemoveAll(g => g.Name == current); return removed > 0; });
                if (action == "Delete Count Group") Pro.CountGroup = doc.CountGroups.FirstOrDefault()?.Name ?? "1"; break;
            case "Toggle Count Group":
                EnsureCountGroup(); Edit(action, () => { var i = doc.CountGroups.FindIndex(g => g.Name == Pro.CountGroup); if (i < 0) return false; doc.CountGroups[i] = doc.CountGroups[i] with { Visible = !doc.CountGroups[i].Visible }; return true; }); break;
            case "Frame Stroke":
                var choice = await TextPrompt.Ask(this, "Frame Stroke", "Color (#RRGGBB), or none", Pro.FrameStroke is { } c ? $"#{c & 0xffffff:X6}" : "none");
                if (choice is null || !ReferenceEquals(doc, _document)) return;
                if (choice.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) || choice.Trim() == Localize.Text("None")) Pro.FrameStroke = null;
                else if (Color.TryParse(choice, out var color)) Pro.FrameStroke = color.ToUInt32();
                else { Say("Enter a color as #RRGGBB."); return; }
                ApplyUtilityOptions(); break;
            case "Note Color": case "Count Color":
                var oldColor = new SKColor(action == "Note Color" ? Pro.NoteColor : Pro.CountColor); var picked = oldColor; var canceled = false;
                var picker = new ColorPickerDialog(action, (oldColor.Red / 255d, oldColor.Green / 255d, oldColor.Blue / 255d));
                picker.Moved += rgb => picked = new SKColor((byte)Math.Round(rgb.Red * 255), (byte)Math.Round(rgb.Green * 255), (byte)Math.Round(rgb.Blue * 255)); picker.Cancelled += () => canceled = true;
                await picker.ShowDialog(this); if (canceled || !ReferenceEquals(doc, _document)) return;
                if (action == "Note Color") Pro.NoteColor = (uint)picked; else Pro.CountColor = (uint)picked; ApplyUtilityOptions(); break;
        }
        SyncUtilityOptions(); RefreshOptionsBar(); ShowAnnotationReadout(); _canvas.InvalidateOverlay();
    }
    private bool BeginRulerHandle(SKPoint at, KeyModifiers keys)
    {
        if (_document is not { } doc || doc.Marks.LastOrDefault(m => m.Kind == MarkKind.Measure) is not { } mark) return false;
        var start = new SKPoint((float)mark.X, (float)mark.Y); var end = start + new SKPoint((float)mark.Width, (float)mark.Height);
        var second = mark.SecondX is { } sx && mark.SecondY is { } sy ? new SKPoint((float)sx, (float)sy) : new SKPoint(float.MaxValue, float.MaxValue);
        _proGrab = keys.HasFlag(KeyModifiers.Alt) ? 5 : SKPoint.Distance(at, end) < 12 / _canvas.Zoom ? 3 : SKPoint.Distance(at, start) < 12 / _canvas.Zoom ? 4 : SKPoint.Distance(at, second) < 12 / _canvas.Zoom ? 5 : -1;
        if (_proGrab < 0) return false;
        _markBefore = mark; _selectedMark = mark.ID; _markHistory = true; _history.Begin("Ruler Tool", doc, Selected); return true;
    }
    private DocumentMark RulerMoved(DocumentMark mark, SKPoint at, SKPoint delta) => _proGrab switch {
        3 => mark with { Width = at.X - mark.X, Height = at.Y - mark.Y },
        4 => mark with { X = at.X, Y = at.Y, Width = mark.X + mark.Width - at.X, Height = mark.Y + mark.Height - at.Y },
        5 => mark with { SecondX = at.X, SecondY = at.Y },
        _ => mark with { X = mark.X + delta.X, Y = mark.Y + delta.Y, SecondX = mark.SecondX + delta.X, SecondY = mark.SecondY + delta.Y }
    };
    private void OpenNote(DocumentMark mark)
    {
        _noteEditor?.Close(); if (_document is not { } doc) return;
        _selectedMark = _editingNote = mark.ID;
        var editor = new NoteEditorWindow { Title = Localize.Text("Notes"), Width = 360, Height = 290, MinWidth = 260, MinHeight = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var text = new TextBox { Text = mark.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 8192, Tag = "note-text", PlaceholderText = Localize.Text("Note") };
        var cancel = new Button { Content = Localize.Text("Cancel"), IsCancel = true }; var save = new Button { Content = Localize.Text("Done"), IsDefault = true }; var discard = false;
        cancel.Click += (_, _) => { discard = true; editor.Close(); }; save.Click += (_, _) => editor.Close();
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        var author = new TextBlock { Text = Localize.Text("Author:") + " " + mark.Author, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(author, Dock.Top); panel.Children.Add(author);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { cancel, save } }; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(text); editor.Content = panel;
        _noteEditor = editor; editor.Closed += (_, _) => {
            if (ReferenceEquals(_noteEditor, editor)) { _noteEditor = null; _editingNote = null; }
            if (!discard && ReferenceEquals(doc, _document) && doc.Marks.FindIndex(m => m.ID == mark.ID) is var index && index >= 0 && doc.Marks[index].Text != text.Text)
                Edit("Edit Note", () => { doc.Marks[index] = doc.Marks[index] with { Text = text.Text ?? "" }; return true; });
            _canvas.InvalidateOverlay();
        };
        editor.Show(this); text.Focus(); SyncUtilityOptions(); RefreshOptionsBar(); _canvas.InvalidateOverlay();
    }
    private void DrawNoteGlyph(DrawingContext context, Point at, DocumentMark mark)
    {
        var editing = mark.ID == _editingNote; var brush = new SolidColorBrush(Color.FromUInt32(mark.Color));
        using var move = context.PushTransform(Matrix.CreateTranslation(at.X - 10, at.Y - 12));
        var paper = StreamGeometry.Parse("M2 1 L19 1 Q21 1 21 3 L21 18 L14 25 L2 25 Q0 25 0 23 L0 3 Q0 1 2 1 Z M14 25 L14 18 L21 18");
        context.DrawGeometry(Skin.ChromeBrush, new Pen(Brushes.Black, 3.5), paper); context.DrawGeometry(brush, new Pen(Brushes.White, 1.5), paper);
        if (editing)
        {
            context.DrawLine(new Pen(Brushes.Black, 1.3), new Point(5, 10), new Point(12, 10)); context.DrawLine(new Pen(Brushes.Black, 1.3), new Point(5, 14), new Point(10, 14));
            var pencil = StreamGeometry.Parse("M11 16 L13 10 L25 -2 Q27 -4 29 -2 L30 -1 Q32 1 30 3 L18 15 Z M13 10 L18 15");
            context.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.5), pencil);
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void BuildNotesPanel(StackPanel body)
    {
        body.Spacing = 6;
        var menu = TypeButton("≡", "Notes Menu", () => { }); menu.HorizontalAlignment = HorizontalAlignment.Right;
        var flyout = new MenuFlyout(); flyout.Items.Add(TypeItem("Close", () => HidePanel(_dockPanels["Notes"])));
        flyout.Items.Add(TypeItem("Close Tab Group", () => { if (_dockPanels["Notes"].Group is { } group) foreach (var entry in Members(group).ToArray()) HidePanel(entry); })); menu.Flyout = flyout; body.Children.Add(menu);
        var empty = new TextBlock { Text = Localize.Text("Use the Note tool to add notes"), FontStyle = FontStyle.Italic, Foreground = Skin.SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 8192, Tag = "note-content" };
        var content = new Grid { Height = 105 }; content.Children.Add(empty); content.Children.Add(text); body.Children.Add(content);
        body.Children.Add(new Separator()); var footer = new Grid { ColumnDefinitions = new("Auto,Auto,*,Auto") }; body.Children.Add(footer);
        Button previous = null!, next = null!, remove = null!;
        CanvasDocument? owner = null; Guid? shown = null; bool updating = false;
        void Save()
        {
            if (updating || owner is null || owner != _document || shown is null) return;
            var index = owner.Marks.FindIndex(m => m.ID == shown && m.Kind == MarkKind.Note); if (index < 0) return;
            var note = owner.Marks[index]; var value = text.Text ?? ""; if (note.Text == value) return;
            Edit("Edit Note", () => { owner.Marks[index] = note with { Text = value }; return true; });
        }
        void Move(int direction)
        {
            Save(); var notes = _document?.Marks.Where(m => m.Kind == MarkKind.Note).ToArray() ?? []; if (notes.Length == 0) return;
            var index = Array.FindIndex(notes, n => n.ID == shown); _selectedMark = notes[Math.Clamp(index + direction, 0, notes.Length - 1)].ID; Update(); _canvas.InvalidateVisual();
        }
        previous = TypeButton("←", "Previous Note", () => Move(-1)); next = TypeButton("→", "Next Note", () => Move(1));
        remove = TypeButton("⌫", "Delete Note", () => { if (owner == _document && shown is { } id) { _selectedMark = id; DeleteToolAnnotation(); Update(); } });
        footer.Children.Add(previous); Grid.SetColumn(next, 1); footer.Children.Add(next); Grid.SetColumn(remove, 3); footer.Children.Add(remove);
        void Update()
        {
            if (updating) return; var notes = _document?.Marks.Where(m => m.Kind == MarkKind.Note).ToArray() ?? [];
            var selected = notes.FirstOrDefault(n => n.ID == _selectedMark) ?? notes.FirstOrDefault();
            // Keep a pending text edit intact while unrelated panels refresh.
            if (text.IsKeyboardFocusWithin && owner == _document && selected?.ID == shown) return;
            updating = true; owner = _document; shown = selected?.ID; text.Text = selected?.Text ?? ""; text.IsVisible = selected is not null; empty.IsVisible = selected is null;
            var at = Array.FindIndex(notes, n => n.ID == shown); previous.IsEnabled = at > 0; next.IsEnabled = at >= 0 && at < notes.Length - 1; remove.IsEnabled = selected is not null; updating = false;
        }
        text.LostFocus += (_, _) => Save(); body.DetachedFromVisualTree += (_, _) => Save(); _utilityRefresh["Notes"] = Update; Update();
    }
}

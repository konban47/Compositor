using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Guid? ActiveArtboard => PropertyLayer is { } layer ? ArtboardOf(layer) : null;
    private Guid? ArtboardOf(ImageLayer layer)
    {
        if (layer.Container == LayerContainer.Artboard) return layer.ID;
        return layer.ParentID is { } parent && _document?.Layers.FirstOrDefault(l => l.ID == parent) is { } owner ? ArtboardOf(owner) : null;
    }
    private void AssignGuideOwner(CanvasDocument document, Guid? id)
    { if (id is { } key && document.Guides.FirstOrDefault(g => g.ID == key) is { } guide) guide.ArtboardID = ActiveArtboard; }
    private bool BuildGuideAndPasteboardMenu(ContextMenu menu)
    {
        var at = _canvas.ContextPoint;
        if (_document is not { } doc || at.X < 0 || at.Y < 0 || at.X > doc.Width || at.Y > doc.Height)
        {
            foreach (var (name, color) in new[] { ("Default", "#282828"), ("Black", "#000000"), ("Dark Gray", "#282828"), ("Medium Gray", "#808080"), ("Light Gray", "#BFBFBF"), ("Custom", EditorPreferences.Current.CustomPasteboard) })
            {
                var row = Command(name, () => SetPasteboard(name, color)); row.ToggleType = MenuItemToggleType.CheckBox; row.IsChecked = EditorPreferences.Current.PasteboardPreset == name; menu.Items.Add(row);
            }
            menu.Items.Add(Command("Select Custom Color…", () => _ = PickPasteboard())); return true;
        }
        if (_canvas.GuideAtPoint(at) is not { } id) return false;
        if (!_canvas.SelectedGuides.Contains(id)) { _canvas.SelectedGuides.Clear(); _canvas.SelectedGuides.Add(id); }
        menu.Items.Add(Command("Edit Selected Guides…", () => _ = EditSelectedGuides())); menu.Items.Add(new Separator());
        var locked = Command("Lock Guides", LockGuides); locked.ToggleType = MenuItemToggleType.CheckBox; locked.IsChecked = _guidesLocked; menu.Items.Add(locked);
        void Clear(string label, Func<CanvasGuide, bool> predicate)
        {
            var item = Command(label, () => { Edit(label, () => doc.Guides.RemoveAll(g => predicate(g)) > 0); _canvas.SelectedGuides.RemoveWhere(id => doc.Guides.All(g => g.ID != id)); _canvas.InvalidateVisual(); });
            item.IsEnabled = !_guidesLocked && doc.Guides.Any(predicate); menu.Items.Add(item);
        }
        Clear("Clear Guides", _ => true);
        Clear("Clear Canvas Guides", g => g.ArtboardID is null || doc.Layers.All(l => l.ID != g.ArtboardID));
        Clear("Clear Selected Guides", g => _canvas.SelectedGuides.Contains(g.ID));
        var artboard = ActiveArtboard; Clear("Clear Selected Artboard Guides", g => artboard is not null && g.ArtboardID == artboard);
        return true;
    }
    private async Task EditSelectedGuides()
    {
        if (_document is not { } doc) return;
        var guides = doc.Guides.Where(g => _canvas.SelectedGuides.Contains(g.ID)).ToArray(); if (guides.Length == 0) return;
        var form = new FormDialog("Edit Selected Guides", 440, 360);
        var axis = form.Choice("Orientation", ["Horizontal", "Vertical"], (int)guides[0].Axis);
        var position = form.Number("Position (px)", guides[0].Position, -1000000, 1000000);
        form.Description("Multiple selected guides move by the same offset.");
        if (!await form.ShowDialog<bool>(this) || !ReferenceEquals(doc, _document) || _guidesLocked) return;
        var delta = (double)(position.Value ?? 0) - guides[0].Position;
        Edit("Edit Selected Guides", () => { foreach (var guide in doc.Guides.Where(g => guides.Any(old => old.ID == g.ID))) { guide.Axis = (GuideAxis)axis.SelectedIndex; guide.Position = Math.Clamp(guide.Position + delta, -1000000, 1000000); } return true; });
    }
    private void SetPasteboard(string name, string color)
    { EditorPreferences.Current.PasteboardPreset = name; EditorPreferences.Current.Pasteboard = color; SavePreferenceChanges(); _canvas.InvalidateVisual(); }
    private async Task PickPasteboard()
    {
        var color = SkiaSharp.SKColor.Parse(EditorPreferences.Current.CustomPasteboard);
        var chosen = EditorPreferences.Current.CustomPasteboard; var cancelled = false;
        var picker = new ColorPickerDialog(Localize.Text("Select Custom Color"), (color.Red / 255d, color.Green / 255d, color.Blue / 255d));
        picker.Moved += rgb => chosen = $"#{(byte)Math.Round(rgb.Red * 255):X2}{(byte)Math.Round(rgb.Green * 255):X2}{(byte)Math.Round(rgb.Blue * 255):X2}";
        picker.Cancelled += () => cancelled = true; await picker.ShowDialog(this); if (cancelled) return;
        EditorPreferences.Current.CustomPasteboard = chosen; SetPasteboard("Custom", chosen);
    }
}

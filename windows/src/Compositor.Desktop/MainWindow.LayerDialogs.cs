using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

/// <summary>The Layer menu's own dialogs, laid out after Photoshop's: what each one asks, and what OK then does.</summary>
public sealed partial class MainWindow
{
    /// <summary>The first "Name n" no layer of the document is called yet, as Photoshop numbers new layers.</summary>
    private static string NextLayerName(CanvasDocument doc, string prefix)
    {
        var taken = doc.Layers.Select(l => l.Name).ToHashSet(); var n = 1;
        while (taken.Contains($"{prefix} {n}")) n++;
        return $"{prefix} {n}";
    }
    private static void ApplyLayerOptions(CanvasDocument doc, ImageLayer layer, LayerOptions options)
    {
        layer.Name = options.Name; layer.Label = options.Label; layer.BlendMode = options.Mode; layer.Opacity = Math.Clamp(options.Opacity, 0, 1);
        if (options.ClipToPrevious) LayerMaskEdits.ToggleSelected(doc, [layer.ID]);
    }
    private async Task NewNamedLayer()
    {
        if (_document is not { } doc) return;
        if (await LayerOptionsDialog.Ask(this, "New Layer", NextLayerName(doc, Localize.Text("Layer")), LayerOptionsDialog.LayerOptionsKind.Layer) is not { } options || !ReferenceEquals(doc, _document)) return;
        Guid? made = null;
        Edit("New Blank Layer", () =>
        {
            made = LayerPlacement.AddBlank(doc, Selected); if (made is null) return false;
            var layer = doc.Layers.Single(l => l.ID == made);
            // Fill with Neutral Color: the color that mode leaves the picture as it is, so later painting or filtering shows through it.
            if (options.FillNeutral && LayerOptionsDialog.NeutralColor(options.Mode) is { } neutral)
            {
                var pixels = Bitmaps.Allocate(Bitmaps.ColorInfo(doc.Width, doc.Height)); pixels.Erase(neutral);
                layer.Asset = ImportedImage.Create(pixels, options.Name); layer.Transform = new LayerTransform(0, 0, doc.Width, doc.Height);
            }
            ApplyLayerOptions(doc, layer, options); return true;
        }); Reselect(made);
    }
    private async Task NewNamedGroup()
    {
        if (_document is not { } doc) return;
        if (await LayerOptionsDialog.Ask(this, "New Group", NextLayerName(doc, Localize.Text("Group")), LayerOptionsDialog.LayerOptionsKind.Group) is not { } options || !ReferenceEquals(doc, _document)) return;
        Guid? made = null;
        Edit("New Group", () => { made = LayerPlacement.AddFolder(doc, Selected); if (made is null) return false; ApplyLayerOptions(doc, doc.Layers.First(l => l.ID == made), options); return true; }); Reselect(made);
    }
    private async Task GroupFromLayers()
    {
        if (_document is not { } doc) return; var ids = SelectedLayers;
        if (await LayerOptionsDialog.Ask(this, "New Group from Layers", NextLayerName(doc, Localize.Text("Group")), LayerOptionsDialog.LayerOptionsKind.Group) is not { } options || !ReferenceEquals(doc, _document)) return;
        Guid? made = null;
        Edit("Group Layers", () => { made = LayerPlacement.GroupSelected(doc, ids); if (made is null) return false; ApplyLayerOptions(doc, doc.Layers.Single(l => l.ID == made), options); return true; }); Reselect(made);
    }
    /// <summary>
    /// Layer ▸ New Adjustment Layer: Photoshop's New Layer dialog first ("Levels 1"), then the adjustment's own settings;
    /// the name, color, mode, opacity and clipping are then given to the layer the adjustment made.
    /// </summary>
    private async Task NewAdjustmentLayer(string title, Func<Task> create)
    {
        if (_document is not { } doc) return;
        var name = NextLayerName(doc, Localize.Text(title.TrimEnd('…', '.')));
        if (await LayerOptionsDialog.Ask(this, "New Layer", name, LayerOptionsDialog.LayerOptionsKind.Content) is not { } options || !ReferenceEquals(doc, _document)) return;
        var before = doc.Layers.Select(l => l.ID).ToHashSet();
        await create();
        if (!ReferenceEquals(doc, _document) || doc.Layers.FirstOrDefault(l => !before.Contains(l.ID)) is not { } made) return;
        Edit("Layer Properties", () => { ApplyLayerOptions(doc, made, options); return true; }); Reselect(made.ID);
    }
    /// <summary>Layer ▸ Duplicate Layer: into this document (on an artboard if one is chosen), another open one, or a new one.</summary>
    private async Task DuplicateNamedLayer()
    {
        if (_document is not { } doc || PropertyLayer is not { } layer) return;
        var tabs = _tabs.Where(t => t.Document is not null).ToList();
        var documents = tabs.Select(t => (t.Name, (IReadOnlyList<(Guid ID, string Name)>)t.Document!.Layers.Where(l => l.Container == LayerContainer.Artboard).Select(l => (l.ID, l.Name)).ToList())).ToList();
        if (await DuplicateLayerDialog.Ask(this, layer.Name, documents, Math.Max(0, tabs.IndexOf(_open))) is not { } choice || !ReferenceEquals(doc, _document)) return;
        if (choice.Document >= 0 && ReferenceEquals(tabs[choice.Document], _open))
        {
            Guid? made = null;
            Edit("Duplicate Layer", () =>
            {
                made = LayerEdits.Duplicate(doc, layer.ID); if (made is null) return false;
                var copy = doc.Layers.First(l => l.ID == made); copy.Name = choice.Name;
                if (choice.Artboard is { } board) copy.ParentID = board;
                return true;
            }); Reselect(made); return;
        }
        if (LayerClipboard.Copy(doc, [layer.ID]) is not { } clip) return;
        if (choice.Document < 0)
        {
            var fresh = new CanvasDocument(Guid.NewGuid(), doc.Width, doc.Height, doc.Resolution);
            var pasted = clip.Paste(fresh, null);
            if (pasted.Count > 0) fresh.Layers.First(l => l.ID == pasted[0]).Name = choice.Name;
            AdoptImported(fresh, "Duplicate Layer"); _open.CustomName = choice.NewDocumentName; RefreshTabs(); return;
        }
        var target = tabs[choice.Document]; var other = target.Document!;
        target.History.Begin("Duplicate Layer", other, null);
        var ids = clip.Paste(other, null);
        if (ids.Count > 0) { var copy = other.Layers.First(l => l.ID == ids[0]); copy.Name = choice.Name; if (choice.Artboard is { } board) copy.ParentID = board; }
        target.History.End(other, ids.Count > 0 ? ids[0] : null);
        RefreshTabs(); Say(Localize.Format($"Duplicated into {target.Name}"));
    }
    private async Task LayerLocksDialog()
    {
        if (_document is not { } doc || doc.Layers.FirstOrDefault(l => l.ID == Selected) is not { } layer) return;
        var ids = SelectedLayers; if (await LockLayersDialog.Ask(this, layer.Locks) is not { } locks) return;
        Edit("Layer Locks", () => LayerProtection.Set(doc, ids, locks)); ShowLayers(doc);
    }
    /// <summary>New Artboard, Artboard from Layers and Add Frame to Layer, with Photoshop's name and size dialogs.</summary>
    private async Task MakeLayerContainer(LayerContainer kind, bool selected)
    {
        if (_document is not { } doc) return; IReadOnlyCollection<Guid> ids = selected ? SelectedLayers : [];
        var bounds = selected ? LayerWorkflow.Bounds(doc, ids) : SKRect.Create(doc.Width, doc.Height);
        var frame = kind == LayerContainer.Frame;
        var title = frame ? "Add Frame to Layer" : selected ? "New Artboard from Layers" : "New Artboard";
        var name = NextLayerName(doc, Localize.Text(frame ? "Frame" : "Artboard"));
        if (await ContainerDialog.Ask(this, title, name, bounds.Width, bounds.Height, frame) is not { } asked || !ReferenceEquals(doc, _document)) return;
        // The size is kept about the middle of what was chosen, or of the canvas.
        var placed = SKRect.Create((float)(bounds.MidX - asked.Width / 2), (float)(bounds.MidY - asked.Height / 2), (float)asked.Width, (float)asked.Height);
        if (placed.Left < 0) placed.Offset(-placed.Left, 0); if (placed.Top < 0) placed.Offset(0, -placed.Top);
        Guid? made = null;
        Edit(frame ? "Frame from Layers" : selected ? "Artboard from Layers" : "New Artboard", () =>
        {
            made = LayerWorkflow.MakeContainer(doc, ids, kind, asked.Name, placed, false);
            if (made is null) return false;
            if (kind == LayerContainer.Artboard) CanvasEdits.Resize(doc, Math.Max(doc.Width, (int)Math.Ceiling(placed.Right)), Math.Max(doc.Height, (int)Math.Ceiling(placed.Bottom)), 0);
            return true;
        }); Reselect(made);
    }
    /// <summary>Layer Style ▸ Global Light…: one angle and altitude for every effect using global light, previewed live.</summary>
    private async Task GlobalLight()
    {
        if (_document is not { } doc) return;
        var styled = doc.Layers.Where(l => l.Effects is not null && LayerWorkflow.Editable(doc, [l.ID])).ToArray();
        var first = styled.FirstOrDefault()?.Effects;
        var start = (Angle: first?.GlobalLightAngle ?? 120, Altitude: first?.GlobalLightAltitude ?? 30);
        var saved = styled.ToDictionary(l => l.ID, l => (l.Effects!.GlobalLightAngle, l.Effects.GlobalLightAltitude));
        void Show(double angle, double altitude) { foreach (var l in styled) { l.Effects!.GlobalLightAngle = angle; l.Effects.GlobalLightAltitude = altitude; } Refresh(); }
        var result = await GlobalLightDialog.Ask(this, start.Angle, start.Altitude, Show);
        foreach (var l in styled) (l.Effects!.GlobalLightAngle, l.Effects.GlobalLightAltitude) = saved[l.ID];
        Refresh();
        if (result is not { } light || !ReferenceEquals(doc, _document)) return;
        Edit("Global Light", () =>
        {
            foreach (var l in styled) { l.Effects = l.Effects!.Scaled(1); l.Effects.GlobalLightAngle = light.Angle; l.Effects.GlobalLightAltitude = light.Altitude; }
            return styled.Length > 0;
        });
    }
}

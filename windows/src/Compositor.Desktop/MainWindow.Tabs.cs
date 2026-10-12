using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Control? _tabDropArea;
    private void WireTabDrop(Control target, Tab? destination)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, CanvasDragOver);
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (_importingDrop || e.Handled) return;
            var files = DroppedPaths(e); e.Handled = true;
            if (destination is null)
            {
                _importingDrop = true;
                try { foreach (var file in files) await OpenPath(file, newTab: true); }
                catch (Exception error) { Say(error.Message); }
                finally { _importingDrop = false; }
            }
            else if (_tabs.Contains(destination)) { Bring(destination); await ImportDroppedPaths(files); }
        });
    }
    private bool DropLayersOnTabs(Point point)
    {
        if (_tabDropArea is not { } area || area.TranslatePoint(default, this) is not { } origin
            || !new Rect(origin, area.Bounds.Size).Contains(point) || _document is not { } source) return false;
        var destination = _tabStrip.Children.OfType<Border>().FirstOrDefault(pill => pill.Tag is Tab
            && pill.TranslatePoint(default, this) is { } at && new Rect(at, pill.Bounds.Size).Contains(point))?.Tag as Tab;
        if (ReferenceEquals(destination, _open)) return true;
        var copied = LayerClipboard.Copy(source, SelectedLayers); if (copied is null) return true;
        if (destination is null || destination.Document is null)
        {
            var made = copied.NewDocument();
            var bounds = LayerWorkflow.Bounds(made, made.Layers.Select(l => l.ID));
            var left = Math.Floor(bounds.Left); var top = Math.Floor(bounds.Top);
            var width = (int)Math.Ceiling(bounds.Right - left); var height = (int)Math.Ceiling(bounds.Bottom - top);
            if (width < 1 || height < 1 || width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide || (long)width * height > DocumentLimits.MaxSurfacePixels) { made.Dispose(); Say("The image exceeds the pixel budget."); return true; }
            foreach (var layer in made.Layers)
            {
                layer.Transform = layer.Transform with { X = layer.Transform.X - left, Y = layer.Transform.Y - top };
                if (layer.Mask is { Placement: { } mask } m) { layer.Mask = m.Clone(); layer.Mask.Placement = mask with { X = mask.X - left, Y = mask.Y - top }; }
            }
            made.Width = width; made.Height = height;
            if (destination is not null) Bring(destination);
            AdoptImported(made, "Drop Layers into New Canvas");
        }
        else
        {
            Bring(destination); IReadOnlyList<Guid> ids = [];
            Edit("Paste Layers", () => (ids = copied.Paste(destination.Document, Selected)).Count > 0);
            if (ids.Count > 0) { Reselect(ids[0]); foreach (var id in ids.Skip(1)) SelectLayerRow(id, true); }
        }
        return true;
    }
}

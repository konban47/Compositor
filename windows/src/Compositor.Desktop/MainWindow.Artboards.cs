using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private SKPoint? _artboardStart;
    private SKRect? _artboardDraft;
    private ImageLayer? SelectedArtboard => ActiveArtboard is { } id ? _document?.Layers.FirstOrDefault(l => l.ID == id) : null;
    private bool ArtboardPressed(SKPoint at, KeyModifiers keys)
    {
        if (_tool != Tool.Artboard || _document is not { } doc) return false;
        if (_canvas.TransformBox is { } box && _canvas.TransformHandleAt(box, at) is not null) return false;
        var hit = doc.Layers.LastOrDefault(l => l.Container == LayerContainer.Artboard && l.Transform.Contains(at));
        if (hit is not null) { SelectLayerRow(hit.ID, keys.HasFlag(KeyModifiers.Shift)); return false; }
        _artboardStart = at; _artboardDraft = null; return true;
    }
    private bool ArtboardMoved(SKPoint at, KeyModifiers keys)
    {
        if (_artboardStart is not { } start) return false;
        _artboardDraft = ShapeEdits.Box(start, at, keys.HasFlag(KeyModifiers.Shift), keys.HasFlag(KeyModifiers.Alt));
        _canvas.InvalidateOverlay(); return true;
    }
    private bool ArtboardReleased()
    {
        if (_artboardStart is not { } start) return false;
        var options = _optionsBar.Artboard;
        var bounds = _artboardDraft is { Width: > 3, Height: > 3 } draft ? draft : SKRect.Create(start.X, start.Y, options.Width, options.Height);
        _artboardStart = null; _artboardDraft = null; CreateArtboard(bounds); return true;
    }
    private void CreateArtboard(SKRect bounds)
    {
        if (_document is not { } doc || bounds.Width < 1 || bounds.Height < 1 || bounds.Width > 30000 || bounds.Height > 30000) return;
        Guid? made = null;
        Edit("New Artboard", () =>
        {
            var count = doc.Layers.Count(l => l.Container == LayerContainer.Artboard) + 1;
            made = LayerWorkflow.MakeContainer(doc, [], LayerContainer.Artboard, Localize.Text("Artboard") + " " + count, bounds);
            if (made is null) return false;
            var board = doc.Layers.Single(l => l.ID == made); board.ArtboardBackground = _optionsBar.Artboard.Background;
            if (EditorPreferences.Current.ArtboardAutoNest)
                foreach (var layer in doc.Layers.Where(l => l.ID != made && l.ParentID is null && l.Container != LayerContainer.Artboard && LayerProtection.CanMove(doc, l.ID)))
                    if (board.Transform.Contains(layer.Transform.Point(.5, .5))) layer.ParentID = made;
            if (EditorPreferences.Current.ArtboardAutoCanvas) FitArtboardCanvas(doc, false);
            return true;
        });
        Reselect(made); ShowTransformBox(); RefreshOptionsBar();
    }
    private async void ArtboardCommand(string operation)
    {
        var options = _optionsBar.Artboard;
        if (operation == "Custom Color")
        {
            var color = new SKColor(options.Background); var boardID = SelectedArtboard?.ID;
            var original = options.Background; var picked = original;
            await ColorPickerDialog.Pick(this, "Artboard Background", (color.Red / 255d, color.Green / 255d, color.Blue / 255d), rgb =>
            {
                picked = rgb == (color.Red / 255d, color.Green / 255d, color.Blue / 255d) ? original : (uint)new SKColor((byte)(rgb.Red * 255), (byte)(rgb.Green * 255), (byte)(rgb.Blue * 255));
            });
            options.Background = picked;
            if (SelectedArtboard?.ID == boardID) ArtboardCommand("Background"); _optionsBar.RefreshArtboardOptions(); return;
        }
        if (operation == "New")
        {
            var previous = SelectedArtboard?.Transform; var at = previous is { } t ? new SKPoint((float)(t.X + t.Width + 50), (float)t.Y) : new SKPoint();
            CreateArtboard(SKRect.Create(at.X, at.Y, options.Width, options.Height)); return;
        }
        if (_document is not { } doc || SelectedArtboard is not { } board || !LayerProtection.CanMove(doc, board.ID)) return;
        Edit("Artboard " + operation, () =>
        {
            if (operation == "Background") { if (board.ArtboardBackground == options.Background) return false; board.ArtboardBackground = options.Background; }
            else board.Transform = board.Transform with { Width = options.Width, Height = options.Height };
            if (EditorPreferences.Current.ArtboardAutoCanvas) FitArtboardCanvas(doc, false);
            return true;
        }); ShowTransformBox(); RefreshOptionsBar();
    }
    private bool FitArtboardCanvas(CanvasDocument doc, bool shrink)
    {
        var boards = doc.Layers.Where(l => l.Container == LayerContainer.Artboard).Select(l => TransformEdits.Bounds(l.Transform)).ToArray();
        if (boards.Length == 0) return false;
        var left = (int)Math.Floor(boards.Min(b => b.Left)); var top = (int)Math.Floor(boards.Min(b => b.Top));
        var right = (int)Math.Ceiling(boards.Max(b => b.Right)); var bottom = (int)Math.Ceiling(boards.Max(b => b.Bottom));
        if (!shrink) { left = Math.Min(0, left); top = Math.Min(0, top); right = Math.Max(doc.Width, right); bottom = Math.Max(doc.Height, bottom); }
        if (!CanvasEdits.Crop(doc, SKRectI.Create(left, top, right - left, bottom - top))) return false;
        if (ReferenceEquals(doc, _document)) _canvas.RestoreViewport((_canvas.Zoom, _canvas.OriginX - left, _canvas.OriginY - top));
        return true;
    }
    private void DrawArtboardDraft(DrawingContext context, Func<SKPoint, Point> screen)
    {
        if (_artboardDraft is not { } rect) return;
        var corners = new[] { screen(new(rect.Left, rect.Top)), screen(new(rect.Right, rect.Top)), screen(new(rect.Right, rect.Bottom)), screen(new(rect.Left, rect.Bottom)) };
        for (var i = 0; i < 4; i++) context.DrawLine(Skin.TransformPen, corners[i], corners[(i + 1) % 4]);
    }
}

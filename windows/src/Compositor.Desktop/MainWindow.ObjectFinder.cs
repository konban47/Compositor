using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private SKBitmap? _objectMatte;
    private CanvasDocument? _objectDocument;
    private Guid _objectRevision;
    private Guid? _objectLayer;
    private bool _findingObjects, _objectAllLayers;
    private TaskCompletionSource? _objectFinderCompletion;
    private SKPath? _objectHover;
    private SKPoint? _objectPoint;
    private bool CurrentObjectMatte => _objectMatte is not null && ReferenceEquals(_document, _objectDocument)
        && _objectRevision == _history.CurrentRevision && _objectLayer == Selected && _objectAllLayers == _options.WandAllLayers;
    private async Task RefreshObjectFinder(bool force = false)
    {
        while (_findingObjects && _objectFinderCompletion is { } pending) await pending.Task;
        if (_document is not { } doc || (!force && CurrentObjectMatte)) return;
        using var source = SelectionEdits.Sample(doc, _options.WandAllLayers ? null : Selected); if (source is null) return;
        var revision = _history.CurrentRevision; var layer = Selected; var all = _options.WandAllLayers; _findingObjects = true;
        var completion = _objectFinderCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var matte = await Task.Run(() => { using var detector = new SubjectSegmentation(); return detector.Predict(source); });
            if (!ReferenceEquals(doc, _document) || revision != _history.CurrentRevision || layer != Selected || all != _options.WandAllLayers) { matte.Dispose(); return; }
            _objectMatte?.Dispose(); _objectMatte = matte; _objectDocument = doc; _objectRevision = revision; _objectLayer = layer; _objectAllLayers = all;
            UpdateObjectHover();
        }
        catch (Exception ex) { Say($"Could not find the subject: {ex.Message}"); }
        finally { _findingObjects = false; completion.TrySetResult(); }
    }
    private void ObjectHover(SKPoint point)
    {
        if (_tool != Tool.Object) return; _objectPoint = point;
        if (_options.Selection.ObjectFinder && _options.Selection.ObjectAutoRefresh && !CurrentObjectMatte) _ = RefreshObjectFinder();
        if (CurrentObjectMatte && (_objectHover is null || _options.Selection.OverlayMode != 1 && !_objectHover.Contains(point.X, point.Y))) UpdateObjectHover();
    }
    private void UpdateObjectHover()
    {
        _objectHover?.Dispose(); _objectHover = null;
        if (CurrentObjectMatte && _options.Selection.ObjectFinder)
            _objectHover = SubjectEdits.Outline(_objectMatte!, _options.Selection.OverlayMode == 1 ? null : _objectPoint);
        _canvas.InvalidateVisual();
    }
    private async Task SelectObjectRegion(SKPath region, SKPoint? point, SelectionPaintMode mode)
    {
        using (region)
        {
            await RefreshObjectFinder(); if (!CurrentObjectMatte || _document is not { } doc) return;
            using var outline = point is { } at ? SubjectEdits.Outline(_objectMatte!, at) : SubjectEdits.InRegion(_objectMatte!, region);
            if (outline.IsEmpty) { Say("No foreground object was found in this area."); return; }
            if (!_options.Selection.ObjectSubtract && mode == SelectionPaintMode.Subtract) mode = SelectionPaintMode.New;
            var mask = DocumentSelection.FromPath(new SKPath(outline), !_options.Selection.HardEdge).Coverage(SKRectI.Create(doc.Width, doc.Height)); if (mask is null) return;
            if (!_options.Selection.HardEdge) for (var y = 0; y < mask.Height; y++) for (var x = 0; x < mask.Width; x++)
            { var v = (byte)(mask.GetPixel(x, y).Red * _objectMatte!.GetPixel(x, y).Red / 255); mask.SetPixel(x, y, new SKColor(v, v, v)); }
            Edit("Object Selection", () => SelectionBrushEdits.Combine(doc, mask, 1, mode));
        }
    }
    private void DrawObjectFinder(DrawingContext context, Func<SKPoint, Point> screen)
    {
        var s = _options.Selection;
        if (_tool != Tool.Object || !s.ObjectFinder || s.OverlayMode == 2 || !CurrentObjectMatte || _objectHover is not { IsEmpty: false } path) return;
        using var displayed = new SKPath(path); var o = screen(default); var x = screen(new SKPoint(1, 0)); var y = screen(new SKPoint(0, 1));
        displayed.Transform(new SKMatrix((float)(x.X - o.X), (float)(y.X - o.X), (float)o.X, (float)(x.Y - o.Y), (float)(y.Y - o.Y), (float)o.Y, 0, 0, 1));
        var color = Color.FromUInt32(s.OverlayColor); var brush = new SolidColorBrush(color, s.OverlayOpacity / 100);
        context.DrawGeometry(brush, s.OverlayOutline > 0 ? new Pen(new SolidColorBrush(color), s.OverlayOutline) : null, StreamGeometry.Parse(displayed.ToSvgPathData()));
        if (s.ObjectDetails && _objectPoint is { } point)
        { var label = new FormattedText(Localize.Text("Object") + $" · {path.Bounds.Width:0} × {path.Bounds.Height:0}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Skin.LabelBrush); var at = screen(point) + new Vector(14, 14); context.DrawRectangle(Skin.ChromeBrush, null, new Rect(at, new Size(label.Width + 8, label.Height + 4))); context.DrawText(label, at + new Vector(4, 2)); }
    }
}

using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly List<SKPoint> _puppetSource = [], _puppetPins = [];
    private void BeginPuppetWarp()
    {
        if (PropertyLayer?.Asset is null) return;
        BeginEditTransform("Warp");
        if (_placeMesh is not { } mesh) return;
        _placeMode = "Puppet"; _puppetSource.Clear(); _puppetPins.Clear();
        for (var i = 1; i < 8; i++) { mesh.Split(true, i / 8f); mesh.Split(false, i / 8f); }
        _canvas.InvalidateVisual();
        Say("Click to add pins, drag a pin to deform, Alt-click to remove. Enter applies; Esc cancels.");
    }
    private bool PuppetPressed(SKPoint at, KeyModifiers modifiers)
    {
        _placeGrab = _puppetPins.Select((p, i) => (p, i)).Where(v => SKPoint.Distance(v.p, at) < 12 / _canvas.Zoom)
            .OrderBy(v => SKPoint.Distance(v.p, at)).Select(v => v.i).DefaultIfEmpty(-1).First();
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            if (_placeGrab >= 0) { _puppetPins.RemoveAt(_placeGrab); _puppetSource.RemoveAt(_placeGrab); RebuildPuppet(); }
            _placeGrab = -1; return true;
        }
        if (_placeGrab < 0 && _puppetPins.Count < 64)
        {
            // New pins lock the current deformation at their location.
            var source = at;
            if (_placeMesh is { } mesh && _placeBasis is { } basis && _puppetSource.Count > 0)
            {
                var best = float.MaxValue;
                for (var y = 0; y <= 64; y++) for (var x = 0; x <= 64; x++)
                { var q = mesh.Map(x / 64f, y / 64f); var distance = SKPoint.Distance(q, at); if (distance < best) { best = distance; source = basis.Transform.Point(x / 64.0, y / 64.0); } }
            }
            _puppetSource.Add(source); _puppetPins.Add(at); _placeGrab = _puppetPins.Count - 1;
        }
        _canvas.InvalidateVisual(); return true;
    }
    private void PuppetMoved(SKPoint at)
    { if (_placeGrab < 0 || _placeGrab >= _puppetPins.Count) return; _puppetPins[_placeGrab] = at; RebuildPuppet(); }
    private void RebuildPuppet()
    {
        if (_placeMesh is not { } mesh || _placeBasis is not { } basis) return;
        for (var y = 0; y < mesh.Rows.Count; y++) for (var x = 0; x < mesh.Columns.Count; x++)
        {
            var source = basis.Transform.Point(mesh.Columns[x], mesh.Rows[y]); double weight = 0, dx = 0, dy = 0;
            for (var i = 0; i < _puppetSource.Count; i++)
            {
                var d = SKPoint.Distance(source, _puppetSource[i]); var w = 1.0 / Math.Pow(Math.Max(.01, d), 4);
                weight += w; dx += (_puppetPins[i].X - _puppetSource[i].X) * w; dy += (_puppetPins[i].Y - _puppetSource[i].Y) * w;
            }
            mesh.Points[y * mesh.Columns.Count + x] = source + (weight > 0 ? new SKPoint((float)(dx / weight), (float)(dy / weight)) : default);
        }
        RenderPlacementMesh();
    }
    private void DrawPuppetPins(DrawingContext context, Func<SKPoint, Point> screen)
    {
        if (_placeMode != "Puppet" || _placementTab is null) return;
        foreach (var pin in _puppetPins) { var p = screen(pin); context.DrawEllipse(Skin.LabelBrush, new Pen(Skin.ChromeBrush, 1.5), p, 5, 5); context.DrawEllipse(Skin.ChromeBrush, null, p, 1.5, 1.5); }
    }
}

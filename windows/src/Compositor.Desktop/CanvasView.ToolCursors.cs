using Avalonia;
using Avalonia.Input;
using Compositor.Core.Document;
using SkiaSharp;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    internal Tool CursorTool { get; set; } = Tool.Move;
    internal string CursorDescription { get; private set; } = "Move";
    private static readonly Dictionary<string, Cursor> IllustratedCursors = [];
    internal TransformHandle? TransformHandleAt(LayerTransform box, SKPoint point)
    {
        var direct = TransformEdits.HandleAt(box, point, TransformEdits.Grab / Zoom, TransformEdits.RotateGrip / Zoom);
        if (direct is not null) return direct;
        // Outside the resize hit zone, the surrounding band rotates the same transform.
        if (!box.Contains(point) && TransformEdits.HandleAt(box, point, 22 / Zoom, TransformEdits.RotateGrip / Zoom) is not null)
            return TransformHandle.Rotate;
        return null;
    }
    private bool SetIllustratedCursor()
    {
        if (_document is null || (IsPanning || PanEnabled) && !_zoomDragging || TypeOnClick && CursorTool is not (Tool.VerticalType or Tool.VerticalTypeMask) || BrushOutlineEnabled || PenCursorEnabled) return false;
        var kind = CursorTool.ToString(); double angle = 0;
        if (_zoomDragging) kind = _dragZoomOut ? "ZoomOut" : "ZoomIn";
        else if (_toolPointer is { } pointer && TransformHandles && TransformBox is { } box && TransformHandleAt(box, ToDocument(pointer)) is { } handle)
        {
            kind = handle == TransformHandle.Rotate ? "Rotate" : "Resize";
            angle = box.Radians * 180 / Math.PI + ViewAngle + (handle switch
            { TransformHandle.Top or TransformHandle.Bottom => 90, TransformHandle.TopLeft or TransformHandle.BottomRight => 45,
              TransformHandle.TopRight or TransformHandle.BottomLeft => -45, _ => 0 });
        }
        else if (_toolPointer is { } cropPointer && CropEnabled && CropBox is { } crop && TransformEdits.HandleAt(CropEdits.Box(crop), ToDocument(cropPointer), TransformEdits.Grab / Zoom) is { } cropHandle)
        {
            kind = "Resize"; angle = ViewAngle + (cropHandle switch { TransformHandle.Top or TransformHandle.Bottom => 90, TransformHandle.TopLeft or TransformHandle.BottomRight => 45, TransformHandle.TopRight or TransformHandle.BottomLeft => -45, _ => 0 });
        }
        else if (CursorTool is Tool.VerticalType or Tool.VerticalTypeMask) kind = "VerticalText";
        else if (ZoomToolEnabled) kind = _sourceModifier ? "ZoomOut" : "ZoomIn";
        else if (RotateViewEnabled) kind = "Rotate";
        else if (CursorTool is Tool.Marquee or Tool.SingleRow or Tool.SingleColumn or Tool.Ellipse or Tool.Shape or Tool.ShapeEllipse or Tool.Triangle or Tool.PolygonShape or Tool.Star or Tool.Line or Tool.CustomShape or Tool.Crop or Tool.PerspectiveCrop or Tool.Artboard or Tool.Frame or Tool.Gradient) kind = "Cross";
        var key = kind + ":" + Math.Round(angle / 15) * 15;
        CursorDescription = kind; ActiveCursorKind = StandardCursorType.Arrow;
        if (!IllustratedCursors.TryGetValue(key, out var cursor))
        {
            using var bitmap = new SKBitmap(36, 36, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
            var hotspot = new PixelPoint(18, 18);
            string path;
            switch (kind)
            {
                case "Resize": path = "M3,18 L33,18 M3,18 L9,12 L9,24 Z M33,18 L27,12 L27,24 Z"; break;
                case "Rotate": path = "M11,6 C-1,13 1,26 13,29 M11,6 L4,6 L10,13 Z M13,29 L6,31 L11,23 Z"; break;
                case "Cross": path = "M18,4 L18,15 M18,21 L18,32 M4,18 L15,18 M21,18 L32,18"; break;
                case "VerticalText": path = "M4,18 L32,18 M4,13 L4,23 M32,13 L32,23 M18,15 L18,21"; break;
                case "ZoomIn": case "ZoomOut":
                    path = "M25,14 A11,11 0 1 1 3,14 A11,11 0 1 1 25,14 M22,22 L33,33 M8,14 L20,14" + (kind == "ZoomIn" ? " M14,8 L14,20" : ""); hotspot = new(14,14); break;
                default:
                    path = EditorIcon.CursorPath(kind) ?? "M2,2 L2,23 L8,17 L13,27 L17,25 L12,15 L21,15 Z";
                    canvas.Translate(6, 6); hotspot = new(18,18); break;
            }
            if (kind == "Resize") canvas.RotateDegrees((float)(Math.Round(angle / 15) * 15), 18,18);
            using var geometry = SKPath.ParseSvgPathData(path);
            using var outline = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
            canvas.DrawPath(geometry, outline); canvas.DrawPath(geometry, ink);
            cursor = new Cursor(ToImage(bitmap), hotspot); IllustratedCursors[key] = cursor;
        }
        Cursor = cursor; return true;
    }
}

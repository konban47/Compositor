using Compositor.Core.IO;

namespace Compositor.Desktop;

internal static class ToolCatalog
{
    internal static bool IsBrush(Tool t) => t is Tool.Brush or Tool.Clone or Tool.Blur or Tool.Liquify or Tool.Smudge or Tool.Heal or Tool.HistoryBrush or Tool.PatternStamp or Tool.ArtHistory or Tool.Eraser or Tool.BackgroundEraser or Tool.Sharpen or Tool.AdjustmentBrush or Tool.Dodge or Tool.Burn or Tool.Sponge;
    internal static bool IsType(Tool t) => t is Tool.Type or Tool.VerticalType or Tool.TypeMask or Tool.VerticalTypeMask;
    internal static bool IsPen(Tool t) => t is Tool.Pen or Tool.FreeformPen or Tool.CurvaturePen;
    internal static bool IsPathEditor(Tool t) => t is Tool.Path or Tool.PathSelection or Tool.AddAnchor or Tool.DeleteAnchor or Tool.ConvertAnchor;
    internal static string[] IDs => Enum.GetNames<Tool>();
    internal static string? Help(Tool tool) => tool switch
    {
        Tool.PatternStamp => "Paint a repeating pattern; load an image in the options bar.",
        Tool.ArtHistory => "Paint stylized strokes from the selected history source.",
        Tool.Eraser => "Erase pixels inside the current selection.",
        Tool.BackgroundEraser => "Erase sampled colors; optionally protect the foreground color.",
        Tool.MagicEraser => "Click to erase matching colors using tolerance and contiguous limits.",
        Tool.Bucket => "Click to fill matching colors inside the current selection.",
        Tool.Sharpen => "Paint to increase local edge contrast.",
        Tool.Dodge => "Paint to lighten shadows, midtones or highlights.",
        Tool.Burn => "Paint to darken shadows, midtones or highlights.",
        Tool.Sponge => "Paint to increase or decrease color saturation.",
        Tool.AdjustmentBrush => "Paint an editable adjustment layer mask; choose New Adjustment for a separate effect.",
        Tool.Pen => "Click for corners, drag for curves; close at the first point or press Enter.",
        Tool.FreeformPen => "Drag to draw an editable curved path.",
        Tool.CurvaturePen => "Click to create smooth curves; Enter finishes the path.",
        Tool.AddAnchor => "Click near a path segment to split it without changing its shape.",
        Tool.DeleteAnchor => "Click an anchor point to remove it.",
        Tool.ConvertAnchor => "Click to convert corner/smooth points; drag to set curve handles.",
        Tool.VerticalType => "Click and type vertically; Ctrl+Enter commits the editable text.",
        Tool.TypeMask or Tool.VerticalTypeMask => "Type, then Ctrl+Enter to create a selection from the letters.",
        _ => null,
    };
    internal static bool IsShape(Tool tool) => tool is Tool.Shape or Tool.ShapeEllipse or Tool.Triangle or Tool.PolygonShape or Tool.Star or Tool.Line or Tool.CustomShape;
    internal static string Title(Tool tool) => tool switch
    {
        Tool.PatternStamp => "Pattern Stamp Tool",
        Tool.ArtHistory => "Art History Brush Tool",
        Tool.Eraser => "Eraser Tool",
        Tool.BackgroundEraser => "Background Eraser Tool",
        Tool.MagicEraser => "Magic Eraser Tool",
        Tool.Bucket => "Paint Bucket Tool",
        Tool.Sharpen => "Sharpen Tool",
        Tool.AdjustmentBrush => "Adjustment Brush Tool",
        Tool.Dodge => "Dodge Tool",
        Tool.Burn => "Burn Tool",
        Tool.Sponge => "Sponge Tool",
        Tool.Pen => "Pen Tool",
        Tool.FreeformPen => "Freeform Pen Tool",
        Tool.CurvaturePen => "Curvature Pen Tool",
        Tool.AddAnchor => "Add Anchor Point Tool",
        Tool.DeleteAnchor => "Delete Anchor Point Tool",
        Tool.ConvertAnchor => "Convert Point Tool",
        Tool.VerticalType => "Vertical Type Tool",
        Tool.VerticalTypeMask => "Vertical Type Mask Tool",
        Tool.TypeMask => "Horizontal Type Mask Tool",
        Tool.Pan => "Hand Tool", Tool.Move => "Move Tool", Tool.Marquee => "Rectangular Marquee Tool", Tool.Ellipse => "Elliptical Marquee Tool",
        Tool.Lasso => "Lasso Tool", Tool.Polygon => "Polygonal Lasso Tool", Tool.Wand => "Magic Wand Tool", Tool.Object => "Object Selection Tool",
        Tool.Brush => "Brush Tool", Tool.Clone => "Clone Stamp Tool", Tool.Blur => "Blur Tool", Tool.Liquify => "Liquify Tool", Tool.Smudge => "Smudge Tool",
        Tool.Heal => "Spot Healing Brush Tool", Tool.Eyedropper => "Eyedropper Tool", Tool.Type => "Type Tool", Tool.Crop => "Crop Tool",
        Tool.Shape => "Rectangle Tool", Tool.ShapeEllipse => "Ellipse Tool", Tool.Triangle => "Triangle Tool", Tool.PolygonShape => "Polygon Tool",
        Tool.Star => "Star Tool", Tool.Line => "Line Tool", Tool.CustomShape => "Custom Shape Tool",
        Tool.Gradient => "Gradient Tool", Tool.HistoryBrush => "History Brush Tool", Tool.Path => "Direct Selection Tool",
        Tool.PathSelection => "Path Selection Tool", Tool.RotateView => "Rotate View Tool", Tool.Zoom => "Zoom Tool", _ => tool.ToString(),
    };
    internal static string Shortcut(Tool tool) => tool switch
    {
        Tool.Pan => "Hand tool", Tool.Move => "Move / Transform tool", Tool.Marquee or Tool.Ellipse => "Marquee tool",
        Tool.Lasso or Tool.Polygon => "Lasso tool", Tool.Wand or Tool.Object => "Magic wand", Tool.Brush => "Brush tool",
        Tool.PatternStamp => "Clone Stamp", Tool.ArtHistory => "History brush tool",
        Tool.Eraser or Tool.BackgroundEraser or Tool.MagicEraser => "Eraser tools",
        Tool.Bucket => "Gradient tool", Tool.Sharpen => "Blur / Smudge / Liquify", Tool.AdjustmentBrush => "Adjustment brush tool",
        Tool.Dodge or Tool.Burn or Tool.Sponge => "Toning tools", Tool.Pen or Tool.FreeformPen or Tool.CurvaturePen or Tool.AddAnchor or Tool.DeleteAnchor or Tool.ConvertAnchor => "Pen tools",
        Tool.VerticalType or Tool.TypeMask or Tool.VerticalTypeMask => "Type tool",
        Tool.Clone => "Clone Stamp", Tool.Blur or Tool.Smudge or Tool.Liquify => "Blur / Smudge / Liquify", Tool.Heal => "Spot Healing",
        Tool.Eyedropper => "Eyedropper tool", Tool.Type => "Type tool", Tool.Crop => "Crop tool", Tool.Gradient => "Gradient tool",
        Tool.HistoryBrush => "History brush tool", Tool.Path or Tool.PathSelection => "Path selection tool", Tool.RotateView => "Rotate view tool",
        Tool.Zoom => "Zoom tool", _ => "Shape tool",
    };
    internal static ToolbarLayout Defaults() => new()
    {
        Groups = [
            ["Move"], ["Marquee", "Ellipse"], ["Lasso", "Polygon"], ["Wand", "Object"], ["Crop"], ["Eyedropper"],
            ["Brush"], ["Clone", "PatternStamp"], ["HistoryBrush", "ArtHistory"], ["Eraser", "BackgroundEraser", "MagicEraser"],
            ["Gradient", "Bucket"], ["Blur", "Sharpen", "Smudge", "Liquify"], ["Heal"], ["AdjustmentBrush"], ["Dodge", "Burn", "Sponge"],
            ["Pen", "FreeformPen", "CurvaturePen", "AddAnchor", "DeleteAnchor", "ConvertAnchor"], ["Type", "VerticalType", "VerticalTypeMask", "TypeMask"],
            ["PathSelection", "Path"], ["Shape", "ShapeEllipse", "Triangle", "PolygonShape", "Star", "Line", "CustomShape"],
            ["Pan", "RotateView"], ["Zoom"]
        ]
    };
    internal static ToolbarLayout Load()
    {
        try { return File.Exists(ToolbarLayout.DefaultPath) ? ToolbarLayout.Load(ToolbarLayout.DefaultPath, IDs) : Defaults(); }
        catch { return Defaults(); }
    }
}

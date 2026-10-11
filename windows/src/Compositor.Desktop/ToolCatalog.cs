using Compositor.Core.IO;

namespace Compositor.Desktop;

internal static class ToolCatalog
{
    internal static bool IsBrush(Tool t) => t is Tool.Brush or Tool.Clone or Tool.Blur or Tool.Liquify or Tool.Smudge or Tool.Heal or Tool.HistoryBrush or Tool.PatternStamp or Tool.ArtHistory or Tool.Eraser or Tool.BackgroundEraser or Tool.Sharpen or Tool.AdjustmentBrush or Tool.Dodge or Tool.Burn or Tool.Sponge or Tool.Pencil or Tool.ColorReplacement or Tool.MixerBrush or Tool.HealingBrush or Tool.Remove;
    internal static bool HasRoundCursor(Tool t) => IsBrush(t) || t is Tool.SelectionBrush or Tool.QuickSelection;
    internal static bool IsAnnotation(Tool t) => t is Tool.Slice or Tool.SliceSelect or Tool.ColorSampler or Tool.Ruler or Tool.Note or Tool.Count;
    internal static bool IsType(Tool t) => t is Tool.Type or Tool.VerticalType or Tool.TypeMask or Tool.VerticalTypeMask;
    internal static bool IsPen(Tool t) => t is Tool.Pen or Tool.FreeformPen or Tool.CurvaturePen;
    internal static bool IsPathEditor(Tool t) => t is Tool.Path or Tool.PathSelection or Tool.AddAnchor or Tool.DeleteAnchor or Tool.ConvertAnchor;
    internal static string[] IDs => Enum.GetNames<Tool>();
    internal static string? Help(Tool tool) => tool switch
    {
        Tool.SelectionBrush => "Paint a selection; Shift adds, Alt subtracts, Shift+Alt intersects.",
        Tool.QuickSelection => "Brush over color regions to grow an edge-bounded selection; Alt subtracts.",
        Tool.MagneticLasso => "Click to start, follow an edge, then Enter or double-click to close; Backspace removes points, Alt bypasses snapping.",
        Tool.PerspectiveCrop => "Drag a frame, adjust its four corners, then Enter to rectify and crop; Esc cancels.",
        Tool.Slice => "Drag a slice; use the options bar to divide, name or export PNG slices.",
        Tool.SliceSelect => "Select or drag a slice; drag its bottom-right corner to resize; double-click for properties.",
        Tool.Frame => "Drag a rectangular or elliptical frame, then place an image to fit inside it.",
        Tool.ColorSampler => "Click to add a color sampler, drag to move it; Alt-click or Delete removes it.",
        Tool.Ruler => "Drag to measure distance and angle; Straighten Layer removes the measured tilt.",
        Tool.Note => "Click to create a note; double-click to edit; drag to move; Delete removes it.",
        Tool.Count => "Click to count; Shift-drag moves a marker; Alt-click removes it. Groups can be hidden independently.",
        Tool.Remove => "Brush over an unwanted area to fill it from nearby image content inside the selection.",
        Tool.HealingBrush => "Alt-click to choose a source, then paint its texture while matching the destination color.",
        Tool.Patch => "Outline an area, then drag inside the selection to a clean source; Destination copies to the target.",
        Tool.ContentMove => "Outline an object, then drag inside the selection to move and fill the original area, or choose Extend.",
        Tool.RedEye => "Drag around a red pupil; adjust Pupil Size and Darken in the options bar.",
        Tool.Pencil => "Paint hard-edged strokes with the foreground color inside the selection.",
        Tool.ColorReplacement => "Paint over the starting color with the foreground hue; tolerance limits the colors replaced.",
        Tool.MixerBrush => "Mix the loaded foreground color with canvas colors; adjust Wet, Load, Mix and Flow.",
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
        Tool.SelectionBrush => "Selection Brush Tool",
        Tool.MagneticLasso => "Magnetic Lasso Tool",
        Tool.QuickSelection => "Quick Selection Tool",
        Tool.PerspectiveCrop => "Perspective Crop Tool",
        Tool.Slice => "Slice Tool",
        Tool.SliceSelect => "Slice Select Tool",
        Tool.Frame => "Frame Tool",
        Tool.ColorSampler => "Color Sampler Tool",
        Tool.Ruler => "Ruler Tool",
        Tool.Note => "Note Tool",
        Tool.Count => "Count Tool",
        Tool.Remove => "Remove Tool",
        Tool.HealingBrush => "Healing Brush Tool",
        Tool.Patch => "Patch Tool",
        Tool.ContentMove => "Content-Aware Move Tool",
        Tool.RedEye => "Red Eye Tool",
        Tool.Pencil => "Pencil Tool",
        Tool.ColorReplacement => "Color Replacement Tool",
        Tool.MixerBrush => "Mixer Brush Tool",
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
        Tool.SelectionBrush or Tool.MagneticLasso => "Lasso tool",
        Tool.QuickSelection => "Magic wand", Tool.PerspectiveCrop or Tool.Slice or Tool.SliceSelect => "Crop tool",
        Tool.Frame => "Frame tool", Tool.ColorSampler or Tool.Ruler or Tool.Note or Tool.Count => "Eyedropper tool",
        Tool.Remove or Tool.HealingBrush or Tool.Patch or Tool.ContentMove or Tool.RedEye => "Spot Healing",
        Tool.Pencil or Tool.ColorReplacement or Tool.MixerBrush => "Brush tool",
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
            ["Move"], ["Marquee", "Ellipse"], ["SelectionBrush", "Lasso", "Polygon", "MagneticLasso"], ["Object", "QuickSelection", "Wand"], ["Crop", "PerspectiveCrop", "Slice", "SliceSelect"], ["Frame"], ["Eyedropper", "ColorSampler", "Ruler", "Note", "Count"],
            ["Brush", "Pencil", "ColorReplacement", "MixerBrush"], ["Clone", "PatternStamp"], ["HistoryBrush", "ArtHistory"], ["Eraser", "BackgroundEraser", "MagicEraser"],
            ["Gradient", "Bucket"], ["Blur", "Sharpen", "Smudge", "Liquify"], ["Heal", "Remove", "HealingBrush", "Patch", "ContentMove", "RedEye"], ["AdjustmentBrush"], ["Dodge", "Burn", "Sponge"],
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

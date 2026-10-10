using Compositor.Core.IO;

namespace Compositor.Desktop;

internal static class ToolCatalog
{
    internal static string[] IDs => Enum.GetNames<Tool>();
    internal static bool IsShape(Tool tool) => tool is Tool.Shape or Tool.ShapeEllipse or Tool.Triangle or Tool.PolygonShape or Tool.Star or Tool.Line or Tool.CustomShape;
    internal static string Title(Tool tool) => tool switch
    {
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
        Tool.Clone => "Clone Stamp", Tool.Blur or Tool.Smudge or Tool.Liquify => "Blur / Smudge / Liquify", Tool.Heal => "Spot Healing",
        Tool.Eyedropper => "Eyedropper tool", Tool.Type => "Type tool", Tool.Crop => "Crop tool", Tool.Gradient => "Gradient tool",
        Tool.HistoryBrush => "History brush tool", Tool.Path or Tool.PathSelection => "Path selection tool", Tool.RotateView => "Rotate view tool",
        Tool.Zoom => "Zoom tool", _ => "Shape tool",
    };
    internal static ToolbarLayout Defaults() => new()
    {
        Groups = [
            ["Move"], ["Marquee", "Ellipse"], ["Lasso", "Polygon"], ["Wand", "Object"], ["Crop"], ["Eyedropper"],
            ["Brush"], ["Clone"], ["Blur", "Smudge", "Liquify"], ["Heal"], ["HistoryBrush"], ["Gradient"], ["Type"],
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

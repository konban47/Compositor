using Avalonia.Controls;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private MenuItem BuildToolsMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("_Tools") };
        void Group(string name, params Tool[] tools)
        {
            var group = new MenuItem { Header = Localize.Text(name) };
            foreach (var tool in tools) group.Items.Add(ToolItem(ToolCatalog.Title(tool), tool, ToolCatalog.Shortcut(tool)));
            menu.Items.Add(group);
        }
        Group("Move and View", Tool.Move, Tool.Artboard, Tool.Pan, Tool.RotateView, Tool.Zoom);
        Group("Selection Tools", Tool.Marquee, Tool.Ellipse, Tool.SelectionBrush, Tool.Lasso, Tool.Polygon,
            Tool.MagneticLasso, Tool.Object, Tool.QuickSelection, Tool.Wand);
        Group("Crop, Slice and Frame", Tool.Crop, Tool.PerspectiveCrop, Tool.Slice, Tool.SliceSelect, Tool.Frame);
        Group("Sampling and Annotations", Tool.Eyedropper, Tool.ColorSampler, Tool.Ruler, Tool.Note, Tool.Count);
        Group("Painting Tools", Tool.Brush, Tool.Pencil, Tool.ColorReplacement, Tool.MixerBrush, Tool.Clone,
            Tool.PatternStamp, Tool.HistoryBrush, Tool.ArtHistory, Tool.Gradient, Tool.Bucket);
        Group("Retouching Tools", Tool.Heal, Tool.Remove, Tool.HealingBrush, Tool.Patch, Tool.ContentMove, Tool.RedEye,
            Tool.Blur, Tool.Sharpen, Tool.Smudge, Tool.Liquify, Tool.AdjustmentBrush, Tool.Dodge, Tool.Burn, Tool.Sponge);
        Group("Eraser Tools", Tool.Eraser, Tool.BackgroundEraser, Tool.MagicEraser);
        Group("Paths and Shapes", Tool.Pen, Tool.FreeformPen, Tool.CurvaturePen, Tool.AddAnchor, Tool.DeleteAnchor,
            Tool.ConvertAnchor, Tool.PathSelection, Tool.Path, Tool.Shape, Tool.ShapeEllipse, Tool.Triangle,
            Tool.PolygonShape, Tool.Star, Tool.Line, Tool.CustomShape);
        Group("Type Tools", Tool.Type, Tool.VerticalType, Tool.TypeMask, Tool.VerticalTypeMask);
        menu.Items.Add(new Separator());
        var options = new MenuItem { Header = Localize.Text("Tool Options") };
        options.Items.Add(_gradientMenu); options.Items.Add(_shapeKinds); options.Items.Add(_cropRatios);
        options.Items.Add(Command("Shape Tool Options…", () => _ = ShapeOptions()));
        options.Items.Add(_paintOnMask); options.Items.Add(_eraseToggle);
        options.Items.Add(Command("_Size…", () => _ = SetBrush(BrushSetting.Size)));
        options.Items.Add(Command("_Hardness…", () => _ = SetBrush(BrushSetting.Hardness)));
        options.Items.Add(Command("_Opacity…", () => _ = SetBrush(BrushSetting.Opacity)));
        options.Items.Add(Command("_Color…", () => _ = SetBrush(BrushSetting.Colour)));
        menu.Items.Add(options);
        menu.Items.Add(Command("Quick Mask", ToggleQuickMask));
        menu.Items.Add(Command("Customize Toolbar…", () => _ = CustomizeToolbar()));
        menu.Items.Add(Command("Generative Workspace…", () => _ = OpenGenerativeWorkspace()));
        return menu;
    }
}

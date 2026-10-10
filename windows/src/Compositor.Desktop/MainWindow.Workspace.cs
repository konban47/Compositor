using Avalonia;
using Avalonia.Controls;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private int _polygonSides = 5;
    private double _starInner = .5;
    private string _customShape = ShapeTemplates.Normalize(ShapeTemplates.Presets["Heart"]);
    private void WireWorkspaceTools()
    {
        _rail.CustomizeRequested += () => _ = CustomizeToolbar();
        _rail.QuickMaskRequested += ToggleQuickMask;
        _rail.ScreenModeRequested += ShowScreenModes;
        _rail.GenerativeRequested += () => _ = OpenGenerativeWorkspace();
        _optionsBar.ShapeToolChosen += SetTool;
        _optionsBar.ShapeOptionsAsked += () => _ = ShapeOptions();
        _optionsBar.ResetViewAsked += () => _canvas.RotateViewTo(0);
    }
    private async Task CustomizeToolbar()
    {
        if (await new ToolbarDialog(_rail.Layout).ShowDialog<ToolbarLayout?>(this) is not { } layout) return;
        try { layout.Save(ToolbarLayout.DefaultPath, ToolCatalog.IDs); _rail.ApplyLayout(layout); _rail.ShowColours(BrushColour(), BackgroundColour()); _rail.ShowShortcuts(_keys); }
        catch (Exception e) { Say(Localize.Text("Could not save toolbar preset") + ": " + e.Message); }
    }
    private void ShowScreenModes()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Command("Standard Screen Mode", ExitScreenMode));
        menu.Items.Add(Command("Full Screen with Tools", () => { if (_canvasOnly) ToggleCanvasOnly(); if (!_editingFullscreen) ToggleEditingFullscreen(); }));
        menu.Items.Add(Command("Canvas Only", () => { if (!_canvasOnly) ToggleCanvasOnly(); }));
        menu.Open(_rail);
    }
    private bool ShortcutIsHidden(string command)
    {
        if (!_rail.Layout.DisableExtraShortcuts || !command.StartsWith(Shortcuts.Canvas + ":")) return false;
        var title = command[(Shortcuts.Canvas.Length + 1)..];
        var candidates = Enum.GetValues<Tool>().Where(t => ToolCatalog.Shortcut(t) == title).ToArray();
        return candidates.Length > 0 && candidates.All(t => _rail.Layout.Extras.Contains(t.ToString()));
    }
    private void PickShapeTool()
    {
        var allowed = Enum.GetValues<Tool>().Where(ToolCatalog.IsShape).Where(t => !_rail.Layout.DisableExtraShortcuts || !_rail.Layout.Extras.Contains(t.ToString())).ToArray();
        if (allowed.Length > 0) SetTool(allowed.Contains(_tool) ? _tool : allowed[0]);
    }
    private async Task ShapeOptions()
    {
        string? selected = PropertyLayer?.LiveShape?.Path;
        if (selected is null && _document?.Selection.Path is { IsEmpty: false } path) selected = path.ToSvgPathData();
        if (await new ShapeSettingsDialog(_polygonSides, _starInner, _customShape, selected).ShowDialog<ShapeSettingsDialog.Result?>(this) is not { } result) return;
        _polygonSides = result.Sides; _starInner = result.Inner; _customShape = result.Path;
        _canvas.InvalidateVisual();
    }
    private void ConfigureShapeTool(Tool tool)
    {
        _options.Shape = tool switch { Tool.Shape => ShapeKind.Rectangle, Tool.ShapeEllipse => ShapeKind.Ellipse, Tool.Line => ShapeKind.Line, _ => ShapeKind.Path };
    }
    private string ShapeToolPath() => _tool switch
    {
        Tool.Triangle => ShapeTemplates.Polygon(3), Tool.PolygonShape => ShapeTemplates.Polygon(_polygonSides),
        Tool.Star => ShapeTemplates.Polygon(_polygonSides, _starInner), _ => _customShape,
    };
    private void ToggleQuickMask()
    {
        if (_document is not { } document) return;
        CommitText();
        var existing = document.Channels.FirstOrDefault(c => c.IsTemporary);
        if (existing is null)
        {
            Edit("Enter Quick Mask", () =>
            {
                if (ChannelEdits.Add(document, Localize.Text("Quick Mask"), true) is not { } id) return false;
                var index = document.Channels.FindIndex(c => c.ID == id);
                document.Channels[index] = document.Channels[index] with { IsTemporary = true };
                _open.ActiveAlpha = id; document.Selection = DocumentSelection.All; return true;
            });
            _open.VisibleChannels = ColorChannels.RGB; SetTool(Tool.Brush);
        }
        else
        {
            Edit("Exit Quick Mask", () =>
            {
                ChannelEdits.Load(document, existing.Asset.Image);
                document.Channels.Remove(existing); return true;
            });
            _open.ActiveAlpha = null; _open.VisibleChannels = ColorChannels.RGB;
        }
        UpdateChannelView(); RefreshChannels();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Compositor.Core.Document;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void WireOptionMenus()
    {
        _optionsBar.SearchAsked += () => _ = FindCommand();
        _optionsBar.HomeAsked += () => { _canvas.Fit(); OpenInspector(0); };
        _optionsBar.MenuAsked += OpenOptionMenu;
        _optionsBar.AlignmentAsked += AlignProperties;
        _optionsBar.ArtboardAsked += ArtboardCommand;
        _optionsBar.TransformVisibilityAsked += on => { if (_transformShown != on) ShowTransformControls(); };
    }
    private void OpenOptionMenu(string name, Control anchor)
    {
        if (name == "Tool Presets") { OpenToolPresets(anchor); return; }
        if (name == "Alignment") { OpenAlignmentOptions(anchor); return; }
        var menu = new ContextMenu(); var p = EditorPreferences.Current;
        void Check(string title, bool value, Action<bool> set)
        { var item = Command(title, () => { set(!value); SavePreferenceChanges(); _canvas.InvalidateOverlay(); }); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = value; menu.Items.Add(item); }
        if (name == "Artboard Alignment")
        {
            foreach (var operation in new[] { "Align Left", "Align H Center", "Align Right", "Align Top", "Align V Center", "Align Bottom", "Distribute Horizontal Spacing", "Distribute Vertical Spacing" })
            {
                if (operation is "Align Top" or "Distribute Horizontal Spacing") menu.Items.Add(new Separator());
                var item = Command(operation, () => AlignProperties(operation)); item.Icon = new InspectorGlyph(operation) { Width = 18, Height = 18 };
                item.IsEnabled = _document is not null && SelectedLayers.Count >= (operation.StartsWith("Distribute", StringComparison.Ordinal) ? 3 : 1); menu.Items.Add(item);
            }
        }
        else if (name == "Move Settings")
        {
            Check("Show Layer Bounds on Hover", p.MoveHoverBounds, v => p.MoveHoverBounds = v);
            Check("Expand Layer Groups on Click", p.MoveExpandGroup, v => p.MoveExpandGroup = v);
            Check("Show Hover Bounds from Layers Panel", p.MoveLayerHoverBounds, v => p.MoveLayerHoverBounds = v);
        }
        else if (name == "Artboard Settings")
        {
            Check("Auto-Nest Layers", p.ArtboardAutoNest, v => p.ArtboardAutoNest = v);
            Check("Auto-Size Canvas", p.ArtboardAutoCanvas, v => p.ArtboardAutoCanvas = v);
            Check("Keep Relative Position When Reordering Layers", p.ArtboardKeepPosition, v => p.ArtboardKeepPosition = v);
            Check("Shrink Canvas on Save", p.ArtboardShrinkOnSave, v => p.ArtboardShrinkOnSave = v);
        }
        else if (name == "Workspace")
        {
            foreach (var workspace in new[] { "Essentials", "Graphic and Web", "Motion", "Painting", "Photography" }.Concat(p.Workspaces.Keys).Distinct())
            { var item = Command(workspace, () => ApplyWorkspace(workspace)); item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = p.ActiveWorkspace == workspace; menu.Items.Add(item); }
            menu.Items.Add(new Separator()); menu.Items.Add(Command("Reset Workspace", ResetWorkspace));
            menu.Items.Add(Command("New Workspace…", () => _ = SaveWorkspace())); menu.Items.Add(Command("Delete Workspace…", () => _ = DeleteWorkspace()));
        }
        menu.Open(anchor);
    }
    private void OpenAlignmentOptions(Control anchor)
    {
        var body = new StackPanel { Width = 280, Spacing = 7, Margin = new Thickness(8) };
        var actions = new List<(Button Button, string Operation)>();
        void UpdateEnabled()
        { foreach (var (button, operation) in actions) button.IsEnabled = _document is not null && SelectedLayers.Count >= (operation.StartsWith("Distribute", StringComparison.Ordinal) ? _alignmentTarget is AlignmentTarget.Canvas or AlignmentTarget.Selection ? 2 : 3 : 1); }
        void Row(params string[] operations)
        {
            var row = new UniformGrid { Columns = operations.Length };
            foreach (var operation in operations)
            {
                var b = InspectorButton(operation, operation, () => AlignProperties(operation));
                actions.Add((b, operation)); row.Children.Add(b);
            }
            body.Children.Add(row);
        }
        body.Children.Add(PropertyLabel("Align:")); Row("Align Left", "Align H Center", "Align Right", "Align Top", "Align V Center", "Align Bottom");
        body.Children.Add(new Separator()); body.Children.Add(PropertyLabel("Distribute:"));
        Row("Distribute Top", "Distribute Vertically", "Distribute Bottom", "Distribute Left", "Distribute Horizontally", "Distribute Right");
        body.Children.Add(new Separator()); body.Children.Add(PropertyLabel("Distribute Spacing:")); Row("Distribute Vertical Spacing", "Distribute Horizontal Spacing");
        var target = new ComboBox { ItemsSource = new[] { "Automatic", "Selected Layers", "Canvas", "Selection" }.Select(Localize.Text).ToArray(), SelectedIndex = (int)_alignmentTarget, Height = 26, Width = 160 };
        target.SelectionChanged += (_, _) => { if (target.SelectedIndex >= 0) _alignmentTarget = (AlignmentTarget)target.SelectedIndex; UpdateEnabled(); };
        body.Children.Add(target); UpdateEnabled(); new Flyout { Content = body, Placement = PlacementMode.BottomEdgeAlignedRight }.ShowAt(anchor);
    }
}

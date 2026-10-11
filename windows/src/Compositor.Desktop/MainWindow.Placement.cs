using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly Border _placementBar = new() { IsVisible = false, Height = 42, Background = Skin.ChromeBrush, Padding = new Thickness(8, 3) };
    private Tab? _placementTab;
    private CanvasDocument? _placementBefore;
    private Guid? _placementSelected;
    private HashSet<Guid> _placedIDs = [];
    private string _placeMode = "Scale";
    private PlacementMesh? _placeMesh;
    private ImageLayer? _placeBasis;
    private bool _placeUpdating, _placeLink = true, _placePivotVisible = true;
    private int _placePivot = 4, _placeGrab = -1;
    private SKPoint _placeStart;
    private SKPoint[] _placePoints = [];
    private (int Column, int Row) _placeSplit;
    private readonly Dictionary<string, NumericUpDown> _placeNumbers = [];
    private readonly List<Button> _placeAnchors = [];
    private Button? _placeLinkButton;

    private void InitializeEditing()
    {
        _canvas.CustomPressed = CanvasActionPressed;
        _canvas.CustomMoved = CanvasActionMoved;
        _canvas.CustomHover = (at, keys) => { if (_tool == Tool.MagneticLasso) ProfessionalMoved(at, keys); };
        _optionsBar.ProfessionalAsked += async action => await ProfessionalAction(action);
        _canvas.CustomReleased = CanvasActionReleased;
        _canvas.EditingOverlay = DrawEditingOverlay;
        var menu = new ContextMenu(); _canvas.ContextMenu = menu;
        menu.Opening += (_, _) => { menu.Items.Clear(); if (_placementTab is not null) BuildPlacementMenu(menu); else BuildCanvasMenu(menu); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        Button Button(string icon, string title, Action action)
        {
            var button = new Button { Content = new EditorIcon(icon), Width = 30, Height = 30, Padding = new Thickness(5), Tag = title };
            ToolTip.SetTip(button, Localize.Text(title)); button.Click += (_, _) => action(); row.Children.Add(button); return button;
        }
        Button("home", "Fit Canvas", _canvas.Fit);
        var anchor = new Grid { Width = 27, Height = 27, RowDefinitions = new RowDefinitions("*,*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        for (var i = 0; i < 9; i++)
        {
            var index = i; var dot = new Button { Padding = new Thickness(0), MinHeight = 0, Content = "·", Tag = "Reference Point" };
            ToolTip.SetTip(dot, Localize.Text("Reference Point")); Grid.SetRow(dot, i / 3); Grid.SetColumn(dot, i % 3);
            dot.Click += (_, _) => { _placePivot = index; UpdatePlacementBar(); _canvas.InvalidateVisual(); }; anchor.Children.Add(dot); _placeAnchors.Add(dot);
        }
        row.Children.Add(anchor);
        foreach (var name in new[] { "X", "Y", "W", "H", "Angle" })
        {
            row.Children.Add(new TextBlock { Text = name == "Angle" ? "∠" : name, VerticalAlignment = VerticalAlignment.Center });
            var number = new NumericUpDown { Width = 80, Height = 30, ShowButtonSpinner = false, Minimum = name is "W" or "H" ? .01m : -1000000m, Maximum = 1000000m,
                Increment = 1, FormatString = "0.##", Tag = "Place " + name };
            ToolTip.SetTip(number, Localize.Text(name is "W" or "H" ? "Scale in percent" : name == "Angle" ? "Rotation in degrees" : "Reference point position in pixels"));
            number.ValueChanged += (_, _) => { if (!_placeUpdating) ChangePlacementNumber(name, (double)(number.Value ?? 0)); };
            _placeNumbers[name] = number; row.Children.Add(number);
            row.Children.Add(new TextBlock { Text = name is "W" or "H" ? "%" : name == "Angle" ? "°" : Localize.Text("px"), Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center });
            if (name == "W") _placeLinkButton = Button("link", "Constrain Proportions", () => { _placeLink = !_placeLink; _canvas.TransformLockRatio = _placeLink; UpdatePlacementBar(); });
        }
        Button("warp", "Warp", () => SetPlacementMode(_placeMode == "Warp" ? "Scale" : "Warp"));
        Button("cancel", "Cancel Placement (Esc)", () => FinishPlacement(false));
        Button("check", "Place (Enter)", () => FinishPlacement(true));
        _placementBar.Child = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    private void BeginPlacement(Tab target, CanvasDocument before, Guid? selected, HashSet<Guid> ids)
    {
        if (!ReferenceEquals(_open, target)) Bring(target);
        _placementTab = target; _placementBefore = before; _placementSelected = selected; _placedIDs = ids;
        SetTool(Tool.Move); _transformShown = true; _canvas.ShowsTransformControls = true; _canvas.TransformLockRatio = _placeLink;
        Reselect(ids.Last()); _placeMode = "Scale"; _placeMesh = null; _placeBasis = null;
        _placementBar.IsVisible = true; _optionsBar.IsVisible = false; UpdatePlacementBar();
        Say("Place: transform the image, then Enter to confirm or Esc to cancel.");
    }
    private void FinishPlacement(bool apply)
    {
        if (_placementTab is not { } tab) return;
        var selected = Selected;
        if (!apply && _placementBefore is not null) tab.Document = _placementBefore;
        _placementTab = null; _placementBefore = null; _placeMesh = null; _placeBasis = null; _placedIDs.Clear(); _placeGrab = -1;
        tab.History.End(tab.Document, apply ? selected : _placementSelected);
        _placementBar.IsVisible = false; _optionsBar.IsVisible = true;
        if (ReferenceEquals(tab, _open)) { Show(tab); Reselect(apply ? selected : _placementSelected); }
        RefreshTabs(); _canvas.Focus();
    }
    private void SetPlacementMode(string mode)
    {
        _placeMode = mode; _placeMesh = null; _placeBasis = null;
        if (PropertyLayer is { Asset: not null } layer && mode is "Skew" or "Perspective" or "Distort" or "Warp")
        { _placeBasis = layer.Clone(); _placeMesh = new PlacementMesh(layer.Transform); }
        _canvas.TransformEnabled = _placeMesh is null; _canvas.InvalidateVisual();
        Say(Localize.Text(mode));
    }
    private void BuildPlacementMenu(ContextMenu menu)
    {
        void Add(string title, Action action, bool enabled = true)
        { var item = new MenuItem { Header = Localize.Text(title), IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("Place", () => FinishPlacement(true)); Add("Cancel", () => FinishPlacement(false)); menu.Items.Add(new Separator());
        foreach (var mode in new[] { "Scale", "Rotate", "Skew", "Perspective", "Distort" }) { var m = mode; Add(m, () => SetPlacementMode(m)); }
        menu.Items.Add(new Separator()); Add("Rotate Object", () => SetPlacementMode("Rotate"));
        foreach (var (title, degrees) in new[] { ("Rotate 180°", 180), ("Rotate 90° Clockwise", 90), ("Rotate 90° Counterclockwise", -90) })
            Add(title, () => RotatePlaced(degrees));
        menu.Items.Add(new Separator()); Add("Warp", () => SetPlacementMode("Warp"));
        Add("Split Warp Horizontally", () => SplitPlaced(true, false), _placeMode == "Warp");
        Add("Split Warp Vertically", () => SplitPlaced(false, true), _placeMode == "Warp");
        Add("Split Warp Crosswise", () => SplitPlaced(true, true), _placeMode == "Warp");
        Add("Remove Warp Split", () => { if (_placeMesh?.RemoveSplit(_placeSplit.Column, _placeSplit.Row) == true) RenderPlacementMesh(); }, _placeMode == "Warp");
        Add("Toggle Transform Anchor", () => { _placePivotVisible = !_placePivotVisible; _canvas.InvalidateVisual(); });
        Add("Toggle Guides", ShowGuides);
        menu.Items.Add(new Separator()); Add("Flip Horizontal", () => FlipPlaced(true)); Add("Flip Vertical", () => FlipPlaced(false));
    }
    private SKPoint PlacementPivot(LayerTransform t) => t.Point((_placePivot % 3) / 2.0, (_placePivot / 3) / 2.0);
    private void UpdatePlacementBar()
    {
        if (_placementTab is null || PropertyLayer is not { } layer) return;
        _placeUpdating = true;
        try
        {
            for (var i = 0; i < _placeAnchors.Count; i++) _placeAnchors[i].Background = i == _placePivot ? Skin.AccentBrush : Brushes.Transparent;
            if (_placeLinkButton is { } link) link.Background = _placeLink ? Skin.AccentBrush : Skin.SurfaceControlBrush;
            var pivot = PlacementPivot(layer.Transform);
            _placeNumbers["X"].Value = (decimal)pivot.X; _placeNumbers["Y"].Value = (decimal)pivot.Y;
            _placeNumbers["W"].Value = (decimal)(layer.Transform.Width / Math.Max(1, layer.Asset?.Width ?? 1) * 100);
            _placeNumbers["H"].Value = (decimal)(layer.Transform.Height / Math.Max(1, layer.Asset?.Height ?? 1) * 100);
            _placeNumbers["Angle"].Value = (decimal)layer.Transform.Rotation;
        }
        finally { _placeUpdating = false; }
    }
    private void ChangePlacementNumber(string name, double value)
    {
        if (_placementTab is null || PropertyLayer is not { } layer || !double.IsFinite(value)) return;
        var old = layer.Transform; var pivot = PlacementPivot(old); var next = old;
        switch (name)
        {
            case "X": next = old with { X = old.X + value - pivot.X }; break;
            case "Y": next = old with { Y = old.Y + value - pivot.Y }; break;
            case "Angle": RotatePlaced(value - old.Rotation); return;
            case "W": var width = Math.Max(.01, (layer.Asset?.Width ?? 1) * value / 100); next = old with { Width = width, Height = _placeLink ? old.Height * width / old.Width : old.Height }; break;
            case "H": var height = Math.Max(.01, (layer.Asset?.Height ?? 1) * value / 100); next = old with { Height = height, Width = _placeLink ? old.Width * height / old.Height : old.Width }; break;
        }
        if (name is "W" or "H") { var moved = PlacementPivot(next); next = next with { X = next.X + pivot.X - moved.X, Y = next.Y + pivot.Y - moved.Y }; }
        if (!next.IsValid) return;
        TransformEdits.SetPlacement(layer, next); SetPlacementMode("Scale"); Refresh(); UpdatePlacementBar();
    }
    private void RotatePlaced(double degrees)
    {
        if (PropertyLayer is not { } layer) return;
        var pivot = PlacementPivot(layer.Transform); var next = layer.Transform with { Rotation = layer.Transform.Rotation + degrees };
        var moved = PlacementPivot(next); next = next with { X = next.X + pivot.X - moved.X, Y = next.Y + pivot.Y - moved.Y };
        TransformEdits.SetPlacement(layer, next); SetPlacementMode("Scale"); Refresh(); UpdatePlacementBar();
    }
    private void FlipPlaced(bool horizontal)
    { Flip(horizontal, false); SetPlacementMode("Scale"); UpdatePlacementBar(); }
    private void SplitPlaced(bool horizontal, bool vertical)
    {
        if (_placeMesh is not { } mesh || _placeBasis is not { } basis) return;
        var point = basis.Transform.InBox(_canvas.ContextPoint);
        var u = point is { } p ? Math.Clamp(p.X / (float)basis.Transform.Width, .05f, .95f) : .5f;
        var v = point is { } q ? Math.Clamp(q.Y / (float)basis.Transform.Height, .05f, .95f) : .5f;
        if (horizontal) mesh.Split(true, (float)v); if (vertical) mesh.Split(false, (float)u);
        _placeSplit = (mesh.Columns.FindIndex(x => x == u), mesh.Rows.FindIndex(y => y == v)); _canvas.InvalidateVisual();
    }
    private bool PlacementPressed(SKPoint at)
    {
        if (_placementTab is null || PropertyLayer is not { } layer) return false;
        if (_placeMesh is null && _placeMode != "Rotate") return false;
        _placeStart = at; _placeGrab = -1;
        if (_placeMesh is { } mesh)
        {
            var nearest = mesh.Points.Select((p, i) => (i, Distance: SKPoint.Distance(at, p))).MinBy(p => p.Distance);
            _placeGrab = nearest.Distance <= 14 / _canvas.Zoom ? nearest.i : -1;
            _placePoints = mesh.Points.ToArray();
            if (_placeGrab >= 0) _placeSplit = (_placeGrab % mesh.Columns.Count, _placeGrab / mesh.Columns.Count);
        }
        else _placeBasis = layer.Clone();
        return true;
    }
    private void PlacementMoved(SKPoint at, KeyModifiers modifiers)
    {
        if (_placeMode == "Rotate" && _placeBasis is { } basis && PropertyLayer is { } target)
        {
            var pivot = PlacementPivot(basis.Transform);
            var degrees = (Math.Atan2(at.Y - pivot.Y, at.X - pivot.X) - Math.Atan2(_placeStart.Y - pivot.Y, _placeStart.X - pivot.X)) * 180 / Math.PI;
            if (modifiers.HasFlag(KeyModifiers.Shift)) degrees = Math.Round(degrees / 15) * 15;
            target.Transform = basis.Transform; var next = basis.Transform with { Rotation = basis.Transform.Rotation + degrees };
            var moved = PlacementPivot(next); target.Transform = next with { X = next.X + pivot.X - moved.X, Y = next.Y + pivot.Y - moved.Y }; Refresh(); UpdatePlacementBar(); return;
        }
        if (_placeMesh is not { } mesh || _placePoints.Length != mesh.Points.Count) return;
        var delta = at - _placeStart; mesh.Points.Clear(); mesh.Points.AddRange(_placePoints);
        if (_placeGrab < 0) for (var i = 0; i < mesh.Points.Count; i++) mesh.Points[i] += delta;
        else
        {
            if (_placeMode == "Skew") delta = Math.Abs(delta.X) > Math.Abs(delta.Y) ? new SKPoint(delta.X, 0) : new SKPoint(0, delta.Y);
            mesh.Points[_placeGrab] += delta;
            if (_placeMode is "Perspective" or "Skew")
            {
                var partner = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? _placeGrab ^ 1 : _placeGrab ^ 2;
                mesh.Points[partner] += _placeMode == "Perspective" ? PlacementMesh.Scale(delta, -1) : delta;
            }
        }
        RenderPlacementMesh();
    }
    private void RenderPlacementMesh()
    {
        if (_placeMesh is not { } mesh || _placeBasis is not { Asset: { } asset } basis || PropertyLayer is not { } layer) return;
        var warped = _placeMode == "Warp" ? mesh.Render(asset.Image)
            : DistortWarp.Warp(asset.Image, basis.Transform, [mesh.Points[0], mesh.Points[1], mesh.Points[3], mesh.Points[2]]);
        if (warped is null) return;
        layer.Asset = ImportedImage.Create(warped.Image, asset.Name); layer.Transform = warped.Transform; Refresh(); UpdatePlacementBar();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Compositor.Core.IO;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// The tool rail down the left of the canvas, as the Mac keeps its own: one button per tool, then the two
/// colours the brush and the background are set to, with a swap and a reset under them. The port has no SF
/// Symbols, so each tool's mark is drawn here from lines and shapes.
/// </summary>
internal sealed class ToolRail : Grid
{
    /// <summary>How wide the rail is, which is the Mac's own 56 points.</summary>
    private const double RailWidth = 56;

    private readonly Dictionary<Tool, Button> _buttons = [];
    private readonly Dictionary<Button, List<Tool>> _groups = [];
    private IReadOnlyDictionary<string, ShortcutChord> _shortcuts = new Dictionary<string, ShortcutChord>();
    private ToolbarLayout _layout = ToolCatalog.Defaults();
    public event Action? CustomizeRequested, QuickMaskRequested, ScreenModeRequested, GenerativeRequested;
    internal ToolbarLayout Layout => _layout;
    internal static Control Icon(Tool tool) => new EditorIcon(tool.ToString()) { Width = 22, Height = 22 };
    public void ApplyLayout(ToolbarLayout layout)
    {
        _layout = layout.Copy(); _buttons.Clear(); _groups.Clear(); Children.Clear();
        _foreground = new Glyph { Kind = Tool.Brush }; _background = new Glyph { Kind = Tool.Brush };
        _front = _back = null; ShowColours(_foregroundColour, _backgroundColour);
        RowDefinitions = new RowDefinitions("*,Auto");
        var tools = new ScrollViewer { Content = Tools(), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Children.Add(tools);
        var footer = new StackPanel();
        if (layout.ShowExtras)
        {
            var extra = new Button { Content = new InspectorGlyph("more"), Width = 44, Height = 28, Tag = "Extra Tools" };
            ToolTip.SetTip(extra, Localize.Text("Extra Tools / Customize Toolbar"));
            extra.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                foreach (var id in _layout.Extras)
                {
                    var tool = Enum.Parse<Tool>(id);
                    var item = new MenuItem { Header = Localize.Text(ToolCatalog.Title(tool)), Icon = Icon(tool) };
                    item.Click += (_, _) => Chosen?.Invoke(tool); menu.Items.Add(item);
                }
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                var customize = new MenuItem { Header = Localize.Text("Customize Toolbar…") };
                customize.Click += (_, _) => CustomizeRequested?.Invoke(); menu.Items.Add(customize); menu.Open(extra);
            };
            footer.Children.Add(extra);
        }
        if (layout.ShowColors) footer.Children.Add(Colours());
        void Footer(string icon, string name, Action fire)
        {
            var button = new Button { Content = icon == "mask" ? new PanelGlyph("mask") : new InspectorGlyph(icon), Width = 44, Height = 28, Tag = name, Padding = new Thickness(11, 3) };
            ToolTip.SetTip(button, Localize.Text(name)); button.Click += (_, _) => fire(); footer.Children.Add(button);
        }
        if (layout.ShowQuickMask) Footer("mask", "Quick Mask (Q)", () => QuickMaskRequested?.Invoke());
        if (layout.ShowScreenMode) Footer("screen", "Screen Mode (F)", () => ScreenModeRequested?.Invoke());
        if (layout.ShowGenerative) Footer("generate", "Generative Workspace", () => GenerativeRequested?.Invoke());
        SetRow(footer, 1); Children.Add(footer);
        Mark(_marked); ShowShortcuts(_shortcuts);
    }
    private Glyph _foreground = new() { Kind = Tool.Brush };
    private Glyph _background = new() { Kind = Tool.Brush };
    private Button? _front;
    private Button? _back;
    private Tool _marked = Tool.Pan;
    private SKColor _foregroundColour = SKColors.Black;
    private SKColor _backgroundColour = SKColors.White;

    /// <summary>A tool was picked from the rail.</summary>
    public event Action<Tool>? Chosen;

    /// <summary>The two colours were swapped.</summary>
    public event Action? ColoursSwapped;

    /// <summary>The two colours were put back to black and white.</summary>
    public event Action? ColoursReset;

    /// <summary>One of the swatches was clicked: true for the foreground, false for the background.</summary>
    public event Action<bool>? ColourChosen;

    public ToolRail()
    {
        Width = RailWidth;
        ApplyLayout(ToolCatalog.Load());
    }

    /// <summary>Marks the tool in hand, which is the only one lit.</summary>
    public void Mark(Tool tool)
    {
        _marked = tool;
        foreach (var (button, tools) in _groups)
        {
            var active = tools.Contains(tool);
            button.Background = active ? new SolidColorBrush(Color.Parse("#1FFFFFFF")) : Brushes.Transparent;
            button.BorderBrush = active ? new SolidColorBrush(Color.Parse("#24FFFFFF")) : Brushes.Transparent;
            button.BorderThickness = new Thickness(active ? 1 : 0);
            if (active) { button.Tag = tool; button.Content = GroupIcon(tool, tools.Count > 1); }
        }
        ShowShortcuts(_shortcuts);
    }

    /// <summary>
    /// The tools as one column with no scroll around them. A bitmap does not lay a scroll view's content out,
    /// so this is what the self check draws: the marks are drawn shapes, and a picture is the only way to look
    /// at them.
    /// </summary>
    internal Control TakeTools()
    {
        var column = Tools();
        column.Margin = new Thickness(4);
        return column;
    }

    /// <summary>The button the rail has marked, which is what the self check reads to see the two agree.</summary>
    internal Tool Marked => _marked;

    /// <summary>
    /// The button a tool is picked by, which the checks press with a pointer: the rail is inside a scroll view,
    /// so a click on it is the one thing that proves the marks are not merely drawn but reachable.
    /// </summary>
    internal Button? ButtonFor(Tool tool) => _buttons.TryGetValue(tool, out var button) ? button : null;

    public void ShowShortcuts(IReadOnlyDictionary<string, Compositor.Core.IO.ShortcutChord> shortcuts)
    {
        _shortcuts = shortcuts;
        foreach (var (button, _) in _groups)
        {
            var tool = button.Tag is Tool picked ? picked : Tool.Pan;
            var key = shortcuts.TryGetValue($"{Shortcuts.Canvas}:{ToolCatalog.Shortcut(tool)}", out var chord) ? chord.Label : "";
            var description = tool == Tool.Zoom ? "Zoom — click to zoom in; Alt-click to zoom out; double-click for 100%" : tool == Tool.RotateView ? "Rotate View — drag to rotate; Shift snaps to 15°; double-click to reset" : Names.GetValueOrDefault(tool, ToolCatalog.Title(tool));
            var parts = Localize.Text(description).Split('—', 2, StringSplitOptions.TrimEntries);
            var tip = Localize.Text(ToolCatalog.Title(tool)) + (key.Length > 0 ? $"({key})" : "")
                + (ToolCatalog.Help(tool) is { } help ? ": " + Localize.Text(help) : parts.Length > 1 ? ": " + parts[1] : "");
            ToolTip.SetTip(button, tip);
            Avalonia.Automation.AutomationProperties.SetName(button, tip);
        }
    }

    /// <summary>One of the two colour swatches, which is what a click there opens the picker through. True for
    /// the foreground. The self check is the only caller.</summary>
    internal Button? SwatchFor(bool foreground) => foreground ? _front : _back;

    /// <summary>Shows the two colours, as a swatch each.</summary>
    public void ShowColours(SKColor foreground, SKColor background)
    {
        _foregroundColour = foreground;
        _backgroundColour = background;
        _foreground.Fill = Colour(foreground);
        _background.Fill = Colour(background);
    }

    /// <summary>The two colours the rail is showing, as the brush and the background have them.</summary>
    internal (SKColor Foreground, SKColor Background) Palette => (_foregroundColour, _backgroundColour);

    private static IBrush Colour(SKColor colour) => new SolidColorBrush(
        Color.FromArgb(colour.Alpha, colour.Red, colour.Green, colour.Blue));

    private static Control GroupIcon(Tool tool, bool grouped)
    {
        var icon = new Grid { Width = 24, Height = 24 }; icon.Children.Add(Icon(tool));
        if (grouped) icon.Children.Add(new TextBlock { Text = "◢", FontSize = 7, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
        return icon;
    }
    private Control Tools()
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        foreach (var ids in _layout.Groups)
        {
            var group = ids.Select(Enum.Parse<Tool>).ToList(); if (group.Count == 0) continue;
            var active = group.Contains(Tool.Pan);
            var button = new Button
            {
                Content = GroupIcon(group[0], group.Count > 1),
                Tag = group[0],
                Width = 44,
                Height = 34,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = active ? Skin.SurfaceControlPressedBrush : Brushes.Transparent,
                BorderBrush = active ? Skin.AccentBrush : Brushes.Transparent,
                BorderThickness = new Thickness(active ? 1 : 0),
            };
            var heldOpen = false;
            void OpenGroup()
            {
                if (group.Count < 2) return;
                heldOpen = true; var menu = new ContextMenu();
                foreach (var tool in group)
                {
                    var item = new MenuItem { Header = Localize.Text(ToolCatalog.Title(tool)), Icon = Icon(tool) };
                    item.Click += (_, _) => Chosen?.Invoke(tool); menu.Items.Add(item);
                }
                menu.Open(button);
            }
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            hold.Tick += (_, _) => { hold.Stop(); OpenGroup(); };
            button.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(button).Properties.IsRightButtonPressed) { OpenGroup(); e.Handled = true; }
                else { heldOpen = false; hold.Start(); }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.AddHandler(PointerReleasedEvent, (_, _) => hold.Stop(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.PointerCaptureLost += (_, _) => hold.Stop();
            button.Click += (_, _) => { hold.Stop(); if (!heldOpen && button.Tag is Tool tool) Chosen?.Invoke(tool); heldOpen = false; };
            _groups[button] = group;
            foreach (var tool in group) _buttons[tool] = button;
            column.Children.Add(button);
        }
        return column;
    }

    /// <summary>
    /// The two swatches, overlapping as Photoshop draws them, with a swap and a reset under them. The foreground
    /// is the one in front, because it is the one that is painted with.
    /// </summary>
    private Control Colours()
    {
        var swatches = new Canvas { Width = RailWidth, Height = 44, Margin = new Thickness(0, 6, 0, 0) };
        var back = Swatch(_background, false);
        var front = Swatch(_foreground, true);
        Canvas.SetLeft(back, 20);
        Canvas.SetTop(back, 16);
        Canvas.SetLeft(front, 8);
        Canvas.SetTop(front, 4);
        swatches.Children.Add(back);
        swatches.Children.Add(front);
        var swap = Small("⇄", "Switch Foreground and Background Colors (X)");
        var reset = Small("◩", "Default Foreground and Background Colors (D)");
        swap.Click += (_, _) => ColoursSwapped?.Invoke();
        reset.Click += (_, _) => ColoursReset?.Invoke();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { swap, reset },
        };
        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children = { swatches, row },
        };
        return column;
    }

    private Button Swatch(Control glyph, bool foreground)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.White, 0.4),
        };
        ToolTip.SetTip(button, Localize.Text(foreground ? "Foreground color" : "Background color"));
        button.Click += (_, _) => ColourChosen?.Invoke(foreground);
        if (foreground) _front = button;
        else _back = button;
        return button;
    }

    private static Button Small(string text, string hint)
    {
        var button = new Button
        {
            Content = Localize.Text(text),
            Width = 20,
            Height = 18,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(button, Localize.Text(hint));
        return button;
    }

    /// <summary>What each button says it is, which is also what the Tools menu calls the tool.</summary>
    private static readonly Dictionary<Tool, string> Names = new()
    {
        [Tool.Pan] = "Pan — drag to scroll",
        [Tool.Move] = "Move — drag the layer, or a handle to scale and turn it",
        [Tool.Marquee] = "Marquee — drag a rectangle",
        [Tool.Ellipse] = "Elliptical marquee — drag an oval",
        [Tool.Lasso] = "Lasso — drag round a shape",
        [Tool.Polygon] = "Polygonal lasso — click each corner",
        [Tool.Object] = "Object Selection — click a subject",
        [Tool.Wand] = "Magic wand — click a color",
        [Tool.Brush] = "Brush",
        [Tool.Clone] = "Clone stamp — Alt-click a source first",
        [Tool.Blur] = "Blur brush",
        [Tool.Liquify] = "Liquify brush — push the pixels around",
        [Tool.Smudge] = "Smudge brush — drag the color along",
        [Tool.Heal] = "Spot healing",
        [Tool.Eyedropper] = "Eyedropper — click the canvas",
        [Tool.Type] = "Type — click where the text goes",
        [Tool.Crop] = "Crop — drag a frame, then apply it",
        [Tool.Shape] = "Shape — drag out a rectangle, ellipse or line",
        [Tool.Gradient] = "Gradient — drag the line it runs along",
        [Tool.HistoryBrush] = "History brush — paint back a chosen state",
        [Tool.Path] = "Path — drag the nodes of a vector outline",
    };

    /// <summary>
    /// One tool's mark, drawn rather than set in a font: the rail has no icon library behind it, so each is a
    /// few lines and shapes in a box of its own. A swatch uses the same control with its fill shown instead.
    /// </summary>
    private sealed class Glyph : Control
    {
        /// <summary>The box a mark is drawn in, centred in whatever the button gives it.</summary>
        private const double Side = 22;

        private static readonly IBrush Ink = Skin.LabelBrush;
        private static readonly IBrush Soft = new SolidColorBrush(Colors.White, 0.85);

        public Tool Kind { get; init; }

        /// <summary>What the swatches fill with, when this is a swatch rather than a tool.</summary>
        public IBrush? Fill { get; set; }

        /// <summary>
        /// A drawn control has no size of its own, and a content presenter hands it none unless it is told to
        /// fill: without this the mark is a control of no size and draws nothing.
        /// </summary>
        public Glyph()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (Fill is { } fill)
            {
                context.FillRectangle(fill, new Rect(size));
                return;
            }
            var ox = (size.Width - Side) / 2;
            var oy = (size.Height - Side) / 2;
            Point At(double x, double y) => new(ox + x, oy + y);
            var pen = new Pen(Ink, 1.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var thin = new Pen(Ink, 1.2) { LineCap = PenLineCap.Round };
            var dashed = new Pen(Ink, 1.2) { DashStyle = new DashStyle([2, 2], 0) };
            void Line(double x1, double y1, double x2, double y2) =>
                context.DrawLine(pen, At(x1, y1), At(x2, y2));

            switch (Kind)
            {
                case Tool.Pan:
                    // Photoshop Hand Tool: open hand with palm, thumb and four fingers
                    context.DrawGeometry(null, pen, Path([
                        (7, 19), (6, 13), (3, 11), (2.5, 9), (5, 9.5), (6.5, 6), (8.5, 6), (8.5, 10),
                        (9, 4), (11, 4), (11, 10), (11.5, 4.5), (13.5, 4.5), (13.5, 10), (14, 7), (16, 7), (16, 12), (15, 19)
                    ], At, close: true));
                    Line(7, 19, 15, 19);
                    break;
                case Tool.Move:
                    // Photoshop Move Tool: 4-directional solid arrow cross
                    Line(4, 11, 18, 11);
                    Line(11, 4, 11, 18);
                    context.DrawGeometry(Ink, null, Path([(11, 2), (8, 6), (14, 6)], At, close: true));
                    context.DrawGeometry(Ink, null, Path([(11, 20), (8, 16), (14, 16)], At, close: true));
                    context.DrawGeometry(Ink, null, Path([(2, 11), (6, 8), (6, 14)], At, close: true));
                    context.DrawGeometry(Ink, null, Path([(20, 11), (16, 8), (16, 14)], At, close: true));
                    context.FillRectangle(Ink, new Rect(At(10, 10), At(12, 12)));
                    break;
                case Tool.Marquee:
                    // Photoshop Rectangular Marquee
                    context.DrawRectangle(null, dashed, new Rect(At(3, 4), At(19, 18)));
                    break;
                case Tool.Ellipse:
                    // Photoshop Elliptical Marquee
                    context.DrawEllipse(null, dashed, At(11, 11), 8, 6);
                    break;
                case Tool.Lasso:
                    // Photoshop Lasso Tool: looped lasso rope with knot and tail
                    context.DrawGeometry(null, pen, Path([
                        (7, 13), (5, 9), (8, 4), (14, 4), (18, 8), (17, 13), (12, 15), (7, 13), (5, 16), (3, 19)
                    ], At, close: false));
                    Line(6, 12, 8, 15);
                    break;
                case Tool.Polygon:
                    // Photoshop Polygonal Lasso Tool: closed polygon with anchor vertices
                    context.DrawGeometry(null, pen, Path([(4, 16), (6, 5), (16, 4), (19, 13), (13, 18)], At, close: true));
                    foreach (var (px, py) in new[] { (4, 16), (6, 5), (16, 4), (19, 13), (13, 18) })
                        context.FillRectangle(Ink, new Rect(At(px - 1.2, py - 1.2), new Size(2.4, 2.4)));
                    break;
                case Tool.Object:
                    // Photoshop Object Selection Tool: marquee box with subject silhouette
                    context.DrawRectangle(null, dashed, new Rect(At(2, 2), At(20, 20)));
                    context.DrawEllipse(Ink, null, At(11, 8), 2.5, 2.5);
                    context.DrawGeometry(Ink, null, Path([(6, 17), (7, 13), (10, 12), (12, 12), (15, 13), (16, 17)], At, close: true));
                    break;
                case Tool.Wand:
                    // Photoshop Magic Wand Tool: diagonal wand with starburst sparkles
                    Line(3, 19, 12, 10);
                    context.DrawGeometry(Ink, null, Path([(10.5, 11.5), (12.5, 9.5), (13.5, 10.5), (11.5, 12.5)], At, close: true));
                    // 4-point sparkle star at wand tip
                    Line(17, 2, 17, 8);
                    Line(14, 5, 20, 5);
                    // Mini sparkles
                    Line(11, 2.5, 11, 5.5);
                    Line(9.5, 4, 12.5, 4);
                    Line(19, 9.5, 19, 12.5);
                    Line(17.5, 11, 20.5, 11);
                    break;
                case Tool.Brush:
                    // Photoshop Brush Tool: angled paintbrush with ferrule and curved bristles
                    Line(19, 3, 14, 8);
                    context.DrawGeometry(Ink, null, Path([(12, 7), (15, 10), (13.5, 11.5), (10.5, 8.5)], At, close: true));
                    context.DrawGeometry(Soft, pen, Path([(10.5, 8.5), (13.5, 11.5), (9, 16), (4, 18), (6, 13)], At, close: true));
                    break;
                case Tool.Clone:
                    // Photoshop Clone Stamp Tool: contoured rubber stamp with knob and base pad
                    context.DrawEllipse(Ink, null, At(11, 4), 3.5, 2.5);
                    Line(11, 6, 11, 9);
                    Line(8, 10, 14, 10);
                    context.DrawGeometry(null, pen, Path([(5, 16), (6, 11), (16, 11), (17, 16)], At, close: true));
                    Line(3, 18, 19, 18);
                    break;
                case Tool.Blur:
                    // Photoshop Blur Tool: smooth water teardrop with highlight
                    context.DrawGeometry(Soft, pen, Path([(11, 3), (16, 11), (16, 14), (13.5, 18), (8.5, 18), (6, 14), (6, 11)], At, close: true));
                    context.DrawGeometry(null, thin, Path([(8.5, 12), (8, 14), (9.5, 16)], At, close: false));
                    break;
                case Tool.Liquify:
                    // Photoshop Liquify Tool: warp distortion wave with center push arrow
                    context.DrawGeometry(null, pen, Path([(3, 10), (7, 6), (11, 13), (15, 6), (19, 10)], At, close: false));
                    context.DrawGeometry(null, pen, Path([(3, 15), (7, 11), (11, 18), (15, 11), (19, 15)], At, close: false));
                    Line(11, 6, 11, 13);
                    context.DrawGeometry(Ink, null, Path([(11, 15), (9, 12), (13, 12)], At, close: true));
                    break;
                case Tool.Smudge:
                    // Photoshop Smudge Tool: finger pushing down and dragging paint
                    context.DrawGeometry(null, pen, Path([(4, 15), (4, 9), (7, 6), (11, 6), (15, 10), (17, 13)], At, close: false));
                    Line(11, 6, 17, 12);
                    context.DrawEllipse(Ink, null, At(17, 12), 2, 2);
                    Line(13, 16, 18, 16);
                    Line(11, 18, 19, 18);
                    break;
                case Tool.Heal:
                    // Photoshop Spot Healing Brush: diagonal adhesive band-aid with perforated dots
                    context.DrawGeometry(Soft, pen, Path([(8, 3), (17, 12), (14, 18), (4, 9)], At, close: true));
                    Line(9, 7.5, 6.5, 10);
                    Line(14.5, 13, 12, 15.5);
                    context.DrawEllipse(Ink, null, At(10.5, 11.5), 1, 1);
                    context.DrawEllipse(Ink, null, At(8.5, 9.5), 0.8, 0.8);
                    context.DrawEllipse(Ink, null, At(12.5, 13.5), 0.8, 0.8);
                    break;
                case Tool.Eyedropper:
                    // Photoshop Eyedropper: angled pipette with rubber bulb, barrel and tip
                    context.DrawGeometry(Ink, null, Path([(15, 6), (17, 3), (19, 5), (16, 8)], At, close: true));
                    Line(15, 7, 9, 13);
                    Line(13, 9, 7, 15);
                    Line(14, 6, 16, 8);
                    Line(9, 13, 4, 18);
                    Line(7, 15, 4, 18);
                    context.FillRectangle(Ink, new Rect(At(3, 18), At(5, 20)));
                    break;
                case Tool.Type:
                    // Photoshop Type Tool: bold serif capital 'T'
                    Line(4, 5, 18, 5);
                    Line(4, 5, 4, 8);
                    Line(18, 5, 18, 8);
                    Line(11, 5, 11, 18);
                    Line(8, 18, 14, 18);
                    break;
                case Tool.Crop:
                    // Photoshop Crop Tool: two interlocking L-shaped cropping blades
                    Line(4, 7, 17, 7);
                    Line(7, 4, 7, 17);
                    Line(5, 15, 18, 15);
                    Line(15, 5, 15, 18);
                    break;
                case Tool.ShapeEllipse:
                    context.DrawEllipse(null, pen, At(11, 11), 8, 6); break;
                case Tool.Triangle:
                    context.DrawGeometry(null, pen, Path([(11,3),(20,19),(2,19)], At, true)); break;
                case Tool.PolygonShape:
                    context.DrawGeometry(null, pen, Path([(6,3),(16,3),(21,11),(16,19),(6,19),(1,11)], At, true)); break;
                case Tool.Star:
                    context.DrawGeometry(null, pen, Path([(11,2),(14,8),(21,8),(16,13),(18,20),(11,16),(4,20),(6,13),(1,8),(8,8)], At, true)); break;
                case Tool.Line: Line(3,19,19,3); break;
                case Tool.CustomShape:
                    context.DrawGeometry(null, pen, Path([(11,19),(2,10),(3,4),(7,2),(11,6),(15,2),(19,4),(20,10)], At, true)); break;
                case Tool.Zoom:
                    context.DrawEllipse(null, pen, At(9,9),6,6); Line(14,14,20,20); Line(6,9,12,9); Line(9,6,9,12); break;
                case Tool.RotateView:
                    context.DrawEllipse(null, pen, At(11,11),8,8); Line(2,2,2,8); Line(2,8,8,8); break;
                case Tool.PathSelection:
                    context.DrawGeometry(Soft, pen, Path([(4,2),(17,12),(11,13),(8,20)], At,true)); break;
                case Tool.Shape:
                    // Photoshop Shape Tool: vector rectangle with corner anchor handles
                    context.DrawRectangle(null, pen, new Rect(At(4, 5), At(18, 17)));
                    foreach (var (sx, sy) in new[] { (3, 4), (17, 4), (17, 16), (3, 16) })
                        context.FillRectangle(Ink, new Rect(At(sx, sy), new Size(2.5, 2.5)));
                    break;
                case Tool.Gradient:
                    // Photoshop Gradient Tool: gradient bar swatch with progressive steps
                    context.DrawRectangle(null, pen, new Rect(At(3, 6), At(19, 16)));
                    for (var step = 0; step < 6; step++)
                    {
                        var alpha = 0.12 + step * 0.16;
                        context.FillRectangle(new SolidColorBrush(Colors.White, alpha),
                            new Rect(At(4 + step * 2.3, 7), At(6.3 + step * 2.3, 15)));
                    }
                    break;
                case Tool.HistoryBrush:
                    // Photoshop History Brush Tool: paintbrush with circular rewind arrow
                    context.DrawGeometry(null, pen, Path([(6, 12), (5, 9), (8, 5), (14, 5), (17, 8)], At, close: false));
                    context.DrawGeometry(Ink, null, Path([(3, 10), (7, 13), (7, 9)], At, close: true));
                    Line(11, 11, 17, 17);
                    context.DrawGeometry(Soft, pen, Path([(8, 14), (11, 11), (12, 12), (9, 15)], At, close: true));
                    break;
                case Tool.Path:
                    // Photoshop Pen Tool: fountain pen nib with slit and breather hole
                    context.DrawGeometry(null, pen, Path([(11, 2), (16, 9), (14, 15), (8, 15), (6, 9)], At, close: true));
                    context.DrawEllipse(Ink, null, At(11, 10), 1.5, 1.5);
                    Line(11, 2, 11, 8.5);
                    Line(8, 17, 14, 17);
                    Line(8, 19, 14, 19);
                    break;
            }
        }

        /// <summary>A closed or open path through points given in the mark's own box.</summary>
        private static StreamGeometry Path((double X, double Y)[] points, Func<double, double, Point> at, bool close)
        {
            var geometry = new StreamGeometry();
            using var path = geometry.Open();
            path.BeginFigure(at(points[0].X, points[0].Y), close);
            for (var index = 1; index < points.Length; index++) path.LineTo(at(points[index].X, points[index].Y));
            path.EndFigure(close);
            return geometry;
        }
    }
}

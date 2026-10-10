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
    internal static Control Icon(Tool tool) => new Glyph { Kind = tool, Width = 22, Height = 22 };
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
            button.Background = tools.Contains(tool) ? Skin.TabFront : Brushes.Transparent;
            if (tools.Contains(tool)) { button.Tag = tool; button.Content = GroupIcon(tool, tools.Count > 1); }
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
                + (parts.Length > 1 ? ": " + parts[1] : "");
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
            var button = new Button { Content = GroupIcon(group[0], group.Count > 1), Tag = group[0], Width = 44, Height = 34, Padding = new Thickness(8, 5),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
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
    /// The two swatches, overlapping as the Mac draws them, with a swap and a reset under them. The foreground
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
        var swap = Small("⇄", "Swap the foreground and background colors");
        var reset = Small("↺", "Put them back to black and white");
        swap.Click += (_, _) => ColoursSwapped?.Invoke();
        reset.Click += (_, _) => ColoursReset?.Invoke();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
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
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.White, 0.35),
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
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
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
                    // A hand: a rounded palm with three fingers over it.
                    context.DrawRectangle(null, pen, new Rect(At(6, 9), At(17, 19)));
                    Line(8, 9, 8, 5);
                    Line(11, 9, 11, 4);
                    Line(14, 9, 14, 5);
                    Line(4, 12, 6, 10);
                    break;
                case Tool.Move:
                    Line(11, 3, 11, 19);
                    Line(3, 11, 19, 11);
                    Line(9, 5, 11, 3);
                    Line(13, 5, 11, 3);
                    Line(9, 17, 11, 19);
                    Line(13, 17, 11, 19);
                    Line(5, 9, 3, 11);
                    Line(5, 13, 3, 11);
                    Line(17, 9, 19, 11);
                    Line(17, 13, 19, 11);
                    break;
                case Tool.Marquee:
                    context.DrawRectangle(null, dashed, new Rect(At(4, 5), At(18, 17)));
                    break;
                case Tool.Ellipse:
                    context.DrawEllipse(null, dashed, At(11, 11), 7, 5.5);
                    break;
                case Tool.Lasso:
                    context.DrawEllipse(null, pen, At(11, 10), 6, 5);
                    Line(7, 14, 4, 18);
                    break;
                case Tool.Polygon:
                    context.DrawGeometry(null, pen, Path([(4, 17), (8, 5), (16, 5), (18, 16)], At, close: true));
                    break;
                case Tool.Object:
                case Tool.Wand:
                    Line(4, 18, 13, 9);
                    Line(16, 3, 16, 9);
                    Line(13, 6, 19, 6);
                    break;
                case Tool.Brush:
                    Line(5, 18, 13, 10);
                    context.DrawGeometry(Soft, null, Path([(12, 11), (17, 4), (18, 12)], At, close: true));
                    break;
                case Tool.Clone:
                    context.DrawRectangle(Soft, pen, new Rect(At(8, 3), At(14, 7)));
                    Line(4, 10, 18, 10);
                    context.DrawGeometry(null, pen, Path([(7, 11), (15, 11), (18, 19), (4, 19)], At, close: true));
                    break;
                case Tool.Blur:
                    context.DrawEllipse(Soft, pen, At(11, 14), 4.5, 4.5);
                    context.DrawGeometry(Soft, null, Path([(7, 12), (11, 4), (15, 12)], At, close: true));
                    break;
                case Tool.Liquify:
                    Line(3, 13, 7, 9);
                    Line(7, 9, 11, 13);
                    Line(11, 13, 15, 9);
                    Line(15, 9, 19, 13);
                    break;
                case Tool.Smudge:
                    Line(3, 16, 8, 10);
                    Line(8, 10, 13, 16);
                    Line(13, 16, 17, 10);
                    context.DrawEllipse(Soft, null, At(18, 9), 2.4, 2.4);
                    break;
                case Tool.Heal:
                    context.DrawEllipse(Soft, pen, At(11, 11), 7.5, 7.5);
                    context.DrawEllipse(null, thin, At(11, 11), 3, 3);
                    break;
                case Tool.Eyedropper:
                    Line(6, 18, 15, 9);
                    context.DrawGeometry(Soft, null, Path([(3, 19), (4, 15), (7, 18)], At, close: true));
                    Line(12, 6, 16, 10);
                    break;
                case Tool.Type:
                    Line(5, 5, 17, 5);
                    Line(11, 5, 11, 19);
                    break;
                case Tool.Crop:
                    Line(4, 7, 16, 7);
                    Line(16, 7, 16, 19);
                    Line(7, 4, 7, 16);
                    Line(7, 16, 19, 16);
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
                    context.DrawRectangle(null, pen, new Rect(At(4, 4), At(14, 14)));
                    context.DrawEllipse(null, pen, At(13, 13), 5, 5);
                    break;
                case Tool.Gradient:
                    context.DrawRectangle(null, pen, new Rect(At(4, 6), At(18, 16)));
                    for (var step = 0; step < 5; step++)
                    {
                        context.FillRectangle(new SolidColorBrush(Colors.White, 0.15 + step * 0.2),
                            new Rect(At(5 + step * 2.6, 7), At(6.6 + step * 2.6, 15)));
                    }
                    break;
                case Tool.HistoryBrush:
                    // A clock, for going back to an earlier moment.
                    context.DrawEllipse(null, pen, At(11, 11), 7, 7);
                    Line(11, 11, 11, 6);
                    Line(11, 11, 15, 13);
                    break;
                case Tool.Path:
                    context.DrawGeometry(null, pen, Path([(4, 17), (8, 6), (15, 8), (18, 17)], At, close: false));
                    context.FillRectangle(Ink, new Rect(At(2.5, 15.5), At(5.5, 18.5)));
                    context.FillRectangle(Ink, new Rect(At(12.5, 6.5), At(15.5, 9.5)));
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

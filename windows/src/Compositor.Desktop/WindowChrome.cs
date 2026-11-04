using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Compositor.Desktop;

/// <summary>
/// The Windows 11 title bar, shared by the editor and its dialogs. The window's content runs to its top edge, so nothing
/// of the system's frame shows above the caption buttons, which are 46 × 32, flush with the top and right edges and
/// square, the window's own rounded corner clipping the close button's red. Each caption part carries the role Windows
/// knows it by, so the maximize button offers Snap Layouts, and the bar drags, snaps and opens the system menu natively.
/// </summary>
internal static class WindowChrome
{
    internal const double Height = 32;
    private const int UseImmersiveDarkMode = 20, WindowCornerPreference = 33, BorderColor = 34, Rounded = 2;
    private const uint DefaultColor = 0xFFFFFFFF;
    /// <summary>A resizable border and a shadow from the system, and everything else, title bar included, drawn here.</summary>
    internal static void Frame(Window window)
    {
        window.WindowDecorations = WindowDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = Height;
    }
    internal static void ApplyNative(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || window.TryGetPlatformHandle() is not { } handle) return;
        var round = Rounded;
        _ = DwmSetWindowAttribute(handle.Handle, WindowCornerPreference, ref round, sizeof(int));
        // The system's own hairline around the window, in the theme's light or dark.
        var dark = EditorPreferences.Current.Theme < 2 ? 1 : 0;
        _ = DwmSetWindowAttribute(handle.Handle, UseImmersiveDarkMode, ref dark, sizeof(int));
        var border = unchecked((int)DefaultColor);
        _ = DwmSetWindowAttribute(handle.Handle, BorderColor, ref border, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static Control Buttons(Window window, bool full)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Height = Height };
        foreach (var symbol in full ? new[] { "Minimize", "Maximize / Restore", "Close" } : ["Close"])
        {
            var glyph = new CaptionGlyph(window, symbol) { Width = 10, Height = 10, IsHitTestVisible = false };
            var button = new Button { Content = glyph, Tag = "caption-" + symbol, Width = 46, Height = Height,
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), BorderBrush = Brushes.Transparent };
            WindowDecorationProperties.SetElementRole(button, symbol switch { "Close" => WindowDecorationsElementRole.CloseButton,
                "Minimize" => WindowDecorationsElementRole.MinimizeButton, _ => WindowDecorationsElementRole.MaximizeButton });
            ToolTip.SetTip(button, Localize.Text(symbol));
            Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(symbol));
            foreach (var state in new[] { ":pointerover", ":pressed" })
            {
                var fill = symbol == "Close" ? new SolidColorBrush(Color.Parse(state == ":pressed" ? "#B3261A" : "#C42B1C")) : state == ":pressed" ? Skin.CaptionPressedBrush : Skin.CaptionHoverBrush;
                button.Styles.Add(new Style(s => s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
                { Setters = { new Setter(ContentPresenter.BackgroundProperty, fill), new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent), new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(0)) } });
            }
            button.PointerEntered += (_, _) => { glyph.Hover = true; glyph.InvalidateVisual(); };
            button.PointerExited += (_, _) => { glyph.Hover = false; glyph.InvalidateVisual(); };
            button.Click += (_, _) => { if (symbol == "Close") window.Close(); else if (symbol == "Minimize") window.WindowState = WindowState.Minimized; else ToggleMaximize(window); };
            window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty || e.Property == WindowBase.IsActiveProperty) glyph.InvalidateVisual(); };
            buttons.Children.Add(button);
        }
        return buttons;
    }
    internal static void DragRegion(Window window, Control region)
    {
        WindowDecorationProperties.SetElementRole(region, WindowDecorationsElementRole.TitleBar);
        region.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(region).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && window.CanResize) ToggleMaximize(window); else window.BeginMoveDrag(e);
        };
    }
    private static void ToggleMaximize(Window window) => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    internal static Control Dialog(Window window, Control content, bool full = false)
    {
        var root = new DockPanel();
        var bar = new DockPanel { Height = Height, Background = Skin.ChromeBrush, Tag = "dialog-title-chrome" };
        var controls = Buttons(window, full); DockPanel.SetDock(controls, Dock.Right); bar.Children.Add(controls);
        var title = new TextBlock { Text = window.Title, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        window.PropertyChanged += (_, e) => { if (e.Property == Window.TitleProperty) title.Text = window.Title; };
        var grip = new Border { Background = Brushes.Transparent, Child = title }; DragRegion(window, grip); bar.Children.Add(grip);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); root.Children.Add(content); return root;
    }
    /// <summary>Windows 11's caption marks: ten pixels square, one-pixel lines, softly rounded, dimmed while the window
    /// is in the background.</summary>
    private sealed class CaptionGlyph(Window window, string symbol) : Control
    {
        internal bool Hover;
        public override void Render(DrawingContext context)
        {
            var ink = symbol == "Close" && Hover ? Brushes.White : window.IsActive || Hover ? Skin.LabelBrush : Skin.SecondaryBrush;
            var pen = new Pen(ink, 1) { LineCap = PenLineCap.Flat };
            if (symbol == "Minimize") context.DrawLine(pen, new Point(0, 5.5), new Point(10, 5.5));
            else if (symbol == "Close") { context.DrawLine(pen, new Point(.5, .5), new Point(9.5, 9.5)); context.DrawLine(pen, new Point(.5, 9.5), new Point(9.5, .5)); }
            else if (window.WindowState == WindowState.Maximized)
            {
                // The window behind shows only past the one in front, leaving the hover surface uncovered.
                context.DrawGeometry(null, pen, StreamGeometry.Parse("M2.5,.5 L8,.5 Q9.5,.5 9.5,2 L9.5,7.5"));
                context.DrawRectangle(null, pen, new Rect(.5, 2.5, 7, 7), 1.2, 1.2);
            }
            else context.DrawRectangle(null, pen, new Rect(.5, .5, 9, 9), 1.5, 1.5);
        }
    }
}

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Compositor.Desktop;

/// <summary>Shared caption geometry and theme for the editor and its dialogs.</summary>
internal static class WindowChrome
{
    internal const double Height = 32;
    internal static void ApplyNative(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || window.TryGetPlatformHandle() is not { } handle) return;
        var round = 2;
        _ = DwmSetWindowAttribute(handle.Handle, 33, ref round, sizeof(int));
        var color = Skin.Chrome.R | Skin.Chrome.G << 8 | Skin.Chrome.B << 16;
        _ = DwmSetWindowAttribute(handle.Handle, 34, ref color, sizeof(int));
        _ = DwmSetWindowAttribute(handle.Handle, 35, ref color, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static Control Buttons(Window window, bool full)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Height = Height };
        foreach (var symbol in full ? new[] { "Minimize", "Maximize / Restore", "Close" } : ["Close"])
        {
            var glyph = new CaptionGlyph(window, symbol) { Width = 14, Height = 14 };
            var button = new Button { Content = glyph, Tag = "caption-" + symbol, Width = 46, Height = Height,
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), BorderBrush = Brushes.Transparent };
            ToolTip.SetTip(button, Localize.Text(symbol));
            Avalonia.Automation.AutomationProperties.SetName(button, Localize.Text(symbol));
            foreach (var state in new[] { ":pointerover", ":pressed" })
            {
                var fill = symbol == "Close" ? new SolidColorBrush(Color.Parse(state == ":pressed" ? "#A92016" : "#C42B1C")) : state == ":pressed" ? Skin.CaptionPressedBrush : Skin.CaptionHoverBrush;
                button.Styles.Add(new Style(s => s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
                { Setters = { new Setter(ContentPresenter.BackgroundProperty, fill), new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent), new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(0)) } });
            }
            button.PointerEntered += (_, _) => { glyph.Hover = true; glyph.InvalidateVisual(); };
            button.PointerExited += (_, _) => { glyph.Hover = false; glyph.InvalidateVisual(); };
            button.Click += (_, _) => { if (symbol == "Close") window.Close(); else if (symbol == "Minimize") window.WindowState = WindowState.Minimized; else ToggleMaximize(window); };
            window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty) glyph.InvalidateVisual(); };
            buttons.Children.Add(button);
        }
        return buttons;
    }
    internal static void DragRegion(Window window, Control region)
    {
        region.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(region).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && window.CanResize) ToggleMaximize(window); else window.BeginMoveDrag(e);
        };
    }
    private static void ToggleMaximize(Window window) => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    internal static Control Dialog(Window window, Control content)
    {
        var root = new DockPanel();
        var bar = new DockPanel { Height = Height, Background = Skin.ChromeBrush, Tag = "dialog-title-chrome" };
        var controls = Buttons(window, false); DockPanel.SetDock(controls, Dock.Right); bar.Children.Add(controls);
        var title = new TextBlock { Text = window.Title, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        window.PropertyChanged += (_, e) => { if (e.Property == Window.TitleProperty) title.Text = window.Title; };
        var grip = new Border { Background = Brushes.Transparent, Child = title }; DragRegion(window, grip); bar.Children.Add(grip);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); root.Children.Add(content); return root;
    }
    private sealed class CaptionGlyph(Window window, string symbol) : Control
    {
        internal bool Hover;
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(symbol == "Close" && Hover ? Brushes.White : Skin.LabelBrush, 1);
            if (symbol == "Minimize") context.DrawLine(pen, new Point(2, 7.5), new Point(12, 7.5));
            else if (symbol == "Close") { context.DrawLine(pen, new Point(1.5, 1.5), new Point(12.5, 12.5)); context.DrawLine(pen, new Point(1.5, 12.5), new Point(12.5, 1.5)); }
            else if (window.WindowState == WindowState.Maximized)
            {
                // Outline both windows without covering the hover surface with a chrome-colored patch.
                context.DrawLine(pen, new(4.5, 1.5), new(12.5, 1.5)); context.DrawLine(pen, new(12.5, 1.5), new(12.5, 9.5)); context.DrawLine(pen, new(10.5, 9.5), new(12.5, 9.5)); context.DrawLine(pen, new(4.5, 1.5), new(4.5, 3.5));
                context.DrawRectangle(null, pen, new Rect(1.5, 4.5, 8, 8), .7, .7);
            }
            else context.DrawRectangle(null, pen, new Rect(2, 2, 10, 10), 1, 1);
        }
    }
}

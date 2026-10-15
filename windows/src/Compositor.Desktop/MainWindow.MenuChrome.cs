using Avalonia;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    internal void RestoreNativeWindowCorners()
    {
        // Keep the same native resizable frame and rounded DWM corners as the original window.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || TryGetPlatformHandle() is not { } handle) return;
        var round = 2;
        _ = DwmSetWindowAttribute(handle.Handle, 33, ref round, sizeof(int));
        var color = Skin.Chrome.R | Skin.Chrome.G << 8 | Skin.Chrome.B << 16;
        _ = DwmSetWindowAttribute(handle.Handle, 34, ref color, sizeof(int));
        _ = DwmSetWindowAttribute(handle.Handle, 35, ref color, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private Control MenuChrome(Menu menu)
    {
        // Keep tool commands available in Edit and in command search, while matching the compact menu order.
        var items = menu.Items.OfType<MenuItem>().ToArray();
        string Name(MenuItem item) => Localize.English((item.Header?.ToString() ?? "").Replace("_", ""));
        var toolMenu = items.FirstOrDefault(i => Name(i) == "Tools"); var edit = items.FirstOrDefault(i => Name(i) == "Edit");
        menu.Items.Clear();
        foreach (var name in new[] { "File", "Edit", "Image", "Layer", "Type", "Select", "Filter", "View", "Plugins", "Window", "Help" })
            if (items.FirstOrDefault(i => Name(i) == name) is { } item) menu.Items.Add(item);
        foreach (var item in items.Where(i => !ReferenceEquals(i, toolMenu) && !menu.Items.Contains(i))) menu.Items.Add(item);
        if (toolMenu is not null && edit is not null) { edit.Items.Add(new Separator()); edit.Items.Add(toolMenu); }
        menu.FontSize = 12; menu.Background = Skin.ChromeBrush;
        var bar = new DockPanel { Height = 28, Background = Skin.ChromeBrush, Tag = "main-menu-chrome" };
        var brand = new Image { Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Compositor/Assets/compositor-logo.png"))), Width = 20, Height = 20,
            Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "project-logo" };
        ToolTip.SetTip(brand, "Compositor"); DockPanel.SetDock(brand, Dock.Left); bar.Children.Add(brand);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (symbol, title, action) in new (string, string, Action)[] {
            ("−", "Minimize", () => WindowState = WindowState.Minimized), ("□", "Maximize / Restore", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized), ("×", "Close", Close) })
        {
            var glyph = new CaptionGlyph(symbol) { Width = 15, Height = 15 };
            var button = new Button { Content = glyph, Tag = "caption-" + title, Width = 44, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            if (symbol == "□") PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) { glyph.Restored = WindowState == WindowState.Maximized; glyph.InvalidateVisual(); } };
            if (symbol == "×") { button.Classes.Add("window-close"); button.Styles.Add(new Avalonia.Styling.Style(s => s.OfType<Button>().Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Name("PART_ContentPresenter")) { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.Parse("#E81123"))) } }); }
            ToolTip.SetTip(button, Localize.Text(title)); button.Click += (_, _) => action(); controls.Children.Add(button);
        }
        DockPanel.SetDock(controls, Dock.Right); bar.Children.Add(controls);
        DockPanel.SetDock(menu, Dock.Left); bar.Children.Add(menu);
        var grip = new Border { Background = Brushes.Transparent };
        grip.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return; if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else BeginMoveDrag(e); };
        bar.Children.Add(grip); return bar;
    }
    private sealed class CaptionGlyph(string symbol) : Control
    {
        internal bool Restored;
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(Skin.LabelBrush, 1.4);
            if (symbol == "−") context.DrawLine(pen, new Point(1, 8), new Point(14, 8));
            else if (symbol == "×") { context.DrawLine(pen, new Point(2, 2), new Point(13, 13)); context.DrawLine(pen, new Point(2, 13), new Point(13, 2)); }
            else if (Restored) { context.DrawRectangle(null, pen, new Rect(4, 1, 10, 10)); context.DrawRectangle(Skin.ChromeBrush, pen, new Rect(1, 4, 10, 10)); }
            else context.DrawRectangle(null, pen, new Rect(2, 2, 11, 11));
        }
    }
}

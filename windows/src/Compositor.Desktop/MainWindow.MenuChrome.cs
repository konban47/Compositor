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
    internal void RestoreNativeWindowCorners() => WindowChrome.ApplyNative(this);
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
        menu.FontSize = 12; menu.Height = 28; menu.VerticalAlignment = VerticalAlignment.Center; menu.Background = Skin.ChromeBrush;
        var bar = new DockPanel { Height = WindowChrome.Height, Background = Skin.ChromeBrush, Tag = "main-menu-chrome" };
        var brand = new Image { Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Compositor/Assets/compositor-logo.png"))), Width = 20, Height = 20,
            Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "project-logo" };
        ToolTip.SetTip(brand, "Compositor"); DockPanel.SetDock(brand, Dock.Left); bar.Children.Add(brand);
        var controls = WindowChrome.Buttons(this, true);
        DockPanel.SetDock(controls, Dock.Right); bar.Children.Add(controls);
        DockPanel.SetDock(menu, Dock.Left); bar.Children.Add(menu);
        var grip = new Border { Background = Brushes.Transparent };
        WindowChrome.DragRegion(this, grip);
        bar.Children.Add(grip); return bar;
    }
}

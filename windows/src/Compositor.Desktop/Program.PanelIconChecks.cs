using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void PanelIconSelfCheck(string output)
    {
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Pump() { Dispatcher.UIThread.RunJobs(); UpdateLayout(); foreach (var w in OwnedWindows) w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        var names = WindowPanelNames.Concat(new[] { "Plugins", "Tools", "Options", "Contextual Task Bar" }).Distinct().ToArray();
        Check(names.Length == 35 && names.All(DockPanelGlyph.HasSymbol), "Missing collapsed panel artwork.");
        Check(DockPanelGlyph.Symbols.Values.Distinct().Count() == names.Length, "Collapsed panels share duplicate artwork.");
        foreach (var name in new[] { "Tools", "Options", "Contextual Task Bar" })
        {
            var entry = _dockPanels[name]; FloatPanel(entry); Pump();
            var width = entry.Floating!.Width; var height = entry.Floating.Height;
            ToggleToolCollapse(entry); Pump();
            Check(entry.ToolCollapsed && entry.Floating.Height == 56 && TopLevel.GetTopLevel(entry.Body) is null, "Tool did not collapse: " + name);
            var layout = CaptureDockWorkspace(); RestoreDockWorkspace(layout); Pump();
            Check(entry.ToolCollapsed && entry.Floating is not null, "Workspace lost collapsed tool: " + name);
            var button = entry.Floating!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "panel-icon-" + name));
            Check(button.Content is DockPanelGlyph, "Collapsed tool has no dedicated icon: " + name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!entry.ToolCollapsed && entry.Body.IsEffectivelyVisible && entry.Floating!.Width == width && entry.Floating.Height == height, "Icon did not restore tool dimensions: " + name);
            DockToolBack(entry);
        }
        var original = EditorPreferences.Current.Theme;
        foreach (var theme in new[] { 0, 2 })
        {
            EditorPreferences.Current.Theme = theme; ApplyPreferences();
            var grid = new Grid { ColumnDefinitions = new("*,*,*,*,*"), RowDefinitions = new("*,*,*,*,*,*,*"), Margin = new Thickness(12) };
            for (var i = 0; i < names.Length; i++)
            {
                var glyphs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, Height = 36 };
                foreach (var size in new[] { 16, 22, 32 }) glyphs.Children.Add(new DockPanelGlyph(names[i]) { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center });
                var cell = new StackPanel { Spacing = 8, Margin = new Thickness(6) };
                cell.Children.Add(glyphs); cell.Children.Add(new TextBlock { Text = Localize.Text(names[i]), FontSize = 12, Foreground = Skin.LabelBrush, HorizontalAlignment = HorizontalAlignment.Center });
                Grid.SetColumn(cell, i % 5); Grid.SetRow(cell, i / 5); grid.Children.Add(cell);
            }
            var gallery = new Window { Width = 880, Height = 640, Background = Skin.SurfaceDarkBrush, Content = grid, WindowDecorations = WindowDecorations.None };
            gallery.Show(this); Pump(); using var frame = gallery.CaptureRenderedFrame(); frame!.Save(Path.ChangeExtension(output, "panel-icons-" + theme + ".png"), new PngBitmapEncoderOptions()); gallery.Close();
        }
        EditorPreferences.Current.Theme = original; ApplyPreferences(); ResetDockPanels(); Pump();
    }
}

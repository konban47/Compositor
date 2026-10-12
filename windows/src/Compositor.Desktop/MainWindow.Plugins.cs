using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private TabItem? _pluginTab;
    private readonly StackPanel _pluginList = new() { Spacing = 12, Margin = new Thickness(12) };
    private bool _pluginRunning;
    private MenuItem BuildPluginsMenu()
    {
        var menu = new MenuItem { Header = Localize.Text("_Plugins") };
        menu.Items.Add(Command("Plugins Panel", OpenPluginsPanel)); menu.Items.Add(Command("Manage Plugins…", () => _ = ManagePlugins()));
        menu.SubmenuOpened += (_, _) =>
        {
            while (menu.Items.Count > 2) menu.Items.RemoveAt(menu.Items.Count - 1);
            foreach (var plugin in PluginHost.List().Where(p => p.Enabled))
            {
                var sub = new MenuItem { Header = plugin.Manifest.Name };
                foreach (var command in plugin.Manifest.Commands) sub.Items.Add(Command(command.Title, () => _ = RunPlugin(plugin, command)));
                menu.Items.Add(sub);
            }
        };
        return menu;
    }
    private void OpenPluginsPanel()
    {
        _layersSide.IsVisible = true;
        if (_pluginTab is null)
        {
            var panel = new DockPanel(); var header = new DockPanel { Margin = new Thickness(8) };
            var options = InspectorButton("more", "Plugin Panel Options", () => { }); options.HorizontalAlignment = HorizontalAlignment.Right;
            options.Click += (_, _) =>
            {
                var context = new ContextMenu(); context.Items.Add(Command("Manage Plugins…", () => _ = ManagePlugins()));
                context.Items.Add(new Separator()); context.Items.Add(Command("Close", ClosePluginsPanel));
                context.Items.Add(Command("Close Tab Group", () => { ClosePluginsPanel(); _layersSide.IsVisible = false; })); context.Open(options);
            };
            header.Children.Add(options); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
            panel.Children.Add(new ScrollViewer { Content = _pluginList });
            _pluginTab = new TabItem { Header = Localize.Text("Plugins"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Content = panel }; _inspectorTabs.Items.Add(_pluginTab);
        }
        _inspectorTabs.SelectedItem = _pluginTab; RefreshPluginPanel();
    }
    private void ClosePluginsPanel()
    { if (_pluginTab is not null) _inspectorTabs.Items.Remove(_pluginTab); _pluginTab = null; _inspectorTabs.SelectedIndex = 0; }
    private void RefreshPluginPanel()
    {
        _pluginList.Children.Clear(); var plugins = PluginHost.List();
        if (plugins.Count == 0)
        {
            _pluginList.Children.Add(new InspectorGlyph("plugin") { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 35, 0, 10) });
            _pluginList.Children.Add(new TextBlock { Text = Localize.Text("Install plugins on this device and access their tools from this panel."), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            _pluginList.Children.Add(PropertyAction("Manage Plugins…", () => _ = ManagePlugins())); return;
        }
        foreach (var plugin in plugins)
        {
            _pluginList.Children.Add(new TextBlock { Text = plugin.Manifest.Name + " · " + plugin.Manifest.Version, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _pluginList.Children.Add(new TextBlock { Text = plugin.Manifest.Description, TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush });
            foreach (var command in plugin.Manifest.Commands)
            { var button = PropertyAction(command.Title, () => _ = RunPlugin(plugin, command)); button.IsEnabled = plugin.Enabled && !_pluginRunning; _pluginList.Children.Add(button); }
        }
    }
    private async Task ManagePlugins()
    {
        var dialog = new FormDialog("Manage Plugins", 740, 620); var list = new ListBox { MinHeight = 190 };
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap }; var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        dialog.Fields.Children.Add(list); dialog.Fields.Children.Add(detail); dialog.Fields.Children.Add(status);
        IReadOnlyList<InstalledPlugin> plugins = []; void Fill() { plugins = PluginHost.List(); list.ItemsSource = plugins.Select(p => $"{p.Manifest.Name}  {p.Manifest.Version} — {Localize.Text(p.Enabled ? "Enabled" : "Disabled")}").ToArray(); }
        list.SelectionChanged += (_, _) => detail.Text = list.SelectedIndex < 0 ? "" : plugins[list.SelectedIndex].Manifest.Description + "\n" + plugins[list.SelectedIndex].Manifest.Author;
        Fill(); dialog.Description("Plugins run as separate programs with your account permissions. Install packages you trust. Photoshop UXP, CEP and 8BF packages use different APIs.");
        dialog.Fields.Children.Add(FormDialog.ActionButton("Install Plugin…", async () =>
        {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [new FilePickerFileType("Compositor plugin manifest") { Patterns = ["compositor-plugin.json"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            try { PluginHost.Install(Path.GetDirectoryName(path)!); Fill(); status.Text = Localize.Text("Plugin installed. Select it and enable it to show its commands."); }
            catch (Exception e) { status.Text = e.Message; }
        }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Enable / Disable", () => { if (list.SelectedIndex >= 0) try { var plugin = plugins[list.SelectedIndex]; PluginHost.Enable(plugin, !plugin.Enabled); Fill(); } catch (Exception e) { status.Text = e.Message; } }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Remove Plugin", () => { if (list.SelectedIndex >= 0) try { PluginHost.Remove(plugins[list.SelectedIndex]); Fill(); } catch (Exception e) { status.Text = e.Message; } }));
        dialog.Fields.Children.Add(FormDialog.ActionButton("Refresh", Fill)); await dialog.ShowDialog<bool>(this); RefreshPluginPanel();
    }
    private async Task RunPlugin(InstalledPlugin plugin, PluginCommand command)
    {
        if (_pluginRunning || _document is not { } document) return; CommitText();
        var selected = PropertyLayer; var revision = _history.CurrentRevision;
        if (command.Result == "replace" && (selected?.Asset is null || !LayerProtection.CanPaint(document, selected.ID))) { Say("Select an unlocked pixel layer first."); return; }
        byte[] input; int width, height; int depth;
        if (selected?.Asset is { } asset) { input = asset.Deep is { } deep ? DeepImageCodec.EncodePng(deep) : PngCodec.Encode(asset.Image); width = asset.Width; height = asset.Height; depth = Math.Min(16, asset.BitDepth); }
        else { using var bitmap = DocumentRenderer.Render(document); input = PngCodec.Encode(bitmap); width = bitmap.Width; height = bitmap.Height; depth = 8; }
        _pluginRunning = true; RefreshPluginPanel(); Say("Running plugin…");
        try
        {
            var result = await PluginHost.Run(plugin, command.ID, input, width, height, selected?.ID, CultureInfo.CurrentUICulture.Name, depth);
            if (!ReferenceEquals(document, _document) || revision != _history.CurrentRevision) { Say("The document changed while the plugin was running. Its result was not applied."); return; }
            if (result.Image is { } bytes && command.Result != "message")
            {
                var pixels = DeepImageCodec.Decode(bytes, result.Response.Name ?? plugin.Manifest.Name);
                if (command.Result == "replace")
                { using (pixels) { if (pixels.Width != width || pixels.Height != height) throw new InvalidDataException("Replacement dimensions must match the layer."); Edit(command.Title, () => ImageAdjustments.SetPixels(document, selected!, pixels.WorkingImage)); } }
                else Edit(command.Title, () =>
                {
                    if (document.Layers.Count >= LayerPlacement.MaxLayers || document.Layers.Sum(l => (long)(l.Asset?.Width ?? 0) * (l.Asset?.Height ?? 0)) + (long)pixels.Width * pixels.Height > DocumentLimits.DocumentPixelBudget) { pixels.Dispose(); return false; }
                    var layer = ImageImporter.Layer(pixels); if (selected is not null) layer.Transform = selected.Transform; document.Layers.Add(layer); return true;
                });
            }
            Say(result.Response.Message ?? "Plugin completed.");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { Say(Localize.Text("Plugin failed.") + " " + e.Message); }
        finally { _pluginRunning = false; RefreshPluginPanel(); }
    }
}

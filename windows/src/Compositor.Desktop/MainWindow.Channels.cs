using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly ListBox _channels = new();
    private bool _showingChannels;
    private CanvasDocument? _channelDocument;
    private Guid _channelRevision;
    private readonly List<WriteableBitmap> _channelImages = [];
    private SKBitmap? _channelThumbnail;

    private Control BuildChannelsPanel()
    {
        var panel = new DockPanel();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
        footer.Children.Add(PanelButton("selection", "Load Channel as Selection", LoadChannelSelection));
        footer.Children.Add(PanelButton("mask", "Save Selection as Channel", () => NewAlpha(true)));
        footer.Children.Add(PanelButton("add", "New Alpha Channel", () => NewAlpha(false)));
        footer.Children.Add(PanelButton("delete", "Delete Channel", DeleteAlpha));
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(_channels);
        _channels.SelectionChanged += (_, _) =>
        {
            if (_showingChannels || _channels.SelectedItem is not ListBoxItem row) return;
            if (row.Tag is ColorChannels color) SelectColorChannel(color);
            else if (row.Tag is Guid id)
            {
                _open.ActiveAlpha = id; _open.VisibleChannels = ColorChannels.None;
                UpdateChannelView(); RefreshChannels();
            }
        };
        return panel;
    }

    private void SelectColorChannel(ColorChannels channel)
    {
        _open.ActiveAlpha = null; _open.VisibleChannels = channel;
        if (_document is { } document) document.EditChannels = channel;
        _panelTabs.SelectedIndex = 1;
        UpdateChannelView(); RefreshChannels();
    }

    private void UpdateChannelView()
    {
        if (_open.ActiveAlpha is { } id && _document?.Channels.All(channel => channel.ID != id) != false) { _open.ActiveAlpha = null; _open.VisibleChannels = ColorChannels.RGB; }
        _canvas.DisplayChannels = _open.VisibleChannels;
        _canvas.AlphaDisplay = _document?.Channels.FirstOrDefault(channel => channel.ID == _open.ActiveAlpha)?.Asset.Image;
        _canvas.InvalidateVisual();
    }

    private void RefreshChannels()
    {
        if (_showingChannels) return;
        _showingChannels = true;
        try
        {
            var document = _document;
            if (document is null) { _channelThumbnail?.Dispose(); _channelThumbnail = null; _channelDocument = null; }
            if (document is not null && (!ReferenceEquals(_channelDocument, document) || _channelRevision != _history.CurrentRevision))
            {
                _channelThumbnail?.Dispose();
                _channelThumbnail = DocumentRenderer.Preview(document, 128);
                _channelDocument = document; _channelRevision = _history.CurrentRevision;
            }
            var rows = new List<ListBoxItem>();
            foreach (var image in _channelImages) image.Dispose(); _channelImages.Clear();
            foreach (var (color, title, key) in new[]
            {
                (ColorChannels.RGB, "RGB", "Ctrl+2"), (ColorChannels.Red, "Red", "Ctrl+3"),
                (ColorChannels.Green, "Green", "Ctrl+4"), (ColorChannels.Blue, "Blue", "Ctrl+5"),
            })
            {
                var visible = color == ColorChannels.RGB ? _open.VisibleChannels == ColorChannels.RGB : _open.VisibleChannels.HasFlag(color);
                using var thumb = _channelThumbnail?.Copy();
                if (thumb is not null) ChannelEdits.View(thumb, color, true);
                rows.Add(ChannelRow(color, title, key, visible, thumb, () =>
                {
                    _open.VisibleChannels = color == ColorChannels.RGB
                        ? (_open.VisibleChannels == ColorChannels.RGB ? ColorChannels.None : ColorChannels.RGB)
                        : _open.VisibleChannels ^ color;
                    UpdateChannelView(); RefreshChannels();
                }));
            }
            foreach (var channel in document?.Channels ?? [])
            {
                var id = channel.ID;
                var row = ChannelRow(id, channel.Name, "", _open.ActiveAlpha == id, channel.Asset.Thumbnail, () =>
                {
                    _open.ActiveAlpha = _open.ActiveAlpha == id ? null : id;
                    UpdateChannelView(); RefreshChannels();
                });
                var menu = new ContextMenu();
                menu.Items.Add(Command("Rename Channel…", () => _ = RenameAlpha(id)));
                menu.Items.Add(Command("Duplicate Channel", () => DuplicateAlpha(id)));
                menu.Items.Add(Command("Load Channel as Selection", () => { _open.ActiveAlpha = id; LoadChannelSelection(); }));
                menu.Items.Add(Command("Delete Channel", () => { _open.ActiveAlpha = id; DeleteAlpha(); }));
                row.ContextMenu = menu; rows.Add(row);
            }
            _channels.ItemsSource = rows;
            _channels.SelectedItem = rows.FirstOrDefault(row => _open.ActiveAlpha is { } id ? Equals(row.Tag, id)
                : Equals(row.Tag, document?.EditChannels ?? ColorChannels.RGB));
        }
        finally { _showingChannels = false; }
    }

    private ListBoxItem ChannelRow(object tag, string name, string key, bool visible, SKBitmap? thumbnail, Action toggle)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,42,*,65"), MinHeight = 50 };
        var eye = new Button { Content = new LayerEye(visible) { Width = 20, Height = 20 }, Width = 28,
            Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false };
        ToolTip.SetTip(eye, Localize.Text("Channel Visibility")); eye.Click += (_, _) => toggle(); grid.Children.Add(eye);
        if (thumbnail is not null)
        {
            var image = CanvasView.ToImage(thumbnail); _channelImages.Add(image);
            var view = new Image { Source = image, Width = 38, Height = 38, Stretch = Stretch.Uniform };
            var frame = new Border { Background = Skin.Checker, Child = view, Width = 38, Height = 38, BorderBrush = Skin.SecondaryBrush, BorderThickness = new Thickness(1) };
            Grid.SetColumn(frame, 1); grid.Children.Add(frame);
        }
        var label = new TextBlock { Text = tag is Guid ? name : Localize.Text(name), Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 2); grid.Children.Add(label);
        var shortcut = new TextBlock { Text = key, FontSize = 11, Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(shortcut, 3); grid.Children.Add(shortcut);
        var row = new ListBoxItem { Tag = tag, Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(2) };
        row.DoubleTapped += (_, _) => { _channels.SelectedItem = row; LoadChannelSelection(); };
        return row;
    }

    private void NewAlpha(bool selection)
    {
        if (_document is not { } document) return;
        var name = "Alpha " + (document.Channels.Count + 1);
        while (document.Channels.Any(channel => channel.Name == name)) name += " " + Localize.Text("copy");
        Guid? id = null;
        Edit("New Channel", () => (id = ChannelEdits.Add(document, name, selection)) is not null);
        if (id is null) { Say("A document can contain up to 64 alpha channels."); return; }
        _open.ActiveAlpha = id; _open.VisibleChannels = ColorChannels.None;
        _panelTabs.SelectedIndex = 1; UpdateChannelView(); RefreshChannels();
    }

    private void DeleteAlpha()
    {
        if (_document is not { } document || _open.ActiveAlpha is not { } id) return;
        Edit("Delete Channel", () => document.Channels.RemoveAll(channel => channel.ID == id) > 0);
        _open.ActiveAlpha = null; _open.VisibleChannels = ColorChannels.RGB; document.EditChannels = ColorChannels.RGB;
        UpdateChannelView(); RefreshChannels();
    }

    private async Task RenameAlpha(Guid id)
    {
        if (_document is not { } document || document.Channels.FirstOrDefault(channel => channel.ID == id) is not { } channel) return;
        var name = await TextPrompt.Ask(this, "Rename Channel", "Name", channel.Name);
        if (string.IsNullOrWhiteSpace(name) || !ReferenceEquals(document, _document)) return;
        Edit("Rename Channel", () => { var index = document.Channels.FindIndex(item => item.ID == id); if (index < 0) return false;
            document.Channels[index] = channel with { Name = name.Trim() }; return true; });
        RefreshChannels();
    }

    private void DuplicateAlpha(Guid id)
    {
        if (_document is not { } document || document.Channels.Count >= 64 || document.Channels.FirstOrDefault(item => item.ID == id) is not { } channel) return;
        var copy = channel with { ID = Guid.NewGuid(), Name = channel.Name + " " + Localize.Text("copy") };
        Edit("Duplicate Channel", () => { document.Channels.Add(copy); return true; });
        _open.ActiveAlpha = copy.ID; UpdateChannelView(); RefreshChannels();
    }

    private void LoadChannelSelection()
    {
        if (_document is not { } document) return;
        if (_open.ActiveAlpha is { } id && document.Channels.FirstOrDefault(channel => channel.ID == id) is { } alpha)
            Edit("Load Channel Selection", () => ChannelEdits.Load(document, alpha.Asset.Image));
        else
        {
            using var composite = DocumentRenderer.Render(document);
            ChannelEdits.View(composite, document.EditChannels, true);
            var gray = Bitmaps.Allocate(Bitmaps.MaskInfo(document.Width, document.Height));
            using (var canvas = new SKCanvas(gray)) canvas.DrawBitmap(composite, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            Edit("Load Channel Selection", () => ChannelEdits.Load(document, gray)); gray.Dispose();
        }
        // Loading a channel as a selection resumes editing the image through that selection.
        _open.ActiveAlpha = null; _open.VisibleChannels = ColorChannels.RGB; document.EditChannels = ColorChannels.RGB;
        UpdateChannelView(); RefreshChannels();
    }

    private bool _editingAlpha;
    private static bool SameLayers(CanvasDocument a, CanvasDocument b) => a.Layers.Count == b.Layers.Count
        && a.Layers.Zip(b.Layers).All(pair => pair.First.SameAs(pair.Second));

    private bool EditActiveAlpha(string name, Func<CanvasDocument, Guid, bool> action)
    {
        if (_document is not { } document || _open.ActiveAlpha is not { } id) return false;
        _editingAlpha = true;
        try { Edit(name, () => ChannelEdits.Edit(document, id, action)); }
        finally { _editingAlpha = false; }
        return true;
    }
}

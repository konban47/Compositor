using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;

namespace Compositor.Desktop;

internal sealed class TimelineRateDialog : DialogWindow
{
    internal double Rate => (double)_rate.Value!;
    private readonly NumericUpDown _rate;
    internal TimelineRateDialog(double rate)
    {
        Title = Localize.Text("Timeline Frame Rate"); Width = 510; Height = 135; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new Grid { ColumnDefinitions = new("*,95"), Margin = new Thickness(20), ColumnSpacing = 18 };
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
        fields.Children.Add(new TextBlock { Text = Localize.Text("Frame Rate"), VerticalAlignment = VerticalAlignment.Center });
        var rates = new[] { 12d, 15, 23.976, 24, 25, 29.97, 30, 50, 59.94, 60, 120 };
        var presets = new ComboBox { Width = 90, ItemsSource = rates, SelectedItem = rate };
        _rate = new NumericUpDown { Value = (decimal)rate, Minimum = 1, Maximum = 120, ShowButtonSpinner = false, FormatString = "0.###", Width = 95, Height = 32, Tag = "timeline-rate" };
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is double value) _rate.Value = (decimal)value; };
        fields.Children.Add(presets); fields.Children.Add(_rate); fields.Children.Add(new TextBlock { Text = "fps", VerticalAlignment = VerticalAlignment.Center }); body.Children.Add(fields);
        var actions = new StackPanel { Spacing = 12 }; actions.Children.Add(FormDialog.ActionButton("OK", () => Close(true))); actions.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false)));
        Grid.SetColumn(actions, 1); body.Children.Add(actions); Content = body;
    }
}

internal sealed class TimelinePanelDialog : DialogWindow
{
    internal int ThumbnailSize { get; private set; }
    internal bool FrameNumbers { get; private set; }
    internal TimelinePanelDialog(CanvasDocument document, DocumentTimeline timeline)
    {
        Title = Localize.Text("Animation Panel Options"); Width = 365; Height = 565; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThumbnailSize = timeline.ThumbnailSize; FrameNumbers = timeline.FrameNumbers;
        var body = new Grid { ColumnDefinitions = new("*,85"), ColumnSpacing = 15, Margin = new Thickness(15) };
        var groups = new StackPanel { Spacing = 18 }; body.Children.Add(groups);
        var thumbs = Group("Thumbnail Size"); groups.Children.Add(thumbs.Border);
        using var pixels = DocumentRenderer.Preview(document, 120); var thumbnail = CanvasView.ToImage(pixels); Closed += (_, _) => thumbnail.Dispose();
        var names = new[] { "None", "Small", "Medium", "Large" };
        for (var i = 0; i < 4; i++)
        {
            var index = i; var option = new RadioButton { GroupName = "timeline-thumbnails", IsChecked = i == ThumbnailSize, Tag = "timeline-thumbnail-" + i, Margin = new Thickness(0, 4) };
            option.Content = i == 0 ? Localize.Text(names[i]) : new Image { Source = thumbnail, Width = new[] { 0, 42, 76, 110 }[i], Height = new[] { 0, 42, 76, 110 }[i], Stretch = Stretch.Uniform };
            ToolTip.SetTip(option, Localize.Text(names[i])); option.IsCheckedChanged += (_, _) => { if (option.IsChecked == true) ThumbnailSize = index; }; thumbs.Body.Children.Add(option);
        }
        var units = Group("Timeline Units"); groups.Children.Add(units.Border);
        foreach (var frames in new[] { true, false })
        {
            var option = new RadioButton { GroupName = "timeline-units", Content = Localize.Text(frames ? "Frame Numbers" : "Timecode"), IsChecked = FrameNumbers == frames };
            option.IsCheckedChanged += (_, _) => { if (option.IsChecked == true) FrameNumbers = frames; }; units.Body.Children.Add(option);
        }
        var actions = new StackPanel { Spacing = 12 }; actions.Children.Add(FormDialog.ActionButton("OK", () => Close(true))); actions.Children.Add(FormDialog.ActionButton("Cancel", () => Close(false))); Grid.SetColumn(actions, 1); body.Children.Add(actions); Content = body;
    }
    private static (Border Border, StackPanel Body) Group(string name)
    {
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12) }; body.Children.Add(new TextBlock { Text = Localize.Text(name) });
        return (new Border { BorderBrush = Skin.BorderControlBrush, BorderThickness = new Thickness(1), Child = body }, body);
    }
}

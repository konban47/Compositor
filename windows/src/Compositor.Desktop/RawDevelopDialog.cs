using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>Develops the decoded RAW before it enters the project. Cancel never changes a document.</summary>
internal sealed class RawDevelopDialog : DialogWindow
{
    private bool _accepted;
    private readonly Slider _exposure = new() { Minimum = -5, Maximum = 5, Value = 0, Width = 240 };
    private readonly Slider _temperature = new() { Minimum = -100, Maximum = 100, Value = 0, Width = 240 };
    private readonly Slider _tint = new() { Minimum = -100, Maximum = 100, Value = 0, Width = 240 };
    private readonly CanvasView _preview = new() { Width = 520, Height = 340 };
    private readonly CanvasDocument _document;

    private RawDevelopDialog(ImportedImage image)
    {
        Title = Localize.Text("Develop Camera RAW");
        Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _document = Compositor.Core.IO.ImageImporter.NewDocument(ImportedImage.Create(Bitmaps.Fitted(image.Image, 720), image.Name));
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        content.Children.Add(_preview);
        foreach (var (label, slider) in new[] { ("Exposure", _exposure), ("Temperature", _temperature), ("Tint", _tint) })
        {
            var value = new TextBlock { Width = 55, VerticalAlignment = VerticalAlignment.Center };
            void Update() { value.Text = slider.Value.ToString("0.0"); Preview(); }
            slider.ValueChanged += (_, _) => Update();
            content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                Children = { new TextBlock { Text = Localize.Text(label), Width = 105, VerticalAlignment = VerticalAlignment.Center }, slider, value } });
            value.Text = Localize.Text("0.0");
        }
        var ok = new Button { Content = Localize.Text("Develop"), IsDefault = true };
        var cancel = new Button { Content = Localize.Text("Cancel"), IsCancel = true };
        ok.Click += (_, _) => { _accepted = true; Close(); }; cancel.Click += (_, _) => Close();
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } });
        Content = content;
        Opened += (_, _) => { Preview(); _preview.Fit(); };
    }

    private CameraRawSettings Settings => new() { Exposure = _exposure.Value, Temperature = _temperature.Value, Tint = _tint.Value };
    private void Preview()
    {
        var document = _document.Clone();
        CameraRawEdits.Apply(document, document.Layers[0].ID, Settings);
        _preview.Document = document;
        _preview.InvalidateVisual();
    }

    internal static async Task<ImportedImage?> Develop(Window owner, ImportedImage image)
    {
        var dialog = new RawDevelopDialog(image);
        await dialog.ShowDialog(owner);
        dialog._document.Dispose();
        if (!dialog._accepted) return null;
        var document = Compositor.Core.IO.ImageImporter.NewDocument(image);
        var settings = dialog.Settings;
        await Task.Run(() => CameraRawEdits.Apply(document, document.Layers[0].ID, settings));
        return document.Layers[0].Asset;
    }
}

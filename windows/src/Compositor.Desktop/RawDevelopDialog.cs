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
    private readonly Slider _highlights = new() { Minimum = 0, Maximum = 100, Width = 240 };
    private readonly Slider _noise = new() { Minimum = 0, Maximum = 100, Width = 240 };
    private readonly Slider _lens = new() { Minimum = -100, Maximum = 100, Width = 240 };
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
        foreach (var (label, slider) in new[] { ("Exposure", _exposure), ("Temperature", _temperature), ("Tint", _tint), ("Highlight Recovery", _highlights), ("Noise Reduction", _noise), ("Lens Correction", _lens) })
        {
            var value = new TextBlock { Width = 55, FontSize = 11, Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
            void Update() { value.Text = slider.Value.ToString("0.0"); Preview(); }
            slider.ValueChanged += (_, _) => Update();
            content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                Children = { new TextBlock { Text = Localize.Text(label), Width = 105, FontSize = 12, Foreground = Skin.LabelBrush, VerticalAlignment = VerticalAlignment.Center }, slider, value } });
            value.Text = Localize.Text("0.0");
        }
        var ok = new Button
        {
            Content = Localize.Text("Develop"),
            IsDefault = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.AccentBrush,
            BorderBrush = Skin.AccentBrush,
            BorderThickness = new Thickness(1),
            Foreground = Avalonia.Media.Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var cancel = new Button
        {
            Content = Localize.Text("Cancel"),
            IsCancel = true,
            MinWidth = 76,
            Height = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(7),
            Background = Skin.SurfaceControlBrush,
            BorderBrush = Skin.BorderControlBrush,
            BorderThickness = new Thickness(1),
            Foreground = Skin.LabelBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ok.Click += (_, _) => { _accepted = true; Close(); }; cancel.Click += (_, _) => Close();
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } });
        Content = content;
        Opened += (_, _) => { Preview(); _preview.Fit(); };
    }

    private RawDevelopSettings Settings => new(_exposure.Value, _temperature.Value, _tint.Value, _highlights.Value, _noise.Value, _lens.Value);
    private void Preview()
    {
        var document = _document.Clone();
        document.Layers[0].Asset = RawDevelop.Apply(_document.Layers[0].Asset!, Settings);
        _preview.Document = document;
        _preview.InvalidateVisual();
    }

    internal static async Task<ImportedImage?> Develop(Window owner, ImportedImage image)
    {
        var dialog = new RawDevelopDialog(image);
        await dialog.ShowDialog(owner);
        dialog._document.Dispose();
        if (!dialog._accepted) return null;
        var settings = dialog.Settings;
        return await Task.Run(() => RawDevelop.Apply(image, settings));
    }
}

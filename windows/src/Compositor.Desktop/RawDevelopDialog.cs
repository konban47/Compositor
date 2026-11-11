using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Pixels;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>Develops the decoded RAW before it enters the project. Cancel never changes a document.</summary>
internal sealed class RawDevelopDialog : DialogWindow
{
    private bool _accepted;
    private bool _openCameraRaw;
    internal sealed record Result(ImportedImage Image, bool OpenCameraRaw);
    private readonly Slider _exposure = new() { Minimum = -5, Maximum = 5, Value = 0, Width = 240 };
    private readonly Slider _temperature = new() { Minimum = -100, Maximum = 100, Value = 0, Width = 240 };
    private readonly Slider _tint = new() { Minimum = -100, Maximum = 100, Value = 0, Width = 240 };
    private readonly Slider _highlights = new() { Minimum = 0, Maximum = 100, Width = 240 };
    private readonly Slider _noise = new() { Minimum = 0, Maximum = 100, Width = 240 };
    private readonly Slider _lens = new() { Minimum = -100, Maximum = 100, Width = 240 };
    private readonly CanvasView _preview = new() { Width = 520, Height = 340 };
    private readonly CanvasDocument _document;

    internal RawDevelopDialog(ImportedImage image)
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
        var cameraRaw = new Button { Content = Localize.Text("Camera Raw…"), Tag = "raw-open-camera", MinWidth = 100, Height = 26, FontSize = 12,
            Padding = new Thickness(8, 0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(cameraRaw, Localize.Text("Import, then open the Camera Raw Filter"));
        cameraRaw.Click += (_, _) => { _accepted = true; _openCameraRaw = true; Close(); };
        ok.Tag = "raw-develop"; cancel.Tag = "raw-cancel";
        ok.Click += (_, _) => { _accepted = true; Close(); }; cancel.Click += (_, _) => Close();
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, cameraRaw, ok } });
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

    /// <summary>
    /// Camera Raw Preferences' Workflow: the developed picture at the size, depth and output sharpening it asks for. Sharpening
    /// works on 8 bits/channel pixels, so a 16-bit result keeps its depth unsharpened.
    /// </summary>
    internal static ImportedImage Finish(ImportedImage image, CameraRawWorkflow workflow)
    {
        var (width, height) = workflow.Fit(image.Width, image.Height);
        var resized = width != image.Width || height != image.Height;
        var eight = workflow.Depth == 0 || image.Deep is null;
        if (!resized && !(eight && (image.Deep is not null || workflow.Sharpen))) return image;
        ImportedImage result;
        if (!eight) result = ImportedImage.CreateDeep(Bitmaps.Scale(image.Deep!, width, height), image.Name, image.BitDepth);
        else
        {
            var pixels = Bitmaps.Scale(image.Image, width, height);
            if (workflow.Sharpen) SharpenPixels.Apply(pixels, workflow.SharpenAmount switch { 0 => 30, 2 => 90, _ => 60 }, workflow.SharpenFor switch { 1 => 1.0, 2 => 1.4, _ => .6 });
            result = ImportedImage.Create(pixels, image.Name);
        }
        result.Metadata = image.Metadata; result.ColorProfile = image.ColorProfile;
        image.Dispose(); return result;
    }

    internal static async Task<Result?> Develop(Window owner, ImportedImage image)
    {
        var dialog = new RawDevelopDialog(image);
        await dialog.ShowDialog(owner);
        dialog._document.Dispose();
        if (!dialog._accepted) return null;
        var settings = dialog.Settings;
        var result = await Task.Run(() => RawDevelop.Apply(image, settings));
        image.Dispose();
        return new(result, dialog._openCameraRaw);
    }
}

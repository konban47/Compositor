using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Compositor.Desktop;

/// <summary>The user's imagegen artwork, rendered as a theme-colored alpha mask.</summary>
internal sealed class SliceOrderIcon : Control
{
    private readonly string _name;
    internal SliceOrderIcon(string name) { _name = name; RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality); }
    private static readonly Dictionary<string, ImageBrush> Images = [];
    public override void Render(DrawingContext context)
    {
        if (!Images.TryGetValue(_name, out var mask))
        {
            using var data = AssetLoader.Open(new Uri($"avares://Compositor/Assets/slice-order/{_name}.png"));
            mask = new ImageBrush(new Bitmap(data)) { Stretch = Stretch.Uniform };
            Images.Add(_name, mask);
        }
        using (context.PushOpacityMask(mask, new Rect(Bounds.Size))) context.FillRectangle(Skin.LabelBrush, new Rect(Bounds.Size));
    }
}

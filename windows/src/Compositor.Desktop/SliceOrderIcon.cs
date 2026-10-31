using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Bring to Front, Bring Forward, Send Backward and Send to Back: two overlapping squares, the one being moved
/// solid, from the app's icon system.</summary>
internal sealed class SliceOrderIcon(string name) : Control
{
    public override void Render(DrawingContext context) => IconSet.Draw(context, "slice-" + name, Bounds.Size);
}

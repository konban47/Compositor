namespace Compositor.Core.IO;

/// <summary>Whether a window can come back where it was left.</summary>
public static class WindowPlacement
{
    /// <summary>A screen's working area, in device pixels.</summary>
    public readonly record struct Area(double X, double Y, double Width, double Height);

    /// <summary>How much of the title bar has to be on a screen for the window to be reachable.</summary>
    private const double Reachable = 120;

    /// <summary>Whether enough of a window's top edge is on one of the screens to catch it, so one left on a monitor that has
    /// since been unplugged isn't reopened out of sight.</summary>
    public static bool IsOnScreen(double x, double y, double width, IEnumerable<Area> screens) => screens.Any(area =>
        y >= area.Y - 8 && y + Reachable / 3 <= area.Y + area.Height
        && Math.Min(x + width, area.X + area.Width) - Math.Max(x, area.X) >= Reachable);
}
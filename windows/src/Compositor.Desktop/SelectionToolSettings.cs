using Compositor.Core.Document;

namespace Compositor.Desktop;

internal sealed class SelectionToolSettings
{
    public SelectionMode Mode { get; set; }
    public double Feather { get; set; }
    public int Style { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public bool MagneticPressure { get; set; }
    public uint OverlayColor { get; set; } = 0xffdf48a5;
    public double BrushAngle { get; set; }
    public bool EnhanceEdge { get; set; }
    public bool CloudSubject { get; set; }
    public int ObjectShape { get; set; }
    public bool ObjectFinder { get; set; } = true;
    public bool ObjectSubtract { get; set; } = true;
    public bool ObjectDetails { get; set; } = true;
    public bool ObjectAutoRefresh { get; set; } = true;
    public bool HardEdge { get; set; } = true;
    public int OverlayMode { get; set; }
    public double OverlayOpacity { get; set; } = 32;
    public double OverlayOutline { get; set; }
}

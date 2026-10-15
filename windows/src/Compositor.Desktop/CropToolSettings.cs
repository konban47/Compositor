namespace Compositor.Desktop;

internal sealed class CropToolSettings
{
    public int Mode { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Resolution { get; set; }
    public int ResolutionUnit { get; set; }
    public bool DeletePixels { get; set; } = true;
    public int Fill { get; set; }
    public int Overlay { get; set; }
    public int OverlayDisplay { get; set; } = 1;
    public int Orientation { get; set; }
    public bool Classic { get; set; }
    public bool ShowArea { get; set; } = true;
    public bool AutoCenter { get; set; } = true;
    public bool Shield { get; set; } = true;
    public bool ShieldMatchCanvas { get; set; } = true;
    public uint ShieldColor { get; set; } = 0xff000000;
    public double ShieldOpacity { get; set; } = 75;
    public bool AutoOpacity { get; set; } = true;
    public bool PerspectiveGrid { get; set; } = true;
    public int SliceStyle { get; set; }
    public double SliceWidth { get; set; } = 1;
    public double SliceHeight { get; set; } = 1;
    public bool HideAutoSlices { get; set; }
}

internal sealed record CropPreset(string Name, int Mode, double Width, double Height, double Resolution);

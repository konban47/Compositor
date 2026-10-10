namespace Compositor.Core.Model;

[Flags]
public enum ColorChannels { None = 0, Red = 1, Green = 2, Blue = 4, RGB = 7 }

/// <summary>Full-canvas grayscale selection data. Pixel assets are immutable and shared by undo snapshots.</summary>
public sealed record AlphaChannel(Guid ID, string Name, ImportedImage Asset);
